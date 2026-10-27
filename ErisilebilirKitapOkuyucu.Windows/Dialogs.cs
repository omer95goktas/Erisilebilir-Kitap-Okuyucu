using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ErisilebilirKitapOkuyucu.Services;
using Microsoft.Win32;

namespace ErisilebilirKitapOkuyucu;

/// <summary>Tüm iletişim kutularının temeli: Esc ile kapanır, açılışta ilk öğeye odaklanır, başlığı ekran okuyucuya söyler.</summary>
public abstract class DialogBase : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(18) };
    protected DialogBase(Window? owner, string title, double width = 560)
    {
        Title = title;
        Owner = owner ?? Application.Current.MainWindow;
        Width = width;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        AutomationProperties.SetName(this, title);
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Body.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["Heading"], Focusable = true });
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }

    protected static Button Btn(string text, RoutedEventHandler click, bool isDefault = false, string? help = null)
    {
        var b = new Button { Content = text, IsDefault = isDefault };
        AutomationProperties.SetName(b, text.Replace("_", ""));
        if (help != null) AutomationProperties.SetHelpText(b, help);
        b.Click += click;
        return b;
    }

    protected static StackPanel ButtonRow(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    protected void FocusFirst(UIElement el) => Loaded += (_, _) => Dispatcher.BeginInvoke(() => el.Focus(), DispatcherPriority.Input);
}

public sealed class PromptDialog : DialogBase
{
    private readonly TextBox _box = new();
    public string? Result { get; private set; }

    private PromptDialog(Window? owner, string title, string label, string initial, string okText) : base(owner, title, 520)
    {
        var lbl = new TextBlock { Text = label, Margin = new Thickness(4, 0, 4, 0) };
        Body.Children.Add(lbl);
        _box.Text = initial;
        AutomationProperties.SetLabeledBy(_box, lbl);
        AutomationProperties.SetName(_box, label);
        _box.SelectAll();
        Body.Children.Add(_box);
        Body.Children.Add(ButtonRow(
            Btn(okText, (_, _) => { Result = _box.Text; DialogResult = true; }, true),
            Btn("İptal", (_, _) => DialogResult = false)));
        FocusFirst(_box);
    }

    public static string? Ask(Window? owner, string title, string label, string initial = "", string okText = "Tamam")
    {
        var d = new PromptDialog(owner, title, label, initial, okText);
        return d.ShowDialog() == true ? d.Result : null;
    }
}

public sealed class ChoiceDialog : DialogBase
{
    private readonly ListBox _list = new() { MaxHeight = 380 };
    public int Result { get; private set; } = -1;

    private ChoiceDialog(Window? owner, string title, IEnumerable<string> items, int selected, string? description) : base(owner, title, 520)
    {
        if (description != null) Body.Children.Add(new TextBlock { Text = description, Margin = new Thickness(4, 0, 4, 8) });
        foreach (var it in items) _list.Items.Add(it);
        _list.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, _list.Items.Count - 1));
        AutomationProperties.SetName(_list, title);
        _list.MouseDoubleClick += (_, _) => Accept();
        _list.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        Body.Children.Add(_list);
        Body.Children.Add(ButtonRow(Btn("Seç", (_, _) => Accept(), true), Btn("Kapat", (_, _) => DialogResult = false)));
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => (_list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) as ListBoxItem ?? (UIElement)_list).Focus(), DispatcherPriority.Input);
    }

    private void Accept() { if (_list.SelectedIndex >= 0) { Result = _list.SelectedIndex; DialogResult = true; } }

    public static int Pick(Window? owner, string title, IEnumerable<string> items, int selected = 0, string? description = null)
    {
        var d = new ChoiceDialog(owner, title, items, selected, description);
        return d.ShowDialog() == true ? d.Result : -1;
    }
}

public sealed class AddBookmarkDialog : DialogBase
{
    private readonly TextBox _note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public string Note => _note.Text.Trim();

