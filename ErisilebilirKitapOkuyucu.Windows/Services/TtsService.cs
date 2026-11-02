using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace ErisilebilirKitapOkuyucu.Services;

public record Sentence(string Text, int Start, int Length);

/// <summary>Android'deki TtsManager'ın Windows karşılığı: cümle cümle okuma, sayfa sonu bildirimi, WAV dışa aktarma.</summary>
public sealed class TtsService : IDisposable
{
    private readonly BookRepository _repo;
    private readonly IReadOnlyList<ITtsEngine> _engines = TtsEngines.CreateAll();
    private readonly System.Windows.Threading.DispatcherTimer _sleep = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _sleepEndTicks;
    private List<Sentence> _sentences = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public bool IsSpeaking { get; private set; }
    public int CurrentIndex { get; private set; }
    public int SleepSecondsLeft { get; private set; }
    public IReadOnlyList<Sentence> Sentences => _sentences;

    public event Action? SpeakingChanged;
    public event Action<int>? SentenceChanged;
    public event Action? PageCompleted;
    public event Action<string>? Error;
    public event Action? SleepTimerFired;
    public event Action? SleepTimerTick;

    public TtsService(BookRepository repo)
    {
        _repo = repo;
        _sleep.Tick += (_, _) => SleepTick();
    }

    public void StartSleepTimer(int minutes)
    {
        _sleep.Stop();
        if (minutes <= 0) { SleepSecondsLeft = 0; SleepTimerTick?.Invoke(); return; }
        SleepSecondsLeft = minutes * 60;
        _sleepEndTicks = Environment.TickCount64 + SleepSecondsLeft * 1000L;
        _sleep.Start();
        SleepTimerTick?.Invoke();
    }

    private void SleepTick()
    {
        SleepSecondsLeft = (int)Math.Max(0, (_sleepEndTicks - Environment.TickCount64) / 1000);
        if (SleepSecondsLeft <= 0)
        {
            _sleep.Stop();
            Stop();
            SleepTimerFired?.Invoke();
        }
        SleepTimerTick?.Invoke();
    }

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
            // 1. Önce çift satır sonu ile ayrılmış paragrafları ara (\r\n\r\n veya \n\n)
            var matches = Regex.Matches(text, @"(?:^|\r?\n\s*\r?\n)([\s\S]*?)(?=(?:\r?\n\s*\r?\n)|$)");
            var rawList = new List<(string text, int start, int length)>();

            foreach (Match m in matches)
            {
                var g = m.Groups[1];
                var raw = g.Value;
                var trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;

                int leadingWs = 0;
                while (leadingWs < raw.Length && char.IsWhiteSpace(raw[leadingWs])) leadingWs++;
                int start = g.Index + leadingWs;

                rawList.Add((trimmed, start, trimmed.Length));
            }

            // 2. Eğer metinde çift satır sonu yoksa ve metin çok satırlıysa (tek satır sonlu PDF veya metinler):
            if (rawList.Count <= 1 && (text.Contains('\n') || text.Contains('\r')))
            {
                rawList.Clear();
                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var currentPara = new StringBuilder();
                int paraStart = -1;
                int currentSearchIdx = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.Trim();
                    int lineIdx = text.IndexOf(line, currentSearchIdx, StringComparison.Ordinal);
                    if (lineIdx < 0) lineIdx = currentSearchIdx;
                    currentSearchIdx = lineIdx + line.Length;

                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        if (currentPara.Length > 0)
                        {
                            var pStr = currentPara.ToString().Trim();
                            rawList.Add((pStr, paraStart, pStr.Length));
                            currentPara.Clear();
                            paraStart = -1;
                        }
                        continue;
                    }

                    if (paraStart < 0) paraStart = lineIdx;
                    if (currentPara.Length > 0) currentPara.Append(' ');
                    currentPara.Append(trimmed);

                    bool endsWithPunct = Regex.IsMatch(trimmed, @"[.!?…:][""'»”]?$");
                    bool nextIndented = (i + 1 < lines.Length) && (lines[i + 1].StartsWith("    ") || lines[i + 1].StartsWith("\t"));
                    bool nextEmpty = (i + 1 < lines.Length) && string.IsNullOrWhiteSpace(lines[i + 1]);

