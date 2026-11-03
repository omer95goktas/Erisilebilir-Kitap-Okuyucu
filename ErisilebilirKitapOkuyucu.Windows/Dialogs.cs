using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (e.Key == Key.Escape || (key == Key.F4 && (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || e.Key == Key.System)))
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
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

public sealed class ChangeShortcutDialog : DialogBase
{
    private readonly ShortcutDef _def;
    private readonly TextBlock _statusText = new() { Margin = new Thickness(4, 12, 4, 12), FontWeight = FontWeights.Bold, FontSize = 15 };

    public ChangeShortcutDialog(Window? owner, ShortcutDef def) : base(owner, $"{def.Name} - Kısayol Belirle", 560)
    {
        _def = def;
        var (curKey, curMods) = ShortcutService.GetCurrent(def.Id);

        Body.Children.Add(new TextBlock { Text = $"Eylem: {def.Name} ({def.Category})", FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 4, 4) });
        Body.Children.Add(new TextBlock { Text = def.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) });

        var promptBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(25, 0, 102, 204)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 102, 204)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(4, 0, 4, 12),
            Focusable = true
        };
        AutomationProperties.SetName(promptBox, $"{def.Name} için yeni kısayol tuşuna basın. Kısayolu kaldırmak için Delete, iptal için Escape tuşuna basabilirsiniz.");

        var promptText = new TextBlock
        {
            Text = "Lütfen atamak istediğiniz yeni tuşa veya tuş kombinasyonuna basın.\nTuşa bastığınız anda yeni kısayol otomatik olarak kaydedilecek ve bu pencere kapanacaktır.\n\n• Örnek tuşlar: Ctrl + P, Boşluk, Alt + Sağ Ok, F2 vb.\n• Kısayolu kaldırmak için: Delete (Sil)\n• İptal etmek için: Esc",
            TextWrapping = TextWrapping.Wrap
        };
        promptBox.Child = promptText;
        Body.Children.Add(promptBox);

        _statusText.Text = "Mevcut Kısayol: " + ShortcutService.FormatCombo(curKey, curMods);
        AutomationProperties.SetName(_statusText, _statusText.Text);
        Body.Children.Add(_statusText);

        var btnClear = Btn("Kısayolu Kaldır (Delete)", (_, _) => Clear());
        var btnCancel = Btn("İptal (Esc)", (_, _) => { DialogResult = false; });

        Body.Children.Add(ButtonRow(btnClear, btnCancel));

        PreviewKeyDown += OnPreviewKey;
        FocusFirst(promptBox);

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                SystemSounds.Asterisk.Play();
                App.Announce($"{_def.Name} için yeni kısayol tuşuna basın. İptal için Escape.", important: true);
            }, DispatcherPriority.Input);
        };
    }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin ||
            key == Key.Capital || key == Key.CapsLock ||
            key == Key.Insert || key == Key.None)
        {
            return;
        }

        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }

        if (key == Key.Delete && mods == ModifierKeys.None)
        {
            e.Handled = true;
            Clear();
            return;
        }

        e.Handled = true;
        var combo = ShortcutService.FormatCombo(key, mods);

        var conflict = ShortcutService.FindConflict(_def.Id, key, mods);
        if (conflict != null)
        {
            SystemSounds.Hand.Play();
            var r = MessageBox.Show(this,
                $"Bu kısayol ({combo}) zaten '{conflict}' eylemine atanmış.\n\nYine de '{_def.Name}' eylemine atamak istiyor musunuz?",
                "Kısayol Çakışması", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes)
            {
                App.Announce("Atama iptal edildi. Lütfen başka bir kısayol tuşuna basın.", important: true);
                return;
            }
        }

        ShortcutService.SaveShortcut(_def.Id, key, mods);
        SystemSounds.Asterisk.Play();
        App.Announce($"{_def.Name} kısayolu {combo} olarak kaydedildi.", important: true);
        DialogResult = true;
    }

    private void Clear()
    {
        ShortcutService.SaveShortcut(_def.Id, Key.None, ModifierKeys.None);
        SystemSounds.Asterisk.Play();
        App.Announce($"{_def.Name} kısayolu kaldırıldı.", important: true);
        DialogResult = true;
    }
}

public sealed class AddBookmarkDialog : DialogBase
{
    private readonly TextBox _note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public string Note => _note.Text.Trim();

