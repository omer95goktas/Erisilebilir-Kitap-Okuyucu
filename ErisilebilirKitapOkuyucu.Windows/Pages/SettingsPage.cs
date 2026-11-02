using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class SettingsPage : PageBase
{
    private readonly TextBlock _heading = Ui.Text("Ayarlar", "Heading");
    private readonly TreeView _tree = new();
    private readonly TextBlock _detailTitle = Ui.Text("Okuma ve Sayfalar", "SubHeading");
    private readonly ContentControl _detailContainer = new();

    // Ses ve Konuşma motoru öğeleri
    private readonly StackPanel _engineBox = new();
    private readonly ListBox _voices = new() { MaxHeight = 240, MinHeight = 100 };
    private readonly TextBlock _voiceInfo = new();
    private bool _syncing = true;
    private string? _engineId;

    // Kategori Panelleri
    private FrameworkElement _readingPanel = null!;
    private FrameworkElement _appearancePanel = null!;
    private FrameworkElement _ttsEnginePanel = null!;
    private FrameworkElement _googleTtsPanel = null!;
    private FrameworkElement _audioPanel = null!;
    private FrameworkElement _shortcutsPanel = null!;
    private FrameworkElement _backupPanel = null!;
    private FrameworkElement _aboutPanel = null!;

    // Klavye Kısayolları öğeleri
    private string _activeShortcutCategory = ShortcutService.CatReader;
    private ListBox _scList = null!;
    private Action<string?> _populateShortcutsList = null!;

    public override string PageTitle => "Ayarlar";
    public override UIElement? InitialFocus => _tree;

    public SettingsPage(string initialCategory = "cat_reading")
    {
        Ui.Name(_heading, "Ayarlar");
        var s = App.Repo.Settings;
        _engineId = s.SelectedTtsEngine;

        // 1. Kategori Panellerini Oluştur
        BuildReadingPanel(s);
        BuildAppearancePanel(s);
        BuildTtsEnginePanel(s);
        BuildGoogleTtsPanel(s);
        BuildAudioPanel(s);
        BuildShortcutsPanel();
        BuildBackupPanel(s);
        BuildAboutPanel();

        // 2. Ağaç Görünümü (TreeView) Yapılandırması
        BuildCategoryTree();

        // 3. Tab ve Shift+Tab ile Ağaç ve Detay Paneli Arasında Kesintisiz Gezinme
        _tree.PreviewKeyDown += (sender, e) =>
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                FocusFirstInDetail();
            }
        };

        _detailContainer.PreviewKeyDown += (sender, e) =>
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var first = Descendants<Control>(_detailContainer)
                    .FirstOrDefault(el => el.Focusable && el.IsVisible && el is (Button or RadioButton or CheckBox or TextBox or PasswordBox or Slider or ListBox));
                if (first != null && (first.IsFocused || Keyboard.FocusedElement == first))
                {
                    e.Handled = true;
                    if (_tree.SelectedItem is TreeViewItem tvi) tvi.Focus();
                    else _tree.Focus();
                }
            }
        };

        // 4. Sayfa Düzeni (Üst Çubuk + Sol Ağaç + Sağ Detay Paneli)
        var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var btnBack = Ui.Button("Kitaplığa Dön", () => Host.ShowLibrary(), "Esc tuşu. Ayarlar anında kaydedilir");
        DockPanel.SetDock(btnBack, Dock.Right);
        topBar.Children.Add(btnBack);
        topBar.Children.Add(_heading);

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sol Sütun: Ağaç Görünümü
        var leftDock = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var treeHeader = Ui.Text("Ayar Kategorileri", "SubHeading");
        DockPanel.SetDock(treeHeader, Dock.Top);
        leftDock.Children.Add(treeHeader);
        leftDock.Children.Add(_tree);
        Grid.SetColumn(leftDock, 0);
        mainGrid.Children.Add(leftDock);

        // Sağ Sütun: Seçili Kategori Başlığı ve İçeriği
        var rightDock = new DockPanel();
        DockPanel.SetDock(_detailTitle, Dock.Top);
        _detailTitle.Margin = new Thickness(0, 0, 0, 10);
        rightDock.Children.Add(_detailTitle);

        var scrollViewer = new ScrollViewer
        {
            Content = _detailContainer,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            IsTabStop = false
        };
        rightDock.Children.Add(scrollViewer);
        Grid.SetColumn(rightDock, 1);
        mainGrid.Children.Add(rightDock);

        var root = new DockPanel();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);
        root.Children.Add(mainGrid);
        Content = root;

        // Başlangıçta istenen paneli göster ve ağaçta seç
        ShowCategory(initialCategory);
        SelectNodeByTag(initialCategory);

        RefreshVoices();
        if (App.Tts.ActiveEngine is EdgeTtsEngine edgeInit) edgeInit.PreWarm();
        _syncing = false;
    }

    private void SelectNodeByTag(string tag)
    {
        bool SelectIn(TreeViewItem node)
        {
            if (node.Tag as string == tag)
            {
                node.IsSelected = true;
                return true;
            }
            foreach (var ch in node.Items)
            {
                if (ch is TreeViewItem childTvi)
                {
                    if (SelectIn(childTvi))
                    {
                        node.IsExpanded = true;
                        return true;
                    }
                }
            }
            return false;
        }

        foreach (var it in _tree.Items)
        {
            if (it is TreeViewItem rootNode && SelectIn(rootNode))
                return;
        }
    }

    private void BuildCategoryTree()
    {
        _tree.BorderThickness = new Thickness(1);
        _tree.BorderBrush = (Brush)Application.Current.Resources["ButtonBorder"];
        _tree.Background = (Brush)Application.Current.Resources["PanelBg"];
        _tree.Foreground = (Brush)Application.Current.Resources["AppFg"];
        _tree.FontSize = 14;
        Ui.Name(_tree, "Ayar Kategorileri Ağacı. Yukarı ve Aşağı yön tuşlarıyla gezinin, Sağ yön tuşuyla alt dalları açın, Tab tuşuyla seçeneklere geçin");

        // 1. Genel Ayarlar (Sağ yön tuşuyla açılır)
        var itemGeneral = MakeNode("Genel Ayarlar", "cat_general",
            MakeNode("Okuma ve Sayfalar", "cat_reading"),
            MakeNode("Görünüm ve Tema", "cat_appearance")
        );

        // 2. Ses ve Konuşma Motorları (Sağ yön tuşuyla açılır)
        var itemTts = MakeNode("Ses ve Konuşma (TTS)", "cat_tts",
            MakeNode("Ses Motoru ve Sesler", "cat_tts_engine"),
            MakeNode("Google Cloud TTS", "cat_google_tts")
        );

        // 3. Sesli Kitaplar
        var itemAudio = MakeNode("Sesli Kitaplar", "cat_audio");

        // 4. Klavye Kısayolları (Sağ yön tuşuyla açılır)
        var itemShortcuts = MakeNode("Klavye Kısayolları", "cat_shortcuts",
            MakeNode("Okuma ve Gezinme Kısayolları", "cat_sc_reader"),
            MakeNode("Sesli Kitap Kısayolları", "cat_sc_audio"),
            MakeNode("Genel Kısayollar", "cat_sc_general")
        );

        // 5. Ayarları Yedekle ve Geri Yükle
        var itemBackup = MakeNode("Ayarları Yedekle ve Geri Yükle", "cat_backup");

        // 6. Hakkında ve Güncellemeler
        var itemAbout = MakeNode("Hakkında ve Güncellemeler", "cat_about");

        _tree.Items.Add(itemGeneral);
        _tree.Items.Add(itemTts);
        _tree.Items.Add(itemAudio);
        _tree.Items.Add(itemShortcuts);
        _tree.Items.Add(itemBackup);
        _tree.Items.Add(itemAbout);

        itemGeneral.IsSelected = true;

        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem tvi && tvi.Tag is string tag)
            {
                ShowCategory(tag);
            }
        };
    }

    private static TreeViewItem MakeNode(string title, string tag, params TreeViewItem[] children)
    {
        var item = new TreeViewItem
        {
            Header = title,
            Tag = tag,
            FontSize = 14,
            FontWeight = children.Length > 0 ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(0, 2, 0, 2),
            IsExpanded = false // Başlangıçta kapalı; kullanıcı Sağ yön tuşuyla açar
        };
        Ui.Name(item, title + (children.Length > 0 ? " (Sağ yön tuşuyla açılabilir kategori)" : ""));
        foreach (var c in children) item.Items.Add(c);
        return item;
    }

    private void ShowCategory(string tag)
    {
        switch (tag)
        {
            case "cat_general":
            case "cat_reading":
                _detailTitle.Text = "Okuma ve Sayfalar";
                _detailContainer.Content = _readingPanel;
                break;

            case "cat_appearance":
                _detailTitle.Text = "Görünüm ve Tema";
                _detailContainer.Content = _appearancePanel;
                break;

            case "cat_tts":
            case "cat_tts_engine":
                _detailTitle.Text = "Ses Motoru ve Sesler";
                _detailContainer.Content = _ttsEnginePanel;
                break;

            case "cat_google_tts":
                _detailTitle.Text = "Google Cloud Seslendirme";
                _detailContainer.Content = _googleTtsPanel;
                break;

            case "cat_audio":
                _detailTitle.Text = "Sesli Kitaplar";
                _detailContainer.Content = _audioPanel;
                break;

            case "cat_shortcuts":
            case "cat_sc_reader":
                _detailTitle.Text = "Okuma ve Gezinme Kısayolları";
                _activeShortcutCategory = ShortcutService.CatReader;
                _populateShortcutsList?.Invoke(null);
                _detailContainer.Content = _shortcutsPanel;
                break;

            case "cat_sc_audio":
                _detailTitle.Text = "Sesli Kitap Kısayolları";
                _activeShortcutCategory = ShortcutService.CatAudio;
                _populateShortcutsList?.Invoke(null);
                _detailContainer.Content = _shortcutsPanel;
                break;

            case "cat_sc_general":
                _detailTitle.Text = "Genel Kısayollar";
                _activeShortcutCategory = ShortcutService.CatGeneral;
                _populateShortcutsList?.Invoke(null);
                _detailContainer.Content = _shortcutsPanel;
                break;

            case "cat_backup":
                _detailTitle.Text = "Ayarları Yedekle ve Geri Yükle";
                _detailContainer.Content = _backupPanel;
                break;

            case "cat_about":
                _detailTitle.Text = "Hakkında ve Güncellemeler";
                _detailContainer.Content = _aboutPanel;
                break;
        }
    }

    private void FocusFirstInDetail()
    {
        if (_detailContainer == null) return;
        var first = Descendants<Control>(_detailContainer)
            .FirstOrDefault(el => el.Focusable && el.IsVisible && el is (Button or RadioButton or CheckBox or TextBox or PasswordBox or Slider or ListBox));
        first?.Focus();
    }

    // ==========================================
    // PANEL 1: OKUMA VE SAYFALAR
    // ==========================================
    private void BuildReadingPanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(Ui.Text("Varsayılan Okuma Modu (TTS ve Gezinme)", "SubHeading"));
        p.Children.Add(Ui.Radio("Cümle Cümle Okuma", "okumamodu", () => Update(x => x.TtsReadMode = "Cümle"), s.TtsReadMode != "Paragraf"));
        p.Children.Add(Ui.Radio("Paragraf Paragraf Okuma (Kesintisiz ve Akıcı)", "okumamodu", () => Update(x => x.TtsReadMode = "Paragraf"), s.TtsReadMode == "Paragraf"));

        var auto = new CheckBox
        {
            Content = "Sayfa bitince sonraki sayfaya otomatik geç",
            IsChecked = s.AutoNextPage,
            Margin = new Thickness(0, 8, 0, 8)
        };
        auto.Checked += (_, _) => Update(x => x.AutoNextPage = true);
        auto.Unchecked += (_, _) => Update(x => x.AutoNextPage = false);
        p.Children.Add(auto);

        p.Children.Add(Ui.Text("Sayfa değişim sesi", "SubHeading"));
        foreach (var v in PageTurnSounds.Voices)
        {
            var voice = v;
            p.Children.Add(Ui.Radio(voice, "sayfasesi", () => { Update(x => x.PageTurnVoice = voice); if (!_syncing) PageTurnSounds.Play(voice, true); }, s.PageTurnVoice == voice));
        }

        var ttsSound = new CheckBox
        {
            Content = "Sesli okurken sayfa geçişinde de sayfa sesini çal",
            IsChecked = s.PlayPageTurnSoundOnTtsNextPage,
            Margin = new Thickness(0, 8, 0, 4)
        };
        ttsSound.Checked += (_, _) => Update(x => x.PlayPageTurnSoundOnTtsNextPage = true);
        ttsSound.Unchecked += (_, _) => Update(x => x.PlayPageTurnSoundOnTtsNextPage = false);
        p.Children.Add(ttsSound);

        _readingPanel = p;
    }

    // ==========================================
    // PANEL 2: GÖRÜNÜM VE TEMA
    // ==========================================
    private void BuildAppearancePanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(Ui.Text("Arayüz Teması", "SubHeading"));
        var themeBox = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        themeBox.Children.Add(Ui.Radio("Sistem Teması (Windows ayarlarını takip eder)", "theme_radio", () => SetTheme("Sistem"), s.AppTheme == "Sistem"));
        themeBox.Children.Add(Ui.Radio("Açık Tema (Net, koyu siyah metinler)", "theme_radio", () => SetTheme("Acik"), s.AppTheme == "Acik"));
        themeBox.Children.Add(Ui.Radio("Koyu Tema (Göz yormayan modern koyu zemin)", "theme_radio", () => SetTheme("Koyu"), s.AppTheme == "Koyu"));
        p.Children.Add(themeBox);

        var hc = new CheckBox
        {
            Content = "Yüksek karşıtlık modu (siyah zemin, sarı vurgu)",
            IsChecked = s.HighContrastMode,
            Margin = new Thickness(0, 6, 0, 8)
        };
        hc.Checked += (_, _) => { Update(x => x.HighContrastMode = true); ThemeManager.Apply(App.Repo.Settings); };
        hc.Unchecked += (_, _) => { Update(x => x.HighContrastMode = false); ThemeManager.Apply(App.Repo.Settings); };
        p.Children.Add(hc);

        p.Children.Add(MakeSlider("Yazı boyutu", s.FontSizeSp, 14, 40, 2, v => Update(x => x.FontSizeSp = (int)v), v => $"{(int)v} punto"));
        p.Children.Add(Ui.Text("Windows sistem yüksek karşıtlık teması aktifken uygulama doğrudan sistem renklerini kullanır."));

        _appearancePanel = p;
    }

    // ==========================================
    // PANEL 3: SES MOTORU VE SESLER
    // ==========================================
    private void BuildTtsEnginePanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(Ui.Text("Konuşma motoru", "SubHeading"));
        _engineBox.Children.Add(Ui.Radio("Otomatik (en uygun motoru seç)", "motor", () => SetEngine(null), s.SelectedTtsEngine == null));
        foreach (var e in App.Tts.Engines)
        {
            var id = e.Id;
            var count = e.GetVoices().Count;
            _engineBox.Children.Add(Ui.Radio($"{e.DisplayName}, {count} ses", "motor", () => SetEngine(id), s.SelectedTtsEngine == id));
        }
        p.Children.Add(_engineBox);

        p.Children.Add(Ui.Text("Ses (sistemde yüklü sesler)", "SubHeading"));
        Ui.Name(_voices, "Ses listesi");
        _voices.SelectionChanged += (_, _) =>
        {
            if (_syncing || _voices.SelectedItem is not ListBoxItem { Tag: VoiceDescriptor v }) return;
            Update(x => x.SelectedVoiceId = v.Id);
            App.Announce($"{v.Name} seçildi");
        };
        p.Children.Add(_voices);
        p.Children.Add(_voiceInfo);
        p.Children.Add(Ui.Button("Sesi Dene", PreviewVoice, "Seçili sesle örnek bir cümle okur"));

        p.Children.Add(MakeSlider("Varsayılan oynatma hızı", s.TtsSpeechRate, 0.5, 3.0, 0.1, v => Update(x => x.TtsSpeechRate = (float)Math.Round(v, 2)), v => Fmt.Speed(v)));
        p.Children.Add(MakeSlider("Ses perdesi", s.TtsPitch, 0.5, 2.0, 0.1, v => Update(x => x.TtsPitch = (float)v), v => $"{v:0.0}"));
        p.Children.Add(MakeSlider("Ses düzeyi", s.TtsVolume, 0.0, 1.0, 0.05, v => Update(x => x.TtsVolume = (float)v), v => $"%{(int)Math.Round(v * 100)}"));

        _ttsEnginePanel = p;
    }

    // ==========================================
    // PANEL 4: GOOGLE CLOUD TTS
    // ==========================================
    private void BuildGoogleTtsPanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        bool hasKey = !string.IsNullOrWhiteSpace(s.GoogleApiKey);
        var keyStatusText = Ui.Text(hasKey ? "Google Cloud API Anahtarı: Kayıtlı ve aktif" : "Google Cloud API Anahtarı: Henüz tanımlanmamış", "SubHeading");
        p.Children.Add(keyStatusText);

        var keyInputContainer = new StackPanel { Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 4, 0, 4) };
        var apiBox = new PasswordBox
        {
            Password = s.GoogleApiKey ?? "",
            Margin = new Thickness(0, 4, 0, 6),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 14
        };
        Ui.Name(apiBox, "Google Cloud API Anahtarı Giriş Alanı");

        var saveKeyBtn = Ui.Button("Anahtarı Kaydet ve Gizle", () =>
        {
            var val = GoogleCloudTtsEngine.CleanApiKey(apiBox.Password);
            Update(x => x.GoogleApiKey = string.IsNullOrWhiteSpace(val) ? null : val);
            if (!string.IsNullOrWhiteSpace(val))
            {
                keyStatusText.Text = "Google Cloud API Anahtarı: Kayıtlı ve aktif";
                keyInputContainer.Visibility = Visibility.Collapsed;
                App.Announce("Google Cloud API anahtarı temizlenip güvenle kaydedildi.");
            }
            else
            {
                keyStatusText.Text = "Google Cloud API Anahtarı: Henüz tanımlanmamış";
                App.Announce("API anahtarı temizlendi.");
            }
        }, "Girilen anahtarı güvenle kaydeder ve ekrandan gizler");

        var testKeyBtn = Ui.Button("API Anahtarını Test Et", async () =>
        {
            var keyToTest = !string.IsNullOrWhiteSpace(apiBox.Password)
                ? GoogleCloudTtsEngine.CleanApiKey(apiBox.Password)
                : App.Repo?.Settings?.GoogleApiKey;

            App.Announce("Google Cloud API anahtarı test ediliyor, lütfen bekleyin...");
            var (success, message) = await GoogleCloudTtsEngine.ValidateApiKeyAsync(keyToTest);
            App.Announce(message);
            MessageBox.Show(message, success ? "Doğrulama Başarılı" : "Doğrulama Başarısız",
                MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }, "Girilen anahtarın Google sunucularıyla çalışıp çalışmadığını test eder");

        keyInputContainer.Children.Add(Ui.Text("API Anahtarınızı yapıştırın:"));
        keyInputContainer.Children.Add(apiBox);
        keyInputContainer.Children.Add(saveKeyBtn);
        keyInputContainer.Children.Add(testKeyBtn);
        p.Children.Add(keyInputContainer);

        var toggleBtn = Ui.Button(hasKey ? "API Anahtarını Değiştir / Düzenle" : "API Anahtarı Tanımla", () =>
        {
            if (keyInputContainer.Visibility == Visibility.Visible)
            {
                keyInputContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                keyInputContainer.Visibility = Visibility.Visible;
                apiBox.Focus();
            }
        }, "API anahtarı giriş kutusunu açar veya kapatır");
        p.Children.Add(toggleBtn);

        if (hasKey)
        {
            var quickTestBtn = Ui.Button("Kayıtlı Anahtarı Test Et", async () =>
            {
                App.Announce("Kayıtlı Google Cloud API anahtarı test ediliyor...");
                var (success, message) = await GoogleCloudTtsEngine.ValidateApiKeyAsync();
                App.Announce(message);
                MessageBox.Show(message, success ? "Doğrulama Başarılı" : "Doğrulama Başarısız",
                    MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }, "Mevcut kayıtlı Google Cloud anahtarını test eder");
            p.Children.Add(quickTestBtn);
        }

        var disclaimerText = new TextBlock
        {
            Text = "Yasal Sorumluluk Bildirimi: Uygulama içerisinde girilen Google Cloud API anahtarları tamamen kullanıcının kendi sorumluluğundadır. İlgili servis sağlayıcıları tarafından belirlenen kotaların aşılması veya ücretlendirme sebebiyle doğabilecek hiçbir maddi borç, fatura veya mali yükümlülükten geliştirici sorumlu tutulamaz. Girilen API anahtarları hiçbir harici sunucuya iletilmez, yalnızca bu bilgisayarda yerel olarak saklanır.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkRed,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 6)
        };
        Ui.Name(disclaimerText, disclaimerText.Text);
        p.Children.Add(disclaimerText);

        _googleTtsPanel = p;
    }

    // ==========================================
    // PANEL 5: SESLİ KİTAPLAR
    // ==========================================
    private void BuildAudioPanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(MakeSlider("Varsayılan oynatma hızı", s.AudioSpeed, 0.5, 3.0, 0.25, v => Update(x => x.AudioSpeed = (float)Math.Round(v, 2)), v => Fmt.Speed(v)));
        p.Children.Add(MakeSlider("İleri ve geri sarma süresi (saniye)", s.SeekIntervalSeconds, 5, 60, 5, v => Update(x => x.SeekIntervalSeconds = (int)v), v => $"{(int)v} saniye"));

        _audioPanel = p;
    }

    // ==========================================
    // PANEL 6: KLAVYE KISAYOLLARI
    // ==========================================
    private void BuildShortcutsPanel()
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(Ui.Text("Listeden değiştirmek istediğiniz eylemi seçin ve Enter tuşuna basın. Açılan pencerede yeni tuş kombinasyonuna bastığınız anda kısayol otomatik kaydedilecektir."));

        _scList = new ListBox { MaxHeight = 320, MinHeight = 160, Margin = new Thickness(0, 8, 0, 8) };
        Ui.Name(_scList, "Kısayollar listesi");

        _populateShortcutsList = (selectId) =>
        {
            var prevId = selectId ?? ((_scList.SelectedItem as ListBoxItem)?.Tag is ShortcutDef s ? s.Id : null);
            _scList.Items.Clear();
            var items = ShortcutService.Definitions.Where(d => d.Category == _activeShortcutCategory).ToList();
            ListBoxItem? toSelect = null;
            foreach (var def in items)
            {
                var cur = ShortcutService.GetDisplayString(def.Id);
                var isCustom = ShortcutService.IsCustomized(def.Id);
                var lbi = new ListBoxItem
                {
                    Content = isCustom ? $"{def.Name}: {cur} (Özelleştirildi)" : $"{def.Name}: {cur}",
                    Tag = def
                };
                Ui.Name(lbi, $"{def.Name}, atanmış kısayol: {cur}" + (isCustom ? ", özelleştirilmiş" : ""));
                _scList.Items.Add(lbi);
                if (def.Id == prevId) toSelect = lbi;
            }
            if (toSelect != null) _scList.SelectedItem = toSelect;
            else if (_scList.Items.Count > 0) _scList.SelectedIndex = 0;
            if (_scList.SelectedItem != null) _scList.ScrollIntoView(_scList.SelectedItem);
        };

        void ChangeSelectedShortcut()
        {
            if (_scList.SelectedItem is ListBoxItem { Tag: ShortcutDef def })
            {
                var d = new ChangeShortcutDialog(Host, def);
                if (d.ShowDialog() == true)
                {
                    _populateShortcutsList(def.Id);
                    Host.SetStatus($"{def.Name} kısayolu güncellendi: {ShortcutService.GetDisplayString(def.Id)}", true);
                    _scList.Dispatcher.BeginInvoke(() =>
                    {
                        if (_scList.ItemContainerGenerator.ContainerFromItem(_scList.SelectedItem) is ListBoxItem container)
                            container.Focus();
                        else
                            _scList.Focus();
                    }, System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            else
            {
                App.Announce("Lütfen önce listeden değiştirmek istediğiniz bir kısayol seçin.");
            }
        }

        _scList.MouseDoubleClick += (_, _) => ChangeSelectedShortcut();
        _scList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; ChangeSelectedShortcut(); }
        };
        p.Children.Add(_scList);

        var scBtnRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        scBtnRow.Children.Add(Ui.Button("Kısayolu Değiştir", ChangeSelectedShortcut, "Enter tuşu. Seçili eylemin kısayol tuşunu değiştirir"));
        scBtnRow.Children.Add(Ui.Button("Seçili Kısayolu Sıfırla", () =>
        {
            if (_scList.SelectedItem is ListBoxItem { Tag: ShortcutDef def })
            {
                ShortcutService.ResetShortcut(def.Id);
                _populateShortcutsList(def.Id);
                Host.SetStatus($"{def.Name} varsayılan kısayoluna döndürüldü.", true);
            }
        }, "Seçili eylemi varsayılan kısayoluna döndürür"));
        scBtnRow.Children.Add(Ui.Button("Tüm Kısayolları Varsayılana Sıfırla", () =>
        {
            var res = MessageBox.Show(Host, "Tüm klavye kısayollarını varsayılan fabrika ayarlarına sıfırlamak istediğinize emin misiniz?", "Kısayolları Sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                ShortcutService.ResetAll();
                _populateShortcutsList(null);
                Host.SetStatus("Tüm kısayollar varsayılana sıfırlandı.", true);
                App.Announce("Tüm kısayollar varsayılan ayarlarına sıfırlandı.", true);
            }
        }, "Tüm özelleştirilmiş kısayolları temizler ve fabrika ayarlarına döner"));

        p.Children.Add(scBtnRow);
        _shortcutsPanel = p;
    }

    // ==========================================
    // PANEL 7: HAKKINDA VE GÜNCELLEMELER
    // ==========================================
    private void BuildAboutPanel()
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        var appVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        p.Children.Add(Ui.Text($"Erişilebilir Kitap Okuyucu (Windows v{appVer})", "SubHeading"));
        p.Children.Add(Ui.Text("Geliştirici: Ömer Göktaş"));

        var linksPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        linksPanel.Children.Add(Ui.Button("E-Posta Gönder", () => OpenUrl("mailto:mail@omergoktas.net"), "Varsayılan e-posta istemcinizi açarak mail@omergoktas.net adresine ileti hazırlar"));
        linksPanel.Children.Add(Ui.Button("Web Sitesini Ziyaret Et", () => OpenUrl("https://omergoktas.net"), "omergoktas.net web sitesini tarayıcınızda açar"));
        linksPanel.Children.Add(Ui.Button("Gizlilik Politikası", () => new PrivacyDialog(Host).ShowDialog(), "Uygulamanın gizlilik politikasını görüntüler"));

        if (UpdateService.IsRunningAsMsix())
        {
            p.Children.Add(Ui.Text("Güncellemeler: Microsoft Store tarafından otomatik olarak yönetilmektedir."));
        }
        else
        {
            linksPanel.Children.Add(Ui.Button("Güncellemeleri Denetle", async () => await UpdateService.CheckForUpdatesAsync(isManual: true), "GitHub üzerindeki yeni sürümleri kontrol eder"));
        }

        linksPanel.Children.Add(Ui.Button("Ayarları Yedekle...", ExportSettings, "Tüm ayarlarınızı ve kısayollarınızı JSON dosyası olarak dışa aktarır"));
        linksPanel.Children.Add(Ui.Button("Ayarları Geri Yükle...", ImportSettings, "Daha önce alınan JSON ayar yedeğini sisteme yükler"));

        p.Children.Add(linksPanel);
        _aboutPanel = p;
    }

    // ==========================================
    // PANEL 8: AYARLARI YEDEKLE VE GERİ YÜKLE
    // ==========================================
    private void BuildBackupPanel(AppSettings s)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };

        p.Children.Add(Ui.Text("Ayarları Yedekleme ve Geri Yükleme", "SubHeading"));
        var desc = Ui.Text(
            "Tüm ses motoru ve ses seçimlerinizi, konuşma hızınızı, okuma modunuzu, " +
            "görünüm tercihlerinizi ve kişiselleştirdiğiniz tüm klavye kısayollarınızı " +
            "tek bir ayar dosyası (.json) olarak bilgisayarınıza yedekleyebilir ya da " +
            "daha önce aldığınız bir yedekten anında geri yükleyebilirsiniz.\n\n" +
            "Bu özellik yeni bir bilgisayara geçerken veya ayarlarınızı yedekleyip " +
            "başka bir kullanıcıyla paylaşırken büyük kolaylık sağlar."
        );
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 0, 0, 14);
        p.Children.Add(desc);

        var btnRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 16) };
        var btnExport = Ui.Button("Ayarları Dışa Aktar (Yedekle)...", ExportSettings, "Tüm ayarlarınızı ve kısayollarınızı bir JSON dosyasına kaydeder");
        var btnImport = Ui.Button("Ayarları İçe Aktar (Geri Yükle)...", ImportSettings, "Daha önce yedeklenen bir JSON ayar dosyasını programa yükler");
        var btnResetAll = Ui.Button("Tüm Ayarları Fabrika Ayarlarına Sıfırla...", ResetAllSettingsToDefault, "Tüm ayarları ve kısayolları varsayılan değerlerine sıfırlar");

        btnExport.Margin = new Thickness(0, 0, 10, 8);
        btnImport.Margin = new Thickness(0, 0, 10, 8);
        btnResetAll.Margin = new Thickness(0, 0, 10, 8);

        btnRow.Children.Add(btnExport);
        btnRow.Children.Add(btnImport);
        btnRow.Children.Add(btnResetAll);
        p.Children.Add(btnRow);

        _backupPanel = p;
    }

    private void ExportSettings()
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Ayarları Dışa Aktar (Yedekle)",
                Filter = "Ayar Dosyası (*.json)|*.json|Tüm Dosyalar (*.*)|*.*",
                FileName = $"ErisilebilirKitapOkuyucu_Ayarlar_{DateTime.Now:yyyyMMdd}.json",
                DefaultExt = ".json"
            };

            if (sfd.ShowDialog(Host) == true)
            {
                App.Repo.ExportSettings(sfd.FileName);
                Host.SetStatus("Ayarlar başarıyla dışa aktarıldı: " + System.IO.Path.GetFileName(sfd.FileName), true);
                App.Announce("Ayarlar başarıyla dışa aktarıldı.", true);
                MessageBox.Show(Host, "Ayarlarınız başarıyla dışa aktarıldı:\n" + sfd.FileName, "Ayarlar Yedeklendi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Host.SetStatus("Dışa aktarma hatası: " + ex.Message, true);
            MessageBox.Show(Host, "Ayarlar dışa aktarılırken bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportSettings()
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ayarları İçe Aktar (Geri Yükle)",
                Filter = "Ayar Dosyası (*.json)|*.json|Tüm Dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (ofd.ShowDialog(Host) == true)
            {
                var text = System.IO.File.ReadAllText(ofd.FileName, System.Text.Encoding.UTF8);
                var imported = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(text, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (imported == null)
                {
                    throw new InvalidOperationException("Seçilen dosya geçerli bir ayar verisi içermiyor.");
                }

                var res = MessageBox.Show(Host,
                    "Seçilen dosyadaki ayarlar yüklenecek ve geçerli ayarlarınız güncellenecektir.\nDevam etmek istiyor musunuz?",
                    "Ayarları İçe Aktar", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                App.Repo.UpdateSettings(imported);
                ThemeManager.Apply(imported);
                Host.ShowSettings("cat_backup");

                Host.SetStatus("Ayarlar başarıyla içe aktarıldı.", true);
                App.Announce("Ayarlar başarıyla içe aktarıldı ve uygulandı.", true);
                MessageBox.Show(Host, "Ayarlarınız başarıyla içe aktarıldı ve tüm sisteme uygulandı.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Host.SetStatus("İçe aktarma hatası: " + ex.Message, true);
            MessageBox.Show(Host, "Ayar dosyası yüklenirken bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetAllSettingsToDefault()
    {
        var res = MessageBox.Show(Host,
            "Tüm ses, okuma, görünüm ve klavye kısayollarını varsayılan fabrika ayarlarına sıfırlamak istediğinize emin misiniz?",
            "Tüm Ayarları Sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var defaultSettings = new AppSettings();
        App.Repo.UpdateSettings(defaultSettings);
        ThemeManager.Apply(defaultSettings);
        Host.ShowSettings("cat_backup");

        Host.SetStatus("Tüm ayarlar fabrika ayarlarına sıfırlandı.", true);
        App.Announce("Tüm ayarlar fabrika ayarlarına sıfırlandı.", true);
        MessageBox.Show(Host, "Tüm ayarlarınız varsayılan fabrika değerlerine döndürüldü.", "Sıfırlandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private FrameworkElement MakeSlider(string label, double value, double min, double max, double step, Action<double> onChange, Func<double, string> fmt)
    {
        var p = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var lbl = Ui.Text(label); lbl.VerticalAlignment = VerticalAlignment.Center; lbl.MinWidth = 280; lbl.Margin = new Thickness(0, 0, 10, 0);
        var val = Ui.Text(fmt(value)); val.VerticalAlignment = VerticalAlignment.Center; val.MinWidth = 100;
        var sl = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = step, LargeChange = step * 2, TickFrequency = step, IsSnapToTickEnabled = true, MinWidth = 220 };

        void UpdateAccessibleName()
        {
            Ui.Name(sl, $"{label} {fmt(sl.Value)}");
        }
        UpdateAccessibleName();

        sl.ValueChanged += (_, _) =>
        {
            val.Text = fmt(sl.Value);
            UpdateAccessibleName();
            if (!_syncing) onChange(sl.Value);
        };
        DockPanel.SetDock(lbl, Dock.Left); DockPanel.SetDock(val, Dock.Right);
        p.Children.Add(lbl); p.Children.Add(val); p.Children.Add(sl);
        return p;
    }

    private void Update(Action<AppSettings> change)
    {
        if (_syncing) return;
        var s = App.Repo.Settings;
        change(s);
        App.Repo.UpdateSettings(s);
    }

    private void SetTheme(string theme)
    {
        if (_syncing) return;
        Update(x => x.AppTheme = theme);
        ThemeManager.Apply(App.Repo.Settings);
        var label = theme switch
        {
            "Koyu" => "Koyu Tema",
            "Acik" => "Açık Tema",
            _ => "Sistem Teması"
        };
        App.Announce($"Tema değiştirildi: {label}");
    }

    private void SetEngine(string? id)
    {
        if (_syncing) return;
        _engineId = id;
        Update(x => { x.SelectedTtsEngine = id; x.SelectedVoiceId = null; });
        RefreshVoices();
        if (App.Tts.ActiveEngine is EdgeTtsEngine edge) edge.PreWarm();
        App.Announce("Konuşma motoru değiştirildi, " + App.Tts.ActiveEngine.DisplayName);
    }

    private void RefreshVoices()
    {
        var was = _syncing; _syncing = true;
        _voices.Items.Clear();
        var engine = App.Tts.ActiveEngine;
        var list = engine.GetVoices().OrderByDescending(v => v.IsTurkish).ThenBy(v => v.Name).ToList();
        var active = App.Tts.ActiveVoiceId;
        foreach (var v in list)
        {
            var item = new ListBoxItem { Content = v.Display, Tag = v };
            Ui.Name(item, v.Display);
            _voices.Items.Add(item);
            if (v.Id == active) item.IsSelected = true;
        }
        _voiceInfo.Text = list.Count == 0
            ? $"{engine.DisplayName} motorunda yüklü ses bulunamadı. Windows Ayarlar > Zaman ve dil > Konuşma bölümünden ses ekleyebilirsiniz."
            : $"Kullanılan motor: {engine.DisplayName}. {list.Count} ses bulundu.";
        if (engine is SapiEngine sapi && sapi.UnusableVoices.Count > 0)
            _voiceInfo.Text += $" Windows'ta kayıtlı olduğu hâlde yüklenemeyen {sapi.UnusableVoices.Count} ses gizlendi: {string.Join(", ", sapi.UnusableVoices)}. Bu seslerin kurulumunu onarmanız gerekebilir.";
        _syncing = was;
    }

    private async void PreviewVoice()
    {
        var engine = App.Tts.ActiveEngine;
        var voice = (_voices.SelectedItem as ListBoxItem)?.Tag as VoiceDescriptor;
        await App.Tts.PreviewAsync("Merhaba, bu ses Erişilebilir Kitap Okuyucu tarafından okunuyor.", engine, voice?.Id ?? App.Tts.ActiveVoiceId);
    }

    private static void OpenUrl(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Announce("Bağlantı açılamadı: " + ex.Message, true); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var desc in Descendants<T>(child)) yield return desc;
        }
    }

    public override void OnHidden() => App.Tts.Stop();
}
