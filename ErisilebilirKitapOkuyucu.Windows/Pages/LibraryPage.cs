using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class LibraryPage : PageBase
{
    private readonly ListBox _folders = new() { MinHeight = 160 };
    private readonly ListBox _books = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _heading = Ui.Text("Kitaplığım", "Heading");
    private readonly TextBlock _empty = new() { Margin = new Thickness(8), Visibility = Visibility.Collapsed };
    private int _filter; // 0 tümü, 1 belgeler, 2 ses
    private bool _suppress;

    private Menu _menu = null!;
    private MenuItem _mFile = null!;
    private MenuItem _mFolders = null!;
    private MenuItem _mBook = null!;
    private MenuItem _mTools = null!;
    private MenuItem _mOptions = null!;
    private MenuItem _mHelp = null!;

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
            return _books;
        }
    }

    public LibraryPage()
    {
        _heading.Text = "Kitaplığım";
        _heading.Focusable = false;
        KeyboardNavigation.SetIsTabStop(_heading, false);
        Ui.Name(_heading, "Kitaplığım");

        // 1. Ana Menü Çubuğu (Varsayılan olarak gizlidir; kullanıcı Alt tuşuna bastığında anında açılır)
        var menu = BuildMenuBar();

        // 2. Sol Bölüm: Klasörler Listesi (Sadeleştirilmiş, Buton Kalabalığı Olmayan Kart)
        var left = new DockPanel();
        var folderLabel = Ui.Text("Klasörler", "SubHeading");
        folderLabel.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(folderLabel, Dock.Top);
        left.Children.Add(folderLabel);

        Ui.Name(_folders, "Klasörler listesi. Yön tuşlarıyla gezinebilir, F2 ile yeniden adlandırabilir, Delete ile silebilirsiniz");
        _folders.SelectionChanged += (_, _) =>
        {
            if (_suppress || _folders.SelectedItem is not ListBoxItem { Tag: string f }) return;
            CurrentFolder = f;
            RefreshBooks(null);
            App.Announce($"{f} klasörü, {_books.Items.OfType<ListBoxItem>().Count(i => i.Tag is BookItem)} kitap");
        };
        _folders.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2) { e.Handled = true; RenameFolder(); }
            else if (e.Key == Key.Delete) { e.Handled = true; DeleteFolder(); }
        };
        left.Children.Add(_folders);

        var leftCard = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBg"],
            BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 14, 0),
            Width = 270,
            Child = left
        };

        // 3. Sağ Bölüm: Filtreler, Arama, Hızlı Ekle ve Kitaplar Listesi
        var right = new DockPanel();

        // Filtre ve Arama Satırı
        var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var filterLabel = Ui.Text("Göster:");
        filterLabel.VerticalAlignment = VerticalAlignment.Center;
        filterLabel.Margin = new Thickness(0, 0, 6, 0);
        filterRow.Children.Add(filterLabel);
        filterRow.Children.Add(Ui.Radio("Tümü", "filtre", () => SetFilter(0), true));
        filterRow.Children.Add(Ui.Radio("PDF ve Metin", "filtre", () => SetFilter(1)));
        filterRow.Children.Add(Ui.Radio("Sesli Kitaplar", "filtre", () => SetFilter(2)));
        DockPanel.SetDock(filterRow, Dock.Top);
        right.Children.Add(filterRow);

        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
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

        DockPanel.SetDock(_empty, Dock.Top);
        right.Children.Add(_empty);

        Ui.Name(_books, "Kitap listesi. Açmak için Enter, silmek için Delete, bilgiler için Ctrl+I");
        _books.MouseDoubleClick += (_, _) => OpenSelected();
        _books.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; OpenSelected(); return; }
            if (e.Key == Key.Delete) { e.Handled = true; RemoveSelected(); return; }
            if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; ShowInfo(); return; }
            if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; MoveSelected(); return; }
            if (e.Key == Key.Y && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; OpenBookmarkManager(); return; }
        };
        _books.ContextMenu = BuildContextMenu();
        right.Children.Add(_books);

        var rightCard = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBg"],
            BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = right
        };

        // 4. Ana Sayfa Düzeni
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        contentGrid.Children.Add(leftCard);
        contentGrid.Children.Add(rightCard);

        var root = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        _heading.Margin = new Thickness(0, 6, 0, 8);
        DockPanel.SetDock(_heading, Dock.Top);
        root.Children.Add(_heading);

        root.Children.Add(contentGrid);
        Content = root;

        // Klavye Kısayolları (Masaüstü Standartları)
        InputBindings.Add(new KeyBinding(new RelayCommand(AddBooks), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(AddWeb), Key.U, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(AddClipboardText), Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(NewFolder), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Host.ShowSettings()), Key.OemComma, ModifierKeys.Control));

        // Alt tuşu ve F6 gezinmesi
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System)
            {
                var sysKey = e.SystemKey;
                if (sysKey == Key.F4)
                {
                    e.Handled = true;
                    Host.Close();
                    return;
                }

                if (sysKey == Key.LeftAlt || sysKey == Key.RightAlt)
                {
                    e.Handled = true;
                    if (_menu.Visibility == Visibility.Visible && _menu.IsKeyboardFocusWithin)
                    {
                        CloseAndHideMenu();
                    }
                    else
                    {
                        FocusMenu(_mFile, openSubmenu: false);
                    }
                    return;
                }

                if (sysKey == Key.D) { e.Handled = true; FocusMenu(_mFile, openSubmenu: true); return; }
                if (sysKey == Key.K) { e.Handled = true; FocusMenu(_mFolders, openSubmenu: true); return; }
                if (sysKey == Key.B) { e.Handled = true; FocusMenu(_mBook, openSubmenu: true); return; }
                if (sysKey == Key.A) { e.Handled = true; FocusMenu(_mTools, openSubmenu: true); return; }
                if (sysKey == Key.S) { e.Handled = true; FocusMenu(_mOptions, openSubmenu: true); return; }
                if (sysKey == Key.Y) { e.Handled = true; FocusMenu(_mHelp, openSubmenu: true); return; }
            }

            if (e.Key == Key.F6)
            {
                e.Handled = true;
                if (_books.IsKeyboardFocusWithin)
                {
                    _folders.Focus();
                }
                else if (_folders.IsKeyboardFocusWithin)
                {
                    _search.Focus();
                }
                else
                {
                    FocusBookItem();
                }
            }

            if (ShortcutService.Matches("General.BookmarkManager", e))
            {
                e.Handled = true;
                OpenBookmarkManager();
                return;
            }
        };

        Loaded += (_, _) => FocusBookItem();
    }

    private void FocusMenu(MenuItem mi, bool openSubmenu = false)
    {
        _mFile.IsSubmenuOpen = false;
        _mFolders.IsSubmenuOpen = false;
        _mBook.IsSubmenuOpen = false;
        _mTools.IsSubmenuOpen = false;
        _mOptions.IsSubmenuOpen = false;
        _mHelp.IsSubmenuOpen = false;

        _menu.Visibility = Visibility.Visible;
        _menu.Dispatcher.BeginInvoke(() =>
        {
            mi.Focus();
            Keyboard.Focus(mi);
            if (openSubmenu)
            {
                mi.IsSubmenuOpen = true;
            }
        }, DispatcherPriority.Input);
    }

    private bool IsTopMenu(MenuItem mi)
    {
        return mi == _mFile || mi == _mFolders || mi == _mBook ||
               mi == _mTools || mi == _mOptions || mi == _mHelp;
    }

    private bool IsAnySubmenuOpen()
    {
        return _mFile.IsSubmenuOpen || _mFolders.IsSubmenuOpen || _mBook.IsSubmenuOpen ||
               _mTools.IsSubmenuOpen || _mOptions.IsSubmenuOpen || _mHelp.IsSubmenuOpen;
    }

    private void CloseAndHideMenu()
    {
        _mFile.IsSubmenuOpen = false;
        _mFolders.IsSubmenuOpen = false;
        _mBook.IsSubmenuOpen = false;
        _mTools.IsSubmenuOpen = false;
        _mOptions.IsSubmenuOpen = false;
        _mHelp.IsSubmenuOpen = false;
        _menu.Visibility = Visibility.Collapsed;
        FocusBookItem();
    }

    private Menu BuildMenuBar()
    {
        _menu = new Menu
        {
            Background = (Brush)Application.Current.Resources["PanelHeaderBg"],
            Foreground = (Brush)Application.Current.Resources["AppFg"],
            BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            FontSize = 14,
            Padding = new Thickness(6, 4, 6, 4),
            Visibility = Visibility.Collapsed // Başlangıçta ekranda yer kaplamaz, Alt tuşuna basıldığında açılır
        };
        Ui.Name(_menu, "Ana Menü Çubuğu. Sağ ve sol yön tuşlarıyla menüler arasında gezinebilir, Aşağı ok ile açabilirsiniz");

        // 1. Dosya Menüsü (Alt + D)
        _mFile = MMenu("_Dosya",
            MItem("_Kitap Ekle...", AddBooks, "Ctrl+O"),
            MItem("_Web Bağlantısı Ekle...", AddWeb, "Ctrl+U"),
            MItem("_Panodan Metin Yapıştır", AddClipboardText, "Ctrl+Shift+V"),
            new Separator(),
            MItem("Ç_ıkış", () => Host.Close(), "Alt+F4")
        );

        // 2. Klasörler Menüsü (Alt + K)
        _mFolders = MMenu("_Klasörler",
            MItem("_Yeni Klasör Oluştur...", NewFolder, "Ctrl+N"),
            MItem("Klasörü _Yeniden Adlandır...", RenameFolder, "F2"),
            MItem("Seçili Klasörü _Sil...", DeleteFolder, "Delete"),
            new Separator(),
            MItem("_Ana Dizine Git", SelectRootFolder)
        );

        // 3. Kitap Menüsü (Alt + B)
        _mBook = MMenu("_Kitap",
            MItem("Kitabı _Aç", OpenSelected, "Enter"),
            MItem("Kitap _Bilgisi...", ShowInfo, "Ctrl+I"),
            MItem("Başka _Klasöre Taşı...", MoveSelected, "Ctrl+M"),
            new Separator(),
            MItem("Kitaplıktan _Kaldır", RemoveSelected, "Delete")
        );

        // 4. Araçlar Menüsü (Alt + A)
        var mNowPlaying = MItem("_Çalan Sesli Kitaba Dön", () => Host.ReturnToAudio());
        _mTools = MMenu("_Araçlar",
            mNowPlaying,
            MItem("_Son Okunan Kitabı Aç", OpenLastReadBook),
            MItem("_Yer İmleri Yöneticisi...", OpenBookmarkManager, "Ctrl+Shift+Y"),
            new Separator(),
            MItem("Kitaplığı _Yenile", () => RefreshBooks(null), "F5")
        );
        _mTools.SubmenuOpened += (_, _) =>
        {
            mNowPlaying.Visibility = (Host.NowPlayingAudio != null && App.Audio.CurrentPath != null)
                ? Visibility.Visible : Visibility.Collapsed;
        };

        // 5. Seçenekler Menüsü (Alt + S)
        var mTheme = MMenu("_Arayüz Teması",
            MItem("_Sistem Teması", () => SetTheme("Sistem")),
            MItem("_Açık Tema", () => SetTheme("Acik")),
            MItem("_Koyu Tema", () => SetTheme("Koyu"))
        );
        var mReadMode = MMenu("_Varsayılan Okuma Modu",
            MItem("_Cümle Cümle Okuma", () => SetReadMode("Cümle")),
            MItem("_Paragraf Paragraf Okuma", () => SetReadMode("Paragraf"))
        );
        _mOptions = MMenu("_Seçenekler",
            MItem("_Ayarlar Penceresi...", () => Host.ShowSettings(), "Ctrl+,"),
            new Separator(),
            mReadMode,
            mTheme
        );

        // 6. Yardım Menüsü (Alt + Y)
        _mHelp = MMenu("_Yardım",
            MItem("_Kullanım Kılavuzu ve Kısayollar", () => new HelpDialog(Host).ShowDialog(), "F1"),
            new Separator(),
            MItem("_Güncellemeleri Denetle...", async () => await UpdateService.CheckForUpdatesAsync(isManual: true)),
            MItem("_Gizlilik Politikası...", () => new PrivacyDialog(Host).ShowDialog()),
            MItem("_Geliştirici Web Sitesi (omergoktas.net)", () => OpenUrl("https://omergoktas.net")),
            new Separator(),
            MItem("Erişilebilir Kitap Okuyucu _Hakkında...", ShowAboutDialog)
        );

        _menu.Items.Add(_mFile);
        _menu.Items.Add(_mFolders);
        _menu.Items.Add(_mBook);
        _menu.Items.Add(_mTools);
        _menu.Items.Add(_mOptions);
        _menu.Items.Add(_mHelp);

        void WireSubmenuClosed(MenuItem mi)
        {
            mi.SubmenuClosed += (_, _) =>
            {
                _menu.Dispatcher.BeginInvoke(() =>
                {
                    if (!_menu.IsKeyboardFocusWithin && !IsAnySubmenuOpen())
                    {
                        _menu.Visibility = Visibility.Collapsed;
                    }
                }, DispatcherPriority.Input);
            };
        }
        WireSubmenuClosed(_mFile);
        WireSubmenuClosed(_mFolders);
        WireSubmenuClosed(_mBook);
        WireSubmenuClosed(_mTools);
        WireSubmenuClosed(_mOptions);
        WireSubmenuClosed(_mHelp);

        _menu.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (!_menu.IsKeyboardFocusWithin)
            {
                _menu.Dispatcher.BeginInvoke(() =>
                {
                    if (!_menu.IsKeyboardFocusWithin && !IsAnySubmenuOpen())
                    {
                        _menu.Visibility = Visibility.Collapsed;
                    }
                }, DispatcherPriority.Input);
            }
        };

        _menu.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F4 && (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || e.Key == Key.System))
            {
                e.Handled = true;
                Host.Close();
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (!IsAnySubmenuOpen())
                {
                    e.Handled = true;
                    CloseAndHideMenu();
                }
            }
            else if (e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Space)
            {
                var focused = Keyboard.FocusedElement as MenuItem;
                if (focused != null && IsTopMenu(focused) && !focused.IsSubmenuOpen)
                {
                    e.Handled = true;
                    focused.IsSubmenuOpen = true;
                }
            }
        };

        return _menu;
    }

    private MenuItem MItem(string header, Action onClick, string? gesture = null)
    {
        var m = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? "",
            FontSize = 13.5
        };
        m.Click += (_, _) =>
        {
            CloseAndHideMenu();
            onClick();
        };
        Ui.Name(m, header.Replace("_", ""));
        return m;
    }

    private static MenuItem MMenu(string header, params object[] items)
    {
        var m = new MenuItem
        {
            Header = header,
            FontSize = 14,
            FontWeight = FontWeights.Medium
        };
        Ui.Name(m, $"{header.Replace("_", "")} menüsü. Açmak için Aşağı ok veya Enter'a basın");
        foreach (var it in items)
        {
            if (it is MenuItem mi) m.Items.Add(mi);
            else if (it is Separator sep) m.Items.Add(sep);
        }
        return m;
    }

    private void SelectRootFolder()
    {
        CurrentFolder = BookItem.RootFolder;
        if (_folders.Items.Count > 0 && _folders.Items[0] is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private void OpenLastReadBook()
    {
        var last = App.Repo.Books.OrderByDescending(b => b.LastReadTimestamp).FirstOrDefault();
        if (last != null)
        {
            Host.OpenBook(last);
        }
        else
        {
            App.Announce("Kitaplığınızda henüz okunmuş bir kitap bulunmuyor.", true);
        }
    }

    private void OpenBookmarkManager()
    {
        var selBook = Selected;
        var dlg = new BookmarkManagerDialog(Window.GetWindow(this), selBook?.Id);
        if (dlg.ShowDialog() == true && dlg.ShouldOpenBook && dlg.SelectedBook is { } book)
        {
            if (dlg.SelectedBookmark is { } bm)
            {
                if (book.Type == BookType.AUDIO)
                {
                    book.CurrentPositionMs = bm.PositionMs;
                }
                else
                {
                    book.CurrentPage = bm.PageNumber;
                }
                App.Repo.Save();
            }
            Host.OpenBook(book);
        }
        else
        {
            Refresh(Selected?.Id);
        }
    }

    private void SetTheme(string theme)
    {
        var s = App.Repo.Settings;
        s.AppTheme = theme;
        App.Repo.UpdateSettings(s);
        ThemeManager.Apply(s);
        var label = theme switch
        {
            "Koyu" => "Koyu Tema",
            "Acik" => "Açık Tema",
            _ => "Sistem Teması"
        };
        App.Announce($"Tema değiştirildi: {label}", true);
        Host.SetStatus($"Tema değiştirildi: {label}", true);
    }

    private void SetReadMode(string mode)
    {
        var s = App.Repo.Settings;
        s.TtsReadMode = mode;
        App.Repo.UpdateSettings(s);
        App.Announce($"Okuma modu değiştirildi: {mode} okuma", true);
        Host.SetStatus($"Varsayılan okuma modu: {mode}", true);
    }

    private void ShowAboutDialog()
    {
        var appVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        MessageBox.Show(Host,
            $"Erişilebilir Kitap Okuyucu v{appVer}\n\n" +
            "Görme engelliler ve tüm okurlar için geliştirilmiş, tam erişilebilir belge ve sesli kitap okuyucu.\n\n" +
            "Geliştirici: Ömer Göktaş\nWeb: https://omergoktas.net\nİletişim: mail@omergoktas.net",
            "Erişilebilir Kitap Okuyucu Hakkında", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void OpenUrl(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Announce("Bağlantı açılamadı: " + ex.Message, true); }
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
            else
            {
                _books.Focus();
                Keyboard.Focus(_books);
            }
        }, DispatcherPriority.Input);
    }

    private ContextMenu BuildContextMenu()
    {
        var m = new ContextMenu();
        void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Add("Aç", OpenSelected);
        Add("Kitap Bilgisi", ShowInfo);
        Add("Yer İmleri Yöneticisi", OpenBookmarkManager);
        Add("Klasöre Taşı", MoveSelected);
        Add("Kitaplıktan Kaldır", RemoveSelected);
        return m;
    }

    public void Refresh(string? selectBookId = null)
    {
        _suppress = true;
        _folders.Items.Clear();
        foreach (var f in App.Repo.Folders)
        {
            var count = App.Repo.Books.Count(b => (b.FolderName ?? BookItem.RootFolder) == f);
            var dock = new DockPanel { LastChildFill = true };
            var countBadge = new Border
            {
                Background = (Brush)Application.Current.Resources["BadgeBg"],
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
                Foreground = (Brush)Application.Current.Resources["BadgeFg"]
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
                BookType.XLSX => "#059669",
                BookType.AUDIO => "#9333EA",
                _ => "#475569"
            };

            var badgeBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)),
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
                Foreground = Brushes.White,
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
                Foreground = (Brush)Application.Current.Resources["SubTextFg"]
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
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Kitap seçin",
                Filter = Services.ImportService.Filter,
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
                ValidateNames = true,
                DereferenceLinks = true
            };

            bool? result = false;
            try
            {
                result = dlg.ShowDialog(Host);
            }
            catch (Exception ex)
            {
                AppPaths.Log("OpenFileDialog Host ile açılamadı, bağımsız deneniyor: " + ex);
                result = dlg.ShowDialog();
            }

            if (result == true && dlg.FileNames != null && dlg.FileNames.Length > 0)
            {
                await Host.ImportFilesAsync(dlg.FileNames);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("AddBooks hatası: " + ex);
            Host.SetStatus("Dosya seçilemedi: " + ex.Message, true);
            MessageBox.Show(Host, "Dosya seçilirken bir hata oluştu:\n" + ex.Message, "Dosya Seçimi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private void SetFilter(int filter)
    {
        _filter = filter;
        RefreshBooks(null);
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