    public AddBookmarkDialog(Window? owner, string location, string suggestedNote) : base(owner, "Yer İmi Ekle", 560)
    {
        Body.Children.Add(new TextBlock { Text = "Konum: " + location, Margin = new Thickness(4, 0, 4, 8) });
        var lbl = new TextBlock { Text = "Yer İmi Notu (isteğe bağlı)", Margin = new Thickness(4, 0, 4, 0) };
        Body.Children.Add(lbl);
        _note.Text = suggestedNote;
        AutomationProperties.SetName(_note, "Yer imi notu");
        Body.Children.Add(_note);

        Body.Children.Add(ButtonRow(
            Btn("Kaydet", (_, _) => { DialogResult = true; }, true),
            Btn("İptal", (_, _) => { DialogResult = false; })));
        FocusFirst(_note);
    }
}

public sealed class BookmarksDialog : DialogBase
{
    private readonly BookItem _book;
    private readonly ListBox _list = new() { MaxHeight = 360, MinHeight = 120 };
    public Bookmark? GoTo { get; private set; }

    public BookmarksDialog(Window? owner, BookItem book) : base(owner, "Yer İmleri", 640)
    {
        _book = book;
        AutomationProperties.SetName(_list, "Yer imleri listesi");
        _list.MouseDoubleClick += (_, _) => Go();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Go(); }
            else if (e.Key == Key.Delete) { e.Handled = true; Remove(); }
        };
        Body.Children.Add(_list);
        Refresh();
        Body.Children.Add(ButtonRow(
            Btn("Git", (_, _) => Go(), true),
            Btn("Sil", (_, _) => Remove(), help: "Seçili yer imini siler (Delete tuşu)"),
            Btn("Kapat", (_, _) => DialogResult = false)));
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (_list.Items.Count > 0) (_list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(); else _list.Focus(); }, DispatcherPriority.Input);
    }

    private string Describe(Bookmark b)
    {
        var where = _book.Type == BookType.AUDIO ? Fmt.SpokenTime(b.PositionMs) : $"sayfa {b.PageNumber}";
        var s = $"{b.Title}, {where}";
        if (!string.IsNullOrWhiteSpace(b.Note)) s += ", not: " + b.Note;
        return s;
    }

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var b in _book.Bookmarks.OrderBy(x => x.CreatedAt))
        {
            var item = new ListBoxItem { Content = new TextBlock { Text = Describe(b) }, Tag = b };
            AutomationProperties.SetName(item, Describe(b));
            _list.Items.Add(item);
        }
        if (_list.Items.Count == 0)
        {
            var empty = new ListBoxItem { Content = "Henüz yer imi yok.", IsEnabled = false };
            AutomationProperties.SetName(empty, "Henüz yer imi yok.");
            _list.Items.Add(empty);
        }
        else _list.SelectedIndex = 0;
    }

    private Bookmark? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as Bookmark;

    private void Go() { if (Selected is { } b) { GoTo = b; DialogResult = true; } }

    private void Remove()
    {
        if (Selected is not { } b) return;
        var idx = _list.SelectedIndex;
        App.Repo.RemoveBookmark(_book.Id, b.Id);
        Refresh();
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(idx, _list.Items.Count - 1);
        App.Announce("Yer imi silindi");
        (_list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, _list.SelectedIndex)) as ListBoxItem)?.Focus();
    }
}

public sealed class SearchDialog : DialogBase
{
    private readonly DocumentSession _doc;
    private readonly TextBox _query = new();
    private readonly ListBox _results = new() { MaxHeight = 320, MinHeight = 100 };
    private readonly TextBlock _status = new() { Margin = new Thickness(4) };
    public int? GoToPage { get; private set; }

