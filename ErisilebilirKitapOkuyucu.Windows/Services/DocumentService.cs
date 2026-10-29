using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HtmlAgilityPack;
using UglyToad.PdfPig;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>
/// Bir belgenin sayfa sayfa metnini sağlar (Android'deki DocumentParser karşılığı).
/// PDF: PdfPig ile dijital metin, metin yoksa Windows OCR; TXT/EPUB/DOCX: 1500 karakterlik sayfalara bölünür.
/// </summary>
public sealed class DocumentSession : IDisposable
{
    /// <summary>OCR (Windows.Data.Pdf + Direct2D) kullanıldıysa true; bazı ekran kartı sürücüleri çıkışta çöktüğü için App.OnExit'te kullanılır.</summary>
    public static bool OcrUsed { get; private set; }

    private readonly BookItem _book;
    private readonly string _path;
    private PdfDocument? _pdf;
    private List<string>? _pages;
    private readonly Dictionary<int, string> _pdfCache = new();
    public int TotalPages { get; private set; } = 1;

    private DocumentSession(BookItem book, string path) { _book = book; _path = path; }

    public static async Task<DocumentSession> OpenAsync(BookItem book, string? pathOverride = null, BookType? typeOverride = null)
    {
        var path = pathOverride ?? book.UriString;
        var s = new DocumentSession(book, path);
        await Task.Run(() => s.Load(typeOverride ?? book.Type));
        return s;
    }

    private void Load(BookType type)
    {
        if (!File.Exists(_path)) { _pages = new() { "Dosya bulunamadı: " + _path }; TotalPages = 1; return; }
        switch (type)
        {
            case BookType.PDF:
                try { _pdf = PdfDocument.Open(_path); TotalPages = Math.Max(1, _pdf.NumberOfPages); }
                catch (Exception e)
                {
                    AppPaths.Log("pdf open: " + e);
                    _pages = new() { "PDF dosyası açılamadı: " + e.Message };
                    TotalPages = 1;
                }
                break;
            case BookType.EPUB: _pages = Paginate(Safe(() => TextExtractor.Epub(_path), "EPUB kitabı okunurken bir hata oluştu")); break;
            case BookType.DOCX: _pages = Paginate(Safe(() => TextExtractor.Docx(_path), "Word belgesi (DOCX) okunurken bir hata oluştu")); break;
            default: _pages = Paginate(TextExtractor.ReflowPdfText(TextExtractor.ReadTextFile(_path))); break;
        }
        if (_pages != null) TotalPages = Math.Max(1, _pages.Count);
    }

    private static string Safe(Func<string> f, string err)
    {
        try { return f(); } catch (Exception e) { AppPaths.Log(err + ": " + e); return $"{err}: {e.Message}"; }
    }

    public async Task<string> GetPageTextAsync(int page)
    {
        page = Math.Clamp(page, 1, TotalPages);
        if (_pages != null) return _pages[page - 1];
        if (_pdf == null) return "";
        if (_pdfCache.TryGetValue(page, out var cached)) return cached;

        var text = await Task.Run(() =>
        {
            try { return TextExtractor.ReflowPdfText(PdfPageText(page)); }
            catch (Exception e) { AppPaths.Log("pdf page: " + e); return ""; }
        });

        if (text.Length <= 50 || text.Count(char.IsLetter) <= 20)
        {
            // Taranmış sayfa: Windows OCR
            string ocr = "";
            try { ocr = await OcrPdfPageAsync(page); } catch (Exception e) { AppPaths.Log("ocr: " + e); }
            if (!string.IsNullOrWhiteSpace(ocr)) text = TextExtractor.ReflowPdfText(ocr);
        }
        if (string.IsNullOrWhiteSpace(text)) text = "Bu sayfada okunabilir metin veya yazı bulunamadı.";
        if (_pdfCache.Count > 12) _pdfCache.Clear();
        _pdfCache[page] = text;
        return text;
    }

