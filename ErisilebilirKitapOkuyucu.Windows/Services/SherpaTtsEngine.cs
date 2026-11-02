using System.IO;
using System.Text;
using System.Net.Http;
using System.Formats.Tar;
using System.IO.Compression;
using NAudio.Wave;
using SherpaOnnx;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>
/// Piper / Sherpa-ONNX Açık Kaynak Çevrim Dışı Yapay Zeka Ses Motoru.
/// %100 yerel çalışır, internet gerektirmez ve stüdyo kalitesinde Türkçe ses sentezler.
/// </summary>
public sealed class SherpaTtsEngine : ITtsEngine
{
    public string Id => "sherpa";
    public string DisplayName => "Açık Kaynak Nöral Ses (Piper & Meta - Çevrim Dışı)";

    private static readonly IReadOnlyList<VoiceDescriptor> _voices = new List<VoiceDescriptor>
    {
        new("piper-fahrettin", "Piper Fahrettin Doğal (Erkek - Çevrim Dışı)", "tr-TR", "sherpa"),
        new("meta-mms-azj", "Meta MMS Azerbaycan (Kadın - Çevrim Dışı)", "az-AZ", "sherpa")
    };

    private OfflineTts? _currentTts;
    private string? _loadedVoiceId;
    private readonly SemaphoreSlim _synthLock = new(1, 1);

    private WaveOutEvent? _currentWaveOut;
    private readonly object _playLock = new();

    private string? _prefetchedText;
    private string? _prefetchedVoice;
    private Task<byte[]>? _prefetchTask;
    private CancellationTokenSource? _prefetchCts;

    // Fallback motoru (herhangi bir beklenmedik durumda yerel OneCore'a düşer)
    private readonly OneCoreEngine _fallbackEngine = new();

    public IReadOnlyList<VoiceDescriptor> GetVoices() => _voices;

    public SherpaTtsEngine()
    {
        // Model ön yüklemesini ilk açılışta zorunlu ve acele yapmayalım;
        // Kullanıcı bu sesi seçtiğinde veya okuma başlattığında yüklensin.
    }

    private static string GetFolderName(string voiceId) =>
        voiceId == "meta-mms-azj" ? "vits-mms-azj-script_latin" : "vits-piper-tr_TR-fahrettin-medium";

    private static string GetOnnxFileName(string voiceId) =>
        voiceId == "meta-mms-azj" ? "model.onnx" : "tr_TR-fahrettin-medium.onnx";

