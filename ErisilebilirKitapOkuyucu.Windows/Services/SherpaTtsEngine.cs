using System.IO;
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
    public string DisplayName => "Açık Kaynak Nöral Ses (Piper - Çevrim Dışı)";

    private static readonly IReadOnlyList<VoiceDescriptor> _voices = new List<VoiceDescriptor>
    {
        new("piper-fahrettin", "Piper Fahrettin Doğal (Erkek - Çevrim Dışı)", "tr-TR", "sherpa")
    };

    private OfflineTts? _currentTts;
    private string? _loadedVoiceId;
    private readonly SemaphoreSlim _synthLock = new(1, 1);

    private WaveOutEvent? _currentWaveOut;
    private readonly object _playLock = new();

    private string? _prefetchedText;
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

    private static string GetFolderName(string voiceId) => "vits-piper-tr_TR-fahrettin-medium";
    private static string GetOnnxFileName(string voiceId) => "tr_TR-fahrettin-medium.onnx";

    public static string? FindModelDirectory(string voiceId)
    {
        string folder = GetFolderName(voiceId);
        string[] candidates =
        {
            Path.Combine(AppPaths.DataDir, "models", folder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", folder),
            @"C:\Users\gokta\.gemini\antigravity\scratch\models\" + folder
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

        // Model klasörü bulunamadıysa AppPaths.DataDir/models altına indir
        string folder = GetFolderName(voiceId);
        string targetDir = Path.Combine(AppPaths.DataDir, "models", folder);
        Directory.CreateDirectory(Path.Combine(AppPaths.DataDir, "models"));

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
        config.Model.Vits.DataDir = dataDir;
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

    private byte[] GenerateWavBytes(string text, string voiceId, float rate)
    {
        var tts = GetOrLoadTts(voiceId);

        // Piper hız katsayısı: 1.0f = normal, 1.2f = %20 daha hızlı
        float speed = Math.Clamp(rate, 0.5f, 3.0f);

        var audio = tts.Generate(text, speed, 0);
        if (audio.Samples == null || audio.Samples.Length == 0)
        {
            throw new InvalidOperationException("Piper ses verisi üretemedi.");
        }

        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, 1)))
        {
            writer.WriteSamples(audio.Samples, 0, audio.Samples.Length);
        }
        return ms.ToArray();
    }

    public Task PreFetchAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        string targetVoice = "piper-fahrettin";

        lock (_playLock)
        {
            if (_prefetchedText == text && _prefetchTask != null && !_prefetchTask.IsFaulted)
            {
                return Task.CompletedTask;
            }

            try { _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); } catch { }
            _prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _prefetchedText = text;
            _prefetchTask = Task.Run(async () =>
            {
                await _synthLock.WaitAsync(_prefetchCts.Token);
                try
                {
                    return GenerateWavBytes(text, targetVoice, rate);
                }
                finally
                {
                    _synthLock.Release();
                }
            }, _prefetchCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        string targetVoice = "piper-fahrettin";

        try
        {
            byte[]? wavData = null;
            Task<byte[]>? existingTask = null;

            lock (_playLock)
            {
                if (_prefetchedText == text && _prefetchTask != null)
                {
                    existingTask = _prefetchTask;
                    _prefetchedText = null;
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
                    var synthTask = Task.Run(() => GenerateWavBytes(text, targetVoice, rate));
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
        string targetVoice = "piper-fahrettin";

        await _synthLock.WaitAsync(ct);
        try
        {
            var bytes = await Task.Run(() => GenerateWavBytes(text, targetVoice, rate), ct);
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
            _prefetchTask = null;
        }
        try { _currentTts?.Dispose(); _currentTts = null; } catch { }
        _synthLock.Dispose();
        _fallbackEngine.Dispose();
    }
}