    private string PdfPageText(int page)
    {
        var p = _pdf!.GetPage(page);
        try
        {
            return UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p);
        }
        catch { return p.Text; }
    }

    private async Task<string> OcrPdfPageAsync(int page)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("tr"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return "";
        OcrUsed = true;
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(_path));
        var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        if (page < 1 || page > doc.PageCount) return "";
        using var pdfPage = doc.GetPage((uint)(page - 1));
        using var stream = new InMemoryRandomAccessStream();
        var opts = new Windows.Data.Pdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Min(4000, Math.Max(1, pdfPage.Size.Width * 2.0)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
        };
        await pdfPage.RenderToStreamAsync(stream, opts);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bmp);
        return TextExtractor.Clean(result.Text);
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults = 50, CancellationToken ct = default)
    {
        var results = new List<SearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;
        var q = query.Trim().ToLower(System.Globalization.CultureInfo.CurrentCulture);
        await Task.Run(() =>
        {
            for (int p = 1; p <= TotalPages && results.Count < maxResults; p++)
            {
                if (ct.IsCancellationRequested) break;
                string text;
                try { text = _pages != null ? _pages[p - 1] : TextExtractor.ReflowPdfText(PdfPageText(p)); } catch { continue; }
                var lines = text.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Length > 0 && line.ToLower(System.Globalization.CultureInfo.CurrentCulture).Contains(q))
                        results.Add(new SearchResult(p, i, line.Length > 160 ? line[..160] + "…" : line));
                }
            }
        }, ct);
        return results;
    }

    public static List<string> Paginate(string full)
    {
        var pages = new List<string>();
        if (string.IsNullOrWhiteSpace(full)) { pages.Add("Dosya boş veya metin içermiyor."); return pages; }
        const int pageSize = 1500;
        int start = 0;
        while (start < full.Length)
        {
            int end = Math.Min(start + pageSize, full.Length);
            if (end < full.Length)
            {
                int nl = full.IndexOf("\n\n", Math.Max(start, end - 200), StringComparison.Ordinal);
                if (nl != -1 && nl < end + 300) end = nl + 2;
                else
                {
                    int dot = full.IndexOf(". ", Math.Max(start, end - 150), StringComparison.Ordinal);
                    if (dot != -1 && dot < end + 200) end = dot + 2;
                }
            }
            end = Math.Min(end, full.Length);
            var content = TextExtractor.Clean(full[start..end]);
            if (content.Length > 0) pages.Add(content);
            start = end;
        }
        if (pages.Count == 0) pages.Add("Boş metin belgesi.");
        return pages;
    }

    public void Dispose() { _pdf?.Dispose(); _pdf = null; }
}

