using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace ErisilebilirKitapOkuyucu.Services;

public record ShortcutDef(
    string Id,
    string Category,
    string Name,
    string Description,
    Key DefaultKey,
    ModifierKeys DefaultModifiers
);

/// <summary>
/// Uygulama genelinde klavye kısayollarını yöneten, özelleştiren ve tuş eşleşmelerini denetleyen servis.
/// </summary>
public static class ShortcutService
{
    public const string CatReader = "Okuma ve Gezinme";
    public const string CatAudio = "Sesli Kitap Oynatma";
    public const string CatGeneral = "Genel";

    public static readonly IReadOnlyList<ShortcutDef> Definitions = new List<ShortcutDef>
    {
        // ---------------------------------------------------- Okuma ve Gezinme (Reader)
        new("Reader.TogglePlay", CatReader, "Sesli Okumayı Başlat / Duraklat", "Metin okuma ekranında sesli okumayı başlatır veya duraklatır", Key.Space, ModifierKeys.None),
        new("Reader.NextPage", CatReader, "Sonraki Sayfaya Geç", "Metin kitabında sonraki sayfaya geçer", Key.PageDown, ModifierKeys.None),
        new("Reader.PrevPage", CatReader, "Önceki Sayfaya Geç", "Metin kitabında önceki sayfaya geçer", Key.PageUp, ModifierKeys.None),
        new("Reader.NextParagraph", CatReader, "Sonraki Paragrafa Git", "İmleci sonraki paragrafın başına taşır ve odaklanır", Key.Down, ModifierKeys.Control),
        new("Reader.PrevParagraph", CatReader, "Önceki Paragrafa Git", "İmleci önceki paragrafın başına taşır ve odaklanır", Key.Up, ModifierKeys.Control),
        new("Reader.NextSentence", CatReader, "Sonraki Parçaya / Cümleye Atla", "Sesli okuma sırasında sonraki cümleye veya parçaya atlar", Key.Right, ModifierKeys.Alt),
        new("Reader.PrevSentence", CatReader, "Önceki Parçaya / Cümleye Atla", "Sesli okuma sırasında önceki cümleye veya parçaya atlar", Key.Left, ModifierKeys.Alt),
        new("Reader.SpeakCurrentLine", CatReader, "Bulunulan Satırı / Paragrafı Oku", "Yalnızca imlecin bulunduğu satırı veya paragrafı seslendirir", Key.Return, ModifierKeys.None),
        new("Reader.ReadFromCurrentLine", CatReader, "Buradan İtibaren Sesli Oku", "İmlecin bulunduğu satırdan itibaren sürekli sesli okumayı başlatır", Key.Return, ModifierKeys.Control),
        new("Reader.CopyCurrentLine", CatReader, "Bulunulan Satırı / Paragrafı Kopyala", "İmlecin bulunduğu satırı veya seçimi panoya kopyalar", Key.C, ModifierKeys.Control),
        new("Reader.AddBookmark", CatReader, "Yer İmi ve Not / Alıntı Ekle", "Bulunulan sayfaya veya satıra yer imi ve serbest not ekler", Key.N, ModifierKeys.Control),
        new("Reader.ShowBookmarks", CatReader, "Yer İmleri ve Notlar Listesi", "Kitapta kayıtlı tüm yer imlerini ve alıntıları açar", Key.B, ModifierKeys.Control | ModifierKeys.Shift),
        new("Reader.Search", CatReader, "Belge İçinde Ara", "Kitap metninde kelime veya ifade arama penceresini açar", Key.F, ModifierKeys.Control),
        new("Reader.GoToPage", CatReader, "Sayfaya Git Kutusuna Odaklan", "Doğrudan sayfa numarası yazma kutusuna odaklanır", Key.G, ModifierKeys.Control),
        new("Reader.SleepTimer", CatReader, "Uyku Zamanlayıcısı Kur", "Belirtilen süre sonunda metin okumayı otomatik durdurur", Key.T, ModifierKeys.Control),
        new("Reader.Export", CatReader, "Sesi Dosyaya (WAV) Aktar", "Seçili sayfaları ses dosyası olarak dışa aktarır", Key.E, ModifierKeys.Control),
        new("Reader.RateUp", CatReader, "Sesli Okuma Hızını Artır", "Metin okuma hızını artırır", Key.Up, ModifierKeys.Control | ModifierKeys.Shift),
        new("Reader.RateDown", CatReader, "Sesli Okuma Hızını Azalt", "Metin okuma hızını azaltır", Key.Down, ModifierKeys.Control | ModifierKeys.Shift),
        new("Reader.VolUp", CatReader, "Ses Düzeyini Artır", "Metin okuma ses seviyesini %5 artırır", Key.Right, ModifierKeys.Control | ModifierKeys.Shift),
        new("Reader.VolDown", CatReader, "Ses Düzeyini Azalt", "Metin okuma ses seviyesini %5 azaltır", Key.Left, ModifierKeys.Control | ModifierKeys.Shift),

        // ---------------------------------------------------- Sesli Kitap Oynatma (Audio)
        new("Audio.TogglePlay", CatAudio, "Oynat / Duraklat", "Sesli kitabı oynatır veya duraklatır", Key.Space, ModifierKeys.None),
        new("Audio.SeekForward", CatAudio, "İleriye Sar", "Sesli kitabı belirlenen saniye kadar ileri sarar", Key.Right, ModifierKeys.None),
        new("Audio.SeekBackward", CatAudio, "Geriye Sar", "Sesli kitabı belirlenen saniye kadar geri sarar", Key.Left, ModifierKeys.None),
        new("Audio.NextChapter", CatAudio, "Sonraki Ayrıma Geç", "ZIP sesli kitaplarda sonraki ses dosyasına / ayrıma geçer", Key.Right, ModifierKeys.Control),
        new("Audio.PrevChapter", CatAudio, "Önceki Ayrıma Geç", "ZIP sesli kitaplarda önceki ses dosyasına / ayrıma geçer", Key.Left, ModifierKeys.Control),
        new("Audio.SleepTimer", CatAudio, "Uyku Zamanlayıcısı Kur", "Belirtilen süre sonunda sesli kitap oynatmayı durdurur", Key.T, ModifierKeys.Control),
        new("Audio.AddBookmark", CatAudio, "Dinlenen Konuma Yer İmi Ekle", "Sesli kitapta kalınan saniyeye yer imi ekler", Key.B, ModifierKeys.Control),
        new("Audio.ShowBookmarks", CatAudio, "Yer İmleri Listesini Aç", "Sesli kitaptaki kayıtlı yer imlerini listeler", Key.B, ModifierKeys.Control | ModifierKeys.Shift),
        new("Audio.RateUp", CatAudio, "Oynatma Hızını Artır", "Sesli kitap oynatma hızını artırır", Key.Up, ModifierKeys.Control),
        new("Audio.RateDown", CatAudio, "Oynatma Hızını Azalt", "Sesli kitap oynatma hızını azaltır", Key.Down, ModifierKeys.Control),
        new("Audio.VolUp", CatAudio, "Ses Seviyesini Artır", "Ses seviyesini %5 artırır", Key.Up, ModifierKeys.Shift),
        new("Audio.VolDown", CatAudio, "Ses Seviyesini Azalt", "Ses seviyesini %5 azaltır", Key.Down, ModifierKeys.Shift),

        // ---------------------------------------------------- Genel (General)
        new("General.Help", CatGeneral, "Yardım ve Kullanım Kılavuzu", "Detaylı kullanım kılavuzu ve kısayol penceresini açar", Key.F1, ModifierKeys.None),
        new("General.Settings", CatGeneral, "Ayarlar Penceresini Aç", "Uygulama ayarlarını açar", Key.OemComma, ModifierKeys.Control),
        new("General.Back", CatGeneral, "Kitaplığa Dön / Kapat", "Açık kitabı kapatır veya önceki ekrana döner", Key.Escape, ModifierKeys.None)
    };

