using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ErisilebilirKitapOkuyucu.Pages;
using ErisilebilirKitapOkuyucu.Services;
using NAudio.Wave;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ErisilebilirKitapOkuyucu;

/// <summary>
/// "ErisilebilirKitapOkuyucu.exe --selftest rapor.txt" ile çalışır: gerçek servisleri ve arayüzü uçtan uca dener,
/// sonucu dosyaya yazar ve çıkar. Çıkış kodu 0 = tüm testler geçti.
/// </summary>
public static class SelfTest
{
    private static readonly StringBuilder Log = new();
    private static int _fail;

    private static void Pass(string name, string? detail = null) => Log.AppendLine($"[GEÇTİ] {name}" + (detail != null ? " — " + detail : ""));
    private static void Fail(string name, string detail) { _fail++; Log.AppendLine($"[KALDI] {name} — {detail}"); }
    private static void Check(string name, bool ok, string detail = "") { if (ok) Pass(name, detail.Length > 0 ? detail : null); else Fail(name, detail); }

    public static async Task<int> RunAsync(string reportPath, string? extraPdfDir)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "kitap_selftest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            await TestDocuments(dir, extraPdfDir);
            await TestRepository(dir);
            await TestTts(dir);
            await TestAudio(dir);
            TestShortcuts();
            await TestUi(dir);
            try { Directory.Delete(dir, true); } catch { }
        }
        catch (Exception e) { Fail("Beklenmeyen istisna", e.ToString()); }
        Log.AppendLine(_fail == 0 ? "\nSONUÇ: TÜM TESTLER GEÇTİ" : $"\nSONUÇ: {_fail} TEST KALDI");
        File.WriteAllText(reportPath, Log.ToString(), new UTF8Encoding(false));
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- belgeler
    private static async Task TestDocuments(string dir, string? extraPdfDir)
    {
        const string turkce = "Çalışkan öğrenci şiirleri güzelce okudu. İstanbul'da ığdır ĞÜŞİÖÇ harfleri doğru görünmeli. Bu ikinci cümledir! Üçüncü cümle de burada mı?";
        // TXT: UTF-8 ve Windows-1254
        var txt8 = Path.Combine(dir, "utf8.txt"); File.WriteAllText(txt8, turkce, new UTF8Encoding(false));
        var txt54 = Path.Combine(dir, "cp1254.txt"); File.WriteAllBytes(txt54, Encoding.GetEncoding(1254).GetBytes(turkce));
        foreach (var f in new[] { txt8, txt54 })
        {
            using var s = await DocumentSession.OpenAsync(new BookItem { UriString = f, Type = BookType.TXT });
            var t = await s.GetPageTextAsync(1);
            Check("TXT " + Path.GetFileName(f) + " Türkçe karakterler", t.Contains("Çalışkan öğrenci şiirleri") && t.Contains("ĞÜŞİÖÇ"), t[..Math.Min(40, t.Length)]);
        }

        // Çok sayfalı TXT + arama
        var big = Path.Combine(dir, "big.txt");
        File.WriteAllText(big, string.Join("\n\n", Enumerable.Range(1, 120).Select(i => $"Paragraf {i}. Bu bir deneme cümlesidir ve zeytin kelimesi {(i == 77 ? "sadece burada" : "")} geçer.")), new UTF8Encoding(false));
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = big, Type = BookType.TXT }))
        {
            Check("TXT sayfalama", s.TotalPages > 3, $"{s.TotalPages} sayfa");
            var r = await s.SearchAsync("sadece burada");
            Check("Belge içi arama", r.Count == 1 && r[0].Page >= 1, r.Count > 0 ? r[0].Display : "sonuç yok");
        }

        // DOCX
        var docx = Path.Combine(dir, "t.docx");
        using (var z = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("word/document.xml");
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                    "<w:p><w:r><w:t>Birinci paragraf şöyle başlar.</w:t></w:r></w:p><w:p><w:r><w:t>İkinci paragraf</w:t></w:r><w:r><w:tab/><w:t>devam eder.</w:t></w:r></w:p></w:body></w:document>");
        }
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = docx, Type = BookType.DOCX }))
        {
            var t = await s.GetPageTextAsync(1);
            Check("DOCX metin çıkarma", t.Contains("Birinci paragraf şöyle başlar.") && t.Contains("İkinci paragraf") && t.Contains("devam eder."), t.Replace("\n", "|"));
        }

        // DOC (Klasik Word 97-2003 / RTF uyumlu belge)
        var docFile = Path.Combine(dir, "eski_belge.doc");
        var rtfSample = "{\\rtf1\\ansi\\ansicpg1254\\deff0 {\\fonttbl{\\f0 Segoe UI;}}\\f0\\fs24 Eski Word belgesi birinci paragraf.\\par \\'ddkinci sat\\'fdr devam ediyor.\\par}";
        File.WriteAllBytes(docFile, Encoding.GetEncoding(1254).GetBytes(rtfSample));
        Check("ImportService DOC dosya tanıma", ImportService.DetectType(docFile) == BookType.DOCX, "DOC -> DOCX (Word)");
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = docFile, Type = BookType.DOCX }))
        {
            var t = await s.GetPageTextAsync(1);
            Check("DOC metin çıkarma", t.Contains("Eski Word belgesi birinci paragraf.") && t.Contains("İkinci satır devam ediyor."), t.Replace("\n", "|"));
        }

        // XLSX (Excel Çalışma Kitabı ve Tablo)
        var xlsxFile = Path.Combine(dir, "tablo.xlsx");
        using (var z = ZipFile.Open(xlsxFile, ZipArchiveMode.Create))
        {
            void AddZipEntry(string name, string content)
            {
                var ze = z.CreateEntry(name);
                using var sw = new StreamWriter(ze.Open(), new UTF8Encoding(false));
                sw.Write(content);
            }

            AddZipEntry("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Notlar\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddZipEntry("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            AddZipEntry("xl/sharedStrings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"4\" uniqueCount=\"4\"><si><t>Öğrenci</t></si><si><t>Ders</t></si><si><t>Ahmet Yılmaz</t></si><si><t>Matematik</t></si></sst>");
            AddZipEntry("xl/worksheets/sheet1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\" t=\"s\"><v>3</v></c></row></sheetData></worksheet>");
        }
        Check("ImportService XLSX dosya tanıma", ImportService.DetectType(xlsxFile) == BookType.XLSX, "XLSX -> XLSX (Excel)");
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = xlsxFile, Type = BookType.XLSX }))
        {
            var t = await s.GetPageTextAsync(1);
            Check("XLSX metin çıkarma", t.Contains("Çalışma Sayfası: Notlar") && t.Contains("Öğrenci | Ders") && t.Contains("Ahmet Yılmaz | Matematik"), t.Replace("\r\n", " # "));
        }

        // MD (Markdown)
        var mdFile = Path.Combine(dir, "notlar.md");
        File.WriteAllText(mdFile, "# Başlık\r\n\r\nMarkdown birinci paragraf.\r\n\r\nMarkdown ikinci paragraf.", new UTF8Encoding(false));
        Check("ImportService MD dosya tanıma", ImportService.DetectType(mdFile) == BookType.TXT, "MD -> TXT");
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = mdFile, Type = BookType.TXT }))
        {
            var t = await s.GetPageTextAsync(1);
            Check("MD metin çıkarma", t.Contains("Markdown birinci paragraf.") && t.Contains("Markdown ikinci paragraf."), t.Replace("\r\n", "|"));
        }

        // EPUB (spine sırası)
        var epub = Path.Combine(dir, "t.epub");
        using (var z = ZipFile.Open(epub, ZipArchiveMode.Create))
        {
            void Add(string n, string c) { var e = z.CreateEntry(n); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(c); }
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
            Add("OEBPS/content.opf", "<package><manifest><item id=\"b\" href=\"b.xhtml\"/><item id=\"a\" href=\"a.xhtml\"/></manifest><spine><itemref idref=\"a\"/><itemref idref=\"b\"/></spine></package>");
            Add("OEBPS/a.xhtml", "<html><head><title>x</title></head><body><h1>Bölüm Bir</h1><p>İlk bölümün metni.</p></body></html>");
            Add("OEBPS/b.xhtml", "<html><body><p>İkinci bölümün metni.</p><script>kotu()</script></body></html>");
        }
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = epub, Type = BookType.EPUB }))
        {
            var t = await s.GetPageTextAsync(1);
            var ok = t.IndexOf("Bölüm Bir", StringComparison.Ordinal) < t.IndexOf("İkinci bölümün", StringComparison.Ordinal) && !t.Contains("kotu");
            Check("EPUB spine sırası ve temizlik", ok, t.Replace("\n", "|"));
        }

        // PDF: üretilen dijital PDF
        var pdf = Path.Combine(dir, "t.pdf");
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 1; i <= 3; i++)
        {
            var p = b.AddPage(595, 842);
            p.AddText($"Page {i}: The quick brown fox jumps over the lazy dog near the river bank number {i}.", 12, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);
        }
        File.WriteAllBytes(pdf, b.Build());
        using (var s = await DocumentSession.OpenAsync(new BookItem { UriString = pdf, Type = BookType.PDF }))
        {
            var t2 = await s.GetPageTextAsync(2);
            Check("PDF sayfa sayısı ve metin", s.TotalPages == 3 && t2.Contains("Page 2") && t2.Contains("river bank number 2"), $"{s.TotalPages} sayfa; '{t2}'");
        }

        // PDF paragraf reflow ve tire birleştirme testi
        string rawWrap = "Bu bir deneme kitabının ilk paragrafıdır ve satırlar görsel olarak\r\n" +
                         "burada kesilmiştir. Bu cümle ile ilk paragraf biter.\r\n" +
                         "İkinci paragraf ise arka-\r\n" +
                         "daşlar kelimesini içermektedir ve devam eder.";
        string reflowed = TextExtractor.ReflowPdfText(rawWrap);
        var reflowParas = reflowed.Split(new[] { "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        bool reflowOk = reflowParas.Length == 2 &&
                        reflowParas[0].Contains("görsel olarak burada kesilmiştir.") &&
                        reflowParas[1].Contains("arkadaşlar kelimesini");
        Check("PDF Paragraf Algılama ve Satır Reflow", reflowOk, $"{reflowParas.Length} paragraf tespit edildi");

        // PDF Gölge / Çift Katman Metin Ayıklama (Shadow Text Deduplication)
        string shadowSample = "9. Sınıf9. Sınıf\r\nDers KitabıDers Kitabı\r\nBiyolojiBiyoloji\r\nTEMA TEMA Yaşam Bilimi\r\nGELİŞTİRİCİ VE İLETİŞİMGELİŞTİRİCİ VE İLETİŞİM";
        string cleanedShadow = TextExtractor.ReflowPdfText(shadowSample);
        bool shadowOk = cleanedShadow.Contains("9. Sınıf") && !cleanedShadow.Contains("9. Sınıf9. Sınıf") &&
                        cleanedShadow.Contains("Ders Kitabı") && !cleanedShadow.Contains("Ders KitabıDers Kitabı") &&
                        cleanedShadow.Contains("Biyoloji") && !cleanedShadow.Contains("BiyolojiBiyoloji") &&
                        cleanedShadow.Contains("GELİŞTİRİCİ VE İLETİŞİM") && !cleanedShadow.Contains("İLETİŞİMGELİŞTİRİCİ");
        Check("PDF Gölge ve Çift Katman Metin Ayıklama", shadowOk, "Tekrarlı gölge başlıklar temizlendi");

        // Gerçek PDF'ler (varsa)
        if (extraPdfDir != null && Directory.Exists(extraPdfDir))
        {
            foreach (var f in Directory.GetFiles(extraPdfDir, "*.pdf").Take(2))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var s = await DocumentSession.OpenAsync(new BookItem { UriString = f, Type = BookType.PDF });
                    var first = await s.GetPageTextAsync(Math.Min(5, s.TotalPages));
                    Check("Gerçek PDF: " + Path.GetFileName(f), s.TotalPages > 0 && first.Length > 0, $"{s.TotalPages} sayfa, ilk okunan {first.Length} karakter, {sw.ElapsedMilliseconds} ms, önizleme: {first[..Math.Min(70, first.Length)].Replace('\n', '|')}");
                }
                catch (Exception e) { Fail("Gerçek PDF: " + Path.GetFileName(f), e.Message); }
            }
        }

        // ZIP: ses ayrımları doğal sıralama
        var wavDir = Path.Combine(dir, "wavs"); Directory.CreateDirectory(wavDir);
        foreach (var n in new[] { "Ayrım 10", "Ayrım 2", "Ayrım 1" }) MakeWav(Path.Combine(wavDir, n + ".wav"), 1);
        var zip = Path.Combine(dir, "kitap.zip"); ZipFile.CreateFromDirectory(wavDir, zip);
        var repo = new BookRepository();
        var imp = new ImportService(repo);
        var res = await imp.AddFileAsync(zip);
        Check("ZIP sesli kitap içe aktarma", res.Book is { IsZipArchive: true } zb && zb.Chapters.Select(c => c.Title).SequenceEqual(new[] { "Ayrım 1", "Ayrım 2", "Ayrım 10" }),
            res.Book == null ? res.Message ?? "" : string.Join(", ", res.Book.Chapters.Select(c => c.Title)));
        if (res.Book != null) repo.RemoveBook(res.Book.Id);
    }

    // ---------------------------------------------------------------- depo
    private static Task TestRepository(string dir)
    {
        var repo = new BookRepository();
        var before = repo.Books.Count;
        var f = Path.Combine(dir, "utf8.txt");
        var book = new BookItem { Title = "Deneme", UriString = f, Type = BookType.TXT, TotalPages = 5 };
        repo.AddBook(book);
        repo.UpdateProgress(book.Id, 3);
        repo.CreateFolder("Okul"); repo.MoveBookToFolder(book.Id, "Okul");
        repo.AddBookmark(book.Id, new Bookmark { Title = "Sayfa 3", PageNumber = 3, Note = "not" });
        var again = new BookRepository(); // diskten yeniden yükle
        var b2 = again.GetBook(book.Id);
        Check("Depo kalıcılığı", b2 is { CurrentPage: 3, FolderName: "Okul" } && b2.Bookmarks.Count == 1 && again.Folders.Contains("Okul"));
        again.RenameFolder("Okul", "Okul2"); again.DeleteFolder("Okul2");
        Check("Klasör silinince kitap Ana Dizin'e taşınır", again.GetBook(book.Id)?.FolderName == BookItem.RootFolder);
        again.RemoveBook(book.Id);
        Check("Kitap silme", again.Books.Count == before && File.Exists(f), "orijinal dosya korundu");

        // Ayarları Dışa ve İçe Aktarma (Settings Export & Import)
        var settingsExportFile = Path.Combine(dir, "exported_settings.json");
        var customS = new AppSettings
        {
            TtsSpeechRate = 1.35f,
            TtsPitch = 0.9f,
            AppTheme = "Koyu",
            TtsReadMode = "Paragraf",
            CustomShortcuts = new Dictionary<string, string> { { "Reader.NextPage", "Ctrl+Alt+PageDown" } }
        };
        again.UpdateSettings(customS);
        again.ExportSettings(settingsExportFile);
        Check("Ayarları Dışa Aktarma (JSON)", File.Exists(settingsExportFile) && new FileInfo(settingsExportFile).Length > 50, $"{new FileInfo(settingsExportFile).Length} bayt");

        // Ayarları sıfırla, ardından içe aktar
        again.UpdateSettings(new AppSettings());
        bool importOk = again.ImportSettings(settingsExportFile);
        bool settingsMatch = importOk &&
                             Math.Abs(again.Settings.TtsSpeechRate - 1.35f) < 0.01 &&
                             again.Settings.AppTheme == "Koyu" &&
                             again.Settings.TtsReadMode == "Paragraf" &&
                             again.Settings.CustomShortcuts.ContainsKey("Reader.NextPage");
        Check("Ayarları İçe Aktarma ve Geri Yükleme", settingsMatch, "Tüm ayarlar ve kısayollar eksiksiz geri yüklendi");

        // Yer İmleri Dışa ve İçe Aktarma Testi (TXT Formatı)
        var bmBook = new BookItem { Title = "Test Romanı", UriString = f, Type = BookType.TXT, TotalPages = 20 };
        again.AddBook(bmBook);
        again.AddBookmark(bmBook.Id, new Bookmark { Title = "Bölüm 1 Giriş", PageNumber = 3, Note = "Burada önemli bir tanım var." });
        again.AddBookmark(bmBook.Id, new Bookmark { Title = "Doruk Noktası", PageNumber = 12, Note = "Kahramanın kararı." });
        var targetBook = again.GetBook(bmBook.Id)!;
        string exportedTxt = BookmarkService.ExportToText(targetBook);
        Check("Yer İmlerini TXT Olarak Dışa Aktarma",
            exportedTxt.Contains("ERİŞİLEBİLİR KİTAP OKUYUCU - YER İMLERİ VE NOTLAR") &&
            exportedTxt.Contains("Test Romanı") &&
            exportedTxt.Contains("Sayfa: 3") &&
            exportedTxt.Contains("Burada önemli bir tanım var.") &&
            exportedTxt.Contains("Sayfa: 12"),
            $"{exportedTxt.Length} karakter metin üretildi");

        var parsedBms = BookmarkService.ParseBookmarksFromText(exportedTxt, out string? detectedTitle);
        Check("Yer İmlerini TXT'den İçe Aktarma / Ayrıştırma",
            parsedBms.Count == 2 && detectedTitle == "Test Romanı" && parsedBms[0].PageNumber == 3 && parsedBms[1].PageNumber == 12,
            $"{parsedBms.Count} yer imi başarıyla ayrıştırıldı");

        // Toplu dışa aktarma ve içe aktarma testi
        string allExported = BookmarkService.ExportAllToText(new[] { targetBook });
        var allParsed = BookmarkService.ParseBookmarksFromText(allExported, out string? _);
        Check("Toplu Yer İmleri Dışa/İçe Aktarma",
            allParsed.Count == 2 && allExported.Contains("TOPLU YER"),
            $"{allParsed.Count} yer imi toplu yedekten başarıyla ayrıştırıldı");

        // Kitaba içe aktarma testi (mükerrer koruması)
        int reimported = BookmarkService.ImportToBook(again, targetBook.Id, allParsed);
        Check("Mükerrer Yer İmi Engelleme",
            reimported == 0 && targetBook.Bookmarks.Count == 2,
            "Mükerrer yer imleri başarıyla elendi");

        again.RemoveBook(bmBook.Id);

        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- TTS
    private static async Task TestTts(string dir)
    {
        var repo = App.Repo;
        var tts = App.Tts;
        foreach (var engine in tts.Engines)
        {
            var voices = engine.GetVoices();
            Log.AppendLine($"  Motor {engine.Id}: {voices.Count} ses => " + string.Join("; ", voices.Take(8).Select(v => v.Display)));
            if (voices.Count == 0) { Log.AppendLine($"  (uyarı) {engine.DisplayName} motorunda ses yok, atlanıyor"); continue; }
            var voice = (voices.FirstOrDefault(v => v.IsTurkish) ?? voices[0]).Id;
            var wav = Path.Combine(dir, $"tts_{engine.Id}.wav");
            try
            {
                await engine.SynthesizeToWavAsync("Merhaba dünya. Bu bir ses denemesidir.", voice, 1.0f, 1.0f, wav, CancellationToken.None);
                var len = File.Exists(wav) ? new FileInfo(wav).Length : 0;
                double secs = 0;
                try { using var r = new WaveFileReader(wav); secs = r.TotalTime.TotalSeconds; } catch { }
                Check($"{engine.DisplayName}: WAV sentezi", len > 4000 && secs > 0.5, $"{len} bayt, {secs:0.0} sn");
            }
            catch (Exception e) { Fail($"{engine.DisplayName}: WAV sentezi", e.Message); }

            if (engine.Id == "sherpa")
            {
                var azVoice = voices.FirstOrDefault(v => v.Language.StartsWith("az", StringComparison.OrdinalIgnoreCase))?.Id;
                if (azVoice != null)
                {
                    var azWav = Path.Combine(dir, "tts_sherpa_az.wav");
                    try
                    {
                        await engine.SynthesizeToWavAsync("Salam dünya. Azərbaycan dilində kitabları oxuyuruq.", azVoice, 1.0f, 1.0f, azWav, CancellationToken.None);
                        var azLen = File.Exists(azWav) ? new FileInfo(azWav).Length : 0;
                        Check("Meta MMS Azerbaycan Sesi (Çevrim Dışı): WAV sentezi", azLen > 4000, $"{azLen} bayt");
                    }
                    catch (Exception e) { Fail("Meta MMS Azerbaycan Sesi (Çevrim Dışı): WAV sentezi", e.Message); }
                }
            }

            if (engine.Id == "edge")
            {
                var azVoice = voices.FirstOrDefault(v => v.Language.StartsWith("az", StringComparison.OrdinalIgnoreCase))?.Id;
                if (azVoice != null)
                {
                    var azWav = Path.Combine(dir, "tts_edge_az.wav");
                    try
                    {
                        await engine.SynthesizeToWavAsync("Salam dünya. Azərbaycan dilində kitabları oxuyuruq.", azVoice, 1.0f, 1.0f, azWav, CancellationToken.None);
                        var azLen = File.Exists(azWav) ? new FileInfo(azWav).Length : 0;
                        Check("Microsoft Edge TTS (Azerbaycan Sesi): WAV sentezi", azLen > 4000, $"{azLen} bayt");
                    }
                    catch (Exception e) { Fail("Microsoft Edge TTS (Azerbaycan Sesi): WAV sentezi", e.Message); }
                }
            }

            // Canlı konuşma + hız/perde parametreleri
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await engine.SpeakAsync("Deneme.", voice, 2.0f, 1.2f, cts.Token);
                Check($"{engine.DisplayName}: canlı konuşma", sw.ElapsedMilliseconds > 100, $"{sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e) { Fail($"{engine.DisplayName}: canlı konuşma", e.Message); }

            // Konuşmayı iptal edebilme
            try
            {
                using var cts = new CancellationTokenSource();
                var task = engine.SpeakAsync(string.Join(" ", Enumerable.Repeat("Bu uzun bir cümledir ve iptal edilecektir.", 12)), voice, 1.0f, 1.0f, cts.Token);
                await Task.Delay(700); cts.Cancel();
                var done = await Task.WhenAny(task, Task.Delay(5000));
                var cancelled = done == task && (task.IsCanceled || task.IsFaulted || task.IsCompleted);
                Check($"{engine.DisplayName}: iptal", cancelled, task.Status.ToString());
            }
            catch (Exception e) { Fail($"{engine.DisplayName}: iptal", e.Message); }
        }
        Check("Aktif motor ve ses çözümlemesi", tts.ActiveEngine != null && tts.ActiveVoiceId != null, $"{tts.ActiveEngine?.Id} / {tts.ActiveVoiceId}");

        // Cümle bölme ve konumlar
        var text = "Birinci cümle burada. İkinci cümle nerede? Üçüncü!\n\nYeni paragraf başladı.";
        var s = TtsService.Split(text);
        Check("Cümle bölme ve vurgulama konumları", s.Count == 4 && s.All(x => text.Substring(x.Start, x.Length) == x.Text), string.Join(" | ", s.Select(x => x.Text)));

        // Paragraf bölme ve konumları
        var pText = "Birinci paragraf ilk cümle. Birinci paragraf ikinci cümle.\r\n\r\nİkinci paragraf tek cümle.\r\n\r\nÜçüncü paragraf son cümle.";
        var pSplits = TtsService.Split(pText, true);
        Check("Paragraf bölme ve vurgulama konumları", pSplits.Count == 3 && pSplits.All(x => pText.Substring(x.Start, x.Length) == x.Text), $"Paragraf sayısı: {pSplits.Count} => " + string.Join(" || ", pSplits.Select(x => x.Text)));

        // Boşluklu satırlarda paragraf ve cümle bölme
        var pTextWithSpaces = "Birinci paragraf ilk cümle.\r\n \r\nİkinci paragraf tek cümle.\r\n \r\nÜçüncü paragraf son cümle.";
        var pSplitsSpaces = TtsService.Split(pTextWithSpaces, true);
        Check("Boşluklu satırlarda paragraf bölme", pSplitsSpaces.Count == 3 && pSplitsSpaces.All(x => pTextWithSpaces.Substring(x.Start, x.Length) == x.Text), $"Paragraf sayısı: {pSplitsSpaces.Count}");
        var sSplitsSpaces = TtsService.Split(pTextWithSpaces, false);
        Check("Boşluklu satırlarda cümle bölme", sSplitsSpaces.Count == 3 && sSplitsSpaces.All(x => pTextWithSpaces.Substring(x.Start, x.Length) == x.Text), $"Cümle sayısı: {sSplitsSpaces.Count}");

        // TtsService döngüsü: cümle olayları + sayfa sonu
        var prevMode = repo.Settings.TtsReadMode;
        repo.Settings.TtsReadMode = "Cümle";
        try
        {
            var events = new List<int>(); var completed = false;
            void OnS(int i) => events.Add(i); void OnC() => completed = true;
            tts.SentenceChanged += OnS; tts.PageCompleted += OnC;
            tts.LoadPageText("Bir. İki. Üç.");
            tts.StartSpeaking(0);
            for (int i = 0; i < 300 && !completed; i++) await Task.Delay(100);
            tts.SentenceChanged -= OnS; tts.PageCompleted -= OnC;
            Check("TtsService cümle cümle okuma ve sayfa sonu", completed && events.SequenceEqual(new[] { 0, 1, 2 }), $"olaylar: {string.Join(",", events)} tamamlandı={completed}");
        }
        finally
        {
            repo.Settings.TtsReadMode = prevMode;
        }

        // Durdurma
        tts.LoadPageText(string.Join(" ", Enumerable.Repeat("Uzun bir cümle okunuyor.", 30)));
        tts.StartSpeaking(0); await Task.Delay(600); tts.Stop(); await Task.Delay(300);
        Check("TtsService durdurma", !tts.IsSpeaking);

        // TTS Ses düzeyi (TtsVolume) ayarı ve kalıcılığı
        var initialVol = repo.Settings.TtsVolume;
        repo.Settings.TtsVolume = 0.75f;
        repo.UpdateSettings(repo.Settings);
        var reloadedSettings = new BookRepository().Settings;
        Check("TTS Ses düzeyi ayarı kalıcılığı", Math.Abs(reloadedSettings.TtsVolume - 0.75f) < 0.01f, $"Kaydedilen: {reloadedSettings.TtsVolume:0.00}");
        repo.Settings.TtsVolume = initialVol;
        repo.UpdateSettings(repo.Settings);

        // TTS Noktalama ve Sembol Filtreleme (SanitizeForSpeech)
        var dirty = "* Bu bir yıldızlı **önemli** nottur! (Açıklama) Ali/Veli — Bekliyorum...";
        var clean = TtsService.SanitizeForSpeech(dirty);
        Check("TTS sembol ve noktalama temizleme", !clean.Contains("*") && !clean.Contains("(") && !clean.Contains("—") && clean.Contains("Bu bir yıldızlı"), clean);
        Check("Yalnızca sembolden oluşan satır boşaltılır", TtsService.SanitizeForSpeech("*** --- ...") == "", "başarılı");
        Check("Kesme işareti ek birleştirme ve kısaltmalar", TtsService.SanitizeForSpeech("Türkiye'de Dr. Ahmet %50 artış ve vb. durumlar").Contains("Türkiyede Doktor Ahmet yüzde 50 artış ve ve benzeri"), TtsService.SanitizeForSpeech("Türkiye'de Dr. Ahmet %50 artış ve vb. durumlar"));
        Check("Alt satır duraklamasını önleme (satır sonları boşluğa dönüşür)", TtsService.SanitizeForSpeech("Bu birinci satır\r\nbu da ikinci satır") == "Bu birinci satır bu da ikinci satır", TtsService.SanitizeForSpeech("Bu birinci satır\r\nbu da ikinci satır"));

        // TTS Uyku Zamanlayıcısı testi
        tts.StartSleepTimer(15);
        Check("TTS Uyku Zamanlayıcısı başlatma", tts.SleepSecondsLeft > 800 && tts.SleepSecondsLeft <= 900, $"{tts.SleepSecondsLeft} sn kaldı");
        tts.StartSleepTimer(0);
        Check("TTS Uyku Zamanlayıcısı kapatma", tts.SleepSecondsLeft == 0, "başarıyla sıfırlandı");

        // Dışa aktarma
        var outWav = Path.Combine(dir, "export.wav");
        var prog = new List<int>();
        var (ok, err) = await tts.ExportToWavAsync(string.Join("\n\n", Enumerable.Repeat("Dışa aktarma deneme cümlesi, Türkçe karakterlerle: çğıöşü.", 6)), outWav, new Progress<int>(prog.Add), CancellationToken.None);
        double dur = 0; try { using var r = new WaveFileReader(outWav); dur = r.TotalTime.TotalSeconds; } catch { }
        Check("Sesi dosyaya aktarma", ok && dur > 2, ok ? $"{dur:0.0} sn" : err ?? "");
        Check("Sayfa girişi ayrıştırma", ExportDialog.ParsePages("50+400", 3, 500).SequenceEqual(new[] { 50, 400 }) && ExportDialog.ParsePages("", 7, 10).SequenceEqual(new[] { 7 }) && ExportDialog.ParsePages("1+2+3+4", 1, 10).Count == 3);
    }

    // ---------------------------------------------------------------- ses oynatıcı
    private static void MakeWav(string path, int seconds)
    {
        using var w = new WaveFileWriter(path, new WaveFormat(16000, 16, 1));
        var buf = new byte[16000 * 2];
        for (int i = 0; i < seconds; i++) { for (int k = 0; k < 16000; k++) { var v = (short)(Math.Sin(k * 2 * Math.PI * 440 / 16000) * 3000); buf[k * 2] = (byte)v; buf[k * 2 + 1] = (byte)(v >> 8); } w.Write(buf, 0, buf.Length); }
    }

    private static async Task TestAudio(string dir)
    {
        var wav = Path.Combine(dir, "tone.wav"); MakeWav(wav, 8);
        var a = App.Audio;
        long dur = 0; a.DurationKnown += d => dur = d;
        string? err = null; a.Error += e => err = e;
        a.Play(wav, 0, "Test");
        for (int i = 0; i < 50 && dur == 0 && err == null; i++) await Task.Delay(100);
        Check("Ses dosyası açıldı ve süre okundu", dur > 7000 && dur < 9000, $"{dur} ms {err}");
        await Task.Delay(1500);
        Check("Oynatma konumu ilerliyor", a.IsPlaying && a.PositionMs > 500, $"{a.PositionMs} ms");
        a.SeekBy(3); await Task.Delay(700);
        Check("İleri sarma", a.PositionMs > 3000, $"{a.PositionMs} ms");
        a.SetSpeed(2.0); await Task.Delay(1200);
        Check("Hız ayarı", Math.Abs(a.Speed - 2.0) < 0.01);
        Check("Evrensel hız biçimlendirmesi", Fmt.Speed(1.0) == "1x" && Fmt.Speed(1.5) == "1.5x" && Fmt.Speed(2.0) == "2x" && Fmt.Speed(1.25) == "1.25x", "1x, 1.5x, 2x, 1.25x");
        a.TogglePlayPause(); var p1 = a.PositionMs; await Task.Delay(700);
        Check("Duraklatma", !a.IsPlaying && Math.Abs(a.PositionMs - p1) < 300);
        a.StartSleepTimer(1);
        Check("Uyku zamanlayıcısı kuruldu", a.SleepSecondsLeft is > 50 and <= 60);
        a.StartSleepTimer(0); a.Stop();
    }

    // ---------------------------------------------------------------- kısayollar
    private static void TestShortcuts()
    {
        // 1. Tanımlı kısayolların varlığı ve kategoriler
        var all = ShortcutService.Definitions;
        Check("Klavye kısayolları tanımları yüklendi", all.Count >= 35, $"{all.Count} kısayol tanımlı");
        
        var catReader = all.Where(d => d.Category == ShortcutService.CatReader).ToList();
        var catAudio = all.Where(d => d.Category == ShortcutService.CatAudio).ToList();
        var catGeneral = all.Where(d => d.Category == ShortcutService.CatGeneral).ToList();
        Check("Kategorilendirilmiş kısayol listeleri", catReader.Count >= 20 && catAudio.Count >= 12 && catGeneral.Count >= 3,
            $"Okuma: {catReader.Count}, Sesli: {catAudio.Count}, Genel: {catGeneral.Count}");

        // 2. Formatlama
        var strF1 = ShortcutService.FormatCombo(Key.F1, ModifierKeys.None);
        var strCtrlT = ShortcutService.FormatCombo(Key.T, ModifierKeys.Control);
        var strCtrlShiftS = ShortcutService.FormatCombo(Key.S, ModifierKeys.Control | ModifierKeys.Shift);
        Check("Kısayol tuş kombinasyonu metin biçimlendirmesi", strF1 == "F1" && strCtrlT.Contains("Ctrl") && strCtrlT.Contains("T") && strCtrlShiftS.Contains("Ctrl") && strCtrlShiftS.Contains("Shift") && strCtrlShiftS.Contains("S"),
            $"{strF1}, {strCtrlT}, {strCtrlShiftS}");

        // 3. Özelleştirme ve Kaydetme
        var origSleepKey = ShortcutService.GetCurrent("Reader.SleepTimer");
        Check("Uyku modu varsayılan kısayol kontrolü", origSleepKey.Key == Key.T && origSleepKey.Modifiers == ModifierKeys.Control);

        ShortcutService.SaveShortcut("Reader.SleepTimer", Key.U, ModifierKeys.Control | ModifierKeys.Alt);
        var newSleepKey = ShortcutService.GetCurrent("Reader.SleepTimer");
        Check("Kısayol özelleştirme ve anlık yansıma", newSleepKey.Key == Key.U && newSleepKey.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt));
        Check("Özelleştirilmiş durum kontrolü", ShortcutService.IsCustomized("Reader.SleepTimer"));

        // 4. Çakışma denetimi
        var conflictName = ShortcutService.FindConflict("Reader.TogglePlay", Key.U, ModifierKeys.Control | ModifierKeys.Alt);
        Check("Kısayol çakışma tespiti", conflictName != null, conflictName ?? "Yok");

        // 5. Sıfırlama
        ShortcutService.ResetShortcut("Reader.SleepTimer");
        var resetSleepKey = ShortcutService.GetCurrent("Reader.SleepTimer");
        Check("Tekil kısayolu varsayılana sıfırlama", resetSleepKey.Key == Key.T && resetSleepKey.Modifiers == ModifierKeys.Control && !ShortcutService.IsCustomized("Reader.SleepTimer"));

        // 6. Tümünü sıfırlama
        ShortcutService.SaveShortcut("General.Help", Key.H, ModifierKeys.Control);
        ShortcutService.ResetAll();
        Check("Tüm kısayolları sıfırlama", !ShortcutService.IsCustomized("General.Help"));
    }

    // ---------------------------------------------------------------- arayüz
    private static async Task TestUi(string dir)
    {
        var win = Application.Current.MainWindow!;
        await Task.Delay(500);
        var txt = Path.Combine(dir, "ui.txt");
        File.WriteAllText(txt, "Ekran okuyucu denemesi birinci satır.\nİkinci satır burada yer alıyor.\n\nÜçüncü satır ile bitirelim.", new UTF8Encoding(false));
        var wav = Path.Combine(dir, "ui.wav"); MakeWav(wav, 5);
        var tb = new BookItem { Title = "UI Metin Kitabı", UriString = txt, Type = BookType.TXT, TotalPages = 1 };
        var ab = new BookItem { Title = "UI Sesli Kitap", UriString = wav, Type = BookType.AUDIO };
        App.Repo.AddBook(tb); App.Repo.AddBook(ab);
        var zipBook = new BookItem
        {
            Title = "UI ZIP Kitap", UriString = wav, Type = BookType.AUDIO, IsZipArchive = true,
            Chapters = new() { new ChapterItem { Title = "Ayrım 1", UriString = wav }, new ChapterItem { Title = "Ayrım 2", UriString = wav } }
        };
        App.Repo.AddBook(zipBook);

        var mw = MainWindow.Instance;
        mw.ShowLibrary(); await Task.Delay(600);
        AuditPage("Kitaplık", mw);
        mw.ShowSettings(); await Task.Delay(600);
        AuditPage("Ayarlar", mw);
        mw.OpenBook(tb); await Task.Delay(1500);
        AuditPage("Belge okuyucu", mw);
        var reader = FindPage<ReaderPage>(mw);
        var textBox = Descendants<TextBox>(reader!).FirstOrDefault(t => t.IsReadOnly);
        Check("Okuyucu standart metin alanına sahip", textBox != null && textBox.Text.Contains("Ekran okuyucu denemesi"), textBox?.Text ?? "");
        var focused = Keyboard.FocusedElement as TextBox;
        Check("Açılışta odak metin alanında (imleç etkin)", focused != null && focused.IsReadOnlyCaretVisible);
        App.Tts.StartSpeaking(0);
        int seenSelection = 0;
        string lastSel = "";
        for (int w = 0; w < 100; w++)
        {
            await Task.Delay(100);
            if (textBox != null && textBox.SelectionLength > 0)
            {
                seenSelection = textBox.SelectionLength;
                lastSel = textBox.SelectedText;
                break;
            }
        }
        Check("Okuma sırasında cümle seçimi ilerliyor", seenSelection > 0, $"seçim: {lastSel}");
        App.Tts.Stop();
        mw.OpenBook(ab); await Task.Delay(1500);
        AuditPage("Sesli kitap oynatıcı", mw);
        App.Audio.Stop();
        mw.OpenBook(zipBook); await Task.Delay(1500);
        AuditPage("ZIP sesli kitap oynatıcı", mw);
        App.Audio.Stop();
        mw.ShowLibrary(); await Task.Delay(300);
        App.Repo.RemoveBook(tb.Id); App.Repo.RemoveBook(ab.Id); App.Repo.RemoveBook(zipBook.Id);
    }

    private static T? FindPage<T>(MainWindow w) where T : class => (w.Content as Grid)?.Children.OfType<ContentControl>().FirstOrDefault()?.Content as T;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    /// <summary>Etkileşimli her öğenin ekran okuyucuya bir ad bildirdiğini ve klavyeyle odaklanabildiğini doğrular.</summary>
    private static void AuditPage(string name, MainWindow w)
    {
        var page = (w.Content as Grid)?.Children.OfType<ContentControl>().FirstOrDefault()?.Content as DependencyObject;
        if (page == null) { Fail("Sayfa denetimi: " + name, "sayfa bulunamadı"); return; }
        int count = 0; var bad = new List<string>();
        foreach (var el in Descendants<Control>(page))
        {
            if (el is not (Button or Slider or TextBox or CheckBox or RadioButton or ListBox or ListBoxItem)) continue;
            // Metin okuyucu alanının (IsReadOnly TextBox) adı olmaz, çünkü ekran okuyucu her satırda bu adı tekrarlar.
            if (el is TextBox { IsReadOnly: true }) continue;
            if (el is ListBoxItem or { Visibility: not Visibility.Visible }) { if (el is ListBoxItem lbi) { count++; var pn = UIElementAutomationPeer.CreatePeerForElement(lbi)?.GetName(); if (string.IsNullOrWhiteSpace(pn)) bad.Add("ListBoxItem adsız"); } continue; }
            count++;
            var peer = UIElementAutomationPeer.CreatePeerForElement(el);
            var n = peer?.GetName();
            if (string.IsNullOrWhiteSpace(n)) bad.Add($"{el.GetType().Name} adsız");
            if (!el.Focusable) bad.Add($"{el.GetType().Name} '{n}' klavyeyle odaklanamıyor");
        }
        var headings = Descendants<TextBlock>(page).Count(t => System.Windows.Automation.AutomationProperties.GetHeadingLevel(t) != System.Windows.Automation.AutomationHeadingLevel.None);
        if (headings == 0) bad.Add("başlık (heading) yok");
        Check($"Erişilebilirlik denetimi: {name}", bad.Count == 0, $"{count} etkileşimli öğe, {headings} başlık" + (bad.Count > 0 ? "; sorunlar: " + string.Join(", ", bad.Distinct()) : ""));
    }
}