    public static string? FindModelDirectory(string voiceId)
    {
        string folder = GetFolderName(voiceId);
        string[] candidates =
        {
            Path.Combine(AppPaths.DataDir, "models", folder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", folder)
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "tokens.txt")))
            {
                return dir;
            }
        }
        return null;
    }

    private static void EnsureModelExists(string voiceId)
    {
        var dir = FindModelDirectory(voiceId);
        if (dir != null) return;

        string folder = GetFolderName(voiceId);
        string targetDir = Path.Combine(AppPaths.DataDir, "models", folder);
        Directory.CreateDirectory(targetDir);

        if (voiceId == "meta-mms-azj")
        {
            AppPaths.Log($"SherpaTtsEngine: Meta MMS modeli indiriliyor ({folder})...");
            using var client = new HttpClient();
            string modelUrl = "https://huggingface.co/willwade/mms-tts-multilingual-models-onnx/resolve/main/azj-script_latin/model.onnx";
            string tokensUrl = "https://huggingface.co/willwade/mms-tts-multilingual-models-onnx/resolve/main/azj-script_latin/tokens.txt";

            var tokensData = client.GetByteArrayAsync(tokensUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(Path.Combine(targetDir, "tokens.txt"), tokensData);

            var modelData = client.GetByteArrayAsync(modelUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(Path.Combine(targetDir, "model.onnx"), modelData);
            return;
        }

        // Model klasörü bulunamadıysa AppPaths.DataDir/models altına indir
        AppPaths.Log($"SherpaTtsEngine: Model eksik, indiriliyor: {folder}...");
        string url = $"https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/{folder}.tar.bz2";
        string tarBz2Path = Path.Combine(AppPaths.DataDir, "models", $"{folder}.tar.bz2");

        using (var client = new HttpClient())
        {
            var data = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(tarBz2Path, data);
        }

        // tar.exe ile aç
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("tar.exe", $"-xvf \"{tarBz2Path}\" -C \"{Path.Combine(AppPaths.DataDir, "models")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"SherpaTtsEngine: Arşiv açma hatası: {ex.Message}");
        }

        try { File.Delete(tarBz2Path); } catch { }
    }

    private OfflineTts GetOrLoadTts(string voiceId)
    {
        if (_currentTts != null && _loadedVoiceId == voiceId)
        {
            return _currentTts;
        }

        EnsureModelExists(voiceId);

        var modelDir = FindModelDirectory(voiceId);
        if (modelDir == null)
        {
            throw new DirectoryNotFoundException($"'{voiceId}' için model dizini bulunamadı.");
        }

        var onnxFile = Path.Combine(modelDir, GetOnnxFileName(voiceId));
        var tokensFile = Path.Combine(modelDir, "tokens.txt");
        var dataDir = Path.Combine(modelDir, "espeak-ng-data");

        if (!File.Exists(onnxFile) || !File.Exists(tokensFile))
        {
            throw new FileNotFoundException($"Model dosyaları eksik: {onnxFile}");
        }

        AppPaths.Log($"SherpaTtsEngine: Model yükleniyor: {voiceId} ({onnxFile})...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = onnxFile;
        config.Model.Vits.Tokens = tokensFile;
        if (Directory.Exists(dataDir))
        {
            config.Model.Vits.DataDir = dataDir;
        }
        else
        {
            config.Model.Vits.DataDir = "";
        }
        // Akustik ve artikülasyon optimizasyonu (harf yutulmasını önleme)
        // Varsayılan NoiseScale 0.667f -> 0.333f: Daha net, kararlı ve duru ses
        config.Model.Vits.NoiseScale = 0.333f;
        // Varsayılan NoiseScaleW 0.8f -> 0.4f: Fonem sürelerindeki aşırı rastgele oynamayı ve harflerin yutulmasını engeller
        config.Model.Vits.NoiseScaleW = 0.4f;
        config.Model.Vits.LengthScale = 1.0f;
        config.SilenceScale = 0.35f; // Cümle ve virgül duraklamalarını rahatlatır
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";
        config.Model.Debug = 0;

        var tts = new OfflineTts(config);

        _currentTts?.Dispose();
        _currentTts = tts;
        _loadedVoiceId = voiceId;

        AppPaths.Log($"SherpaTtsEngine: Model yüklendi ({sw.ElapsedMilliseconds} ms).");
        return tts;
    }

    public static void SynthesizeMmsDirect(string textFile, string outputFile, float speed, float pitch)
    {
        var text = File.ReadAllText(textFile, Encoding.UTF8);
        var modelDir = FindModelDirectory("meta-mms-azj");
        if (modelDir == null) throw new DirectoryNotFoundException("MMS model dizini bulunamadı.");

        var onnxFile = Path.Combine(modelDir, GetOnnxFileName("meta-mms-azj"));
        var tokensFile = Path.Combine(modelDir, "tokens.txt");

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = onnxFile;
        config.Model.Vits.Tokens = tokensFile;
        config.Model.Vits.DataDir = "";
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";

        using var tts = new OfflineTts(config);
        var audio = tts.Generate(text, speed, 0);
        if (audio.Samples == null || audio.Samples.Length == 0)
            throw new InvalidOperationException("MMS ses verisi üretemedi.");

        var samples = audio.Samples;
        if (Math.Abs(pitch - 1.0f) > 0.02f)
        {
            float pitchFactor = Math.Clamp(pitch, 0.5f, 2.0f);
            var shifter = new NAudio.Dsp.SmbPitchShifter();
            shifter.PitchShift(pitchFactor, samples.Length, 1024, 4, audio.SampleRate, samples);
        }

        using var writer = new WaveFileWriter(outputFile, WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, 1));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private byte[] GenerateWavBytes(string text, string voiceId, float rate, float pitch = 1.0f)
    {
        if (voiceId == "meta-mms-azj")
        {
            EnsureModelExists("meta-mms-azj");
            var tempText = Path.Combine(AppPaths.TempDir, $"mms_in_{Guid.NewGuid():N}.txt");
            var tempWav = Path.Combine(AppPaths.TempDir, $"mms_out_{Guid.NewGuid():N}.wav");
            try
            {
                File.WriteAllText(tempText, text, Encoding.UTF8);
                var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    float speedMms = Math.Clamp(rate, 0.5f, 3.0f);
                    var psi = new System.Diagnostics.ProcessStartInfo(exe, $"--mms-synth \"{tempText}\" \"{tempWav}\" {speedMms.ToString(System.Globalization.CultureInfo.InvariantCulture)} {pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit();
                    if (File.Exists(tempWav))
                    {
                        return File.ReadAllBytes(tempWav);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempText)) File.Delete(tempText); } catch { }
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            }
        }

        var tts = GetOrLoadTts(voiceId);

        // Piper hız katsayısı: 1.0f = normal, 1.2f = %20 daha hızlı
        float speed = Math.Clamp(rate, 0.5f, 3.0f);

        var audio = tts.Generate(text, speed, 0);
        if (audio.Samples == null || audio.Samples.Length == 0)
        {
            throw new InvalidOperationException("Piper ses verisi üretemedi.");
        }

        var samples = audio.Samples;
        // Ses perdesi (pitch) modifikasyonu (0.5x - 2.0x, 1.0x normal)
        if (Math.Abs(pitch - 1.0f) > 0.02f)
        {
            float pitchFactor = Math.Clamp(pitch, 0.5f, 2.0f);
            var shifter = new NAudio.Dsp.SmbPitchShifter();
            shifter.PitchShift(pitchFactor, samples.Length, 1024, 4, audio.SampleRate, samples);
        }

        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
        return ms.ToArray();
    }

    public Task PreFetchAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        string targetVoice = !string.IsNullOrWhiteSpace(voiceId) ? voiceId : "piper-fahrettin";

        lock (_playLock)
        {
            if (_prefetchedText == text && _prefetchedVoice == targetVoice && _prefetchTask != null && !_prefetchTask.IsFaulted)
            {
                return Task.CompletedTask;
            }

            try { _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); } catch { }
            _prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _prefetchedText = text;
            _prefetchedVoice = targetVoice;
            _prefetchTask = Task.Run(async () =>
            {
                await _synthLock.WaitAsync(_prefetchCts.Token);
                try
                {
                    return GenerateWavBytes(text, targetVoice, rate, pitch);
                }
                finally
                {
                    _synthLock.Release();
                }
            }, _prefetchCts.Token);
        }

        return Task.CompletedTask;
    }

    public Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct) => SpeakAsync(text, voiceId, rate, pitch, 1.0f, ct);

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, float volume, CancellationToken ct)
    {
        string targetVoice = !string.IsNullOrWhiteSpace(voiceId) ? voiceId : "piper-fahrettin";

        try
        {
            byte[]? wavData = null;
            Task<byte[]>? existingTask = null;

            lock (_playLock)
            {
                if (_prefetchedText == text && _prefetchedVoice == targetVoice && _prefetchTask != null)
                {
                    existingTask = _prefetchTask;
                    _prefetchedText = null;
                    _prefetchedVoice = null;
                    _prefetchTask = null;
                }
            }

            if (existingTask != null)
            {
                AppPaths.Log($"Sherpa TTS: '{targetVoice}' için önceden hazırlanan ses kullanılıyor (anında oynatma)...");
                var completed = await Task.WhenAny(existingTask, Task.Delay(Timeout.Infinite, ct));
                if (completed != existingTask)
                {
                    ct.ThrowIfCancellationRequested();
                }
                wavData = await existingTask;
            }
            else
            {
                await _synthLock.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    AppPaths.Log($"Sherpa TTS: '{targetVoice}' ile sentezleniyor (metin: {text.Length} karakter)...");
                    var synthTask = Task.Run(() => GenerateWavBytes(text, targetVoice, rate, pitch));
                    var completed = await Task.WhenAny(synthTask, Task.Delay(Timeout.Infinite, ct));
                    if (completed != synthTask)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    wavData = await synthTask;
                }
                finally
                {
                    _synthLock.Release();
                }
            }

            if (wavData == null || wavData.Length == 0)
            {
                throw new InvalidOperationException($"'{targetVoice}' ses motorundan ses verisi alınamadı.");
            }

            ct.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource();
            using var wavStream = new MemoryStream(wavData);
            using var wavReader = new WaveFileReader(wavStream);
            using var waveOut = new WaveOutEvent();

            lock (_playLock) { _currentWaveOut = waveOut; }

            waveOut.PlaybackStopped += (_, _) =>
            {
                lock (_playLock) { if (_currentWaveOut == waveOut) _currentWaveOut = null; }
                tcs.TrySetResult();
            };

            using var reg = ct.Register(() =>
            {
                try { waveOut.Stop(); } catch { }
                tcs.TrySetCanceled(ct);
            });

            waveOut.Volume = Math.Clamp(volume, 0.0f, 1.0f);
            waveOut.Init(wavReader);
            waveOut.Play();

            await tcs.Task;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Sherpa TTS hatası ('{targetVoice}'): {ex.Message}. Yerel OneCore sesine geçiliyor.");
            await _fallbackEngine.SpeakAsync(text, null, rate, pitch, ct);
        }
    }

    public async Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct)
    {
        string targetVoice = !string.IsNullOrWhiteSpace(voiceId) ? voiceId : "piper-fahrettin";

        await _synthLock.WaitAsync(ct);
        try
        {
            var bytes = await Task.Run(() => GenerateWavBytes(text, targetVoice, rate, pitch), ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        finally
        {
            _synthLock.Release();
        }
    }

    public void Dispose()
    {
        lock (_playLock)
        {
            try { _currentWaveOut?.Stop(); _currentWaveOut?.Dispose(); _currentWaveOut = null; } catch { }
            try { _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); _prefetchCts = null; } catch { }
            _prefetchedText = null;
            _prefetchedVoice = null;
            _prefetchTask = null;
        }
        try { _currentTts?.Dispose(); _currentTts = null; } catch { }
        _synthLock.Dispose();
        _fallbackEngine.Dispose();
    }
}
