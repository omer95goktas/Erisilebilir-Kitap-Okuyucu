using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class LibraryPage : PageBase
{
    private readonly ListBox _folders = new() { MinHeight = 120 };
    private readonly ListBox _books = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _heading = Ui.Text("Kitaplığım", "Heading");
    private readonly TextBlock _empty = new() { Margin = new Thickness(8), Visibility = Visibility.Collapsed };
    private Button? _btnAdd;
    private int _filter; // 0 tümü, 1 belgeler, 2 ses
    private bool _suppress;

    public string CurrentFolder { get; private set; } = BookItem.RootFolder;
    public override string PageTitle => "Kitaplığım";
    public override UIElement? InitialFocus
    {
        get
        {
            if (_books.Items.Count > 0)
            {
                var idx = Math.Clamp(_books.SelectedIndex >= 0 ? _books.SelectedIndex : 0, 0, _books.Items.Count - 1);
                return _books.Items[idx] as ListBoxItem ?? (UIElement)_books;
            }
            return _btnAdd ?? (UIElement)_books;
        }
    }

    public LibraryPage()
    {
        _heading.Text = "Kitaplığım";
        _heading.Focusable = false;
        KeyboardNavigation.SetIsTabStop(_heading, false);
        Ui.Name(_heading, "Kitaplığım");

        _btnAdd = Ui.Button("+ Kitap Ekle", AddBooks, "Ctrl+O. PDF, TXT, EPUB, DOCX, ZIP veya ses dosyası ekler", "Kitap Ekle", isPrimary: true);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        toolbar.Children.Add(_btnAdd);
        toolbar.Children.Add(Ui.Button("Web Bağlantısı Ekle", AddWeb, "Ctrl+U. Bir web sayfasındaki makaleyi okunabilir kitap olarak ekler", "Web Bağlantısı Ekle"));
        toolbar.Children.Add(Ui.Button("Metin Yapıştır", AddClipboardText, "Ctrl+Shift+V. Panodaki metni kitap olarak ekler", "Metin Yapıştır"));
        toolbar.Children.Add(Ui.Button("Ayarlar", () => Host.ShowSettings(), "Ctrl+Virgül. Ses motoru, okuma hızı ve görünüm ayarları", "Ayarlar"));
        toolbar.Children.Add(Ui.Button("Yardım", () => new HelpDialog(Host).ShowDialog(), "F1. Klavye kısayolları", "Yardım"));
        if (Host.NowPlayingAudio != null && App.Audio.CurrentPath != null)
            toolbar.Children.Add(Ui.Button("Çalan Sesli Kitaba Dön", () => Host.ReturnToAudio(), $"Şu an çalan: {Host.NowPlayingAudio.Title}"));

        // --- Sol: klasörler
        var left = new DockPanel();
        var folderLabel = Ui.Text("Klasörler", "SubHeading");
        DockPanel.SetDock(folderLabel, Dock.Top);
        left.Children.Add(folderLabel);
        var folderButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        folderButtons.Children.Add(Ui.Button("Yeni Klasör", NewFolder, "Ctrl+N"));
        folderButtons.Children.Add(Ui.Button("Yeniden Adlandır", RenameFolder));
        folderButtons.Children.Add(Ui.Button("Klasörü Sil", DeleteFolder));
        DockPanel.SetDock(folderButtons, Dock.Bottom);
        left.Children.Add(folderButtons);
        Ui.Name(_folders, "Klasörler listesi");
        _folders.SelectionChanged += (_, _) =>
        {
            if (_suppress || _folders.SelectedItem is not ListBoxItem { Tag: string f }) return;
            CurrentFolder = f;
            RefreshBooks(null);
            App.Announce($"{f} klasörü, {_books.Items.OfType<ListBoxItem>().Count(i => i.Tag is BookItem)} kitap");
        };
        left.Children.Add(_folders);

        var leftCard = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBg"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 12, 0),
            Width = 270,
            Child = left
        };

        // --- Sağ: kitaplar
        var right = new DockPanel();
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var filterLabel = Ui.Text("Göster:");
        filterLabel.VerticalAlignment = VerticalAlignment.Center;
        filterRow.Children.Add(filterLabel);
        filterRow.Children.Add(Ui.Radio("Tümü", "filtre", () => SetFilter(0), true));
        filterRow.Children.Add(Ui.Radio("PDF ve Metin", "filtre", () => SetFilter(1)));
        filterRow.Children.Add(Ui.Radio("Sesli Kitaplar", "filtre", () => SetFilter(2)));
        DockPanel.SetDock(filterRow, Dock.Top);
        right.Children.Add(filterRow);

        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var searchLabel = Ui.Text("Kitap ara:");
        searchLabel.VerticalAlignment = VerticalAlignment.Center;
        searchLabel.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchRow.Children.Add(searchLabel);
        Ui.Name(_search, "Kitap ara");
        _search.TextChanged += (_, _) => RefreshBooks(null);
        searchRow.Children.Add(_search);
        DockPanel.SetDock(searchRow, Dock.Top);
        right.Children.Add(searchRow);

        var bookButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        bookButtons.Children.Add(Ui.Button("Kitabı Aç", OpenSelected, "Enter", isPrimary: true));
        bookButtons.Children.Add(Ui.Button("Kitap Bilgisi", ShowInfo));
        bookButtons.Children.Add(Ui.Button("Klasöre Taşı", MoveSelected));
        bookButtons.Children.Add(Ui.Button("Kitaplıktan Kaldır", RemoveSelected, "Delete tuşu"));
        DockPanel.SetDock(bookButtons, Dock.Bottom);
        right.Children.Add(bookButtons);

        DockPanel.SetDock(_empty, Dock.Top);
        right.Children.Add(_empty);
        Ui.Name(_books, "Kitap listesi");
        _books.MouseDoubleClick += (_, _) => OpenSelected();
        _books.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; OpenSelected(); return; }
            if (e.Key == Key.Delete) { e.Handled = true; RemoveSelected(); return; }
        };
        _books.ContextMenu = BuildContextMenu();
        right.Children.Add(_books);

        var rightCard = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBg"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = right
        };

        var content = new DockPanel();
        DockPanel.SetDock(leftCard, Dock.Left);
        content.Children.Add(leftCard);
        content.Children.Add(rightCard);

        var root = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top);
        root.Children.Add(_heading);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(content);
        Content = root;

        InputBindings.Add(new KeyBinding(new RelayCommand(AddBooks), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(AddWeb), Key.U, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(AddClipboardText), Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(NewFolder), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Host.ShowSettings()), Key.OemComma, ModifierKeys.Control));

        // F6 ile araç çubuğu ve kitap listesi arasında hızlı geçiş (Standart Windows erişilebilirlik kısayolu)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F6)
            {
                e.Handled = true;
                if (_books.IsKeyboardFocusWithin)
                {
                    _btnAdd?.Focus();
                    if (_btnAdd != null) Keyboard.Focus(_btnAdd);
                }
                else
                {
                    FocusBookItem();
                }
            }
        };

        Loaded += (_, _) => FocusBookItem();
    }

    public void FocusBookItem()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_books.Items.Count > 0)
            {
                var idx = Math.Clamp(_books.SelectedIndex >= 0 ? _books.SelectedIndex : 0, 0, _books.Items.Count - 1);
                _books.SelectedIndex = idx;
                if (_books.Items[idx] is ListBoxItem item)
                {
                    item.IsSelected = true;
                    item.Focus();
                    Keyboard.Focus(item);
                    _books.ScrollIntoView(item);
                }
                else
                {
                    _books.Focus();
                    Keyboard.Focus(_books);
                }
            }
            else if (_btnAdd != null)
            {
                _btnAdd.Focus();
                Keyboard.Focus(_btnAdd);
            }
        }, DispatcherPriority.Input);
    }

    private ContextMenu BuildContextMenu()
    {
        var m = new ContextMenu();
        void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Add("Aç", OpenSelected); Add("Kitap Bilgisi", ShowInfo); Add("Klasöre Taşı", MoveSelected); Add("Kitaplıktan Kaldır", RemoveSelected);
        return m;
    }

    public override void OnShown()
    {
        App.Repo.LibraryChanged += OnLibraryChanged;
        Refresh(null);
        FocusBookItem();
    }

    public override void OnHidden() => App.Repo.LibraryChanged -= OnLibraryChanged;

    public override bool HandleBack() => false;

    private void OnLibraryChanged() => Dispatcher.BeginInvoke(() => Refresh(null));

    private void SetFilter(int f) { _filter = f; if (IsLoaded || _books != null) RefreshBooks(null); }

    public void Refresh(string? selectBookId)
    {
        _suppress = true;
        _folders.Items.Clear();
        foreach (var f in App.Repo.Folders)
        {
            var count = App.Repo.Books.Count(b => (b.FolderName ?? BookItem.RootFolder) == f);
            var dock = new DockPanel { LastChildFill = true };
            var countBadge = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources["BadgeBg"],
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(countBadge, Dock.Right);
            countBadge.Child = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BadgeFg"]
            };
            dock.Children.Add(countBadge);

            var nameBlock = new TextBlock
            {
                Text = f,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dock.Children.Add(nameBlock);

            var item = new ListBoxItem { Content = dock, Tag = f };
            Ui.Name(item, $"{f} klasörü, {count} kitap");
            _folders.Items.Add(item);
            if (f == CurrentFolder) item.IsSelected = true;
        }
        if (_folders.SelectedItem == null && _folders.Items.Count > 0)
        {
            CurrentFolder = BookItem.RootFolder;
            (_folders.Items[0] as ListBoxItem)!.IsSelected = true;
        }
        _suppress = false;
        RefreshBooks(selectBookId);
    }

    private void RefreshBooks(string? selectId)
    {
        if (_books == null) return;
        var prevId = selectId ?? ((_books.SelectedItem as ListBoxItem)?.Tag as BookItem)?.Id;
        _books.Items.Clear();
        var q = _search.Text.Trim();
        var list = App.Repo.Books.Where(b => (b.FolderName ?? BookItem.RootFolder) == CurrentFolder)
            .Where(b => _filter == 0 || (_filter == 1 ? b.Type != BookType.AUDIO : b.Type == BookType.AUDIO))
            .Where(b => q.Length == 0 || b.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();

        foreach (var b in list)
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(2) };

            var badgeColor = b.Type switch
            {
                BookType.PDF => "#DC2626",
                BookType.EPUB => "#16A34A",
                BookType.DOCX => "#2563EB",
                BookType.AUDIO => "#9333EA",
                _ => "#475569"
            };

            var badgeBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(badgeColor)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(badgeBorder, Dock.Left);
            badgeBorder.Child = new TextBlock
            {
                Text = b.TypeLabel.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dock.Children.Add(badgeBorder);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = b.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var summaryBlock = new TextBlock
            {
                Text = b.Summary,
                FontSize = 13,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["SubTextFg"]
            };
            textStack.Children.Add(titleBlock);
            textStack.Children.Add(summaryBlock);
            dock.Children.Add(textStack);

            var item = new ListBoxItem { Content = dock, Tag = b };
            Ui.Name(item, b.AccessibleName);
            _books.Items.Add(item);
            if (b.Id == prevId) item.IsSelected = true;
        }
        if (_books.SelectedItem == null && _books.Items.Count > 0) _books.SelectedIndex = 0;

        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = App.Repo.Books.Count == 0
            ? "Kitaplığınızda henüz kayıtlı bir kitap bulunmuyor. \"Kitap Ekle\" düğmesine basarak (Ctrl+O) bilgisayarınızdaki PDF, metin, EPUB, Word veya sesli kitapları ekleyebilirsiniz."
            : "Bu klasörde, seçili filtreyle eşleşen kitap yok.";
    }

    private BookItem? Selected => (_books.SelectedItem as ListBoxItem)?.Tag as BookItem;

    private async void AddBooks()
    {
        var dlg = new OpenFileDialog { Title = "Kitap seçin", Filter = Services.ImportService.Filter, Multiselect = true };
        if (dlg.ShowDialog(Host) == true) await Host.ImportFilesAsync(dlg.FileNames);
    }

    private async void AddWeb()
    {
        var url = PromptDialog.Ask(Host, "Web Bağlantısı Ekle", "Makale bağlantısı (https://…)", "", "Ekle");
        if (string.IsNullOrWhiteSpace(url)) return;
        url = Services.ImportService.ExtractUrl(url) ?? (url.StartsWith("http") ? url : "https://" + url.Trim());
        Host.SetStatus("Web sayfası indiriliyor, lütfen bekleyin…", true);
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var r = await App.Importer.AddWebArticleAsync(url);
            if (r.Book != null)
            {
                App.Repo.MoveBookToFolder(r.Book.Id, CurrentFolder);
                Host.SetStatus(r.Message ?? "Eklendi.", true);
                Refresh(r.Book.Id);
            }
            else
            {
                Host.SetStatus(r.Message ?? "Eklenemedi.", true);
                MessageBox.Show(Host, r.Message, "Web bağlantısı eklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void AddClipboardText()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            Host.SetStatus("Panoda metin yok.", true);
            return;
        }
        var url = Services.ImportService.ExtractUrl(text);
        if (url != null && text.Trim().Length < url.Length + 20)
        {
            var ans = MessageBox.Show(Host, $"Panoda bir web bağlantısı var:\n{url}\n\nMakale olarak eklensin mi?", "Bağlantı bulundu", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans == MessageBoxResult.Yes) { AddWebFromUrl(url); return; }
        }
        var r = App.Importer.AddPlainText(text);
        if (r.Book != null) { App.Repo.MoveBookToFolder(r.Book.Id, CurrentFolder); Host.SetStatus("Metin kitaplığa eklendi.", true); Refresh(r.Book.Id); }
    }

    private async void AddWebFromUrl(string url)
    {
        var r = await App.Importer.AddWebArticleAsync(url);
        Host.SetStatus(r.Message ?? "İşlem tamamlandı.", true);
        if (r.Book != null) { App.Repo.MoveBookToFolder(r.Book.Id, CurrentFolder); Refresh(r.Book.Id); }
    }

    private void OpenSelected() { if (Selected is { } b) Host.OpenBook(b); }

    private void ShowInfo()
    {
        if (Selected is not { } b) return;
        var lines = new List<string> { $"Başlık: {b.Title}", $"Biçim: {b.TypeLabel}" };
        if (b.Type == BookType.AUDIO)
        {
            if (b.IsZipArchive) lines.Add($"Toplam ayrım: {b.Chapters.Count}");
            lines.Add($"Kaldığınız süre: {Fmt.Time(b.CurrentPositionMs)}");
        }
        else { lines.Add($"Toplam sayfa: {b.TotalPages}"); lines.Add($"Kaldığınız sayfa: {b.CurrentPage} / {b.TotalPages}"); }
        lines.Add($"Yer imi sayısı: {b.Bookmarks.Count}");
        lines.Add($"Klasör: {b.FolderName}");
        lines.Add("Son erişim: " + DateTimeOffset.FromUnixTimeMilliseconds(b.LastReadTimestamp).LocalDateTime.ToString("dd.MM.yyyy HH:mm"));
        MessageBox.Show(Host, string.Join("\n", lines), "Kitap Bilgisi", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MoveSelected()
    {
        if (Selected is not { } b) return;
        var folders = App.Repo.Folders.ToList();
        var idx = ChoiceDialog.Pick(Host, "Klasöre Taşı", folders, Math.Max(0, folders.IndexOf(b.FolderName ?? BookItem.RootFolder)), $"\"{b.Title}\" hangi klasöre taşınsın?");
        if (idx < 0) return;
        App.Repo.MoveBookToFolder(b.Id, folders[idx]);
        Host.SetStatus($"\"{b.Title}\" {folders[idx]} klasörüne taşındı.", true);
        Refresh(null);
    }

    private void RemoveSelected()
    {
        if (Selected is not { } b) return;
        var ans = MessageBox.Show(Host, $"\"{b.Title}\" kitaplıktan kaldırılsın mı? Yer imleri ve ilerleme bilgisi de silinir.", "Kitaplıktan Kaldır", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;
        var idx = _books.SelectedIndex;
        if (Host.NowPlayingAudio?.Id == b.Id) { App.Audio.Stop(); Host.NowPlayingAudio = null; }
        App.Repo.RemoveBook(b.Id);
        Host.SetStatus($"\"{b.Title}\" kaldırıldı.", true);
        Refresh(null);
        if (_books.Items.Count > 0) { _books.SelectedIndex = Math.Min(idx, _books.Items.Count - 1); (_books.ItemContainerGenerator.ContainerFromIndex(_books.SelectedIndex) as UIElement)?.Focus(); }
        else _heading.Focus();
    }

    private void NewFolder()
    {
        var name = PromptDialog.Ask(Host, "Yeni Klasör", "Klasör Adı", "", "Oluştur");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (App.Repo.CreateFolder(name)) { CurrentFolder = name.Trim(); Host.SetStatus($"{CurrentFolder} klasörü oluşturuldu.", true); Refresh(null); }
        else Host.SetStatus("Bu adla bir klasör zaten var veya ad geçersiz.", true);
    }

    private void RenameFolder()
    {
        if (CurrentFolder == BookItem.RootFolder) { Host.SetStatus("Ana Dizin yeniden adlandırılamaz.", true); return; }
        var name = PromptDialog.Ask(Host, "Yeniden Adlandır", "Yeni Klasör Adı", CurrentFolder, "Kaydet");
        if (string.IsNullOrWhiteSpace(name)) return;
        var old = CurrentFolder;
        if (App.Repo.RenameFolder(old, name)) { CurrentFolder = name.Trim(); Host.SetStatus($"Klasör {CurrentFolder} olarak adlandırıldı.", true); Refresh(null); }
    }

    private void DeleteFolder()
    {
        if (CurrentFolder == BookItem.RootFolder) { Host.SetStatus("Ana Dizin silinemez.", true); return; }
        var ans = MessageBox.Show(Host, $"{CurrentFolder} klasörünü silmek istediğinize emin misiniz? İçindeki kitaplar silinmez, Ana Dizine taşınır.", "Klasörü Sil?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;
        var f = CurrentFolder;
        App.Repo.DeleteFolder(f);
        CurrentFolder = BookItem.RootFolder;
        Host.SetStatus($"{f} klasörü silindi.", true);
        Refresh(null);
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _a;
    public RelayCommand(Action a) { _a = a; }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _a();
}
