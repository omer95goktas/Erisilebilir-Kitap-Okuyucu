using System.Text.Json.Serialization;

namespace ErisilebilirKitapOkuyucu;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookType { PDF, TXT, EPUB, DOCX, AUDIO }

public class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public int PageNumber { get; set; } = 1;
    public long PositionMs { get; set; }
    public string Note { get; set; } = "";
    public string? AudioPath { get; set; }
    public long AudioDurationMs { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class ChapterItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string UriString { get; set; } = "";
    public long DurationMs { get; set; }
    public long CurrentPositionMs { get; set; }
}

public class BookItem
{
    public const string RootFolder = "Ana Dizin";

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    /// <summary>Dosyanın tam yolu (Windows sürümünde URI değil yol saklanır).</summary>
    public string UriString { get; set; } = "";
    public BookType Type { get; set; }
    public int TotalPages { get; set; } = 1;
    public int CurrentPage { get; set; } = 1;
    public long CurrentPositionMs { get; set; }
    public long TotalDurationMs { get; set; }
    public long LastReadTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public List<Bookmark> Bookmarks { get; set; } = new();
    public bool IsZipArchive { get; set; }
    public List<ChapterItem> Chapters { get; set; } = new();
    public int CurrentChapterIndex { get; set; }
    public string? FolderName { get; set; } = RootFolder;

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        BookType.PDF => "PDF",
        BookType.TXT => "Metin",
        BookType.EPUB => "EPUB",
        BookType.DOCX => "Word",
        BookType.AUDIO => IsZipArchive ? "Sesli kitap (ZIP)" : "Sesli kitap",
        _ => ""
    };

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Type == BookType.AUDIO)
            {
                if (IsZipArchive && Chapters.Count > 0)
                {
                    var idx = Math.Clamp(CurrentChapterIndex, 0, Chapters.Count - 1);
                    return $"{TypeLabel}, {Chapters.Count} ayrım, şu an: {Chapters[idx].Title}, {Fmt.Time(CurrentPositionMs)}";
                }
                return $"{TypeLabel}, kaldığınız yer {Fmt.Time(CurrentPositionMs)}" +
                       (TotalDurationMs > 0 ? $" / {Fmt.Time(TotalDurationMs)}" : "");
            }
            return $"{TypeLabel}, sayfa {CurrentPage} / {TotalPages}";
        }
    }

    [JsonIgnore]
    public string AccessibleName => $"{Title}, {Summary}";
}

public class AppSettings
{
    public float TtsSpeechRate { get; set; } = 1.0f;
    public float TtsPitch { get; set; } = 1.0f;
    public bool AutoNextPage { get; set; } = true;
    public bool HighContrastMode { get; set; } = true;
    public int FontSizeSp { get; set; } = 20;
    public float AudioSpeed { get; set; } = 1.0f;
    public float AudioVolume { get; set; } = 1.0f;
    /// <summary>"sapi" (SAPI 5), "onecore" (Windows OneCore) ya da null (otomatik).</summary>
    public string? SelectedTtsEngine { get; set; }
    public string? SelectedVoiceId { get; set; }
    public int SeekIntervalSeconds { get; set; } = 15;
    public string PageTurnVoice { get; set; } = "Varsayılan";
    public bool PlayPageTurnSoundOnTtsNextPage { get; set; }
}

public record SearchResult(int Page, int LineIndex, string Snippet)
{
    public string Display => $"Sayfa {Page}: {Snippet}";
}

public static class Fmt
{
    public static string Time(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Ekran okuyucular için "1 saat 2 dakika 3 saniye" biçimi.</summary>
    public static string SpokenTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        var parts = new List<string>();
        if (t.TotalHours >= 1) parts.Add($"{(int)t.TotalHours} saat");
        if (t.Minutes > 0) parts.Add($"{t.Minutes} dakika");
        parts.Add($"{t.Seconds} saniye");
        return string.Join(" ", parts);
    }
}