    public static (Key Key, ModifierKeys Modifiers) GetCurrent(string id)
    {
        var def = Definitions.FirstOrDefault(d => d.Id == id);
        if (def == null) return (Key.None, ModifierKeys.None);

        var custom = App.Repo.Settings.CustomShortcuts;
        if (custom != null && custom.TryGetValue(id, out var val))
        {
            return Deserialize(val);
        }

        return (def.DefaultKey, def.DefaultModifiers);
    }

    public static bool Matches(string id, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var (boundKey, boundMods) = GetCurrent(id);

        if (boundKey == Key.None) return false;

        // Özel durum: Reader.AddBookmark için varsayılan olarak hem Ctrl+N hem Ctrl+B geçerli olabilir
        if (id == "Reader.AddBookmark" && !IsCustomized(id))
        {
            if (key == Key.B && mods == ModifierKeys.Control) return true;
        }

        // Özel durum: Reader.VolUp / VolDown için Alt+Yukarı/Aşağı alternatifini de varsayılan olarak destekle
        if (id == "Reader.VolUp" && !IsCustomized(id) && key == Key.Up && mods == ModifierKeys.Alt) return true;
        if (id == "Reader.VolDown" && !IsCustomized(id) && key == Key.Down && mods == ModifierKeys.Alt) return true;

        return key == boundKey && mods == boundMods;
    }

