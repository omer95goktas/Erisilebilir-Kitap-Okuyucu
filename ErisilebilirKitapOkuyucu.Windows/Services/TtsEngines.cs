using System.IO;
using System.Security;
using SapiSynth = System.Speech.Synthesis.SpeechSynthesizer;
using OcSynth = Windows.Media.SpeechSynthesis.SpeechSynthesizer;
using Windows.Media.Core;
using Windows.Media.Playback;
using NAudio.Wave;

namespace ErisilebilirKitapOkuyucu.Services;

public sealed record VoiceDescriptor(string Id, string Name, string Language, string EngineId)
{
    public string Display => $"{Name} ({Language})";
    public bool IsTurkish => Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Windows'ta kullanılabilen konuşma motorlarının ortak arayüzü.</summary>
public interface ITtsEngine : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<VoiceDescriptor> GetVoices();
    Task SpeakAsync(string text, string? voiceId, float rate, float pitch, float volume, CancellationToken ct);
    Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct) => SpeakAsync(text, voiceId, rate, pitch, 1.0f, ct);
    Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct);
    Task PreFetchAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>SAPI 5 motoru: sistemde kayıtlı tüm SAPI 5 sesleri (Microsoft Tolga/Hazel, 3. parti sesler vb.).</summary>
public sealed class SapiEngine : ITtsEngine
{
    public string Id => "sapi";
    public string DisplayName => "SAPI 5 (klasik Windows sesleri)";
    private SapiSynth? _synth;

    /// <summary>Kayıtlı olduğu hâlde yüklenemeyen (bozuk kurulum, lisans vb.) SAPI sesleri.</summary>
    public IReadOnlyList<string> UnusableVoices { get; private set; } = Array.Empty<string>();
    private IReadOnlyList<VoiceDescriptor>? _cache;

    public IReadOnlyList<VoiceDescriptor> GetVoices()
    {
        if (_cache != null) return _cache;
        try
        {
            using var s = new SapiSynth();
            var good = new List<VoiceDescriptor>();
            var bad = new List<string>();
            foreach (var v in s.GetInstalledVoices().Where(v => v.Enabled))
            {
                try { s.SelectVoice(v.VoiceInfo.Name); good.Add(new VoiceDescriptor(v.VoiceInfo.Name, v.VoiceInfo.Name, v.VoiceInfo.Culture.Name, Id)); }
                catch (Exception e) { bad.Add(v.VoiceInfo.Name); AppPaths.Log($"sapi voice unusable: {v.VoiceInfo.Name}: {e.Message}"); }
            }
            UnusableVoices = bad;
            return _cache = good;
        }
        catch (Exception e) { AppPaths.Log("sapi voices: " + e); return Array.Empty<VoiceDescriptor>(); }
    }

    /// <summary>SAPI hızı -10..10; 1.0x = 0.</summary>
    internal static int MapRate(float r) => r < 1f ? (int)Math.Round((Math.Max(r, 0.5f) - 1f) * 10f) : (int)Math.Round(Math.Min((r - 1f) * 5f, 10f));

    private static void SelectVoice(SapiSynth s, string? voiceId)
    {
        if (string.IsNullOrEmpty(voiceId)) return;
        try { s.SelectVoice(voiceId); } catch (Exception e) { AppPaths.Log("sapi select voice: " + e.Message); }
    }

    private static string Ssml(string text, float pitch, string lang)
    {
        var pct = (int)Math.Round((pitch - 1f) * 50f);
        var p = pct >= 0 ? "+" + pct + "%" : pct + "%";
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{lang}\"><prosody pitch=\"{p}\">{SecurityElement.Escape(text)}</prosody></speak>";
    }

