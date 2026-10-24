using System.IO;
using System.Text.Json;

namespace ErisilebilirKitapOkuyucu.Services;

public static class AppPaths
{
    public static readonly string DataDir = Directory.CreateDirectory(
        Environment.GetEnvironmentVariable("KITAP_DATA_DIR") is { Length: > 0 } custom ? custom :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErisilebilirKitapOkuyucu")).FullName;
    public static string ImportedDir => Directory.CreateDirectory(Path.Combine(DataDir, "imported_books")).FullName;
    public static string VoiceNotesDir => Directory.CreateDirectory(Path.Combine(DataDir, "voice_notes")).FullName;
    public static string TempDir => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ErisilebilirKitapOkuyucu")).FullName;
    public static string LogFile => Path.Combine(DataDir, "app.log");

    public static void Log(string message)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); }
        catch { /* günlük yazılamazsa uygulama etkilenmesin */ }
    }
}

/// <summary>Android'deki SharedPreferences tabanlı BookRepository'nin JSON dosyası karşılığı.</summary>
public class BookRepository
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _booksFile = Path.Combine(AppPaths.DataDir, "library.json");
    private readonly string _settingsFile = Path.Combine(AppPaths.DataDir, "settings.json");
    private readonly object _lock = new();

    private class LibraryData
    {
        public List<BookItem> Books { get; set; } = new();
        public List<string> Folders { get; set; } = new() { BookItem.RootFolder };
    }

    private LibraryData _data = new();
    public AppSettings Settings { get; private set; } = new();

    public event Action? LibraryChanged;
    public event Action? SettingsChanged;

    public IReadOnlyList<BookItem> Books => _data.Books;
    public IReadOnlyList<string> Folders => _data.Folders;

    public BookRepository()
    {
        try
        {
            if (File.Exists(_booksFile))
                _data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(_booksFile), Json) ?? new();
        }
        catch (Exception e) { AppPaths.Log("library load: " + e); _data = new(); }

        if (!_data.Folders.Contains(BookItem.RootFolder)) _data.Folders.Insert(0, BookItem.RootFolder);
        foreach (var b in _data.Books)
        {
            if (string.IsNullOrWhiteSpace(b.FolderName)) b.FolderName = BookItem.RootFolder;
            // Son sayfada kalınmışsa baştan başlat (Android davranışı)
            if (b.Type != BookType.AUDIO && b.TotalPages > 1 && b.CurrentPage >= b.TotalPages) b.CurrentPage = 1;
        }
        SortBooks();

        try
        {
            if (File.Exists(_settingsFile))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile), Json) ?? new();
        }
        catch (Exception e) { AppPaths.Log("settings load: " + e); Settings = new(); }
    }

    private void SortBooks() => _data.Books = _data.Books.OrderByDescending(b => b.LastReadTimestamp).ToList();

    private void Save(bool notify = true)
    {
        lock (_lock)
        {
            try
            {
                var tmp = _booksFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
                File.Move(tmp, _booksFile, true);
            }
            catch (Exception e) { AppPaths.Log("library save: " + e); }
        }
        if (notify) LibraryChanged?.Invoke();
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void AddBook(BookItem book)
    {
        var i = _data.Books.FindIndex(b => b.UriString == book.UriString);
        if (i >= 0) { book.LastReadTimestamp = Now; _data.Books[i] = book; }
        else _data.Books.Insert(0, book);
        SortBooks();
        Save();
    }

    public void AddBooks(IReadOnlyList<BookItem> books)
    {
        var baseTime = Now;
        for (int n = 0; n < books.Count; n++)
        {
            var b = books[n];
            b.LastReadTimestamp = baseTime + (books.Count - n) * 100L;
            var i = _data.Books.FindIndex(x => x.UriString == b.UriString);
            if (i >= 0) _data.Books[i] = b; else _data.Books.Add(b);
        }
        SortBooks();
        Save();
    }

    public BookItem? GetBook(string id) => _data.Books.FirstOrDefault(b => b.Id == id);

    public void UpdateProgress(string id, int page, long positionMs = 0)
    {
        var b = GetBook(id); if (b == null) return;
        b.CurrentPage = page; b.CurrentPositionMs = positionMs; b.LastReadTimestamp = Now;
        Save(notify: false);
    }

    public void UpdateTotalPages(string id, int pages)
    {
        var b = GetBook(id); if (b == null) return;
        b.TotalPages = Math.Max(1, pages);
        Save(notify: false);
    }

    public void UpdateDuration(string id, long durationMs)
    {
        var b = GetBook(id); if (b == null) return;
        b.TotalDurationMs = durationMs;
        Save(notify: false);
    }

    public void UpdateZipProgress(string id, int chapterIndex, long positionMs)
    {
        var b = GetBook(id); if (b == null) return;
        b.CurrentChapterIndex = Math.Clamp(chapterIndex, 0, Math.Max(0, b.Chapters.Count - 1));
        b.CurrentPositionMs = positionMs;
        if (chapterIndex >= 0 && chapterIndex < b.Chapters.Count) b.Chapters[chapterIndex].CurrentPositionMs = positionMs;
        b.LastReadTimestamp = Now;
        Save(notify: false);
    }

    public void UpdateZipDuration(string id, int chapterIndex, long durationMs)
    {
        var b = GetBook(id); if (b == null) return;
        b.TotalDurationMs = durationMs;
        if (chapterIndex >= 0 && chapterIndex < b.Chapters.Count) b.Chapters[chapterIndex].DurationMs = durationMs;
        Save(notify: false);
    }

    public void AddBookmark(string id, Bookmark bm)
    {
        var b = GetBook(id); if (b == null) return;
        b.Bookmarks.Add(bm);
        Save();
    }

    public void RemoveBookmark(string id, string bookmarkId)
    {
        var b = GetBook(id); if (b == null) return;
        var bm = b.Bookmarks.FirstOrDefault(x => x.Id == bookmarkId);
        if (bm?.AudioPath != null) TryDelete(bm.AudioPath);
        b.Bookmarks.RemoveAll(x => x.Id == bookmarkId);
        Save();
    }

    public void RemoveBook(string id)
    {
        var b = GetBook(id); if (b == null) return;
        foreach (var bm in b.Bookmarks) if (bm.AudioPath != null) TryDelete(bm.AudioPath);
        try
        {
            var imported = AppPaths.ImportedDir;
            if (b.IsZipArchive && b.Chapters.Count > 0)
            {
                var dir = Path.GetDirectoryName(b.Chapters[0].UriString);
                if (dir != null && Path.GetFileName(dir).StartsWith("zip_") &&
                    string.Equals(Path.GetDirectoryName(dir), imported, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(dir, true);
            }
            else if (b.UriString.StartsWith(imported, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(b.UriString); // yalnızca uygulamanın kendi kopyası silinir, kullanıcının orijinal dosyasına dokunulmaz
            }
        }
        catch (Exception e) { AppPaths.Log("remove book: " + e); }
        _data.Books.Remove(b);
        Save();
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    public void UpdateSettings(AppSettings s)
    {
        Settings = s;
        try { File.WriteAllText(_settingsFile, JsonSerializer.Serialize(s, Json)); }
        catch (Exception e) { AppPaths.Log("settings save: " + e); }
        SettingsChanged?.Invoke();
    }

    public bool CreateFolder(string name)
    {
        var n = name.Trim();
        if (n.Length == 0 || _data.Folders.Contains(n)) return false;
        _data.Folders.Add(n);
        Save();
        return true;
    }

    public bool RenameFolder(string oldName, string newName)
    {
        var n = newName.Trim();
        if (oldName == BookItem.RootFolder || n.Length == 0 || oldName == n) return false;
        var i = _data.Folders.IndexOf(oldName);
        if (i < 0) return false;
        if (!_data.Folders.Contains(n)) _data.Folders[i] = n; else _data.Folders.RemoveAt(i);
        foreach (var b in _data.Books.Where(b => b.FolderName == oldName)) b.FolderName = n;
        Save();
        return true;
    }

    public bool DeleteFolder(string name)
    {
        if (name == BookItem.RootFolder || !_data.Folders.Remove(name)) return false;
        foreach (var b in _data.Books.Where(b => b.FolderName == name)) b.FolderName = BookItem.RootFolder;
        Save();
        return true;
    }

    public void MoveBookToFolder(string id, string folder)
    {
        var b = GetBook(id); if (b == null) return;
        b.FolderName = folder;
        Save();
    }
}