/// <summary>Biçim bağımsız düz metin çıkarma yardımcıları.</summary>
public static class TextExtractor
{
    public static string Clean(string text) =>
        Regex.Replace(Regex.Replace(Regex.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "[ \\t]+", " "), " ?\\n ?", "\n"), "\\n{3,}", "\n\n").Trim();

    /// <summary>
    /// PDF ve OCR gibi görsel satır sonu içeren metinleri doğal paragraflara dönüştürür.
    /// Satır içi kesmeleri birleştirir, kelime sonu tirelerini (- ) bağlar,
    /// gerçek paragraf bitişlerine çift satır sonu (\r\n\r\n) ekler.
    /// </summary>
    public static string ReflowPdfText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";

        var rawLines = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var filtered = new List<string>();

        foreach (var l in rawLines)
        {
            var trimmed = l.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                filtered.Add("");
                continue;
            }

            // Sayfa üst/alt bilgi ve ardışık nokta içeren satır numaralarını (koşu başlıkları) temizle
            if (Regex.IsMatch(trimmed, @"\.{4,}") && (Regex.IsMatch(trimmed, @"\d+") || Regex.IsMatch(trimmed, @"(?i)tezg[aâ]h|bask[ıi]|sayfa")))
                continue;
            if (Regex.IsMatch(trimmed, @"^[-—]?\s*\d+\s*[-—]?$"))
                continue;

            filtered.Add(l.TrimEnd());
        }

        var lines = new List<string>();
        for (int i = 0; i < filtered.Count; i++)
        {
            var line = filtered[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                lines.Add("");
                continue;
            }

            var trimmed = line.Trim();
            // Satır sonundaki kelime bölme tirelerini güvenle birleştir
            var m = Regex.Match(trimmed, @"(\p{L}+)-\s*$");
            if (m.Success && i + 1 < filtered.Count && !string.IsNullOrWhiteSpace(filtered[i + 1]))
            {
                var nextLine = filtered[i + 1].TrimStart();
                var nextWord = Regex.Match(nextLine, @"^(\p{L}+)([\s\S]*)$");
                if (nextWord.Success)
                {
                    var combinedWord = m.Groups[1].Value + nextWord.Groups[1].Value;
                    var lineRest = trimmed.Substring(0, m.Index) + combinedWord;
                    lines.Add(lineRest);
                    filtered[i + 1] = nextWord.Groups[2].Value.TrimStart();
                    continue;
                }
            }
            lines.Add(line);
        }

        var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
        if (nonEmpty.Count == 0) return "";

        var sortedLens = nonEmpty.Select(l => l.Length).OrderBy(l => l).ToList();
        double typicalMaxLen = sortedLens[(int)(sortedLens.Count * 0.85)];
        double shortThreshold = Math.Max(30, typicalMaxLen * 0.82);

        var paragraphs = new List<string>();
        var currentPara = new StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentPara.Length > 0)
                {
                    paragraphs.Add(currentPara.ToString().Trim());
                    currentPara.Clear();
                }
                continue;
            }

            var trimmed = line.Trim();
            bool hasIndent = line.StartsWith("   ") || line.StartsWith("\t");

            if (hasIndent && currentPara.Length > 0)
            {
                paragraphs.Add(currentPara.ToString().Trim());
                currentPara.Clear();
            }

            if (currentPara.Length > 0) currentPara.Append(' ');
            currentPara.Append(trimmed);

            bool endsWithPunct = Regex.IsMatch(trimmed, @"[.!?…:;][""'»”]?$");
            bool isShortLine = line.Length < shortThreshold;
            bool nextIsIndented = (i + 1 < lines.Count) &&
                                  (lines[i + 1].StartsWith("   ") || lines[i + 1].StartsWith("\t"));
            bool nextIsDialogueOrBullet = (i + 1 < lines.Count) &&
                                  Regex.IsMatch(lines[i + 1].TrimStart(), @"^(?:[—–\-•*]|\d+[\.\)]|[a-zA-Z][\.\)])\s+");

            if ((endsWithPunct && (isShortLine || nextIsIndented || nextIsDialogueOrBullet)) ||
                (i + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[i + 1])))
            {
                paragraphs.Add(currentPara.ToString().Trim());
                currentPara.Clear();
            }
        }

        if (currentPara.Length > 0)
        {
            paragraphs.Add(currentPara.ToString().Trim());
        }

        return string.Join("\r\n\r\n", paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static int PdfPageCount(string path)
    {
        using var d = PdfDocument.Open(path);
        return Math.Max(1, d.NumberOfPages);
    }

    /// <summary>BOM, UTF-8, ardından Türkçe Windows-1254 denemesi (Android ile aynı mantık).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        var strict = new UTF8Encoding(false, true);
        try { return strict.GetString(bytes); } catch (DecoderFallbackException) { }
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(200, bytes.Length));
        var m = Regex.Match(head, "encoding\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        if (m.Success) { try { return Encoding.GetEncoding(m.Groups[1].Value.Trim()).GetString(bytes); } catch { } }
        try { return Encoding.GetEncoding(1254).GetString(bytes); } catch { return Encoding.UTF8.GetString(bytes); }
    }

    public static string ReadTextFile(string path) => Decode(File.ReadAllBytes(path));

    // ---------- DOCX ----------
    public static string Docx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return "";
        using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var sb = new StringBuilder();
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element)
            {
                switch (xr.LocalName)
                {
                    case "t": sb.Append(xr.ReadElementContentAsString()); break;
                    case "tab": sb.Append("    "); break;
                    case "br": case "cr": sb.Append('\n'); break;
                }
                if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "p") sb.Append("\n\n");
            }
            else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "p") sb.Append("\n\n");
        }
        return Clean(sb.ToString());
    }

    // ---------- EPUB ----------
    public static string Epub(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var byNorm = new Dictionary<string, ZipArchiveEntry>();
        var byBase = new Dictionary<string, ZipArchiveEntry>();
        foreach (var e in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var norm = e.FullName.Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();
            byNorm.TryAdd(norm, e);
            byBase.TryAdd(norm[(norm.LastIndexOf('/') + 1)..], e);
        }

        string? opfPath = null;
        if (byNorm.TryGetValue("meta-inf/container.xml", out var container))
        {
            var xml = ReadEntry(container);
            var m = Regex.Match(xml, "full-path\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (m.Success) opfPath = Uri.UnescapeDataString(m.Groups[1].Value.Trim());
        }
        opfPath ??= byNorm.Keys.FirstOrDefault(k => k.EndsWith(".opf"));

        var sb = new StringBuilder();
        if (opfPath != null)
        {
            var cleanOpf = opfPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            if (byNorm.TryGetValue(cleanOpf, out var opfEntry) || byBase.TryGetValue(cleanOpf[(cleanOpf.LastIndexOf('/') + 1)..], out opfEntry))
            {
                var baseDir = opfEntry.FullName.Replace('\\', '/');
                baseDir = baseDir.Contains('/') ? baseDir[..baseDir.LastIndexOf('/')] : "";
                var opf = ReadEntry(opfEntry);
                var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match im in Regex.Matches(opf, "<(?:\\w+:)?item\\b([^>]+)>", RegexOptions.IgnoreCase))
                {
                    var attrs = im.Groups[1].Value;
                    var id = Regex.Match(attrs, "(?:^|\\s)id\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    var href = Regex.Match(attrs, "(?:^|\\s)href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    if (id.Length > 0 && href.Length > 0) manifest[id] = href;
                }
                foreach (Match rm in Regex.Matches(opf, "<(?:\\w+:)?itemref\\b([^>]+)>", RegexOptions.IgnoreCase))
                {
                    var idref = Regex.Match(rm.Groups[1].Value, "idref\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    if (!manifest.TryGetValue(idref, out var href)) continue;
                    var resolved = ResolvePath(baseDir, href).ToLowerInvariant();
                    if (byNorm.TryGetValue(resolved, out var he) || byBase.TryGetValue(resolved[(resolved.LastIndexOf('/') + 1)..], out he))
                    {
                        var t = HtmlToText(ReadEntry(he));
                        if (t.Length > 0) sb.Append(t).Append("\n\n");
                    }
                }
            }
        }

        if (sb.Length == 0)
        {
            foreach (var kv in byNorm.Where(k => (k.Key.EndsWith(".xhtml") || k.Key.EndsWith(".html") || k.Key.EndsWith(".htm") || k.Key.EndsWith(".xml"))
                         && !k.Key.Contains("container.xml") && !k.Key.EndsWith(".opf") && !k.Key.EndsWith(".ncx"))
                     .OrderBy(k => NaturalKey(k.Key), StringComparer.Ordinal))
            {
                var t = HtmlToText(ReadEntry(kv.Value));
                if (t.Length > 0) sb.Append(t).Append("\n\n");
            }
        }
        return Clean(sb.ToString());
    }

    private static string ReadEntry(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    private static string NaturalKey(string s) => Regex.Replace(s, "\\d+", m => m.Value.PadLeft(10, '0'));

    private static string ResolvePath(string baseDir, string href)
    {
        var clean = Uri.UnescapeDataString(href.Split('#')[0].Split('?')[0].Trim());
        var combined = baseDir.Length == 0 ? clean : baseDir + "/" + clean;
        var segs = new List<string>();
        foreach (var part in combined.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..") { if (segs.Count > 0) segs.RemoveAt(segs.Count - 1); }
            else segs.Add(part);
        }
        return string.Join("/", segs);
    }

    // ---------- HTML ----------
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "tr", "blockquote", "dt", "dd", "section", "article" };

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var n in doc.DocumentNode.SelectNodes("//script|//style|//head|//noscript")?.ToList() ?? new()) n.Remove();
        var sb = new StringBuilder();
        Walk(doc.DocumentNode, sb);
        return Clean(sb.ToString());
    }

    public static void Walk(HtmlNode node, StringBuilder sb)
    {
        foreach (var c in node.ChildNodes)
        {
            if (c.NodeType == HtmlNodeType.Text) sb.Append(HtmlEntity.DeEntitize(c.InnerText).Replace('\n', ' '));
            else if (c.NodeType == HtmlNodeType.Element)
            {
                if (c.Name.Equals("br", StringComparison.OrdinalIgnoreCase)) { sb.Append('\n'); continue; }
                Walk(c, sb);
                if (BlockTags.Contains(c.Name)) sb.Append("\n\n");
            }
        }
    }
}