    public Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct) => SpeakAsync(text, voiceId, rate, pitch, 1.0f, ct);

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, float volume, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        int vol = Math.Clamp((int)Math.Round(volume * 100), 0, 100);

        if (Math.Abs(pitch - 1.0f) <= 0.02f)
        {
            var synth = new SapiSynth { Volume = vol, Rate = MapRate(rate) };
            _synth = synth;
            SelectVoice(synth, voiceId);
            CancellationTokenRegistration reg = default;
            synth.SpeakCompleted += (_, e) =>
            {
                reg.Dispose();
                try { synth.Dispose(); } catch { }
                if (e.Cancelled) tcs.TrySetCanceled();
                else if (e.Error != null) tcs.TrySetException(e.Error);
                else tcs.TrySetResult();
            };
            reg = ct.Register(() => { try { synth.SpeakAsyncCancelAll(); } catch { } });
            try { synth.SpeakAsync(text); }
            catch (Exception e) { reg.Dispose(); tcs.TrySetException(e); }
            await tcs.Task;
            return;
        }

        // SAPI motorlarının çoğu (örn. Microsoft Zira Desktop) SSML perde etiketini desteklemediğinden
        // perde 1.0'dan farklı olduğunda sesi DSP SmbPitchShifter ile kesin olarak kaydırıyoruz.
        byte[]? outBytes = null;
        await Task.Run(() =>
        {
            using var synth = new SapiSynth { Volume = 100, Rate = MapRate(rate) };
            SelectVoice(synth, voiceId);

            using var mem = new MemoryStream();
            synth.SetOutputToWaveStream(mem);
            ct.ThrowIfCancellationRequested();
            synth.Speak(text);
            synth.SetOutputToNull();

            byte[] pcmBytes = mem.ToArray();
            if (pcmBytes.Length <= 44) return;
            int sampleRate = BitConverter.ToInt32(pcmBytes, 24);
            int channels = BitConverter.ToInt16(pcmBytes, 22);
            int bits = BitConverter.ToInt16(pcmBytes, 34);

            int bytesPerSample = Math.Max(1, bits / 8);
            int sampleCount = (pcmBytes.Length - 44) / bytesPerSample;
            var floatSamples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(pcmBytes, 44 + i * 2);
                floatSamples[i] = sample / 32768f;
            }

            float pitchFactor = Math.Clamp(pitch, 0.5f, 2.0f);
            var shifter = new NAudio.Dsp.SmbPitchShifter();
            shifter.PitchShift(pitchFactor, sampleCount, 1024, 4, sampleRate, floatSamples);

            using var outMem = new MemoryStream();
            using (var writer = new WaveFileWriter(outMem, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)))
            {
                writer.WriteSamples(floatSamples, 0, sampleCount);
            }
            outBytes = outMem.ToArray();
        }, ct);

        if (outBytes == null || outBytes.Length == 0) return;
        ct.ThrowIfCancellationRequested();

        var playTcs = new TaskCompletionSource();
        using var playStream = new MemoryStream(outBytes);
        using var reader = new WaveFileReader(playStream);
        using var waveOut = new WaveOutEvent();
        waveOut.Volume = Math.Clamp(volume, 0.0f, 1.0f);
        waveOut.Init(reader);

        waveOut.PlaybackStopped += (_, _) => playTcs.TrySetResult();
        using var cancelReg = ct.Register(() =>
        {
            try { waveOut.Stop(); } catch { }
            playTcs.TrySetCanceled(ct);
        });

        waveOut.Play();
        await playTcs.Task;
    }

    public Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var s = new SapiSynth { Rate = MapRate(rate), Volume = 100 };
            SelectVoice(s, voiceId);

            if (Math.Abs(pitch - 1.0f) <= 0.02f)
            {
                s.SetOutputToWaveFile(path);
                ct.ThrowIfCancellationRequested();
                s.Speak(text);
                s.SetOutputToNull();
                return;
            }

            using var mem = new MemoryStream();
            s.SetOutputToWaveStream(mem);
            ct.ThrowIfCancellationRequested();
            s.Speak(text);
            s.SetOutputToNull();

            byte[] pcmBytes = mem.ToArray();
            if (pcmBytes.Length <= 44) return;
            int sampleRate = BitConverter.ToInt32(pcmBytes, 24);
            int channels = BitConverter.ToInt16(pcmBytes, 22);
            int bits = BitConverter.ToInt16(pcmBytes, 34);

            int bytesPerSample = Math.Max(1, bits / 8);
            int sampleCount = (pcmBytes.Length - 44) / bytesPerSample;
            var floatSamples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(pcmBytes, 44 + i * 2);
                floatSamples[i] = sample / 32768f;
            }

            float pitchFactor = Math.Clamp(pitch, 0.5f, 2.0f);
            var shifter = new NAudio.Dsp.SmbPitchShifter();
            shifter.PitchShift(pitchFactor, sampleCount, 1024, 4, sampleRate, floatSamples);

            using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
            writer.WriteSamples(floatSamples, 0, sampleCount);
        }, ct);

    public void Dispose() { try { _synth?.SpeakAsyncCancelAll(); _synth?.Dispose(); } catch { } }
}

