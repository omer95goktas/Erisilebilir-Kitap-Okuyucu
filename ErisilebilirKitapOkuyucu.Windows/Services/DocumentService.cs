using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
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
            case BookType.DOCX: _pages = Paginate(Safe(() => TextExtractor.Word(_path), "Word belgesi okunurken bir hata oluştu")); break;
            case BookType.XLSX: _pages = Paginate(Safe(() => TextExtractor.Xlsx(_path), "Excel tablosu (XLSX) okunurken bir hata oluştu")); break;
            default: _pages = Paginate(TextExtractor.Clean(TextExtractor.ReadTextFile(_path))); break;
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

    public static string DeduplicateShadowText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // 1. "KelimeKelime" (bitişik harf tekrarı, min 3 harf) örn: "BiyolojiBiyoloji", "YaşamYaşam"
        text = Regex.Replace(text, @"(\p{L}{3,})\1", "$1");

        // 2. Ardışık kelime veya kelime öbeği tekrarı (en az 2 harf içeren öbekler):
        // örn: "9. Sınıf9. Sınıf", "9. Sınıf 9. Sınıf", "Ders KitabıDers Kitabı", "TEMA TEMA"
        text = Regex.Replace(text, @"(?i)\b([A-Za-zÇĞİÖŞÜçğıöşü0-9\.\-]*\p{L}{2,}[A-Za-zÇĞİÖŞÜçğıöşü0-9\.\-]*(?:\s+[A-Za-zÇĞİÖŞÜçğıöşü0-9\.\-]+){0,4})\s*\1\b", "$1");

        // 3. Eğer satırın tamamı simetrik olarak ikiye bölünmüşse ve her iki yarı birbirinin aynısıysa (örn: "GELİŞTİRİCİ VE İLETİŞİMGELİŞTİRİCİ VE İLETİŞİM")
        var trimmed = text.Trim();
        if (trimmed.Length >= 6 && trimmed.Length % 2 == 0)
        {
            int half = trimmed.Length / 2;
            if (trimmed.Substring(0, half).Equals(trimmed.Substring(half), StringComparison.OrdinalIgnoreCase))
            {
                text = trimmed.Substring(0, half);
            }
        }
        else if (trimmed.Length >= 7)
        {
            // Arada tek boşluk varsa: "İfade İfade"
            int half = (trimmed.Length - 1) / 2;
            if (trimmed[half] == ' ' && trimmed.Substring(0, half).Equals(trimmed.Substring(half + 1), StringComparison.OrdinalIgnoreCase))
            {
                text = trimmed.Substring(0, half);
            }
        }

        return text;
    }

    /// <summary>
    /// PDF ve OCR gibi görsel satır sonu içeren metinleri doğal paragraflara dönüştürür.
    /// Satır içi kesmeleri birleştirir, kelime sonu tirelerini (- ) bağlar,
    /// gölge ve çift katman tekrarlarını temizler,
    /// gerçek paragraf bitişlerine çift satır sonu (\r\n\r\n) ekler.
    /// </summary>
    public static string ReflowPdfText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";

        var rawLines = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var filtered = new List<string>();
        string? lastNonEmpty = null;

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

            // Gölge / katman metinlerini ve ardışık çift kelimeleri temizle
            var deduped = DeduplicateShadowText(trimmed);

            // Birebir ardışık tekrarlanan satırları atla
            if (lastNonEmpty != null && deduped.Equals(lastNonEmpty, StringComparison.OrdinalIgnoreCase))
                continue;

            lastNonEmpty = deduped;
            filtered.Add(deduped);
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
            bool isCurrentBullet = Regex.IsMatch(trimmed, @"^(?:[—–\-•*]|\d+[\.\)]|[a-zA-Z][\.\)])\s+");

            if ((hasIndent || isCurrentBullet) && currentPara.Length > 0)
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

            if (nextIsDialogueOrBullet ||
                (endsWithPunct && (isShortLine || nextIsIndented)) ||
                (isShortLine && !trimmed.EndsWith(',') && !trimmed.EndsWith('-') && i + 1 < lines.Count && (char.IsUpper(lines[i + 1].TrimStart().FirstOrDefault()))) ||
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

    // ---------- WORD (DOCX ve DOC) ----------
    public static string Word(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".doc") return Doc(path);
        return Docx(path);
    }

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

    public static string Doc(string path)
    {
        if (!File.Exists(path)) return "";
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) { AppPaths.Log("doc read: " + ex); return ""; }
        if (bytes.Length == 0) return "";

        // 1. Dosya aslında DOCX (Zip) mi? (PK\x03\x04)
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
        {
            return Docx(path);
        }

        // 2. Dosya RTF mi? ({\rtf)
        if (bytes.Length >= 5 && bytes[0] == (byte)'{' && bytes[1] == (byte)'\\' && bytes[2] == (byte)'r' && bytes[3] == (byte)'t' && bytes[4] == (byte)'f')
        {
            return ExtractRtfText(Decode(bytes));
        }

        // 3. Dosya HTML / XML olarak kaydedilmiş bir Word dosyası mı?
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();
        if (head.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            head.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return HtmlToText(Decode(bytes));
        }

        // 4. Standart OLE2 Compound Document Word 97-2003 (.doc)
        if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
        {
            try
            {
                var text = ExtractOleDoc(bytes);
                if (!string.IsNullOrWhiteSpace(text)) return Clean(text);
            }
            catch (Exception ex)
            {
                AppPaths.Log("ole doc extract: " + ex);
            }
        }

        // 5. Fallback: Düz metin / karakter dizisi tarama
        return Clean(FallbackExtractText(bytes));
    }

    public static string ExtractRtfText(string rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf)) return "";
        try
        {
            // Meta veri bloklarını temizle (font, renk tablosu, başlık bilgisi vb.)
            rtf = Regex.Replace(rtf, @"\{\\\*(?:fonttbl|colortbl|stylesheet|info|pict)[^}]*\}", "", RegexOptions.Singleline);
            rtf = Regex.Replace(rtf, @"\{(?:fonttbl|colortbl|stylesheet|info|pict)[^}]*\}", "", RegexOptions.Singleline);

            // Unicode karakterler: \uNNNN?
            rtf = Regex.Replace(rtf, @"\\u([0-9]{1,5})\??", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int code))
                    return ((char)code).ToString();
                return "";
            });

            // Türkçe karakter hex kodları: \'e7 -> ç vb.
            rtf = Regex.Replace(rtf, @"\\'([0-9a-fA-F]{2})", m =>
            {
                try
                {
                    byte b = Convert.ToByte(m.Groups[1].Value, 16);
                    return Encoding.GetEncoding(1254).GetString(new[] { b });
                }
                catch { return ""; }
            });

            // Satır ve paragraf sonları
            rtf = rtf.Replace("\\par", "\n\n").Replace("\\line", "\n").Replace("\\tab", "    ");
            // Kalan kontrol kelimelerini temizle
            rtf = Regex.Replace(rtf, @"\\[a-zA-Z]+-?[0-9]*\s?", "");
            // Küme parantezleri
            rtf = rtf.Replace("{", "").Replace("}", "");
            return Clean(rtf);
        }
        catch { return Clean(rtf); }
    }

    private static string ExtractOleDoc(byte[] rawBytes)
    {
        var ole = new OleReader(rawBytes);
        var wordDoc = ole.GetStream("WordDocument");
        if (wordDoc == null || wordDoc.Length < 32)
            return FallbackExtractText(rawBytes);

        ushort magic = BitConverter.ToUInt16(wordDoc, 0);
        if (magic != 0xA5EC)
            return FallbackExtractText(wordDoc);

        ushort flags = BitConverter.ToUInt16(wordDoc, 0x000A);
        bool whichTable = (flags & 0x0200) != 0;
        var tableBytes = ole.GetStream(whichTable ? "1Table" : "0Table") ?? ole.GetStream("0Table") ?? ole.GetStream("1Table");

        if (tableBytes != null && wordDoc.Length >= 0x01AA)
        {
            uint fcClx = BitConverter.ToUInt32(wordDoc, 0x01A2);
            uint lcbClx = BitConverter.ToUInt32(wordDoc, 0x01A6);

            if (lcbClx > 0 && fcClx + lcbClx <= tableBytes.Length)
            {
                var text = ParseClx(tableBytes, (int)fcClx, (int)lcbClx, wordDoc);
                if (!string.IsNullOrWhiteSpace(text)) return CleanWordDocText(text);
            }
        }

        return CleanWordDocText(FallbackExtractText(wordDoc));
    }

    private static string ParseClx(byte[] table, int fcClx, int lcbClx, byte[] wordDoc)
    {
        int pos = fcClx;
        int end = fcClx + lcbClx;
        while (pos < end && table[pos] == 0x01)
        {
            if (pos + 3 > end) break;
            short cb = BitConverter.ToInt16(table, pos + 1);
            pos += 3 + cb;
        }

        if (pos < end && table[pos] == 0x02)
        {
            pos++;
            if (pos + 4 > end) return "";
            int lcbPlcfpcd = BitConverter.ToInt32(table, pos);
            pos += 4;
            if (lcbPlcfpcd <= 4) return "";

            int n = (lcbPlcfpcd - 4) / 12;
            int cpOffset = pos;
            int pcdOffset = pos + (n + 1) * 4;

            if (pcdOffset + n * 8 > end) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                int cpStart = BitConverter.ToInt32(table, cpOffset + i * 4);
                int cpEnd = BitConverter.ToInt32(table, cpOffset + (i + 1) * 4);
                int charCount = cpEnd - cpStart;
                if (charCount <= 0) continue;

                uint fc = BitConverter.ToUInt32(table, pcdOffset + i * 8 + 2);
                bool isAnsi = (fc & 0x40000000u) != 0;
                uint actualFc = fc & ~0x40000000u;

                if (isAnsi)
                {
                    int byteOffset = (int)(actualFc / 2);
                    if (byteOffset + charCount <= wordDoc.Length)
                    {
                        sb.Append(Encoding.GetEncoding(1254).GetString(wordDoc, byteOffset, charCount));
                    }
                }
                else
                {
                    int byteOffset = (int)actualFc;
                    if (byteOffset + charCount * 2 <= wordDoc.Length)
                    {
                        sb.Append(Encoding.Unicode.GetString(wordDoc, byteOffset, charCount * 2));
                    }
                }
            }
            return sb.ToString();
        }
        return "";
    }

    private static string CleanWordDocText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append("\r\n\r\n");
            }
            else if (c == '\x07') // Word hücre / satır sonu
            {
                sb.Append(" | ");
            }
            else if (c == '\x0B') // Shift + Enter
            {
                sb.Append("\r\n");
            }
            else if (c == '\x0C') // Sayfa sonu
            {
                sb.Append("\r\n\r\n");
            }
            else if (c == '\t')
            {
                sb.Append("    ");
            }
            else if (c >= 0x20 || c == '\n')
            {
                sb.Append(c);
            }
        }
        return Clean(sb.ToString());
    }

    public static string FallbackExtractText(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        var sb = new StringBuilder();

        // 1. Önce UTF-16LE karakter dizilerini tara (Word belgelerinde standart)
        int uCount = 0;
        int uStart = -1;
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            char c = (char)(bytes[i] | (bytes[i + 1] << 8));
            bool isPrintable = (c >= ' ' && c <= '~') ||
                               "çğıöşüÇĞİÖŞÜâîûÂÎÛ".Contains(c) ||
                               c == '\r' || c == '\n' || c == '\t' ||
                               (c >= 0x00A0 && c <= 0x024F);
            if (isPrintable)
            {
                if (uStart == -1) uStart = i;
                uCount++;
            }
            else
            {
                if (uCount >= 4)
                {
                    sb.Append(Encoding.Unicode.GetString(bytes, uStart, uCount * 2)).Append(' ');
                }
                uStart = -1;
                uCount = 0;
            }
        }
        if (uCount >= 4)
        {
            sb.Append(Encoding.Unicode.GetString(bytes, uStart, uCount * 2));
        }

        var result = CleanWordDocText(sb.ToString());
        if (result.Length >= 20) return result;

        // 2. Yeterli UTF-16 çıkmazsa ANSI Windows-1254 bloklarını tara
        sb.Clear();
        int aCount = 0;
        int aStart = -1;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            bool isPrintable = (b >= 32 && b <= 126) || b == 13 || b == 10 || b == 9 ||
                               b == 0xE7 || b == 0xC7 || b == 0xF0 || b == 0xD0 ||
                               b == 0xFD || b == 0xDD || b == 0xF6 || b == 0xD6 ||
                               b == 0xFE || b == 0xDE || b == 0xFC || b == 0xDC;
            if (isPrintable)
            {
                if (aStart == -1) aStart = i;
                aCount++;
            }
            else
            {
                if (aCount >= 4)
                {
                    sb.Append(Encoding.GetEncoding(1254).GetString(bytes, aStart, aCount)).Append(' ');
                }
                aStart = -1;
                aCount = 0;
            }
        }
        if (aCount >= 4)
        {
            sb.Append(Encoding.GetEncoding(1254).GetString(bytes, aStart, aCount));
        }

        return CleanWordDocText(sb.ToString());
    }

    // ---------- EXCEL (XLSX) ----------
    public static string Xlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        // 1. Shared Strings tablosunu oku (xl/sharedStrings.xml)
        var sharedStrings = new List<string>();
        var sstEntry = zip.GetEntry("xl/sharedStrings.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
        if (sstEntry != null)
        {
            using var s = sstEntry.Open();
            var doc = XDocument.Load(s);
            foreach (var si in doc.Descendants().Where(e => e.Name.LocalName == "si"))
            {
                var tNodes = si.Descendants().Where(e => e.Name.LocalName == "t");
                var text = string.Concat(tNodes.Select(t => t.Value));
                sharedStrings.Add(text);
            }
        }

        // 2. Çalışma Sayfalarını ve adlarını tespit et (xl/workbook.xml ve .rels)
        var sheetEntries = new List<(string name, string entryPath)>();
        var wbEntry = zip.GetEntry("xl/workbook.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase));
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("xl/_rels/workbook.xml.rels", StringComparison.OrdinalIgnoreCase));

        var rIdToTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (relsEntry != null)
        {
            using var rs = relsEntry.Open();
            var relDoc = XDocument.Load(rs);
            foreach (var rel in relDoc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    var norm = target.Replace('\\', '/').TrimStart('/');
                    if (!norm.StartsWith("xl/")) norm = "xl/" + norm;
                    rIdToTarget[id] = norm;
                }
            }
        }

        if (wbEntry != null)
        {
            using var ws = wbEntry.Open();
            var wbDoc = XDocument.Load(ws);
            foreach (var sheet in wbDoc.Descendants().Where(e => e.Name.LocalName == "sheet"))
            {
                var name = (string?)sheet.Attribute("name") ?? "Sayfa";
                var rId = (string?)sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
                if (rId != null && rIdToTarget.TryGetValue(rId, out var targetPath))
                {
                    sheetEntries.Add((name, targetPath));
                }
            }
        }

        // Eğer workbook.xml okunamadıysa doğrudan xl/worksheets/ klasöründeki sayfaları al
        if (sheetEntries.Count == 0)
        {
            var wsEntries = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"xl/worksheets/sheet\d+\.xml", RegexOptions.IgnoreCase)).ToList();
            int idx = 1;
            foreach (var we in wsEntries)
            {
                sheetEntries.Add(($"Sayfa {idx++}", we.FullName));
            }
        }

        var sb = new StringBuilder();
        foreach (var (sheetName, entryPath) in sheetEntries)
        {
            var sheetEntry = zip.GetEntry(entryPath)
                ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
            if (sheetEntry == null) continue;

            sb.AppendLine($"=== [Çalışma Sayfası: {sheetName}] ===");
            sb.AppendLine();

            using var ss = sheetEntry.Open();
            var sheetDoc = XDocument.Load(ss);

            foreach (var row in sheetDoc.Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var rowIndex = (string?)row.Attribute("r");
                var rowCells = new List<string>();

                foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    var cellType = (string?)c.Attribute("t");
                    string? val = null;

                    if (cellType == "s")
                    {
                        var vElem = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                        if (vElem != null && int.TryParse(vElem.Value.Trim(), out int sstIdx) && sstIdx >= 0 && sstIdx < sharedStrings.Count)
                        {
                            val = sharedStrings[sstIdx];
                        }
                    }
                    else if (cellType == "inlineStr")
                    {
                        var tElem = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "t");
                        val = tElem?.Value.Trim();
                    }
                    else if (cellType == "b")
                    {
                        var vElem = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                        val = vElem?.Value.Trim() == "1" ? "DOĞRU" : "YANLIŞ";
                    }
                    else
                    {
                        var vElem = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                        val = vElem?.Value.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        rowCells.Add(val);
                    }
                }

                if (rowCells.Count > 0)
                {
                    var rowText = string.Join(" | ", rowCells);
                    if (!string.IsNullOrWhiteSpace(rowText))
                    {
                        if (!string.IsNullOrEmpty(rowIndex))
                            sb.AppendLine($"Satır {rowIndex}: {rowText}");
                        else
                            sb.AppendLine(rowText);
                    }
                }
            }

            sb.AppendLine();
        }

        return Clean(sb.ToString());
    }

    /// <summary>Saf yönetilen (pure managed) OLE2 Compound File Binary Format okuyucusu.</summary>
    private class OleReader
    {
        private readonly byte[] _bytes;
        private readonly int _sectorSize;
        private readonly List<int> _fat = new();
        private readonly List<int> _miniFat = new();
        private readonly Dictionary<string, (int startSect, long size)> _entries = new(StringComparer.OrdinalIgnoreCase);
        private byte[]? _miniStreamBytes;

        public OleReader(byte[] bytes)
        {
            _bytes = bytes;
            if (bytes.Length < 512) return;

            int uSectorShift = BitConverter.ToUInt16(bytes, 30);
            _sectorSize = 1 << (uSectorShift > 0 && uSectorShift < 16 ? uSectorShift : 9);

            int numFatSectors = BitConverter.ToInt32(bytes, 44);
            int sectDirStart = BitConverter.ToInt32(bytes, 48);
            int sectMiniFatStart = BitConverter.ToInt32(bytes, 60);
            int numMiniFatSectors = BitConverter.ToInt32(bytes, 64);
            int sectDifStart = BitConverter.ToInt32(bytes, 68);
            int numDifSectors = BitConverter.ToInt32(bytes, 72);

            // 1. DIFAT
            var fatSectors = new List<int>();
            for (int i = 0; i < Math.Min(numFatSectors, 109); i++)
            {
                int sect = BitConverter.ToInt32(bytes, 76 + i * 4);
                if (sect >= 0) fatSectors.Add(sect);
            }
            if (numDifSectors > 0 && sectDifStart >= 0)
            {
                int curDif = sectDifStart;
                int difCount = 0;
                while (curDif >= 0 && difCount++ < numDifSectors && fatSectors.Count < numFatSectors)
                {
                    int offset = (curDif + 1) * _sectorSize;
                    if (offset + _sectorSize > bytes.Length) break;
                    int intsInDif = _sectorSize / 4 - 1;
                    for (int i = 0; i < intsInDif && fatSectors.Count < numFatSectors; i++)
                    {
                        int s = BitConverter.ToInt32(bytes, offset + i * 4);
                        if (s >= 0) fatSectors.Add(s);
                    }
                    curDif = BitConverter.ToInt32(bytes, offset + intsInDif * 4);
                }
            }

            // 2. FAT
            int intsPerSector = _sectorSize / 4;
            foreach (var fatSect in fatSectors)
            {
                int offset = (fatSect + 1) * _sectorSize;
                for (int i = 0; i < intsPerSector && offset + (i + 1) * 4 <= bytes.Length; i++)
                {
                    _fat.Add(BitConverter.ToInt32(bytes, offset + i * 4));
                }
            }

            // 3. Mini FAT
            if (numMiniFatSectors > 0 && sectMiniFatStart >= 0)
            {
                var mfBytes = ReadChain(sectMiniFatStart, _fat, _sectorSize, numMiniFatSectors * _sectorSize);
                for (int i = 0; i + 4 <= mfBytes.Length; i += 4)
                {
                    _miniFat.Add(BitConverter.ToInt32(mfBytes, i));
                }
            }

            // 4. Dizin Akışı
            var dirBytes = ReadChain(sectDirStart, _fat, _sectorSize, -1);
            int dirEntrySize = 128;
            for (int i = 0; i + dirEntrySize <= dirBytes.Length; i += dirEntrySize)
            {
                int nameLen = BitConverter.ToUInt16(dirBytes, i + 64);
                if (nameLen <= 2) continue;
                string name = Encoding.Unicode.GetString(dirBytes, i, Math.Min(nameLen - 2, 64));
                int startSect = BitConverter.ToInt32(dirBytes, i + 116);
                long size = BitConverter.ToUInt32(dirBytes, i + 120);
                _entries[name] = (startSect, size);

                // Kök girdi (Root Entry) Mini Stream'i tutar
                if (i == 0 && size > 0 && startSect >= 0)
                {
                    _miniStreamBytes = ReadChain(startSect, _fat, _sectorSize, (int)size);
                }
            }
        }

        public byte[]? GetStream(string name)
        {
            if (!_entries.TryGetValue(name, out var entry)) return null;
            if (entry.size < 4096 && _miniStreamBytes != null && _miniFat.Count > 0)
            {
                return ReadChain(entry.startSect, _miniFat, 64, (int)entry.size, _miniStreamBytes);
            }
            return ReadChain(entry.startSect, _fat, _sectorSize, (int)entry.size, _bytes);
        }

        private byte[] ReadChain(int startSect, List<int> fat, int secSize, int maxSize, byte[]? src = null)
        {
            src ??= _bytes;
            using var ms = new MemoryStream();
            int cur = startSect;
            int visited = 0;
            while (cur >= 0 && cur < fat.Count && cur != -2 && visited++ < 100000)
            {
                int offset = src == _bytes ? (cur + 1) * secSize : cur * secSize;
                if (offset >= src.Length) break;
                int count = Math.Min(secSize, src.Length - offset);
                ms.Write(src, offset, count);
                if (maxSize > 0 && ms.Length >= maxSize) break;
                cur = fat[cur];
            }
            var res = ms.ToArray();
            if (maxSize > 0 && res.Length > maxSize) Array.Resize(ref res, maxSize);
            return res;
        }
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
