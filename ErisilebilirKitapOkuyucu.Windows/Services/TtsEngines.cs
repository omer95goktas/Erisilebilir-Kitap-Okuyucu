using System.IO;
using System.Security;
using SapiSynth = System.Speech.Synthesis.SpeechSynthesizer;
using OcSynth = Windows.Media.SpeechSynthesis.SpeechSynthesizer;
using Windows.Media.Core;
using Windows.Media.Playback;

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
    Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct);
    Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct);
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

    public Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        var synth = new SapiSynth { Volume = 100, Rate = MapRate(rate) };
        _synth = synth;
        SelectVoice(synth, voiceId);
        var lang = synth.Voice?.Culture?.Name ?? "tr-TR";
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
        try
        {
            if (Math.Abs(pitch - 1f) > 0.01f)
            {
                try { synth.SpeakSsmlAsync(Ssml(text, pitch, lang)); }
                catch { synth.SpeakAsync(text); }
            }
            else synth.SpeakAsync(text);
        }
        catch (Exception e) { reg.Dispose(); tcs.TrySetException(e); }
        return tcs.Task;
    }

    public Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var s = new SapiSynth { Rate = MapRate(rate), Volume = 100 };
            SelectVoice(s, voiceId);
            s.SetOutputToWaveFile(path);
            ct.ThrowIfCancellationRequested();
            if (Math.Abs(pitch - 1f) > 0.01f)
            {
                try { s.SpeakSsml(Ssml(text, pitch, s.Voice?.Culture?.Name ?? "tr-TR")); return; }
                catch { /* düz metne düş */ }
            }
            s.Speak(text);
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

    private static OcSynth Create(string? voiceId, float rate, float pitch)
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
            s.Options.AudioVolume = 1.0;
        }
        catch { }
        return s;
    }

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        using var synth = Create(voiceId, rate, pitch);
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource();
        using var player = new MediaPlayer { AutoPlay = false };
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
    public static IReadOnlyList<ITtsEngine> CreateAll() => new ITtsEngine[] { new OneCoreEngine(), new SapiEngine() };
}
