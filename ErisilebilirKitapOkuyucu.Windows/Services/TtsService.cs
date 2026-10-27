using System.IO;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace ErisilebilirKitapOkuyucu.Services;

public record Sentence(string Text, int Start, int Length);

/// <summary>Android'deki TtsManager'ın Windows karşılığı: cümle cümle okuma, sayfa sonu bildirimi, WAV dışa aktarma.</summary>
public sealed class TtsService : IDisposable
{
    private readonly BookRepository _repo;
    private readonly IReadOnlyList<ITtsEngine> _engines = TtsEngines.CreateAll();
    private List<Sentence> _sentences = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public bool IsSpeaking { get; private set; }
    public int CurrentIndex { get; private set; }
    public IReadOnlyList<Sentence> Sentences => _sentences;

    public event Action? SpeakingChanged;
    public event Action<int>? SentenceChanged;
    public event Action? PageCompleted;
    public event Action<string>? Error;

    public TtsService(BookRepository repo) { _repo = repo; }

    public IReadOnlyList<ITtsEngine> Engines => _engines;

    public ITtsEngine ActiveEngine
    {
        get
        {
            var id = _repo.Settings.SelectedTtsEngine;
            var e = _engines.FirstOrDefault(x => x.Id == id);
            if (e != null) return e;
            // Otomatik: OneCore'da ses varsa onu, yoksa SAPI'yi kullan
            return _engines.FirstOrDefault(x => x.GetVoices().Count > 0) ?? _engines[0];
        }
    }

    public string? ActiveVoiceId
    {
        get
        {
            var engine = ActiveEngine;
            var voices = engine.GetVoices();
            var saved = _repo.Settings.SelectedVoiceId;
            if (saved != null && voices.Any(v => v.Id == saved)) return saved;
            return (voices.FirstOrDefault(v => v.IsTurkish) ?? voices.FirstOrDefault())?.Id;
        }
    }

    /// <summary>Metni cümlelere veya paragraflara böler; her parçanın özgün metindeki konumunu da döndürür.</summary>
    public static List<Sentence> Split(string text) => Split(text, false);

    public static List<Sentence> Split(string text, bool paragraphMode)
    {
        var result = new List<Sentence>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        if (paragraphMode)
        {
            int pos = 0;
            // Paragraflar: Çift satır sonu (\n\n veya \r\n\r\n) ile veya metin satırları ile ayrılmış paragrafları bul
            var paras = Regex.Matches(text, @"[^\r\n]+(?:\r?\n(?!\r?\n)[^\r\n]+)*");
            foreach (Match p in paras)
            {
                var clean = p.Value.Trim();
                if (clean.Length == 0) continue;
                var idx = text.IndexOf(clean, pos, StringComparison.Ordinal);
                if (idx < 0) idx = text.IndexOf(clean, StringComparison.Ordinal);
                if (idx < 0) idx = pos;
                result.Add(new Sentence(clean, idx, clean.Length));
                pos = idx + clean.Length;
            }
            return result.Count > 0 ? result : new List<Sentence> { new(text.Trim(), 0, text.Trim().Length) };
        }

        int posSentence = 0;
        foreach (Match para in Regex.Matches(text, @"[^\n]+(?:\n(?!\n)[^\n]*)*"))
        {
            foreach (var part in Regex.Split(para.Value, @"(?<=[.!?…])\s+"))
            {
                var clean = part.Trim();
                if (clean.Length == 0) continue;
                var idx = text.IndexOf(clean, posSentence, StringComparison.Ordinal);
                if (idx < 0) idx = posSentence;
                result.Add(new Sentence(clean, idx, clean.Length));
                posSentence = idx + clean.Length;
            }
        }
        return result;
    }

    public void LoadPageText(string text, int startIndex = 0)
    {
        Stop();
        bool isParagraph = _repo.Settings.TtsReadMode == "Paragraf";
        _sentences = Split(text, isParagraph);
        CurrentIndex = Math.Clamp(startIndex, 0, Math.Max(0, _sentences.Count - 1));
    }

    public void PauseOrResume()
    {
        if (IsSpeaking) Stop(); else StartSpeaking(CurrentIndex);
    }

    public void NextSentence()
    {
        if (CurrentIndex + 1 < _sentences.Count) JumpTo(CurrentIndex + 1);
        else { Stop(); PageCompleted?.Invoke(); }
    }

    public void PreviousSentence() => JumpTo(Math.Max(0, CurrentIndex - 1));

    private void JumpTo(int index)
    {
        if (IsSpeaking) StartSpeaking(index);
        else { CurrentIndex = Math.Clamp(index, 0, Math.Max(0, _sentences.Count - 1)); SentenceChanged?.Invoke(CurrentIndex); }
    }