    public static bool IsCustomized(string id) =>
        App.Repo.Settings.CustomShortcuts != null && App.Repo.Settings.CustomShortcuts.ContainsKey(id);

    public static string GetDisplayString(string id)
    {
        var (key, mods) = GetCurrent(id);
        return FormatCombo(key, mods);
    }

    public static string FormatCombo(Key key, ModifierKeys mods)
    {
        if (key == Key.None) return "Atanmamış";

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatKey(key));

        return string.Join(" + ", parts);
    }

    public static string FormatKey(Key k) => k switch
    {
        Key.Space => "Boşluk",
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Up => "Yukarı Ok",
        Key.Down => "Aşağı Ok",
        Key.Left => "Sol Ok",
        Key.Right => "Sağ Ok",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.OemComma => "Virgül",
        Key.OemPeriod => "Nokta",
        Key.OemMinus => "Tire",
        Key.OemPlus => "Artı",
        Key.Back => "Geri (Backspace)",
        Key.Tab => "Tab",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.Insert => "Insert",
        _ => k.ToString()
    };

    public static string Serialize(Key k, ModifierKeys m)
    {
        if (k == Key.None) return "None";
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(k.ToString());
        return string.Join("+", parts);
    }

    public static (Key Key, ModifierKeys Modifiers) Deserialize(string str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Equals("None", StringComparison.OrdinalIgnoreCase))
            return (Key.None, ModifierKeys.None);

        var tokens = str.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = ModifierKeys.None;
        var key = Key.None;

        foreach (var t in tokens)
        {
            if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Control;
            else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Alt;
            else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Shift;
            else if (t.Equals("Win", StringComparison.OrdinalIgnoreCase) || t.Equals("Windows", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Windows;
            else
            {
                if (Enum.TryParse<Key>(t, true, out var parsedKey)) key = parsedKey;
            }
        }

        return (key, mods);
    }

    public static void SaveShortcut(string id, Key key, ModifierKeys mods)
    {
        var s = App.Repo.Settings;
        s.CustomShortcuts ??= new();
        s.CustomShortcuts[id] = Serialize(key, mods);
        App.Repo.UpdateSettings(s);
    }

    public static void ResetShortcut(string id)
    {
        var s = App.Repo.Settings;
        if (s.CustomShortcuts != null && s.CustomShortcuts.Remove(id))
        {
            App.Repo.UpdateSettings(s);
        }
    }

    public static void ResetAll()
    {
        var s = App.Repo.Settings;
        s.CustomShortcuts = new();
        App.Repo.UpdateSettings(s);
    }

    public static string? FindConflict(string targetId, Key key, ModifierKeys mods)
    {
        if (key == Key.None) return null;
        var targetDef = Definitions.FirstOrDefault(d => d.Id == targetId);
        if (targetDef == null) return null;

        foreach (var def in Definitions)
        {
            if (def.Id == targetId) continue;
            // Çakışmayı aynı kategori veya genel kategori içinde denetle
            if (def.Category == targetDef.Category || def.Category == CatGeneral || targetDef.Category == CatGeneral)
            {
                var (k, m) = GetCurrent(def.Id);
                if (k == key && m == mods)
                {
                    return def.Name;
                }
            }
        }

        return null;
    }
}