    public AddBookmarkDialog(Window? owner, string location, string suggestedNote) : base(owner, "Yer İmi ve Not / Alıntı Ekle", 560)
    {
        Body.Children.Add(new TextBlock { Text = "Konum: " + location, Margin = new Thickness(4, 0, 4, 8) });
        var lbl = new TextBlock { Text = "Alıntı veya Notunuz (isteğe bağlı):", Margin = new Thickness(4, 0, 4, 0) };
        Body.Children.Add(lbl);
        _note.Text = suggestedNote;
        AutomationProperties.SetName(_note, "Alıntı veya not metni");
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

    public BookmarksDialog(Window? owner, BookItem book) : base(owner, "Yer İmleri ve Notlar", 680)
    {
        _book = book;
        AutomationProperties.SetName(_list, "Yer imleri ve notlar listesi");
        _list.MouseDoubleClick += (_, _) => Go();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Go(); }
            else if (e.Key == Key.Delete) { e.Handled = true; Remove(); }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; CopyNote(); }
        };
        Body.Children.Add(_list);
        Refresh();
        Body.Children.Add(ButtonRow(
            Btn("Git", (_, _) => Go(), true),
            Btn("Notu Kopyala", (_, _) => CopyNote(), help: "Seçili notu veya alıntıyı panoya kopyalar (Ctrl+C)"),
            Btn("Dışa Aktar (.txt)...", (_, _) => BookmarkService.ExportWithDialog(this, _book), help: "Bu kitaptaki yer imlerini TXT dosyası olarak dışa aktarır"),
            Btn("İçe Aktar (.txt)...", (_, _) => { if (BookmarkService.ImportWithDialog(this, App.Repo, _book) > 0) Refresh(); }, help: "TXT dosyasındaki yer imlerini bu kitaba yükler"),
            Btn("Sil", (_, _) => Remove(), help: "Seçili yer imini siler (Delete tuşu)"),
            Btn("Kapat", (_, _) => DialogResult = false)));
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (_list.Items.Count > 0) (_list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(); else _list.Focus(); }, DispatcherPriority.Input);
    }

    private void CopyNote()
    {
        if (Selected is not { } b) return;
        var textToCopy = !string.IsNullOrWhiteSpace(b.Note) ? b.Note : b.Title;
        try
        {
            Clipboard.SetText(textToCopy);
            App.Announce("Not veya alıntı panoya kopyalandı");
        }
        catch (Exception ex)
        {
            App.Announce("Kopyalanamadı: " + ex.Message);
        }
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
            var empty = new ListBoxItem { Content = "Henüz yer imi veya not yok.", IsEnabled = false };
            AutomationProperties.SetName(empty, "Henüz yer imi veya not yok.");
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

public sealed class BookmarkManagerDialog : DialogBase
{
    private readonly ComboBox _bookCombo = new() { Margin = new Thickness(0, 4, 0, 10), MinHeight = 32 };
    private readonly TextBlock _infoText = new() { Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
    private readonly ListBox _list = new() { MaxHeight = 360, MinHeight = 140 };
    private List<BookItem> _books = new();

    public Bookmark? SelectedBookmark { get; private set; }
    public BookItem? SelectedBook { get; private set; }
    public bool ShouldOpenBook { get; private set; }

    public BookmarkManagerDialog(Window? owner, string? initialBookId = null) : base(owner, "Yer İmleri Yöneticisi", 720)
    {
        AutomationProperties.SetName(_bookCombo, "Kitap seçimi");
        AutomationProperties.SetHelpText(_bookCombo, "Yer imlerini görüntülemek ve yönetmek istediğiniz kitabı seçin");

        AutomationProperties.SetName(_list, "Yer imleri ve notlar listesi");
        _list.MouseDoubleClick += (_, _) => OpenSelectedBookmark();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; OpenSelectedBookmark(); }
            else if (e.Key == Key.Delete) { e.Handled = true; RemoveSelected(); }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; CopyNote(); }
        };

        Body.Children.Add(new TextBlock { Text = "Kitap Seçin:", Margin = new Thickness(0, 4, 0, 2) });
        Body.Children.Add(_bookCombo);
        Body.Children.Add(_infoText);
        Body.Children.Add(_list);

        LoadBooks(initialBookId);

        _bookCombo.SelectionChanged += (_, _) => RefreshList();

        Body.Children.Add(ButtonRow(
            Btn("Kitapta Aç / Git", (_, _) => OpenSelectedBookmark(), true, help: "Seçili yer iminin bulunduğu kitabı açar"),
            Btn("Notu Kopyala", (_, _) => CopyNote(), help: "Seçili notu veya alıntıyı panoya kopyalar (Ctrl+C)"),
            Btn("Dışa Aktar (.txt)...", (_, _) => ExportCurrent(), help: "Seçili kitabın yer imlerini TXT dosyasına kaydeder"),
            Btn("İçe Aktar (.txt)...", (_, _) => ImportCurrent(), help: "TXT dosyasındaki yer imlerini seçili kitaba ekler"),
            Btn("Tümünü Dışa Aktar...", (_, _) => ExportAll(), help: "Tüm kitaplardaki yer imlerini tek bir TXT dosyasına yedekler"),
            Btn("Sil", (_, _) => RemoveSelected(), help: "Seçili yer imini siler (Delete)"),
            Btn("Kapat", (_, _) => DialogResult = false)
        ));

        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_bookCombo.Items.Count > 0) _bookCombo.Focus();
            else _list.Focus();
        }, DispatcherPriority.Input);
    }

    private void LoadBooks(string? selectBookId)
    {
        _books = App.Repo.Books.OrderByDescending(x => x.Bookmarks.Count).ThenBy(x => x.Title).ToList();
        _bookCombo.Items.Clear();

        int selectedIdx = 0;
        for (int i = 0; i < _books.Count; i++)
        {
            var b = _books[i];
            var countStr = b.Bookmarks.Count > 0 ? $" ({b.Bookmarks.Count} yer imi)" : " (0 yer imi)";
            var item = new ComboBoxItem
            {
                Content = $"{b.Title}{countStr}",
                Tag = b
            };
            AutomationProperties.SetName(item, $"{b.Title}, {b.Bookmarks.Count} yer imi kayıtlı");
            _bookCombo.Items.Add(item);

            if (!string.IsNullOrEmpty(selectBookId) && b.Id == selectBookId)
            {
                selectedIdx = i;
            }
        }

        if (_bookCombo.Items.Count > 0)
        {
            _bookCombo.SelectedIndex = selectedIdx;
        }
        else
        {
            _infoText.Text = "Kütüphanenizde henüz kitap bulunmuyor.";
        }
    }

    private BookItem? CurrentBook => (_bookCombo.SelectedItem as ComboBoxItem)?.Tag as BookItem;
    private Bookmark? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as Bookmark;

    private string Describe(BookItem book, Bookmark b)
    {
        var where = book.Type == BookType.AUDIO ? Fmt.SpokenTime(b.PositionMs) : $"Sayfa {b.PageNumber}";
        var s = $"{where} - {b.Title}";
        if (!string.IsNullOrWhiteSpace(b.Note)) s += $" | Not: {b.Note}";
        var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        s += $" ({dateStr})";
        return s;
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        var book = CurrentBook;
        if (book == null)
        {
            _infoText.Text = "Lütfen bir kitap seçin.";
            return;
        }

        var freshBook = App.Repo.GetBook(book.Id) ?? book;
        _infoText.Text = $"Seçili Kitap: '{freshBook.Title}' — Toplam {freshBook.Bookmarks.Count} yer imi ve not.";

        foreach (var b in freshBook.Bookmarks.OrderBy(x => x.CreatedAt))
        {
            var desc = Describe(freshBook, b);
            var item = new ListBoxItem { Content = new TextBlock { Text = desc }, Tag = b };
            AutomationProperties.SetName(item, desc);
            _list.Items.Add(item);
        }

        if (_list.Items.Count == 0)
        {
            var empty = new ListBoxItem { Content = "Bu kitapta henüz kayıtlı yer imi bulunmuyor.", IsEnabled = false };
            AutomationProperties.SetName(empty, "Bu kitapta henüz kayıtlı yer imi bulunmuyor.");
            _list.Items.Add(empty);
        }
        else
        {
            _list.SelectedIndex = 0;
        }
    }

    private void OpenSelectedBookmark()
    {
        if (CurrentBook is not { } book || Selected is not { } bm) return;
        SelectedBook = book;
        SelectedBookmark = bm;
        ShouldOpenBook = true;
        DialogResult = true;
    }

    private void CopyNote()
    {
        if (Selected is not { } b) return;
        var textToCopy = !string.IsNullOrWhiteSpace(b.Note) ? b.Note : b.Title;
        try
        {
            Clipboard.SetText(textToCopy);
            App.Announce("Not panoya kopyalandı");
        }
        catch (Exception ex)
        {
            App.Announce("Kopyalanamadı: " + ex.Message);
        }
    }

    private void ExportCurrent()
    {
        if (CurrentBook is not { } book) return;
        BookmarkService.ExportWithDialog(this, book);
    }

    private void ImportCurrent()
    {
        if (CurrentBook is not { } book) return;
        if (BookmarkService.ImportWithDialog(this, App.Repo, book) > 0)
        {
            var curId = book.Id;
            LoadBooks(curId);
            RefreshList();
        }
    }

    private void ExportAll()
    {
        var booksWithBm = _books.Where(x => x.Bookmarks.Count > 0).ToList();
        if (booksWithBm.Count == 0)
        {
            MessageBox.Show("Kütüphanenizdeki kitaplarda kayıtlı yer imi bulunamadı.", "Yer İmleri", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Tüm Yer İmlerini Dışa Aktar",
            Filter = "Metin Dosyaları (*.txt)|*.txt|Tüm Dosyalar (*.*)|*.*",
            FileName = "Tum_Kitaplar_Yer_Imleri_Yedegi.txt",
            DefaultExt = ".txt"
        };

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                var content = BookmarkService.ExportAllToText(_books);
                File.WriteAllText(dlg.FileName, content, new UTF8Encoding(true));
                int totalCount = booksWithBm.Sum(x => x.Bookmarks.Count);
                App.Announce($"{totalCount} yer imi başarıyla dışa aktarıldı.");
                MessageBox.Show($"Tebrikler!\n{booksWithBm.Count} kitaba ait toplam {totalCount} adet yer imi başarıyla dışa aktarıldı:\n\n{dlg.FileName}", "Toplu Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dışa aktarma sırasında hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RemoveSelected()
    {
        if (CurrentBook is not { } book || Selected is not { } bm) return;
        var idx = _list.SelectedIndex;
        App.Repo.RemoveBookmark(book.Id, bm.Id);
        App.Announce("Yer imi silindi");
        LoadBooks(book.Id);
        RefreshList();
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(idx, _list.Items.Count - 1);
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

    public HelpDialog(Window? owner) : base(owner, "Kullanım Kılavuzu ve Klavye Kısayolları", 800)
    {
        var html = @"<!DOCTYPE html>
<html lang=""tr"">
<head>
<meta charset=""utf-8"">
<style>
  body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; font-size: 16px; line-height: 1.6; color: #111; background: #fff; padding: 18px; margin: 0; }
  h1 { font-size: 22px; color: #004d99; border-bottom: 2px solid #004d99; padding-bottom: 6px; margin-top: 0; }
  h2 { font-size: 18px; color: #1a5276; margin-top: 22px; margin-bottom: 8px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }
  h3 { font-size: 16px; color: #0e3d59; margin-top: 14px; margin-bottom: 6px; }
  .tip-box { background: #eef6ff; border-left: 5px solid #004d99; padding: 12px; margin: 12px 0; border-radius: 4px; }
  .feature-box { background: #f4fbf4; border-left: 5px solid #2e7d32; padding: 12px; margin: 12px 0; border-radius: 4px; }
  ul { list-style-type: square; margin-top: 4px; padding-left: 24px; }
  li { margin-bottom: 6px; }
  strong { color: #000; }
  .badge { background: #004d99; color: #fff; padding: 2px 7px; border-radius: 4px; font-size: 13px; font-weight: bold; }
</style>
</head>
<body>
<h1>Erişilebilir Kitap Okuyucu v2.0.2 - Kolay Kullanım Kılavuzu</h1>

<div class=""tip-box"">
  <strong>Hoş Geldiniz!</strong> Bu uygulama; görme engelli dostlarımızın kitapları, PDF'leri, Word belgelerini ve sesli kitapları hiçbir engelle karşılaşmadan en verimli ve bağımsız biçimde okuyup dinleyebilmesi için geliştirilmiştir. Aşağıdaki adımları okuyarak programı ve tüm yeni v2.0.0 özelliklerini kolayca keşfedebilirsiniz.
</div>

<h2>1. Kitap veya Belge Nasıl Eklenir?</h2>
<p>Program açıldığında klavye odağı doğrudan Kitaplık listesinde yer alır:</p>
<ul>
  <li><strong>Bilgisayarınızdan Dosya Eklemek İçin:</strong> Klavyeden <strong>Ctrl + O</strong> tuşuna basın. Açılan dosya seçim penceresinden PDF, Word (DOCX / DOC), Excel Tablosu (XLSX), EPUB, Markdown (MD), Düz Metin (TXT) veya sesli kitap (ZIP / MP3) dosyanızı seçip Aç deyin. Kitabınız hemen listenize eklenir.</li>
  <li><strong>İnternetteki Bir Yazıyı Eklemek İçin:</strong> <strong>Ctrl + U</strong> tuşuna basıp haberin veya makalenin internet bağlantısını (URL) yapıştırın.</li>
  <li><strong>Panodaki Metni Kitap Yapmak İçin:</strong> Kopyaladığınız bir metni <strong>Ctrl + Shift + V</strong> tuşuyla anında kitaplık listesine ekleyebilirsiniz.</li>
  <li><strong>Kitabı Açmak İçin:</strong> Listede kitabın üzerine gelip <strong>Enter</strong> tuşuna basın.</li>
  <li><strong>Kitap Silmek İçin:</strong> Seçili kitap üzerinde <strong>Delete</strong> tuşuna basıp onaylayın. (Orijinal dosyanız silinmez).</li>
</ul>

<h2>2. Belge ve Kitap Nasıl Okunur? (Metin Gezinme Standartları)</h2>
<p>Bir kitabı açtığınızda imleç doğrudan metin alanının en başına odaklanır ve ekran okuyucunuz hazır duruma gelir:</p>
<ul>
  <li><strong>Sesli Okumayı Başlatmak / Duraklatmak:</strong> Klavyenizdeki <strong>Boşluk (Space)</strong> tuşuna basın! Program seçtiğiniz Türkçe sesle okumaya başlar. Bir sayfa bittiğinde hiç duraksamadan sonraki sayfaya geçer ve siz durdurana kadar kesintisiz okur. Tekrar <strong>Boşluk</strong> tuşuna basarak duraklatabilirsiniz.</li>
  <li><strong>Hızlı Susturma (Ekran Okuyucu Refleksi):</strong> Sesli okuma devam ederken klavyedeki <strong>Ctrl</strong> tuşuna tek başına dokunup bıraktığınızda okuma anında duraklar.</li>
  <li><strong>Kendi Ekran Okuyucunuzla (NVDA / JAWS / Narrator) Gezinme:</strong>
    <ul>
      <li><strong>Yukarı / Aşağı Ok:</strong> Satır satır gezinir. Sayfa sonundayken Aşağı Ok'a bastığınızda otomatik olarak sonraki sayfaya geçer ve ilk cümleyi seslendirir.</li>
      <li><strong>Ctrl + Sol / Sağ Ok:</strong> Standart Windows kuralına uygun olarak <em>kelime kelime</em> gezinir.</li>
      <li><strong>Ctrl + Yukarı / Aşağı Ok:</strong> Akıllı biçimlendirilmiş paragraflar arasında önceki / sonraki paragrafa atlar.</li>
      <li><strong>PageDown / PageUp:</strong> Sonraki veya önceki sayfaya doğrudan geçiş yapar (Ctrl + PageDown / PageUp da kullanılabilir).</li>
      <li><strong>Ctrl + Enter:</strong> İmlecin bulunduğu satırdan itibaren sürekli sesli okumayı başlatır.</li>
      <li><strong>Enter:</strong> Yalnızca imlecin üzerinde bulunduğu mevcut satırı / cümleyi seslendirir.</li>
    </ul>
  </li>
  <li><strong>Sesli Okuma Esnasında Cümle Atlamak:</strong> Okuma sürerken sonraki cümleye atlamak için <strong>Alt + Sağ Ok</strong>, önceki cümleye dönmek için <strong>Alt + Sol Ok</strong> tuşlarını kullanabilirsiniz.</li>
</ul>

<h2>3. Sesi Açıp Kısma ve Hız Ayarı</h2>
<ul>
  <li><strong>Ses Düzeyi:</strong> Okuma sırasında sesi artırmak için <strong>Ctrl + Shift + Sağ Ok</strong> (veya <strong>Alt + Yukarı Ok</strong>), kısmak için <strong>Ctrl + Shift + Sol Ok</strong> (veya <strong>Alt + Aşağı Ok</strong>) tuşlarına basın. Seviye yüzde olarak duyurulur.</li>
  <li><strong>Okuma Hızı:</strong> Hızı artırmak için <strong>Ctrl + Shift + Yukarı Ok</strong>, yavaşlatmak için <strong>Ctrl + Shift + Aşağı Ok</strong> tuşlarını kullanın.</li>
</ul>

<h2>4. Uyku Zamanlayıcısı (Sleep Timer)</h2>
<div class=""feature-box"">
  <strong>Yeni v2.0.0 Özelliği:</strong> Gece yatarken veya dinlenirken kitap okumayı otomatik durdurmak için <strong>Ctrl + T</strong> kısayoluna basın. Açılan menüden 5, 10, 15, 30, 45 veya 60 dakika gibi hazır seçeneklerden birini seçin. Süre başladığında ve her kontrol edişinizde ekran okuyucunuz kalan süreyi net bir şekilde seslendirir. Süre dolduğunda okuma sistemi sessizce durdurur.
</div>

<h2>5. Klavye Kısayollarını Özelleştirme Motoru</h2>
<div class=""feature-box"">
  <strong>Yeni v2.0.0 Özelliği:</strong> Programdaki 35+ kısayolun tamamı kişisel tercihinize göre özelleştirilebilir!
  <ul>
    <li><strong>Kısayol Ekranına Ulaşım:</strong> <strong>Ctrl + Virgül</strong> ile Ayarlar penceresini açın. Sol ağaç menüsünden <strong>Klavye Kısayolları</strong> sekmesini seçin.</li>
    <li><strong>Kategoriler:</strong> Kısayollar; <em>Okuma ve Gezinme Kısayolları</em>, <em>Sesli Kitap Kısayolları</em> ve <em>Genel Kısayollar</em> olarak gruplandırılmıştır.</li>
    <li><strong>Değiştirme:</strong> İstediğiniz kısayolun üzerine gelip <strong>Kısayol Ata (F2 veya Enter)</strong> düğmesine basın; ardından klavyenizden vermek istediğiniz yeni tuşa veya tuş kombinasyonuna (örneğin Alt+S, Ctrl+Shift+P vb.) basın.</li>
    <li><strong>Çakışma Güvenliği:</strong> Başka bir eylemde kullanılan bir tuşa basarsanız sistem çakışmayı anında sesli olarak duyurur ve karışıklığı önler.</li>
    <li><strong>Sıfırlama:</strong> Beğenmediğinizde tek bir kısayolu veya listenin tamamını tek tıkla varsayılan fabrika ayarlarına döndürebilirsiniz.</li>
  </ul>
</div>

<h2>6. Ayarları Yedekleme ve Geri Yükleme (JSON)</h2>
<div class=""feature-box"">
  <strong>Yeni v2.0.0 Özelliği:</strong> Tüm tercihlerinizi, ses motoru ayarlarınızı, görsel temalarınızı ve özelleştirdiğiniz kısayol haritanızı tek tıkla güvenceye alabilirsiniz:
  <ul>
    <li><strong>Yedek Almak İçin:</strong> <strong>Ctrl + Virgül</strong> ile Ayarlar'a girin, soldan <strong>Yedekleme ve Geri Yükleme</strong>'yi seçip <strong>""Ayarları Dosyaya Yedekle...""</strong> düğmesine basın. Ayarlarınız masaüstünüze veya istediğiniz yere hafif bir JSON dosyası olarak kaydedilir.</li>
    <li><strong>Geri Yüklemek İçin:</strong> Bilgisayar değiştirdiğinizde veya sistemi sıfırladığınızda <strong>""Yedekten Ayarları Geri Yükle...""</strong> düğmesine basarak yedeğinizi seçin. Tüm ayarlarınız saniyeler içinde anında uygulanır.</li>
  </ul>
</div>

<h2>7. Yer İmleri Yöneticisi & Dışa/İçe Aktarma (Yeni v2.0.2)</h2>
<div class=""feature-box"">
  <strong>Yeni Özellik:</strong> Kütüphanenizdeki kitapların yer imlerini merkezi olarak yönetebilir, temiz metin (.txt) olarak dışa aktarabilir ve paylaşabilirsiniz!
  <ul>
    <li><strong>Açılış:</strong> Kütüphanede <strong>Ctrl + Shift + Y</strong> tuşuna basın veya <strong>Araçlar &gt; Yer İmleri Yöneticisi...</strong> menüsünü açın. Okuyucu içindeyken ise yer imleri penceresindeki <em>Dışa Aktar</em> veya <em>İçe Aktar</em> düğmelerini kullanabilirsiniz.</li>
    <li><strong>TXT Olarak Dışa Aktarma:</strong> Seçtiğiniz kitaptaki tüm yer imlerini, işaretlediğiniz metin satırlarını, sayfa numaralarını ve notlarınızı ekran okuyucular için tertemiz biçimlendirilmiş bir <strong>.txt</strong> dosyası olarak kaydedebilirsiniz.</li>
    <li><strong>Arkadaştan / Yedekten İçe Aktarma:</strong> Başka bir kullanıcının veya arkadaşınızın size ilettiği yer imi TXT dosyasını seçtiğiniz kitaba tek tıkla yükleyebilirsiniz. Akıllı mükerrer kontrolü sayesinde daha önce eklenmiş kayıtlar yinelenmez.</li>
    <li><strong>Doğrudan Kitaba Gitme:</strong> Listedeki herhangi bir yer imine tıklayıp Enter'a bastığınızda kitap doğrudan o sayfadan veya süreden açılır.</li>
  </ul>
</div>
<ul>
  <li>İmleç istediğiniz satırdayken veya seçtiğiniz bir metin üzerindeyken <strong>Ctrl + N</strong> (veya <strong>Ctrl + B</strong>) tuşuna basarak o sayfaya not ve alıntı ekleyebilirsiniz.</li>
  <li>Tüm kayıtlı notlarınızı görmek için <strong>Ctrl + Shift + B</strong> tuşuna basın. Listedeki notların üzerinde <strong>Ctrl + C</strong> tuşuna basarak alıntıyı doğrudan panoya kopyalayabilirsiniz.</li>
</ul>

<h2>8. Sesli Kitap Oynatıcı (MP3 / ZIP Arşivleri)</h2>
<ul>
  <li><strong>Boşluk:</strong> Sesli kitabı oynatır veya duraklatır.</li>
  <li><strong>Sol / Sağ Ok:</strong> Belirlenen saniye kadar geri veya ileri sarar. <em>(Yeni: Ayrımın başına gelindiğinde sol ok otomatik olarak önceki ayrımın son kısmına geçerek kesintisiz geriye sarar!)</em></li>
  <li><strong>Ctrl + Sol / Sağ Ok:</strong> Çok bölümlü ZIP sesli kitaplarda önceki veya sonraki ayrıma / ses dosyasına geçer.</li>
  <li><strong>Ctrl + T:</strong> Sesli kitap için uyku zamanlayıcısı kurar.</li>
  <li><strong>Ctrl + Yukarı / Aşağı Ok:</strong> Oynatma hızını artırır veya azaltır.</li>
  <li><strong>Shift + Yukarı / Aşağı Ok:</strong> Ses seviyesini artırır veya azaltır.</li>
</ul>

<h2>9. Excel Tablolarını Okuma ve Dinleme (XLSX)</h2>
<p>
  Programa eklediğiniz Excel (<strong>.xlsx</strong>) tabloları, görme engelli ve ekran okuyucu kullanıcıları için özel olarak optimize edilir:
</p>
<ul>
  <li>Çok sayfalı Excel çalışma kitaplarında her sayfa <em>""=== [Çalışma Sayfası: Sayfa Adı] ===""</em> başlığıyla net biçimde ayrıştırılır.</li>
  <li>Tablo satırları ve hücreler <em>""Satır 1: Başlık 1 | Başlık 2 | Başlık 3""</em> biçiminde düzenli sütun ayraçlarıyla ekran okuyucu imlecine sunulur. Böylece tablolar Not Defteri akıcılığında satır satır veya kesintisiz sesle kolayca incelenebilir.</li>
</ul>

<hr style=""margin:24px 0; border:0; border-top:1px solid #ccc;"" />

<h2>Tüm Varsayılan Klavye Kısayolları Tablosu</h2>

<h3>Kitaplık Ekranı</h3>
<ul>
  <li><strong>Ctrl + O:</strong> Bilgisayardan Kitap / Dosya Ekle (PDF, Word DOCX/DOC, Excel XLSX, EPUB, TXT, MD, ZIP)</li>
  <li><strong>Ctrl + U:</strong> Web Bağlantısından Makale Ekle</li>
  <li><strong>Ctrl + Shift + V:</strong> Panodaki Metni Kitap Yap</li>
  <li><strong>Ctrl + N:</strong> Yeni Klasör Oluştur</li>
  <li><strong>Enter:</strong> Seçili kitabı aç</li>
  <li><strong>Delete:</strong> Seçili kitabı kitaplıktan kaldır</li>
  <li><strong>Ctrl + Shift + Y:</strong> Yer İmleri Yöneticisi (Tüm Kitaplar & Dışa/İçe Aktarma)</li>
  <li><strong>Ctrl + Virgül:</strong> Ayarlar Penceresini Aç</li>
  <li><strong>F1:</strong> Kullanım Kılavuzu ve Kısayollar (Bu Pencere)</li>
  <li><strong>Esc:</strong> Uygulamadan çıkış veya önceki menüye dönüş</li>
  <li><strong>Alt + D / G / K / A / S / Y:</strong> Üst menü çubuğuna hızlı erişim (Dosya, Görünüm, Kitaplık, Araçlar, Seçenekler, Yardım)</li>
</ul>

<h3>Belge ve Kitap Okuyucu</h3>
<ul>
  <li><strong>Yukarı / Aşağı Ok:</strong> Satır satır okuma (sayfa başında / sonunda otomatik sayfa değiştirir)</li>
  <li><strong>Ctrl + Sol / Sağ Ok:</strong> Kelime kelime gezinme</li>
  <li><strong>Ctrl + Yukarı / Aşağı Ok:</strong> Önceki veya sonraki paragrafa geçiş</li>
  <li><strong>PageDown / PageUp:</strong> Sonraki veya önceki sayfaya geç (veya Ctrl + PageDown / PageUp)</li>
  <li><strong>Boşluk Tuşu:</strong> Kesintisiz sesli okumayı başlat / duraklat</li>
  <li><strong>Ctrl Tuşu (Tek):</strong> Sesli okumayı anında duraklat / sustur</li>
  <li><strong>Enter:</strong> Yalnızca imlecin bulunduğu mevcut satırı seslendir</li>
  <li><strong>Ctrl + Enter:</strong> Bulunulan satırdan itibaren sürekli sesli oku</li>
  <li><strong>Alt + Sağ / Sol Ok:</strong> Sonraki veya önceki cümleye atla</li>
  <li><strong>Ctrl + C:</strong> Bulunulan satırı veya seçili metni panoya kopyala</li>
  <li><strong>Ctrl + G:</strong> Sayfa numarası kutusuna odaklanarak sayfaya git</li>
  <li><strong>Ctrl + F:</strong> Belge içinde kelime / metin ara</li>
  <li><strong>Ctrl + N (veya Ctrl + B):</strong> Bulunulan konuma yer imi / alıntı notu ekle</li>
  <li><strong>Ctrl + Shift + B:</strong> Yer imleri ve notlar listesini aç</li>
  <li><strong>Ctrl + T:</strong> Uyku zamanlayıcısı kur</li>
  <li><strong>Ctrl + E:</strong> Sayfaları ses (WAV) dosyasına aktar</li>
  <li><strong>Ctrl + Shift + Yukarı / Aşağı Ok:</strong> Sesli okuma hızını artır / azalt</li>
  <li><strong>Ctrl + Shift + Sağ / Sol Ok (veya Alt + Yukarı / Aşağı):</strong> Ses düzeyini artır / azalt</li>
  <li><strong>Esc:</strong> Kitabı kapat ve Kitaplığa dön</li>
</ul>

<h3>Sesli Kitap Oynatıcı</h3>
<ul>
  <li><strong>Boşluk Tuşu:</strong> Oynat / Duraklat</li>
  <li><strong>Sol / Sağ Ok:</strong> Geriye / İleriye sar</li>
  <li><strong>Ctrl + Sol / Sağ Ok:</strong> Önceki / Sonraki ayrıma geç (ZIP kitaplarda)</li>
  <li><strong>Ctrl + T:</strong> Uyku zamanlayıcısı kur</li>
  <li><strong>Ctrl + B:</strong> Dinlenen konuma yer imi ekle</li>
  <li><strong>Ctrl + Shift + B:</strong> Yer imleri listesini aç</li>
  <li><strong>Ctrl + Yukarı / Aşağı Ok:</strong> Oynatma hızını artır / azalt</li>
  <li><strong>Shift + Yukarı / Aşağı Ok:</strong> Ses düzeyini artır / azalt</li>
  <li><strong>Esc:</strong> Oynatıcıyı kapat ve Kitaplığa dön</li>
</ul>

<h2>Ses Motorları ve Sorumluluk Bildirimi</h2>
<p style=""font-size:14px; color:#333;"">
  Uygulama; tamamen yerel ve internetsiz çalışan açık kaynak nöral yapay zeka sesini (Piper Fahrettin FP32), modern sistem seslerini (Windows OneCore Tolga vb.), klasik Windows seslerini (SAPI 5) ve çevrim içi doğal sesleri (Microsoft Emel, Babek & Banu [Azerbaycan] ve isteğe bağlı kişisel Google Cloud API) destekler.
</p>
<p style=""font-size:14px; color:#333;"">
  <strong>Üçüncü Taraf Servisler ve API Sorumluluk Reddi:</strong> Uygulama içerisinde isteğe bağlı olarak girilen Google Cloud ve benzeri üçüncü taraf API anahtarları tamamen kullanıcının kendi sorumluluğundadır. İlgili servis sağlayıcılarının kota aşımlarından veya faturalandırma işlemlerinden geliştirici sorumlu tutulamaz. Girilen API anahtarları hiçbir harici sunucuya iletilmez, yalnızca bilgisayarınızda yerel olarak saklanır.
</p>

<h2>Geliştirici ve İletişim</h2>
<ul>
  <li><strong>Uygulama:</strong> Erişilebilir Kitap Okuyucu (Windows v2.0.0)</li>
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
