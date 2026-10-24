using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ErisilebilirKitapOkuyucu.Services;

public record ImportResult(BookItem? Book, string? Message);

/// <summary>Android MainViewModel'deki kitap ekleme mantığı: dosya, ZIP, web makalesi, düz metin.</summary>
public class ImportService
{
    private static readonly string[] AudioExt = { ".mp3", ".m4a", ".wav", ".aac", ".ogg", ".flac", ".wma", ".opus" };
    private static readonly string[] DocExt = { ".pdf", ".txt", ".epub", ".docx" };
    private readonly BookRepository _repo;
    private static readonly HttpClient Http = CreateHttp();

    public ImportService(BookRepository repo) { _repo = repo; }

    public static string Filter =>
        "Desteklenen dosyalar|*.pdf;*.txt;*.epub;*.docx;*.zip;*.mp3;*.m4a;*.wav;*.aac;*.ogg;*.flac;*.wma;*.opus|" +
        "Belgeler (PDF, TXT, EPUB, DOCX)|*.pdf;*.txt;*.epub;*.docx|Sesli kitaplar|*.mp3;*.m4a;*.wav;*.aac;*.ogg;*.flac;*.wma;*.opus|ZIP arşivleri|*.zip|Tüm dosyalar|*.*";

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Referrer = new Uri("https://www.google.com");
        return c;
    }

    public static BookType? DetectType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => BookType.PDF,
            ".epub" => BookType.EPUB,
            ".docx" => BookType.DOCX,
            ".txt" => BookType.TXT,
            _ when AudioExt.Contains(ext) => BookType.AUDIO,
            _ => null
        };
    }

    public async Task<ImportResult> AddFileAsync(string path, string folder = BookItem.RootFolder)
    {
        try
        {
            if (!File.Exists(path)) return new(null, "Dosya bulunamadı.");
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip" && !LooksLikeEpub(path)) return await Task.Run(() => AddZip(path));

            var type = DetectType(path) ?? (LooksLikeEpub(path) ? BookType.EPUB : BookType.TXT);
            var baseName = Path.GetFileNameWithoutExtension(path);
            var title = baseName.Replace('_', ' ').Trim();
            if (title.Length == 0) title = "Kitap " + DateTime.Now.Ticks;

            // Aynı dosya zaten kitaplıkta varsa onu aç: kalınan yer korunur
            var size = new FileInfo(path).Length;
            var existing = _repo.Books.FirstOrDefault(b => !b.IsZipArchive && b.Type == type && b.Title == title &&
                File.Exists(b.UriString) && new FileInfo(b.UriString).Length == size);
            if (existing != null) return new(existing, null);

            // Kopya al: orijinal dosya taşınsa/silinse bile kitap kaybolmasın (Android davranışı)
            var safe = Regex.Replace(baseName, @"[^\w\-\s]", "_").Trim();
            if (safe.Length == 0) safe = "Kitap";
            var target = Path.Combine(AppPaths.ImportedDir, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{safe}{ext}");
            await Task.Run(() => File.Copy(path, target, true));

            int pages = 1;
            if (type == BookType.PDF) pages = await Task.Run(() => TextExtractor.PdfPageCount(target));
            else if (type != BookType.AUDIO)
            {
                using var s = await DocumentSession.OpenAsync(new BookItem { UriString = target, Type = type });
                pages = s.TotalPages;
            }

            var book = new BookItem { Title = title, UriString = target, Type = type, TotalPages = pages, FolderName = folder };
            _repo.AddBook(book);
            return new(book, null);
        }
        catch (Exception e)
        {
            AppPaths.Log("import: " + e);
            return new(null, "Dosya açılamadı: " + e.Message);
        }
    }

    private static bool LooksLikeEpub(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase)) return true;
            using var z = ZipFile.OpenRead(path);
            return z.Entries.Take(20).Any(e => e.FullName.Equals("mimetype", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string NaturalKey(string s) => Regex.Replace(s, @"\d+", m => m.Value.PadLeft(10, '0'));

    private ImportResult AddZip(string zipPath)
    {
        var zipName = Path.GetFileNameWithoutExtension(zipPath);
        var folder = Path.Combine(AppPaths.ImportedDir, "zip_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Directory.CreateDirectory(folder);
        var chapters = new List<ChapterItem>();
        bool audioArchive = true;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.Name.StartsWith(".") || entry.FullName.StartsWith("__MACOSX")) continue;
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                bool isAudio = AudioExt.Contains(ext), isDoc = DocExt.Contains(ext);
                if (!isAudio && !isDoc) continue;
                if (!isAudio) audioArchive = false;
                var cleanBase = Path.GetFileNameWithoutExtension(entry.Name);
                var safe = Regex.Replace(cleanBase, @"[^\w\-\s]", "_") + ext;
                var target = Path.Combine(folder, safe);
                int n = 1;
                while (File.Exists(target)) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(safe)}_{n++}{ext}");
                entry.ExtractToFile(target, true);
                chapters.Add(new ChapterItem { Title = cleanBase.Trim(), UriString = target });
            }
        }
        catch (Exception e)
        {
            AppPaths.Log("zip: " + e);
            try { Directory.Delete(folder, true); } catch { }
            return new(null, "ZIP dosyası açılırken bir hata oluştu: " + e.Message);
        }

        if (chapters.Count == 0)
        {
            try { Directory.Delete(folder, true); } catch { }
            return new(null, "ZIP arşivi içerisinde desteklenen ses veya kitap dosyası bulunamadı.");
        }

        var sorted = chapters.OrderBy(c => NaturalKey(c.Title), StringComparer.OrdinalIgnoreCase).ToList();
        var bookTitle = zipName.Replace('_', ' ').Trim();
        if (bookTitle.Length == 0) bookTitle = "ZIP Kitabı";

        if (audioArchive)
        {
            var book = new BookItem
            {
                Title = bookTitle, UriString = sorted[0].UriString, Type = BookType.AUDIO,
                TotalPages = sorted.Count, IsZipArchive = true, Chapters = sorted
            };
            _repo.AddBook(book);
            return new(book, $"\"{bookTitle}\" adlı ZIP kitap {sorted.Count} ayrım olarak kitaplığınıza eklendi.");
        }

        // Belge içeren ZIP: her belge ayrı kitap olarak, ZIP adıyla yeni klasöre eklenir
        _repo.CreateFolder(bookTitle);
        var books = new List<BookItem>();
        foreach (var c in sorted)
        {
            var t = DetectType(c.UriString) ?? BookType.TXT;
            int pages = 1;
            try
            {
                if (t == BookType.PDF) pages = TextExtractor.PdfPageCount(c.UriString);
                else if (t != BookType.AUDIO)
                {
                    using var s = DocumentSession.OpenAsync(new BookItem { UriString = c.UriString, Type = t }).GetAwaiter().GetResult();
                    pages = s.TotalPages;
                }
            }
            catch { }
            books.Add(new BookItem { Title = c.Title.Replace('_', ' '), UriString = c.UriString, Type = t, TotalPages = pages, FolderName = bookTitle });
        }
        _repo.AddBooks(books);
        return new(books[0], $"ZIP içindeki {books.Count} belge \"{bookTitle}\" klasörüne eklendi.");
    }

    // ---------- Web makalesi ----------
    public static string? ExtractUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"https?://[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}(?:/[^\s]*)?", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }

    public async Task<ImportResult> AddWebArticleAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var html = TextExtractor.Decode(await resp.Content.ReadAsByteArrayAsync());
            var (title, content) = ParseArticle(html, url);
            if (content == null) return new(null, "Web bağlantısından içerik alınamadı.");

            var file = Path.Combine(AppPaths.ImportedDir, $"web_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt");
            await File.WriteAllTextAsync(file, content, new UTF8Encoding(false));
            var pages = DocumentSession.Paginate(content).Count;
            var book = new BookItem { Title = title, UriString = file, Type = BookType.TXT, TotalPages = pages };
            _repo.AddBook(book);
            return new(book, $"\"{title}\" kitaplığınıza eklendi.");
        }
        catch (Exception e)
        {
            AppPaths.Log("web: " + e);
            return new(null, "Web bağlantısından içerik alınamadı: " + e.Message);
        }
    }

    public static (string title, string? content) ParseArticle(string html, string url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        string Meta(string xpath) => doc.DocumentNode.SelectSingleNode(xpath)?.GetAttributeValue("content", "")?.Trim() ?? "";
        var title = Meta("//meta[@property='og:title']");
        if (title.Length == 0) title = Meta("//meta[@name='twitter:title']");
        if (title.Length == 0) title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? "").Trim();
        if (title.Length == 0) title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
        if (title.Length == 0) title = "Web Yazısı";
        int pipe = title.LastIndexOf('|'); if (pipe > 10) title = title[..pipe].Trim();
        int dash = title.LastIndexOf(" - ", StringComparison.Ordinal); if (dash > 10) title = title[..dash].Trim();

        // 1. Akıllı Makale Alanı Tespiti (Readability algoritması: WordPress, PHP, özel CMS, haber portalları için evrensel)
        var candidates = doc.DocumentNode.SelectNodes("//article | //main | //div | //section")?.ToList() ?? new();
        HtmlNode? bestNode = null;
        int bestScore = -1;

        var negativeKeywords = new[]
        {
            "sidebar", "comment", "footer", "header", "menu", "nav", "related", "popular",
            "ad-", "advert", "widget", "banner", "breadcrumb", "share", "author", "modal", "popup"
        };
        var positiveKeywords = new[]
        {
            "article", "content", "post", "body", "entry", "detail", "story", "news", "text", "haber", "yazi"
        };

        foreach (var n in candidates)
        {
            var tag = n.Name.ToLowerInvariant();
            if (tag == "body" || tag == "html") continue;
            var cls = n.GetAttributeValue("class", "").ToLowerInvariant();
            var id = n.GetAttributeValue("id", "").ToLowerInvariant();

            bool isNegative = false;
            foreach (var nk in negativeKeywords)
            {
                if (cls.Contains(nk) || id.Contains(nk)) { isNegative = true; break; }
            }
            if (isNegative) continue;

            var ps = n.SelectNodes("./p | ./div/p");
            int pCount = ps?.Count ?? 0;
            int pTextLen = 0;
            if (ps != null)
            {
                foreach (var p in ps)
                {
                    var t = p.InnerText.Trim();
                    if (t.Length > 25) pTextLen += t.Length;
                }
            }

            int score = pTextLen;
            foreach (var pk in positiveKeywords)
            {
                if (cls.Contains(pk) || id.Contains(pk)) score += 300;
            }
            if (tag == "article") score += 500;
            if (tag == "main") score += 400;

            if (score > bestScore && pCount >= 2)
            {
                bestScore = score;
                bestNode = n;
            }
        }

        // Eğer puanlama ile bulunamazsa klasik fallback
        var container = bestNode ??
                        doc.DocumentNode.SelectSingleNode("//article") ??
                        doc.DocumentNode.SelectSingleNode("//main") ??
                        doc.DocumentNode.SelectSingleNode("//*[contains(@class,'entry-content') or contains(@class,'post-content')]") ??
                        doc.DocumentNode.SelectSingleNode("//body") ??
                        doc.DocumentNode;

        // 2. Konteyner içindeki reklam, sosyal buton, galeri, yorum gibi gürültüleri temizle
        var unwanted = ".//script|.//style|.//nav|.//header|.//footer|.//aside|.//iframe|.//noscript|.//svg|.//form|.//button|.//input" +
                       "|//*[@role='navigation']|//*[@role='banner']|//*[@role='contentinfo']" +
                       "|//*[contains(@class,'social-share')]|//*[contains(@class,'cookie')]|//*[contains(@class,'popup')]" +
                       "|//*[@id='comments']|//*[contains(@class,'comments')]|//*[contains(@class,'related')]" +
                       "|//*[contains(@class,'gallery')]|//*[contains(@class,'affiliate')]" +
                       "|//*[contains(@class,'ad-')]|//*[contains(@class,'advert')]";

        var noiseNodes = container.SelectNodes(unwanted)?.ToList();
        if (noiseNodes != null)
        {
            foreach (var n in noiseNodes)
            {
                var tag = n.Name.ToLowerInvariant();
                if (tag != "body" && tag != "article" && tag != "main")
                {
                    n.Remove();
                }
            }
        }

        // Reklam ve gürültü kalıpları (Web sitelerinin aralara serpiştirdiği reklam metinleri)
        var ignorePhrases = new[]
        {
            "ilginizi çekebilir", "içerikten görseller", "abone ol", "takip et", "paylaş", "reklam", "yorumlar", "tıklayın", "kaynak:"
        };

        // 3. Paragrafları, başlıkları ve liste maddelerini sırayla topla
        var paragraphs = new List<string>();
        var elements = container.SelectNodes(".//p | .//h1 | .//h2 | .//h3 | .//h4 | .//li");
        if (elements != null)
        {
            foreach (var el in elements)
            {
                var text = Regex.Replace(HtmlEntity.DeEntitize(el.InnerText), @"\s+", " ").Trim();
                if (text.Length == 0) continue;

                var lower = text.ToLowerInvariant();
                bool skip = false;
                foreach (var ip in ignorePhrases)
                {
                    if (lower.Equals(ip) || lower.StartsWith(ip) || lower.EndsWith(ip))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;
                if (el.Name == "li" && text.Length < 5) continue;
                if (!paragraphs.Contains(text)) paragraphs.Add(text);
            }
        }

        if (paragraphs.Count == 0)
        {
            var full = Regex.Replace(HtmlEntity.DeEntitize(container.InnerText), @"\s+", " ").Trim();
            if (full.Length > 0) paragraphs.Add(full);
        }
        if (paragraphs.Count == 0) return (title, null);

        var sb = new StringBuilder();
        sb.Append(title).Append("\n\n");
        sb.Append("Kaynak: ").Append(url).Append('\n');
        sb.Append("Tarih: ").Append(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).Append('\n');
        sb.Append("----------------------------------------\n\n");
        foreach (var p in paragraphs) sb.Append(p).Append("\n\n");
        return (title, sb.ToString());
    }

    // ---------- Düz metin ----------
    public ImportResult AddPlainText(string text, string titlePrefix = "Yapıştırılan Metin")
    {
        var file = Path.Combine(AppPaths.ImportedDir, $"text_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt");
        File.WriteAllText(file, text, new UTF8Encoding(false));
        var pages = DocumentSession.Paginate(text).Count;
        var book = new BookItem { Title = $"{titlePrefix} {DateTime.Now:dd.MM.yyyy HH:mm}", UriString = file, Type = BookType.TXT, TotalPages = pages };
        _repo.AddBook(book);
        return new(book, null);
    }
}
