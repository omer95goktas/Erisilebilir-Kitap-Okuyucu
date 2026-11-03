using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>
/// Yer imleri ve notlar için TXT formatında dışa aktarma, içe aktarma ve yedekleme servisi.
/// Çıktılar hem ekran okuyucular (NVDA/JAWS) hem de insanlar için tertemiz ve anlaşılır biçimdedir.
/// </summary>
public static class BookmarkService
{
    private const string SeparatorLine = "--------------------------------------------------------------------------------";
    private const string HeaderLine = "================================================================================";

    /// <summary>
    /// Tek bir kitabın yer imlerini erişilebilir TXT biçiminde biçimlendirir.
    /// </summary>
    public static string ExportToText(BookItem book)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HeaderLine);
        sb.AppendLine("ERİŞİLEBİLİR KİTAP OKUYUCU - YER İMLERİ VE NOTLAR");
        sb.AppendLine(HeaderLine);
        sb.AppendLine($"Kitap: {book.Title}");
        sb.AppendLine($"Tür: {(book.Type == BookType.AUDIO ? "Sesli Kitap" : "Metin Belgesi")}");
        sb.AppendLine($"Dışa Aktarma Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Toplam Yer İmi Sayısı: {book.Bookmarks.Count}");
        sb.AppendLine(HeaderLine);
        sb.AppendLine();

        int index = 1;
        foreach (var bm in book.Bookmarks.OrderBy(x => x.CreatedAt))
        {
            sb.AppendLine($"[YER İMİ {index++}]");
            if (book.Type == BookType.AUDIO)
            {
                sb.AppendLine($"Konum: {Fmt.SpokenTime(bm.PositionMs)} ({bm.PositionMs} ms)");
            }
            else
            {
                sb.AppendLine($"Sayfa: {bm.PageNumber}");
            }

            if (!string.IsNullOrWhiteSpace(bm.Title))
            {
                sb.AppendLine($"Başlık: {bm.Title}");
            }

            var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(bm.CreatedAt).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            sb.AppendLine($"Tarih: {dateStr}");

            sb.AppendLine("İşaretlenen Metin / Not:");
            if (!string.IsNullOrWhiteSpace(bm.Note))
            {
                sb.AppendLine(bm.Note.Trim());
            }
            else
            {
                sb.AppendLine("(Not eklenmemiş)");
            }

            sb.AppendLine(SeparatorLine);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Kütüphanedeki tüm kitapların yer imlerini tek bir toplu TXT dosyasında biçimlendirir.
    /// </summary>
    public static string ExportAllToText(IEnumerable<BookItem> books)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HeaderLine);
        sb.AppendLine("ERİŞİLEBİLİR KİTAP OKUYUCU - TOPLU YER İMİ YEDEĞİ");
        sb.AppendLine($"Dışa Aktarma Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine(HeaderLine);
        sb.AppendLine();

        int bookCount = 0;
        int totalBookmarks = 0;

        foreach (var book in books)
        {
            if (book.Bookmarks.Count == 0) continue;
            bookCount++;
            totalBookmarks += book.Bookmarks.Count;

            sb.AppendLine($"### KİTAP: {book.Title} (ID: {book.Id}) ###");
            sb.AppendLine(ExportToText(book));
            sb.AppendLine();
        }

        if (bookCount == 0)
        {
            sb.AppendLine("Kütüphanenizde henüz kayıtlı yer imi bulunmamaktadır.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Dışa aktarılmış veya elle yazılmış TXT metnini ayrıştırarak yer imleri listesi üretir.
    /// </summary>
    public static List<Bookmark> ParseBookmarksFromText(string text, out string? detectedBookTitle)
    {
        detectedBookTitle = null;
        var list = new List<Bookmark>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Kitap adını tespit etmeye çalış
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^Kitap\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                detectedBookTitle = match.Groups[1].Value.Trim();
                break;
            }
        }

        // Yer imi bloklarını ayrıştır
        int curPage = 1;
        long curPosMs = 0;
        string curTitle = "";
        long curCreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var noteBuilder = new StringBuilder();
        bool inNoteSection = false;
        bool hasData = false;

        void FlushCurrent()
        {
            if (!hasData) return;
            var note = noteBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(curTitle))
            {
                curTitle = curPosMs > 0 ? Fmt.SpokenTime(curPosMs) : $"Sayfa {curPage}";
            }

            list.Add(new Bookmark
            {
                Id = Guid.NewGuid().ToString(),
                Title = curTitle,
                PageNumber = curPage,
                PositionMs = curPosMs,
                Note = note,
                CreatedAt = curCreatedAt
            });

            curPage = 1;
            curPosMs = 0;
            curTitle = "";
            curCreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            noteBuilder.Clear();
            inNoteSection = false;
            hasData = false;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.Trim();

            // Blok sonu veya yeni yer imi başlangıcı
            if (trimmed.StartsWith("[YER İMİ", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(trimmed, @"^\[\d+\]") ||
                trimmed.StartsWith(SeparatorLine) ||
                (trimmed.StartsWith("### KİTAP:", StringComparison.OrdinalIgnoreCase)))
            {
                FlushCurrent();
                if (trimmed.StartsWith("### KİTAP:", StringComparison.OrdinalIgnoreCase))
                {
                    var bmMatch = Regex.Match(trimmed, @"### KİTAP:\s*(.+?)(?:\s*\(ID:|$)", RegexOptions.IgnoreCase);
                    if (bmMatch.Success) detectedBookTitle = bmMatch.Groups[1].Value.Trim();
                }
                continue;
            }

            // Sayfa:
            var pageMatch = Regex.Match(trimmed, @"^(?:Sayfa|Page)\s*[:\s]\s*(\d+)", RegexOptions.IgnoreCase);
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out var p))
            {
                curPage = Math.Max(1, p);
                hasData = true;
                inNoteSection = false;
                continue;
            }

            // Konum / Süre:
            var posMatch = Regex.Match(trimmed, @"^(?:Konum|Süre|PositionMs)\s*[:\s]\s*(.*)$", RegexOptions.IgnoreCase);
            if (posMatch.Success)
            {
                var val = posMatch.Groups[1].Value.Trim();
                var msMatch = Regex.Match(val, @"(\d+)\s*ms", RegexOptions.IgnoreCase);
                if (msMatch.Success && long.TryParse(msMatch.Groups[1].Value, out var msVal))
                {
                    curPosMs = msVal;
                }
                else
                {
                    // "01:23:45" veya "12:30" formatı
                    var timeMatch = Regex.Match(val, @"(?:(\d{1,2}):)?(\d{1,2}):(\d{2})");
                    if (timeMatch.Success)
                    {
                        int h = timeMatch.Groups[1].Success ? int.Parse(timeMatch.Groups[1].Value) : 0;
                        int m = int.Parse(timeMatch.Groups[2].Value);
                        int s = int.Parse(timeMatch.Groups[3].Value);
                        curPosMs = (h * 3600L + m * 60L + s) * 1000L;
                    }
                }
                hasData = true;
                inNoteSection = false;
                continue;
            }

            // Başlık:
            var titleMatch = Regex.Match(trimmed, @"^(?:Başlık|Title)\s*:\s*(.*)$", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                curTitle = titleMatch.Groups[1].Value.Trim();
                hasData = true;
                inNoteSection = false;
                continue;
            }

            // Tarih:
            var dateMatch = Regex.Match(trimmed, @"^(?:Tarih|Date)\s*:\s*(.*)$", RegexOptions.IgnoreCase);
            if (dateMatch.Success)
            {
                if (DateTime.TryParse(dateMatch.Groups[1].Value.Trim(), out var parsedDate))
                {
                    curCreatedAt = new DateTimeOffset(parsedDate).ToUnixTimeMilliseconds();
                }
                inNoteSection = false;
                continue;
            }

            // Not bölümü başlangıcı
            if (Regex.IsMatch(trimmed, @"^(?:İşaretlenen Metin|Not|Note)\s*(?:/.*)?\s*:\s*(.*)$", RegexOptions.IgnoreCase))
            {
                var inlineMatch = Regex.Match(trimmed, @"^(?:İşaretlenen Metin|Not|Note)\s*(?:/.*)?\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                var inlineVal = inlineMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(inlineVal) && inlineVal != "(Not eklenmemiş)")
                {
                    noteBuilder.AppendLine(inlineVal);
                }
                inNoteSection = true;
                hasData = true;
                continue;
            }

            // Notun devam eden satırları
            if (inNoteSection)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    noteBuilder.AppendLine(line);
                    hasData = true;
                }
                continue;
            }

            // Standart olmayan basit format: "Sayfa 14: Önemli cümle"
            var simpleMatch = Regex.Match(trimmed, @"^Sayfa\s+(\d+)\s*[:\-]\s*(.+)$", RegexOptions.IgnoreCase);
            if (simpleMatch.Success)
            {
                FlushCurrent();
                int sp = int.Parse(simpleMatch.Groups[1].Value);
                string sn = simpleMatch.Groups[2].Value.Trim();
                list.Add(new Bookmark
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = $"Sayfa {sp}",
                    PageNumber = sp,
                    Note = sn,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        FlushCurrent();
        return list;
    }

    /// <summary>
    /// Dışarıdan gelen yer imlerini hedef kitaba ekler. Çakışan veya aynı olan yer imlerini tekrarlamaz.
    /// Eklenen yer imi sayısını döner.
    /// </summary>
    public static int ImportToBook(BookRepository repo, string bookId, IEnumerable<Bookmark> imported)
    {
        var book = repo.GetBook(bookId);
        if (book == null) return 0;

        int count = 0;
        foreach (var bm in imported)
        {
            // Aynı sayfa/konum ve nota sahip olanları kontrol et
            bool exists = book.Bookmarks.Any(x =>
                x.PageNumber == bm.PageNumber &&
                x.PositionMs == bm.PositionMs &&
                (string.Equals(x.Note?.Trim(), bm.Note?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Title?.Trim(), bm.Title?.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (!exists)
            {
                book.Bookmarks.Add(bm);
                count++;
            }
        }

        if (count > 0)
        {
            repo.Save();
        }

        return count;
    }

    /// <summary>
    /// Kullanıcıya SaveFileDialog açarak tek kitabın yer imlerini dışa aktarır.
    /// </summary>
    public static bool ExportWithDialog(Window? owner, BookItem book)
    {
        if (book.Bookmarks.Count == 0)
        {
            MessageBox.Show(
                $"'{book.Title}' kitabında dışa aktarılacak kayıtlı bir yer imi bulunamadı.",
                "Yer İmleri", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var safeName = Regex.Replace(book.Title, @"[\\/:*?""<>|]", "_");
        if (safeName.Length > 40) safeName = safeName.Substring(0, 40);

        var dlg = new SaveFileDialog
        {
            Title = $"Yer İmlerini Dışa Aktar - {book.Title}",
            Filter = "Metin Dosyaları (*.txt)|*.txt|Tüm Dosyalar (*.*)|*.*",
            FileName = $"{safeName}_Yer_Imleri.txt",
            DefaultExt = ".txt"
        };

        if (dlg.ShowDialog(owner ?? Application.Current.MainWindow) == true)
        {
            try
            {
                var content = ExportToText(book);
                File.WriteAllText(dlg.FileName, content, new UTF8Encoding(true));
                App.Announce($"{book.Bookmarks.Count} yer imi başarıyla dışa aktarıldı.");
                MessageBox.Show(
                    $"Tebrikler!\n'{book.Title}' kitabına ait {book.Bookmarks.Count} adet yer imi ve işaretlenen satır başarıyla kaydedildi:\n\n{dlg.FileName}",
                    "Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dışa aktarma sırasında bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        return false;
    }

    /// <summary>
    /// Kullanıcıya OpenFileDialog açarak yer imlerini TXT dosyasından içe aktarır.
    /// </summary>
    public static int ImportWithDialog(Window? owner, BookRepository repo, BookItem targetBook)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Yer İmlerini İçe Aktar - {targetBook.Title}",
            Filter = "Metin Dosyaları (*.txt)|*.txt|Tüm Dosyalar (*.*)|*.*",
            Multiselect = false
        };

        if (dlg.ShowDialog(owner ?? Application.Current.MainWindow) == true)
        {
            try
            {
                var content = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                var parsed = ParseBookmarksFromText(content, out var detectedTitle);

                if (parsed.Count == 0)
                {
                    MessageBox.Show(
                        "Seçilen metin dosyasında geçerli bir yer imi veya not kaydı bulunamadı.",
                        "İçe Aktarma", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 0;
                }

                int added = ImportToBook(repo, targetBook.Id, parsed);
                string msg;
                if (added > 0)
                {
                    msg = $"{added} adet yeni yer imi '{targetBook.Title}' kitabına başarıyla eklendi.";
                    if (added < parsed.Count)
                    {
                        msg += $"\n({parsed.Count - added} adet mükerrer kayıt atlandı.)";
                    }
                }
                else
                {
                    msg = "Dosyadaki tüm yer imleri bu kitapta zaten mevcuttu. Yeni bir kayıt eklenmedi.";
                }

                App.Announce(msg);
                MessageBox.Show(msg, "İçe Aktarma Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                return added;
            }
            catch (Exception ex)
            {
                MessageBox.Show("İçe aktarma sırasında bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        return 0;
    }
}