                    if ((endsWithPunct && (currentPara.Length >= 200 || nextIndented || nextEmpty)) || currentPara.Length >= 1200)
                    {
                        var pStr = currentPara.ToString().Trim();
                        rawList.Add((pStr, paraStart, pStr.Length));
                        currentPara.Clear();
                        paraStart = -1;
                    }
                }

                if (currentPara.Length > 0)
                {
                    var pStr = currentPara.ToString().Trim();
                    rawList.Add((pStr, paraStart, pStr.Length));
                }
            }

            foreach (var item in rawList)
            {
                result.Add(new Sentence(item.text, item.start, item.length));
            }

            return result.Count > 0 ? result : new List<Sentence> { new(text.Trim(), 0, text.Trim().Length) };
        }

        // Cümle Cümle Okuma Modu
        int posSentence = 0;
        var paraBlocks = Regex.Split(text, @"(?:\r?\n\s*\r?\n)+");
        foreach (var para in paraBlocks)
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            foreach (var part in Regex.Split(para, @"(?<=[.!?…])\s+"))
            {
                var clean = part.Trim();
                if (clean.Length == 0) continue;
                var idx = text.IndexOf(clean, posSentence, StringComparison.Ordinal);
                if (idx < 0) idx = text.IndexOf(clean, StringComparison.Ordinal);
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

            var textToSpeak = SanitizeForSpeech(_sentences[i].Text);
            if (string.IsNullOrWhiteSpace(textToSpeak))
            {
                // Yalnızca sembolden oluşan veya boş satırlarda kısa bir bekleme yapıp devam et
                try { await Task.Delay(200, cts.Token); } catch (OperationCanceledException) { return; }
                continue;
            }

            // Sonraki cümleyi arka planda önceden getir (çevrim içi seslerde cümleler arası sıfır gecikme)
            if (i + 1 < _sentences.Count)
            {
                var nextText = SanitizeForSpeech(_sentences[i + 1].Text);
                if (!string.IsNullOrWhiteSpace(nextText))
                    _ = engine.PreFetchAsync(nextText, voice, s.TtsSpeechRate, s.TtsPitch, cts.Token);
            }

            try
            {
                await engine.SpeakAsync(textToSpeak, voice, s.TtsSpeechRate, s.TtsPitch, s.TtsVolume, cts.Token);
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
        var textToSpeak = SanitizeForSpeech(text);
        if (string.IsNullOrWhiteSpace(textToSpeak)) textToSpeak = text;
        try { await engine.SpeakAsync(textToSpeak, voiceId, s.TtsSpeechRate, s.TtsPitch, s.TtsVolume, CancellationToken.None); }
        catch (Exception e) { Error?.Invoke("Ses denenemedi: " + e.Message); }
    }

    /// <summary>
    /// TTS motorlarının sembolleri (yıldız, alt çizgi, tırnak, parantez, kare vb.) kelime olarak
    /// ("yıldız", "parantez aç" vb.) okumasını engeller; nokta ve virgüle göre doğal duraklama sağlar.
    /// </summary>
    public static string SanitizeForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // 1. Markdown başlık imleri (# ## ###) temizle
        text = Regex.Replace(text, @"^[\s#]+", "");

        // 2. Madde imleri ve özel liste sembollerini temizle
        text = Regex.Replace(text, @"[•·◦▪▫►■◆◇★☆✓✔✕✖\u2022\u2023\u25E6\u2043\u2219]", " ");

        // 3. Yıldız (*), alt çizgi (_), diyez (#), tilda (~), şapka (^), dikey çizgi (|), ters bölü (\) temizle
        text = text.Replace("*", " ")
                   .Replace("#", " ")
                   .Replace("_", " ")
                   .Replace("~", " ")
                   .Replace("^", " ")
                   .Replace("|", ", ")
                   .Replace("\\", " ");

        // 4. Kesme işareti ile ayrılmış ekleri birleştir (örn: Türkiye'de -> Türkiyede, Ahmet'in -> Ahmetin, 1923'te -> 1923te)
        // Böylece TTS eki ayrı bir kelime gibi koparmadan veya yutmadan akıcı telaffuz eder
        text = Regex.Replace(text, @"(?<=[\p{L}\d])['’‘´`](?=[\p{L}\d])", "");

        // 5. Satır sonu hece bölmelerini birleştir (örn: anla- tıldı -> anlatıldı)
        text = Regex.Replace(text, @"(\p{L}+)-\s+(\p{L}+)", "$1$2");

        // 5b. Satır sonu karakterlerini (Enter / alt satıra geçiş) boşluğa çevir.
        // Böylece TTS motorları (özellikle Emel / Edge TTS, Fahrettin ve SAPI) her satır sonunda
        // yapay bir duraklama veya nefes alma yapmaz, cümle ve paragraf akıcı okunur.
        text = Regex.Replace(text, @"[\r\n]+", " ");

        // 6. Yüzde işareti (%50 -> yüzde 50)
        text = Regex.Replace(text, @"%(\s*\d+)", "yüzde $1");

        // 7. Yaygın Türkçe kısaltmaları doğal okunuşlarına aç
        text = Regex.Replace(text, @"\bvb\.", "ve benzeri");
        text = Regex.Replace(text, @"\bvs\.", "ve saire");
        text = Regex.Replace(text, @"\börn\.", "örneğin");
        text = Regex.Replace(text, @"\bbkz\.", "bakınız");
        text = Regex.Replace(text, @"\bsf\.\s*(?=\d)|\bs\.\s*(?=\d)", "sayfa ");
        text = Regex.Replace(text, @"\byy\.", "yüzyıl");
        text = Regex.Replace(text, @"\bdk\.", "dakika");
        text = Regex.Replace(text, @"\bsn\.", "saniye");
        text = Regex.Replace(text, @"\bDr\.", "Doktor");
        text = Regex.Replace(text, @"\bProf\.", "Profesör");
        text = Regex.Replace(text, @"\bDoç\.", "Doçent");
        text = Regex.Replace(text, @"\bAv\.", "Avukat");
        text = Regex.Replace(text, @"\bM\.Ö\.", "Milattan önce");
        text = Regex.Replace(text, @"\bM\.S\.", "Milattan sonra");
        text = Regex.Replace(text, @"\bHz\.", "Hazreti");

        // 8. Parantez ve köşeli ayraçlar: ( ) [ ] { } < > -> hafif duraklama (virgül) veya boşluk
        text = Regex.Replace(text, @"[\[\(\{<]+", ", ");
        text = Regex.Replace(text, @"[\]\)\}>]+", ", ");

        // 9. Kalan tırnak işaretleri: " “ ” ‘ ’ ` « »
        text = Regex.Replace(text, "[\"“”«»„‟'’‘´`]", " ");

        // 10. Konuşma çizgileri ve tireler: — – -- (diyalog başındaki veya arasındaki tireleri virgüle/boşluğa dönüştür)
        text = Regex.Replace(text, @"(?:^|\s)[—–\-]{1,3}\s*", ", ");
        text = Regex.Replace(text, @"\s*[—–]{1,3}\s*", ", ");

        // 11. Kelime arasındaki eğik çizgiler (ve/veya -> ve veya)
        text = Regex.Replace(text, @"(?<=\p{L})/(?=\p{L})", " ");

        // 12. Ardışık noktalar (... veya …), ardışık soru ve ünlem işaretleri (???, !!!, ?!)
        text = text.Replace("…", ".");
        text = Regex.Replace(text, @"\.{2,}", ".");
        text = Regex.Replace(text, @"\?{2,}", "?");
        text = Regex.Replace(text, @"!{2,}", "!");
        text = Regex.Replace(text, @"\?!|!\?", "?");

        // 9. Ardışık virgül ve noktalı işaretleri sadeleştir (örn: ", , " -> ", ")
        text = Regex.Replace(text, @"\s*,\s*,+", ", ");
        text = Regex.Replace(text, @"\s*,\s*\.", ".");
        text = Regex.Replace(text, @"\s*\.\s*,", ".");

        // 10. Fazla boşlukları temizle
        text = Regex.Replace(text, @"[ \t]+", " ").Trim();

        // Yalnızca virgül veya noktalama kalmışsa boşalt
        if (Regex.IsMatch(text, @"^[\s,.\-;:!?]+$")) return "";

        return text;
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
                var cleanChunk = SanitizeForSpeech(chunks[i]);
                if (string.IsNullOrWhiteSpace(cleanChunk)) continue;

                var part = Path.Combine(AppPaths.TempDir, $"tts_part_{Guid.NewGuid():N}.wav");
                try
                {
                    await engine.SynthesizeToWavAsync(cleanChunk, voice, s.TtsSpeechRate, s.TtsPitch, part, ct);
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
        _sleep.Stop();
        foreach (var e in _engines) e.Dispose();
    }
}