    public SearchDialog(Window? owner, DocumentSession doc) : base(owner, "Belgede Ara", 640)
    {
        _doc = doc;
        var lbl = new TextBlock { Text = "Aranacak kelime veya ifade", Margin = new Thickness(4, 0, 4, 0) };
        Body.Children.Add(lbl);
        AutomationProperties.SetName(_query, "Aranacak kelime veya ifade");
        _query.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunSearch(); } };
        Body.Children.Add(_query);
        Body.Children.Add(Btn("Ara", async (_, _) => await RunSearch(), false));
        _status.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        Body.Children.Add(_status);
        AutomationProperties.SetName(_results, "Arama sonuçları");
        _results.MouseDoubleClick += (_, _) => Accept();
        _results.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        Body.Children.Add(_results);
        Body.Children.Add(ButtonRow(Btn("Sayfaya Git", (_, _) => Accept(), false), Btn("Kapat", (_, _) => DialogResult = false)));
        FocusFirst(_query);
    }

    private async Task RunSearch()
    {
        _results.Items.Clear();
        if (string.IsNullOrWhiteSpace(_query.Text)) { _status.Text = "Lütfen aranacak bir ifade yazın."; return; }
        _status.Text = "Aranıyor…";
        var found = await _doc.SearchAsync(_query.Text);
        foreach (var r in found)
        {
            var item = new ListBoxItem { Content = new TextBlock { Text = r.Display }, Tag = r };
            AutomationProperties.SetName(item, r.Display);
            _results.Items.Add(item);
        }
        _status.Text = found.Count == 0 ? "Sonuç bulunamadı." : $"{found.Count} sonuç bulundu. Sonuçlar listesine geçmek için Sekme tuşuna basın.";
        if (found.Count > 0) _results.SelectedIndex = 0;
    }

    private void Accept()
    {
        if ((_results.SelectedItem as ListBoxItem)?.Tag is SearchResult r) { GoToPage = r.Page; DialogResult = true; }
    }
}