/// <summary>Modern Windows OneCore (Windows.Media.SpeechSynthesis) motoru: "Ayarlar > Konuşma" altındaki sesler.</summary>
public sealed class OneCoreEngine : ITtsEngine
{
    public string Id => "onecore";
    public string DisplayName => "Windows OneCore (modern Windows sesleri)";

    public IReadOnlyList<VoiceDescriptor> GetVoices()
    {
        try
        {
            return OcSynth.AllVoices
                .Select(v => new VoiceDescriptor(v.Id, v.DisplayName, v.Language, Id)).ToList();
        }
        catch (Exception e) { AppPaths.Log("onecore voices: " + e); return Array.Empty<VoiceDescriptor>(); }
    }

    private static OcSynth Create(string? voiceId, float rate, float pitch, float volume = 1.0f)
    {
        var s = new OcSynth();
        if (!string.IsNullOrEmpty(voiceId))
        {
            var v = OcSynth.AllVoices.FirstOrDefault(x => x.Id == voiceId);
            if (v != null) s.Voice = v;
        }
        try
        {
            s.Options.SpeakingRate = Math.Clamp(rate, 0.5f, 6.0f);
            s.Options.AudioPitch = Math.Clamp(pitch, 0f, 2f);
            s.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0);
        }
        catch { }
        return s;
    }

    public Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct) => SpeakAsync(text, voiceId, rate, pitch, 1.0f, ct);

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, float volume, CancellationToken ct)
    {
        using var synth = Create(voiceId, rate, pitch, volume);
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource();
        using var player = new MediaPlayer { AutoPlay = false, Volume = Math.Clamp(volume, 0.0, 1.0) };
        player.CommandManager.IsEnabled = false; // medya tuşları sesli kitap oynatıcısına ait kalsın
        player.MediaEnded += (_, _) => tcs.TrySetResult();
        player.MediaFailed += (_, e) => tcs.TrySetException(new InvalidOperationException("OneCore oynatma hatası: " + e.ErrorMessage));
        using var reg = ct.Register(() => { try { player.Pause(); } catch { } tcs.TrySetCanceled(); });
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        player.Play();
        await tcs.Task;
    }

    public async Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct)
    {
        using var synth = Create(voiceId, rate, pitch);
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        ct.ThrowIfCancellationRequested();
        using var input = stream.AsStreamForRead();
        await using var fs = File.Create(path);
        await input.CopyToAsync(fs, ct);
    }

    public void Dispose() { }
}

public static class TtsEngines
{
    public static IReadOnlyList<ITtsEngine> CreateAll() => new ITtsEngine[]
    {
        new SherpaTtsEngine(),
        new EdgeTtsEngine(),
        new GoogleCloudTtsEngine(),
        new OneCoreEngine(),
        new SapiEngine()
    };
}