    public async void StartSpeaking(int? from = null)
    {
        if (_sentences.Count == 0) return;
        CancelCurrent();
        var gen = ++_generation;
        var cts = _cts = new CancellationTokenSource();
        SetSpeaking(true);

        var s = _repo.Settings;
        var engine = ActiveEngine;
        var voice = ActiveVoiceId;
        int failures = 0;

        for (int i = Math.Clamp(from ?? CurrentIndex, 0, _sentences.Count - 1); i < _sentences.Count; i++)
        {
            if (gen != _generation) return;
            CurrentIndex = i;
            SentenceChanged?.Invoke(i);

            // Sonraki cümleyi arka planda önceden getir (çevrim içi seslerde cümleler arası sıfır gecikme)
            if (i + 1 < _sentences.Count)
            {
                var nextText = _sentences[i + 1].Text;
                _ = engine.PreFetchAsync(nextText, voice, s.TtsSpeechRate, s.TtsPitch, cts.Token);
            }

            try
            {
                await engine.SpeakAsync(_sentences[i].Text, voice, s.TtsSpeechRate, s.TtsPitch, cts.Token);
                failures = 0;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                AppPaths.Log("tts speak: " + e);
                if (gen != _generation) return;
                if (++failures >= 3)
                {
                    SetSpeaking(false);
                    Error?.Invoke("Konuşma motoru ses üretemedi: " + e.Message + " Ayarlar bölümünden başka bir ses veya motor seçebilirsiniz.");
                    return;
                }
            }
        }
        if (gen != _generation) return;
        SetSpeaking(false);
        PageCompleted?.Invoke();
    }

    private void CancelCurrent()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    public void Stop()
    {
        _generation++;
        CancelCurrent();
        SetSpeaking(false);
    }

    private void SetSpeaking(bool v)
    {
        if (IsSpeaking == v) return;
        IsSpeaking = v;
        SpeakingChanged?.Invoke();
    }

    /// <summary>Sesi/motoru kısa bir cümleyle dener (ayarlar ekranı).</summary>
    public async Task PreviewAsync(string text, ITtsEngine engine, string? voiceId)
    {
        Stop();
        var s = _repo.Settings;
        try { await engine.SpeakAsync(text, voiceId, s.TtsSpeechRate, s.TtsPitch, CancellationToken.None); }
        catch (Exception e) { Error?.Invoke("Ses denenemedi: " + e.Message); }
    }

    public static IEnumerable<string> SplitIntoChunks(string text, int limit = 2500)
    {
        var chunk = new System.Text.StringBuilder();
        foreach (var sentence in Split(text))
        {
            var parts = sentence.Text.Length > limit
                ? Regex.Split(sentence.Text, @"(?<=[,;:\-])\s+|\s+").Where(p => p.Length > 0)
                : new[] { sentence.Text };
            foreach (var p in parts)
            {
                if (chunk.Length + p.Length + 1 > limit && chunk.Length > 0) { yield return chunk.ToString().Trim(); chunk.Clear(); }
                if (chunk.Length > 0) chunk.Append(' ');
                chunk.Append(p);
            }
        }
        if (chunk.Length > 0) yield return chunk.ToString().Trim();
    }

    /// <summary>Metni WAV dosyasına dönüştürür (Android'deki "sesi dışa aktar").</summary>
    public async Task<(bool ok, string? error)> ExportToWavAsync(string text, string outputPath, IProgress<int>? progress, CancellationToken ct)
    {
        var chunks = SplitIntoChunks(text).ToList();
        if (chunks.Count == 0) return (false, "Sentezlenecek metin bulunamadı.");
        var s = _repo.Settings;
        var engine = ActiveEngine;
        var voice = ActiveVoiceId;
        var temps = new List<string>();
        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var part = Path.Combine(AppPaths.TempDir, $"tts_part_{Guid.NewGuid():N}.wav");
                try
                {
                    await engine.SynthesizeToWavAsync(chunks[i], voice, s.TtsSpeechRate, s.TtsPitch, part, ct);
                    if (File.Exists(part) && new FileInfo(part).Length > 44) temps.Add(part);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { AppPaths.Log("export chunk: " + e); }
                progress?.Report((i + 1) * 100 / chunks.Count);
            }
            if (temps.Count == 0) return (false, "Seçili konuşma motoru ses verisi üretemedi.");
            await Task.Run(() => MergeWav(temps, outputPath), ct);
            return (true, null);
        }
        catch (OperationCanceledException) { TryDelete(outputPath); return (false, "İptal edildi."); }
        catch (Exception e) { AppPaths.Log("export: " + e); return (false, e.Message); }
        finally { foreach (var t in temps) TryDelete(t); }
    }

    private static void MergeWav(List<string> inputs, string output)
    {
        WaveFileWriter? writer = null;
        try
        {
            foreach (var f in inputs)
            {
                using var r = new WaveFileReader(f);
                writer ??= new WaveFileWriter(output, r.WaveFormat);
                if (!r.WaveFormat.Equals(writer.WaveFormat)) continue;
                var buf = new byte[65536];
                int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0) writer.Write(buf, 0, n);
            }
        }
        finally { writer?.Dispose(); }
    }

    private static void TryDelete(string p) { try { File.Delete(p); } catch { } }

    public void Dispose()
    {
        Stop();
        foreach (var e in _engines) e.Dispose();
    }
}