public sealed class ExportDialog : DialogBase
{
    private readonly TextBox _pages = new();
    private readonly ProgressBar _bar = new() { Height = 22, Minimum = 0, Maximum = 100, Margin = new Thickness(4, 10, 4, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { Margin = new Thickness(4) };
    private readonly Button _start;
    private readonly Button _close;
    private readonly BookItem _book;
    private readonly DocumentSession _doc;
    private readonly int _currentPage;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public ExportDialog(Window? owner, BookItem book, DocumentSession doc, int currentPage) : base(owner, "Sesi Dosyaya Aktar", 600)
    {
        _book = book; _doc = doc; _currentPage = currentPage;
        Body.Children.Add(new TextBlock
        {
            Text = $"Seçili konuşma sesiyle sayfalar WAV ses dosyasına dönüştürülür. Boş bırakılırsa mevcut sayfa ({currentPage}) aktarılır. " +
                   "Birden çok sayfa için + kullanın (örnek: 50+51+52). En fazla 3 sayfa.",
            Margin = new Thickness(4, 0, 4, 8)
        });
        var lbl = new TextBlock { Text = "Sayfalar (En fazla 3 sayfa)", Margin = new Thickness(4, 0, 4, 0) };
        Body.Children.Add(lbl);
        AutomationProperties.SetName(_pages, "Sayfalar, en fazla 3 sayfa, artı ile ayırın");
        Body.Children.Add(_pages);
        _status.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        Body.Children.Add(_bar);
        Body.Children.Add(_status);
        _start = Btn("Dışa Aktar", async (_, _) => await Run(), true);
        _close = Btn("Kapat", (_, _) => { if (_busy) _cts?.Cancel(); else DialogResult = false; });
        Body.Children.Add(ButtonRow(_start, _close));
        FocusFirst(_pages);
    }

    public static List<int> ParsePages(string input, int defaultPage, int max)
    {
        if (string.IsNullOrWhiteSpace(input)) return new() { defaultPage };
        var list = new List<int>();
        foreach (var part in input.Split('+', ',', ';', ' '))
            if (int.TryParse(part.Trim(), out var n) && n >= 1 && n <= max && !list.Contains(n)) list.Add(n);
        return list.Take(3).ToList();
    }

    private async Task Run()
    {
        var pages = ParsePages(_pages.Text, _currentPage, _doc.TotalPages);
        if (pages.Count == 0) { _status.Text = $"Geçerli sayfa numarası girin (1 - {_doc.TotalPages})."; return; }

        var safeTitle = string.Concat(_book.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var label = string.Join("-", pages);
        var dlg = new SaveFileDialog
        {
            Title = "Ses dosyasını kaydet",
            Filter = "WAV ses dosyası|*.wav",
            FileName = $"{safeTitle} - sayfa {label}.wav",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        if (dlg.ShowDialog(this) != true) return;

        _busy = true; _start.IsEnabled = false; _pages.IsEnabled = false;
        _bar.Visibility = Visibility.Visible; _bar.Value = 0;
        _close.Content = "İptal Et";
        AutomationProperties.SetName(_close, "İptal Et");
        _status.Text = "Ses dosyası hazırlanıyor…";
        App.Announce("Dışa aktarma başladı");
        _cts = new CancellationTokenSource();
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in pages) sb.Append(await _doc.GetPageTextAsync(p)).Append("\n\n");
            int last = -1;
            var progress = new Progress<int>(v =>
            {
                _bar.Value = v;
                _status.Text = $"%{v} tamamlandı";
                if (v / 25 != last / 25) { last = v; App.Announce($"Yüzde {v}"); }
            });
            var (ok, error) = await App.Tts.ExportToWavAsync(sb.ToString(), dlg.FileName, progress, _cts.Token);
            if (ok)
            {
                _status.Text = "Ses dosyası kaydedildi: " + dlg.FileName;
                App.Announce("Ses dosyası kaydedildi", true);
            }
            else
            {
                _status.Text = "Dışa aktarma başarısız: " + error;
                App.Announce("Dışa aktarma başarısız. " + error, true);
            }
        }
        finally
        {
            _busy = false; _start.IsEnabled = true; _pages.IsEnabled = true;
            _close.Content = "Kapat";
            AutomationProperties.SetName(_close, "Kapat");
        }
    }
}

public sealed class HelpDialog : DialogBase
{
    private readonly WebBrowser _browser = new()
    {
        MinHeight = 400,
        Height = 460
    };

    public HelpDialog(Window? owner) : base(owner, "Klavye Kısayolları ve Yardım", 760)
    {
        var html = @"<!DOCTYPE html>
<html lang=""tr"">
<head>
<meta charset=""utf-8"">
<style>
  body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; font-size: 16px; line-height: 1.6; color: #111; background: #fff; padding: 16px; margin: 0; }
  h1 { font-size: 22px; color: #004d99; border-bottom: 2px solid #004d99; padding-bottom: 6px; margin-top: 0; }
  h2 { font-size: 18px; color: #1a5276; margin-top: 20px; margin-bottom: 8px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }
  ul { list-style-type: square; margin-top: 4px; padding-left: 24px; }
  li { margin-bottom: 6px; }
  strong { color: #000; }
</style>
</head>
<body>
<h1>Klavye Kısayolları ve Yardım</h1>

<h2>Kitaplık Kısayolları</h2>
<ul>
  <li><strong>Ctrl + O:</strong> Kitap Ekle (PDF, TXT, EPUB, DOCX, ZIP ve ses dosyaları)</li>
  <li><strong>Ctrl + U:</strong> Web Bağlantısından Makale Ekle</li>
  <li><strong>Ctrl + Shift + V:</strong> Panodaki Metni Kitap Olarak Ekle</li>
  <li><strong>Ctrl + N:</strong> Yeni Klasör Oluştur</li>
  <li><strong>Enter:</strong> Seçili kitabı veya ayrımı aç</li>
  <li><strong>Delete:</strong> Seçili kitabı kitaplıktan kaldır</li>
  <li><strong>Ctrl + Virgül:</strong> Ayarlar penceresini aç</li>
  <li><strong>F1:</strong> Bu yardım ve kısayollar penceresini aç</li>
  <li><strong>Esc:</strong> Önceki ekrana dön veya açık pencereyi kapat</li>
</ul>

<h2>Belge ve Kitap Okuyucu</h2>
<ul>
  <li><strong>Yukarı / Aşağı Ok Tuşları:</strong> Satır satır okuma</li>
  <li><strong>Ctrl + Yukarı Ok / Ctrl + Aşağı Ok:</strong> Önceki veya sonraki paragrafa git ve odaklan</li>
  <li><strong>Sol / Sağ Ok Tuşları:</strong> Karakter veya kelime gezinmesi</li>
  <li><strong>Enter:</strong> Yalnızca imlecin üzerinde bulunduğu satırı seslendir</li>
  <li><strong>Ctrl + C:</strong> Bulunulan satırın metnini panoya kopyala</li>
  <li><strong>Ctrl + Enter:</strong> Bulunulan satırdan itibaren sürekli sesli okumayı başlat</li>
  <li><strong>Boşluk Tuşu:</strong> Sayfayı sesli okumayı başlat veya duraklat</li>
  <li><strong>Ctrl + Sağ Ok / Ctrl + Sol Ok:</strong> Sonraki veya önceki sayfaya geç</li>
  <li><strong>Alt + Sağ Ok / Alt + Sol Ok:</strong> Sonraki veya önceki parçaya / cümleye atla</li>
  <li><strong>Ctrl + G:</strong> Sayfa numarası yazarak doğrudan sayfaya git</li>
  <li><strong>Ctrl + F:</strong> Belge içinde kelime / metin ara</li>
  <li><strong>Ctrl + B:</strong> Bulunulan konuma yer imi ekle</li>
  <li><strong>Ctrl + Shift + B:</strong> Kayıtlı yer imlerini listele</li>
  <li><strong>Ctrl + E:</strong> Seçili sayfaları ses (WAV) dosyasına aktar</li>
  <li><strong>Ctrl + Shift + Yukarı / Aşağı Ok:</strong> Sesli okuma hızını artır veya azalt</li>
</ul>

<h2>Sesli Kitap Oynatıcı</h2>
<ul>
  <li><strong>Boşluk Tuşu:</strong> Sesli kitabı oynat veya duraklat</li>
  <li><strong>Sol / Sağ Ok Tuşları:</strong> Belirlenen saniye kadar geri veya ileri sar</li>
  <li><strong>Ctrl + Sağ Ok / Ctrl + Sol Ok:</strong> Sonraki veya önceki ayrım (ZIP arşivleri)</li>
  <li><strong>Ctrl + B:</strong> Dinlenen konuma yer imi ekle</li>
  <li><strong>Ctrl + T:</strong> Uyku zamanlayıcısı kur</li>
  <li><strong>Ctrl + Yukarı Ok / Ctrl + Aşağı Ok:</strong> Oynatma hızını artır veya azalt</li>
  <li><strong>Shift + Yukarı Ok / Shift + Aşağı Ok:</strong> Ses düzeyini artır veya azalt</li>
</ul>

<h2>Ses Motorları ve Yasal Sorumluluk Bildirimi</h2>
<p style=""font-size:14px; color:#333;"">
  Uygulama; tamamen yerel ve internetsiz çalışan açık kaynak nöral yapay zeka sesi (Piper Fahrettin), sistem sesleri (Windows OneCore ve SAPI 5) ile çevrim içi doğal sesleri (Microsoft Emel ve isteğe bağlı kişisel API ile Google Cloud) destekler.
</p>
<p style=""font-size:14px; color:#333;"">
  <strong>Üçüncü Taraf Servisler ve API Sorumluluk Reddi:</strong> Uygulama içerisinde kullanıcı tarafından isteğe bağlı olarak girilen Google Cloud ve benzeri üçüncü taraf API anahtarları tamamen kullanıcının kendi sorumluluğundadır. İlgili servis sağlayıcıları tarafından belirlenen kotaların aşılması veya faturalandırma sebebiyle doğabilecek hiçbir maddi borç, ücret veya mali yükümlülükten uygulama geliştiricisi (Ömer Göktaş) sorumlu tutulamaz. Girilen API anahtarları hiçbir harici sunucuya iletilmez, yalnızca kullanıcının kendi bilgisayarında yerel olarak saklanır.
</p>

<h2>Geliştirici ve İletişim</h2>
<ul>
  <li><strong>Uygulama:</strong> Erişilebilir Kitap Okuyucu (Windows v{Services.UpdateService.CurrentVersion})</li>
  <li><strong>Geliştirici:</strong> Ömer Göktaş</li>
  <li><strong>E-Posta:</strong> mail@omergoktas.net</li>
  <li><strong>Web Sitesi:</strong> https://omergoktas.net</li>
</ul>
</body>
</html>";

        _browser.NavigateToString(html);
        Body.Children.Add(_browser);

        var webBtn = Btn("Web Sitesini Aç", (_, _) => OpenLink("https://omergoktas.net"), help: "Geliştiricinin resmi web sitesini tarayıcıda açar");
        var mailBtn = Btn("E-Posta Gönder", (_, _) => OpenLink("mailto:mail@omergoktas.net"), help: "Geliştiriciye e-posta göndermek için varsayılan posta uygulamasını açar");
        var privacyBtn = Btn("Gizlilik Politikası", (_, _) => new PrivacyDialog(this).ShowDialog(), help: "Uygulamanın gizlilik politikasını görüntüler");
        var closeBtn = Btn("Kapat", (_, _) => DialogResult = false, true);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(webBtn);
        actions.Children.Add(mailBtn);
        actions.Children.Add(privacyBtn);
        actions.Children.Add(closeBtn);
        Body.Children.Add(actions);

        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _browser.Focus();
        }, DispatcherPriority.Input);
    }

    private static void OpenLink(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Announce("Bağlantı açılamadı: " + ex.Message, true); }
    }
}

public sealed class PrivacyDialog : DialogBase
{
    private readonly WebBrowser _browser = new()
    {
        MinHeight = 360,
        Height = 440
    };

    public PrivacyDialog(Window? owner) : base(owner, "Gizlilik Politikası", 680)
    {
        var html = @"<!DOCTYPE html>
<html lang=""tr"">
<head>
<meta charset=""utf-8"">
<style>
  body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; font-size: 16px; line-height: 1.6; color: #111; background: #fff; padding: 16px; margin: 0; }
  h1 { font-size: 22px; color: #004d99; border-bottom: 2px solid #004d99; padding-bottom: 6px; margin-top: 0; }
  h2 { font-size: 18px; color: #1a5276; margin-top: 18px; margin-bottom: 8px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }
  p { margin: 6px 0 12px 0; }
  ul { list-style-type: square; margin-top: 4px; padding-left: 24px; }
  li { margin-bottom: 6px; }
  strong { color: #000; }
</style>
</head>
<body>
<h1>Erişilebilir Kitap Okuyucu - Gizlilik Politikası</h1>
<p><em>Son Güncelleme: 24 Ekim 2026</em></p>

<h2>1. Kişisel Veri Toplanmaz</h2>
<p>Erişilebilir Kitap Okuyucu uygulaması, kullanıcıların kişisel gizliliğine ve güvenliğine en üst düzeyde saygı duyar. Uygulamamız herhangi bir ad, soyad, konum veya kullanıcı kimliği toplamaz, kaydetmez veya üçüncü taraflarla paylaşmaz.</p>

<h2>2. Kitaplar ve Yerel Veriler</h2>
<p>Uygulamaya eklediğiniz tüm kitaplar (PDF, TXT, EPUB, DOCX, sesli kitaplar), yer imleriniz ve okuma geçmişiniz yalnızca kendi bilgisayarınızda yerel olarak saklanır. Hiçbir belgeniz herhangi bir harici sunucuya veya bulut ortamına yüklenmez.</p>

<h2>3. Metin Okuma (TTS) ve Ses Motorları</h2>
<p>Metin seslendirme işlemleri doğrudan Windows işletim sisteminizde yerel olarak kurulu bulunan Microsoft SAPI 5 ve Windows OneCore ses motorları aracılığıyla tamamen çevrimdışı gerçekleştirilir. Okunan metinler internete aktarılmaz.</p>

<h2>4. Web Bağlantıları</h2>
<p>Web makalesi ekleme özelliğini kullandığınızda, uygulama yalnızca sizin belirttiğiniz adresteki açık web sayfasını indirir, reklam ve gürültüleri ayıklayarak okumanız için yerel cihazınıza kaydeder.</p>

<h2>5. İletişim</h2>
<p>Gizlilik politikamız veya uygulamayla ilgili her türlü görüş, öneri ve destek talebiniz için geliştiriciyle iletişime geçebilirsiniz:</p>
<ul>
  <li><strong>Geliştirici:</strong> Ömer Göktaş</li>
  <li><strong>E-Posta:</strong> mail@omergoktas.net</li>
  <li><strong>Web Sitesi:</strong> https://omergoktas.net</li>
</ul>
</body>
</html>";

        _browser.NavigateToString(html);
        Body.Children.Add(_browser);
        Body.Children.Add(ButtonRow(Btn("Kapat", (_, _) => DialogResult = false, true)));

        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _browser.Focus();
        }, DispatcherPriority.Input);
    }
}
