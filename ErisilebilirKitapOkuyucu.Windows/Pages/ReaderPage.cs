using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class ReaderPage : PageBase
{
    private readonly BookItem _book;
    private DocumentSession? _doc;
    private int _page = 1;
    private int _loadToken;
    private bool _loading;
    private bool _firstLoadDone;

    private readonly TextBlock _heading;
    private readonly TextBlock _pageInfo = new() { FontSize = 18, Margin = new Thickness(0, 0, 0, 6) };

    /// <summary>
    /// Ekran okuyucuların (NVDA, JAWS) ok tuşlarıyla satır satır, kelime kelime ve harf harf
    /// serbestçe okuyabilmesi için standart salt-okunur TextBox.
    /// IsReadOnlyCaretVisible = true ile imleç daima etkindir.
    /// </summary>
    private readonly TextBox _text = new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = true,
        IsInactiveSelectionHighlightEnabled = true,
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MinHeight = 220,
        Padding = new Thickness(14, 12, 14, 12),
        Margin = new Thickness(0, 0, 0, 8),
        SpellCheck = { IsEnabled = false }
    };

    private readonly Button _play, _prevPage, _nextPage, _btnReadMode, _btnSpeakCurrent;
    private readonly TextBox _gotoBox = new() { Width = 90 };
    private readonly Slider _rate = new() { Minimum = 0.5, Maximum = 3.0, SmallChange = 0.1, LargeChange = 0.5, TickFrequency = 0.1, IsSnapToTickEnabled = true, MinWidth = 200 };
    private readonly Slider _pitch = new() { Minimum = 0.5, Maximum = 2.0, SmallChange = 0.1, LargeChange = 0.5, TickFrequency = 0.1, IsSnapToTickEnabled = true, MinWidth = 200 };
    private readonly Slider _volume = new() { Minimum = 0.0, Maximum = 1.0, SmallChange = 0.05, LargeChange = 0.1, TickFrequency = 0.1, IsSnapToTickEnabled = false, MinWidth = 200 };
    private readonly TextBlock _rateLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
    private readonly TextBlock _pitchLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
    private readonly TextBlock _volumeLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
    private readonly CheckBox _autoNext = new() { Content = "Sayfa bitince sonrakine geç" };
    private readonly TextBlock _sleepStatus = new() { Margin = new Thickness(0, 4, 0, 4) };
    private readonly DispatcherTimer _restartTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _syncing;
    private bool _ctrlPressedSolo;
    private bool _pageTransitioningWithTts;

    public ReaderPage(BookItem book)
    {
        _book = book;
        _page = Math.Clamp(book.CurrentPage, 1, Math.Max(1, book.TotalPages));
        _heading = Ui.Text(book.Title, "Heading");
        Ui.Name(_heading, book.Title);
        Ui.Name(_pageInfo, "Sayfa bilgisi");
        // NVDA / JAWS metin kutusuna Name atandığında her ok tuşuna basışta veya satır geçişinde
        // Name + Satır Metnini peş peşe okuyarak çift seslendirme oluşturur.
        // Bu yüzden _text üzerinde AutomationProperties.Name atanmaz.
        Ui.Name(_gotoBox, "Gidilecek sayfa numarası");
        Ui.Name(_rate, $"Oynatma hızı {Fmt.Speed(_rate.Value)}");
        Ui.Name(_pitch, $"Ses perdesi {_pitch.Value:0.0}");
        Ui.Name(_volume, $"Ses düzeyi %{Math.Round(_volume.Value * 100)}");

        _prevPage = Ui.Button("Önceki Sayfa", () => Step(-1), "PageUp veya Ctrl+PageUp");
        _play = Ui.Button("Oku", ToggleTts, "Boşluk tuşu. Sayfayı sesli okumayı başlatır veya duraklatır", "Oku", isPrimary: true);
        _nextPage = Ui.Button("Sonraki Sayfa", () => Step(1), "PageDown veya Ctrl+PageDown");
        _btnReadMode = Ui.Button($"Okuma Modu: {App.Repo.Settings.TtsReadMode}", ToggleReadMode, "Cümle veya Paragraf bazlı okuma arasında geçiş yapar");

        var nav = new WrapPanel();
        nav.Children.Add(_prevPage); nav.Children.Add(_play);
        nav.Children.Add(Ui.Button("Durdur", () => { App.Tts.Stop(); App.Announce("Durduruldu"); }));
        nav.Children.Add(_nextPage);
        nav.Children.Add(Ui.Button("Önceki Parça", () => App.Tts.PreviousSentence(), "Alt+Sol ok"));
        nav.Children.Add(Ui.Button("Sonraki Parça", () => App.Tts.NextSentence(), "Alt+Sağ ok"));

        // Satır / Paragraf / İmleç İşlemleri Düğmeleri (Metin kutusundan hemen sonra Tab ile gelir)
        var lineTools = new WrapPanel();
        _btnSpeakCurrent = Ui.Button(App.Repo.Settings.TtsReadMode == "Paragraf" ? "Bulunulan Paragrafı Seslendir" : "Bulunulan Satırı Seslendir", SpeakCurrentLine, "Enter tuşu");
        lineTools.Children.Add(Ui.Button("Önceki Paragraf", GoPreviousParagraph, "Ctrl+Yukarı ok"));
        lineTools.Children.Add(_btnSpeakCurrent);
        lineTools.Children.Add(Ui.Button("Sonraki Paragraf", GoNextParagraph, "Ctrl+Aşağı ok"));
        lineTools.Children.Add(Ui.Button("Bulunulan Satırı Kopyala", CopyCurrentLine, "Control C"));
        lineTools.Children.Add(Ui.Button("Yer İmi ve Not Ekle", AddBookmark, "Ctrl+N veya Ctrl+B"));
        lineTools.Children.Add(Ui.Button("Buradan Okumaya Başla", ReadFromCurrentLine, "Control Enter"));

        var go = new WrapPanel();
        var goLabel = Ui.Text("Sayfaya git:"); goLabel.VerticalAlignment = VerticalAlignment.Center;
        go.Children.Add(goLabel);
        _gotoBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; GoFromBox(); } };
        go.Children.Add(_gotoBox);
        go.Children.Add(Ui.Button("Git", GoFromBox, "Ctrl+G ile sayfa numarası kutusuna odaklanabilirsiniz"));

        var rateRow = new DockPanel();
        var rl = Ui.Text("Oynatma hızı:"); rl.VerticalAlignment = VerticalAlignment.Center; rl.MinWidth = 120;
        DockPanel.SetDock(rl, Dock.Left); DockPanel.SetDock(_rateLabel, Dock.Right);
        rateRow.Children.Add(rl); rateRow.Children.Add(_rateLabel); rateRow.Children.Add(_rate);
        var pitchRow = new DockPanel();
        var pl = Ui.Text("Ses perdesi:"); pl.VerticalAlignment = VerticalAlignment.Center; pl.MinWidth = 120;
        DockPanel.SetDock(pl, Dock.Left); DockPanel.SetDock(_pitchLabel, Dock.Right);
        pitchRow.Children.Add(pl); pitchRow.Children.Add(_pitchLabel); pitchRow.Children.Add(_pitch);
        var volRow = new DockPanel();
        var vl = Ui.Text("Ses düzeyi:"); vl.VerticalAlignment = VerticalAlignment.Center; vl.MinWidth = 120;
        DockPanel.SetDock(vl, Dock.Left); DockPanel.SetDock(_volumeLabel, Dock.Right);
        volRow.Children.Add(vl); volRow.Children.Add(_volumeLabel); volRow.Children.Add(_volume);

        _rate.ValueChanged += (_, _) => { if (_syncing) return; UpdateSettings(s => s.TtsSpeechRate = (float)Math.Round(_rate.Value, 2)); _rateLabel.Text = Fmt.Speed(_rate.Value); Ui.Name(_rate, $"Oynatma hızı {Fmt.Speed(_rate.Value)}"); RestartSoon(); };
        _pitch.ValueChanged += (_, _) => { if (_syncing) return; UpdateSettings(s => s.TtsPitch = (float)Math.Round(_pitch.Value, 1)); _pitchLabel.Text = $"{_pitch.Value:0.0}"; Ui.Name(_pitch, $"Ses perdesi {_pitch.Value:0.0}"); RestartSoon(); };
        _volume.ValueChanged += (_, _) => { if (_syncing) return; UpdateSettings(s => s.TtsVolume = (float)Math.Round(_volume.Value, 2)); _volumeLabel.Text = $"%{(int)Math.Round(_volume.Value * 100)}"; Ui.Name(_volume, $"Ses düzeyi %{(int)Math.Round(_volume.Value * 100)}"); RestartSoon(); };
        _autoNext.Checked += (_, _) => { if (!_syncing) UpdateSettings(s => s.AutoNextPage = true); };
        _autoNext.Unchecked += (_, _) => { if (!_syncing) UpdateSettings(s => s.AutoNextPage = false); };
        _restartTimer.Tick += (_, _) => { _restartTimer.Stop(); if (App.Tts.IsSpeaking) App.Tts.StartSpeaking(App.Tts.CurrentIndex); };

        var tools = new WrapPanel();
        tools.Children.Add(_btnReadMode);
        tools.Children.Add(Ui.Button("Uyku Zamanlayıcısı", SleepTimer, "Ctrl+T"));
        tools.Children.Add(Ui.Button("Belgede Ara", Search, "Ctrl+F"));
        tools.Children.Add(Ui.Button("Yer İmleri ve Notlar", ShowBookmarks, "Ctrl+Shift+B"));
        tools.Children.Add(Ui.Button("Sesi Dosyaya Aktar", Export, "Ctrl+E"));
        tools.Children.Add(Ui.Button("Sayfa Metnini Kopyala", CopyPage));
        tools.Children.Add(Ui.Button("Ayarlar", () => Host.ShowSettings()));
        tools.Children.Add(Ui.Button("Kitaplığa Dön", () => Host.ShowLibrary(), "Esc tuşu"));

        var controls = new StackPanel();
        controls.Children.Add(lineTools);
        controls.Children.Add(nav);
        controls.Children.Add(go);
        controls.Children.Add(rateRow);
        controls.Children.Add(pitchRow);
        controls.Children.Add(volRow);
        controls.Children.Add(_autoNext);
        controls.Children.Add(_sleepStatus);
        controls.Children.Add(tools);

        // Tab sırası: Metin kutusu -> Satır araçları -> Navigasyon -> Diğerleri
        KeyboardNavigation.SetTabIndex(_text, 0);
        KeyboardNavigation.SetTabIndex(controls, 1);

        var root = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading);
        DockPanel.SetDock(_pageInfo, Dock.Top); root.Children.Add(_pageInfo);
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);

        _text.ContextMenu = BuildContextMenu();
        _text.PreviewKeyDown += OnTextPreviewKeyDown;
        root.Children.Add(_text);
        Content = root;

        PreviewKeyDown += OnKey;
        PreviewKeyUp += OnKeyUp;
    }

    public override string PageTitle => _book.Title;
    public override UIElement? InitialFocus => _text;

    private void UpdateSettings(Action<AppSettings> change)
    {
        var s = App.Repo.Settings;
        change(s);
        App.Repo.UpdateSettings(s);
    }

    private void SyncControls()
    {
        _syncing = true;
        var s = App.Repo.Settings;
        _rate.Value = Math.Clamp(s.TtsSpeechRate, 0.5, 3.0);
        _pitch.Value = Math.Clamp(s.TtsPitch, 0.5, 2.0);
        _volume.Value = Math.Clamp(s.TtsVolume, 0.0, 1.0);
        _rateLabel.Text = Fmt.Speed(_rate.Value);
        Ui.Name(_rate, $"Oynatma hızı {Fmt.Speed(_rate.Value)}");
        _pitchLabel.Text = $"{_pitch.Value:0.0}";
        _volumeLabel.Text = $"%{(int)Math.Round(_volume.Value * 100)}";
        _autoNext.IsChecked = s.AutoNextPage;
        _text.FontSize = Math.Clamp(s.FontSizeSp, 14, 48);
        _syncing = false;
    }

    private void RestartSoon() { _restartTimer.Stop(); _restartTimer.Start(); }

    public override async void OnShown()
    {
        SyncControls();
        App.Repo.SettingsChanged += OnRepoSettingsChanged;
        App.Tts.SpeakingChanged += OnSpeakingChanged;
        App.Tts.SentenceChanged += OnSentenceChanged;
        App.Tts.PageCompleted += OnPageCompleted;
        App.Tts.Error += OnTtsError;
        App.Tts.SleepTimerFired += OnSleep;
        App.Tts.SleepTimerTick += OnSleepTick;
        UpdateSleepStatus();
        Host.SetStatus("Kitap yükleniyor…", true);
        _loading = true;
        try
        {
            _doc = await DocumentSession.OpenAsync(_book);
            if (_doc.TotalPages != _book.TotalPages)
            {
                _book.TotalPages = _doc.TotalPages;
                App.Repo.UpdateTotalPages(_book.Id, _doc.TotalPages);
            }
            _page = Math.Clamp(_page, 1, _doc.TotalPages);
            _loading = false;
            await ShowPage(_page, autoPlay: false, announce: false);
            Host.SetStatus($"{_book.Title}, sayfa {_page} / {_doc.TotalPages}");
            App.Announce($"{_book.Title} açıldı. Sayfa {_page} / {_doc.TotalPages}. Yön tuşlarıyla metni okuyabilir, sesli okumayı başlatmak için boşluk tuşuna basabilirsiniz.");
            if (App.Tts.ActiveEngine is EdgeTtsEngine edgeReader) edgeReader.PreWarm();
            FocusTextAt(0);
        }
        catch (Exception e)
        {
            _loading = false;
            AppPaths.Log("reader open: " + e);
            _text.Text = "Kitap açılamadı: " + e.Message;
            App.Announce("Kitap açılamadı: " + e.Message, true);
        }
    }

    public override void OnHidden()
    {
        App.Repo.SettingsChanged -= OnRepoSettingsChanged;
        App.Tts.Stop();
        App.Tts.SpeakingChanged -= OnSpeakingChanged;
        App.Tts.SentenceChanged -= OnSentenceChanged;
        App.Tts.PageCompleted -= OnPageCompleted;
        App.Tts.Error -= OnTtsError;
        App.Tts.SleepTimerFired -= OnSleep;
        App.Tts.SleepTimerTick -= OnSleepTick;
        _restartTimer.Stop();
        App.Repo.UpdateProgress(_book.Id, _page);
        _doc?.Dispose();
    }

    // ------------------------------------------------------------ Metin ve İmleç İşlemleri

    private void FocusTextAt(int caretIndex)
    {
        if (_text.Text.Length > 0)
        {
            var idx = Math.Clamp(caretIndex, 0, _text.Text.Length);
            _text.CaretIndex = idx;
            _text.Select(idx, 0);
            int lineIdx = _text.GetLineIndexFromCharacterIndex(idx);
            if (lineIdx >= 0) _text.ScrollToLine(lineIdx);
            if (idx == 0) _text.ScrollToHome();
        }
        else
        {
            _text.CaretIndex = 0;
            _text.Select(0, 0);
            _text.ScrollToHome();
        }
        _text.Focus();
    }

    /// <summary>
    /// İmlecin bulunduğu geçerli satırın metnini ve başlangıç/uzunluk aralığını döndürür.
    /// Eğer kullanıcı bir metin bloğu seçtiyse o bloğu döndürür.
    /// </summary>
    private (string text, int start, int length) GetCurrentLineOrSelection()
    {
        if (_text.SelectionLength > 0)
        {
            return (_text.SelectedText.Trim(), _text.SelectionStart, _text.SelectionLength);
        }

        if (string.IsNullOrEmpty(_text.Text)) return ("", 0, 0);

        try
        {
            int lineIdx = _text.GetLineIndexFromCharacterIndex(_text.CaretIndex);
            if (lineIdx >= 0 && lineIdx < _text.LineCount)
            {
                int start = _text.GetCharacterIndexFromLineIndex(lineIdx);
                int len = _text.GetLineLength(lineIdx);
                string text = _text.GetLineText(lineIdx).Trim('\r', '\n', ' ');
                return (text, start, len);
            }
        }
        catch { }

        var full = _text.Text;
        var caret = Math.Clamp(_text.CaretIndex, 0, full.Length);
        var lineStart = full.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = full.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = full.Length;
        var l = Math.Max(0, lineEnd - lineStart);
        return (full.Substring(lineStart, l).Trim('\r', ' '), lineStart, l);
    }

    private (string text, int start, int length) GetCurrentParagraph()
    {
        if (_text.SelectionLength > 0)
        {
            return (_text.SelectedText.Trim(), _text.SelectionStart, _text.SelectionLength);
        }

        if (string.IsNullOrEmpty(_text.Text)) return ("", 0, 0);

        string text = _text.Text;
        int caret = Math.Clamp(_text.CaretIndex, 0, text.Length);

        // Önceki çift satır sonunu (\r\n\r\n veya \n\n) bul
        int paraStart = 0;
        var prevMatches = Regex.Matches(text.Substring(0, caret), @"\r?\n\s*\r?\n", RegexOptions.RightToLeft);
        if (prevMatches.Count > 0)
        {
            paraStart = prevMatches[0].Index + prevMatches[0].Length;
        }

        // Sonraki çift satır sonunu bul
        int paraEnd = text.Length;
        var nextMatch = Regex.Match(text.Substring(caret), @"\r?\n\s*\r?\n");
        if (nextMatch.Success)
        {
            paraEnd = caret + nextMatch.Index;
        }

        while (paraStart < paraEnd && (text[paraStart] == '\r' || text[paraStart] == '\n' || text[paraStart] == ' '))
            paraStart++;
        while (paraEnd > paraStart && (text[paraEnd - 1] == '\r' || text[paraEnd - 1] == '\n' || text[paraEnd - 1] == ' '))
            paraEnd--;

        int len = Math.Max(0, paraEnd - paraStart);
        string result = len > 0 ? text.Substring(paraStart, len).Trim() : "";

        if (string.IsNullOrWhiteSpace(result))
        {
            return GetCurrentLineOrSelection();
        }

        return (result, paraStart, len);
    }

    private void SpeakCurrentLine()
    {
        bool isParaMode = App.Repo.Settings.TtsReadMode == "Paragraf";
        var (textToSpeak, _, _) = isParaMode ? GetCurrentParagraph() : GetCurrentLineOrSelection();
        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            App.Announce(isParaMode ? "Bu paragraf boş" : "Bu satır boş");
            return;
        }
        App.Tts.Stop();
        _ = App.Tts.PreviewAsync(textToSpeak, App.Tts.ActiveEngine, App.Tts.ActiveVoiceId);
    }

    private void CopyCurrentLine()
    {
        bool isParaMode = App.Repo.Settings.TtsReadMode == "Paragraf";
        var (textToCopy, _, _) = isParaMode ? GetCurrentParagraph() : GetCurrentLineOrSelection();
        if (string.IsNullOrWhiteSpace(textToCopy))
        {
            App.Announce(isParaMode ? "Kopyalanacak paragraf boş" : "Kopyalanacak satır boş");
            return;
        }
        try
        {
            Clipboard.SetText(textToCopy);
            Host.SetStatus(isParaMode ? "Paragraf panoya kopyalandı." : "Satır panoya kopyalandı.", true);
            App.Announce(isParaMode ? "Paragraf kopyalandı" : "Satır kopyalandı");
        }
        catch (Exception e)
        {
            Host.SetStatus("Kopyalanamadı: " + e.Message, true);
        }
    }

    private void ReadFromCurrentLine()
    {
        if (_doc == null) return;
        bool isParaMode = App.Repo.Settings.TtsReadMode == "Paragraf";
        var (_, start, _) = isParaMode ? GetCurrentParagraph() : GetCurrentLineOrSelection();
        var sentences = App.Tts.Sentences;
        int targetIdx = 0;
        for (int i = 0; i < sentences.Count; i++)
        {
            if (sentences[i].Start + sentences[i].Length > start)
            {
                targetIdx = i;
                break;
            }
        }
        App.Tts.StartSpeaking(targetIdx);
        _play.Focus();
        App.Announce(isParaMode
            ? "Bu paragraftan itibaren sesli okuma başlatıldı"
            : "Bu satırdan itibaren sesli okuma başlatıldı");
    }

    private List<int> GetParagraphStartIndices()
    {
        var starts = new List<int> { 0 };
        string text = _text.Text;
        if (string.IsNullOrEmpty(text)) return starts;

        var matches = Regex.Matches(text, @"\r?\n\s*\r?\n");
        foreach (Match m in matches)
        {
            int idx = m.Index + m.Length;
            while (idx < text.Length && (text[idx] == '\r' || text[idx] == '\n' || text[idx] == ' '))
                idx++;
            if (idx < text.Length && !starts.Contains(idx))
                starts.Add(idx);
        }
        return starts;
    }

    private async void GoNextParagraph()
    {
        if (string.IsNullOrEmpty(_text.Text)) return;
        int caret = _text.CaretIndex;
        var starts = GetParagraphStartIndices();

        int nextStart = -1;
        foreach (var s in starts)
        {
            if (s > caret)
            {
                nextStart = s;
                break;
            }
        }

        if (nextStart != -1)
        {
            FocusTextAt(nextStart);
            var (para, _, _) = GetCurrentParagraph();
            if (!string.IsNullOrWhiteSpace(para))
                App.Announce(para);
            return;
        }

        // Sayfa sonundayız: bir sonraki sayfaya geç ve 1. paragraftan devam et
        if (_doc != null && _page < _doc.TotalPages)
        {
            await ShowPage(_page + 1, autoPlay: false, announce: false);
            FocusTextAt(0);
            var (firstPara, _, _) = GetCurrentParagraph();
            App.Announce($"Sayfa {_page} / {_doc.TotalPages}. {firstPara}".Trim());
        }
        else
        {
            App.Announce("Kitabın sonundasınız");
        }
    }

    private async void GoPreviousParagraph()
    {
        if (string.IsNullOrEmpty(_text.Text)) return;
        int caret = _text.CaretIndex;
        var starts = GetParagraphStartIndices();

        int currentStart = 0;
        int prevStart = -1;
        for (int i = 0; i < starts.Count; i++)
        {
            if (starts[i] <= caret)
            {
                currentStart = starts[i];
                prevStart = i > 0 ? starts[i - 1] : -1;
            }
            else break;
        }

        int target = (caret > currentStart + 1) ? currentStart : prevStart;

        if (target != -1)
        {
            FocusTextAt(target);
            var (para, _, _) = GetCurrentParagraph();
            if (!string.IsNullOrWhiteSpace(para))
                App.Announce(para);
            return;
        }

        // Sayfanın en başındayız: bir önceki sayfaya geç ve o sayfanın son paragrafına odaklan
        if (_doc != null && _page > 1)
        {
            await ShowPage(_page - 1, autoPlay: false, announce: false);
            var prevPageStarts = GetParagraphStartIndices();
            int lastStart = prevPageStarts.Count > 0 ? prevPageStarts[^1] : 0;
            FocusTextAt(lastStart);
            var (lastPara, _, _) = GetCurrentParagraph();
            App.Announce($"Sayfa {_page} / {_doc.TotalPages}. {lastPara}".Trim());
        }
        else
        {
            App.Announce("İlk sayfadasınız");
        }
    }

    private void ToggleReadMode()
    {
        var s = App.Repo.Settings;
        s.TtsReadMode = s.TtsReadMode == "Paragraf" ? "Cümle" : "Paragraf";
        App.Repo.UpdateSettings(s);
        UpdateReadModeUi();
        App.Tts.LoadPageText(_text.Text, 0);
        App.Announce($"Okuma modu {s.TtsReadMode} olarak ayarlandı");
    }

    private void UpdateReadModeUi()
    {
        var s = App.Repo.Settings;
        bool isPara = s.TtsReadMode == "Paragraf";
        _btnReadMode.Content = $"Okuma Modu: {s.TtsReadMode}";
        Ui.Name(_btnReadMode, $"Okuma Modu: {s.TtsReadMode}");
        if (_btnSpeakCurrent != null)
        {
            _btnSpeakCurrent.Content = isPara ? "Bulunulan Paragrafı Seslendir" : "Bulunulan Satırı Seslendir";
            Ui.Name(_btnSpeakCurrent, isPara ? "Bulunulan Paragrafı Seslendir" : "Bulunulan Satırı Seslendir");
        }
    }

    private void OnRepoSettingsChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateReadModeUi();
            if (_doc != null && !string.IsNullOrEmpty(_text.Text))
            {
                App.Tts.LoadPageText(_text.Text, App.Tts.CurrentIndex);
            }
        });
    }

    private ContextMenu BuildContextMenu()
    {
        var m = new ContextMenu();
        void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
        bool isPara = App.Repo.Settings.TtsReadMode == "Paragraf";
        Add("Önceki Paragraf (Ctrl+Yukarı Ok)", GoPreviousParagraph);
        Add("Sonraki Paragraf (Ctrl+Aşağı Ok)", GoNextParagraph);
        Add(isPara ? "Bulunulan Paragrafı Seslendir (Enter)" : "Bulunulan Satırı Seslendir (Enter)", SpeakCurrentLine);
        Add(isPara ? "Bulunulan Paragrafı Kopyala (Ctrl+C)" : "Bulunulan Satırı Kopyala (Ctrl+C)", CopyCurrentLine);
        Add("Yer İmi ve Not / Alıntı Ekle (Ctrl+N, Ctrl+B)", AddBookmark);
        Add(isPara ? "Bu Paragraftan İtibaren Okumaya Başla (Ctrl+Enter)" : "Bu Satırdan İtibaren Okumaya Başla (Ctrl+Enter)", ReadFromCurrentLine);
        m.Items.Add(new Separator());
        Add("Tüm Sayfa Metnini Kopyala", CopyPage);
        return m;
    }

    private void OnTextPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;

        // Ctrl + Aşağı ok: Sonraki paragrafa geç
        if (ShortcutService.Matches("Reader.NextParagraph", e))
        {
            GoNextParagraph();
            e.Handled = true;
            return;
        }

        if (ShortcutService.Matches("Reader.PrevParagraph", e))
        {
            GoPreviousParagraph();
            e.Handled = true;
            return;
        }

        if (ShortcutService.Matches("Reader.SpeakCurrentLine", e))
        {
            SpeakCurrentLine();
            e.Handled = true;
            return;
        }

        if (ShortcutService.Matches("Reader.ReadFromCurrentLine", e))
        {
            ReadFromCurrentLine();
            e.Handled = true;
            return;
        }

        if (ShortcutService.Matches("Reader.CopyCurrentLine", e) && _text.SelectionLength == 0)
        {
            CopyCurrentLine();
            e.Handled = true;
            return;
        }

        if (ShortcutService.Matches("Reader.TogglePlay", e))
        {
            ToggleTts();
            e.Handled = true;
            return;
        }

        // Aşağı ok tuşu: Sayfanın son satırındayken bir sonraki sayfaya geç
        if (e.Key == Key.Down && mods == ModifierKeys.None)
        {
            if (_text.LineCount > 0)
            {
                int curLine = _text.GetLineIndexFromCharacterIndex(_text.CaretIndex);
                if (curLine >= _text.LineCount - 1)
                {
                    if (_doc != null && _page < _doc.TotalPages)
                    {
                        Step(1, announce: true, landAtEnd: false);
                        e.Handled = true;
                        return;
                    }
                    else if (_doc != null && _page >= _doc.TotalPages)
                    {
                        App.Announce("Kitabın sonundasınız");
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // Yukarı ok tuşu: Sayfanın ilk satırındayken bir önceki sayfaya geç
        if (e.Key == Key.Up && mods == ModifierKeys.None)
        {
            if (_text.LineCount > 0)
            {
                int curLine = _text.GetLineIndexFromCharacterIndex(_text.CaretIndex);
                if (curLine <= 0)
                {
                    if (_doc != null && _page > 1)
                    {
                        Step(-1, announce: true, landAtEnd: true);
                        e.Handled = true;
                        return;
                    }
                    else if (_doc != null && _page <= 1)
                    {
                        App.Announce("İlk sayfadasınız");
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ Sayfa Gezinme

    private async Task ShowPage(int page, bool autoPlay, bool announce = true, bool landAtEnd = false)
    {
        if (_doc == null) return;
        var firstLoad = !_firstLoadDone;
        _firstLoadDone = true;
        var token = ++_loadToken;
        var hadFocus = _text.IsKeyboardFocusWithin || firstLoad;

        if (autoPlay)
        {
            _pageTransitioningWithTts = true;
            _play.Focus(); // TTS ile sayfa geçişinde odağı hemen Oku/Duraklat butonunda tut, metin kutusuna düşüp ekran okuyucuyu tetiklemesin
        }

        App.Tts.Stop();
        page = Math.Clamp(page, 1, _doc.TotalPages);
        _loading = true;
        _text.Text = "Sayfa yükleniyor…";

        var text = await _doc.GetPageTextAsync(page);
        if (token != _loadToken) return;

        _loading = false;
        _page = page;
        // Satır sonlarını Windows standardı \r\n olarak normalize et.
        // Paragraf ayrımlarını (\r\n \r\n) yap: Boş satırlara tek boşluk koyarak WPF UIA'nın
        // boş satırda sonraki satırı çift okuma (double reading) hatasını engelle.
        var cleanNewlines = text.Replace("\r\n", "\n").Replace('\r', '\n');
        cleanNewlines = Regex.Replace(cleanNewlines, @"\n[ \t]*\n+", "\n\n");
        var normalized = cleanNewlines.Replace("\n\n", "___PARABREAK___")
                                      .Replace("\n", "\r\n")
                                      .Replace("___PARABREAK___", "\r\n \r\n")
                                      .Trim();
        _text.Text = normalized;
        App.Tts.LoadPageText(normalized);

        _pageInfo.Text = $"Sayfa {_page} / {_doc.TotalPages}";
        Ui.Name(_pageInfo, _pageInfo.Text);
        _prevPage.IsEnabled = _page > 1;
        _nextPage.IsEnabled = _page < _doc.TotalPages;

        App.Repo.UpdateProgress(_book.Id, _page);
        Host.SetStatus($"{_book.Title}, {_pageInfo.Text}");

        if (autoPlay)
        {
            _pageTransitioningWithTts = false;
            App.Tts.StartSpeaking(0);
            _play.Focus();
        }
        else
        {
            _pageTransitioningWithTts = false;
            string lineTextToAnnounce = "";
            int targetCharIndex = 0;

            if (_text.LineCount > 0)
            {
                if (landAtEnd)
                {
                    int lastLine = _text.LineCount - 1;
                    while (lastLine > 0 && string.IsNullOrWhiteSpace(_text.GetLineText(lastLine).Trim()))
                        lastLine--;
                    targetCharIndex = _text.GetCharacterIndexFromLineIndex(lastLine);
                    lineTextToAnnounce = _text.GetLineText(lastLine).Trim();
                }
                else
                {
                    int firstLine = 0;
                    while (firstLine < _text.LineCount - 1 && string.IsNullOrWhiteSpace(_text.GetLineText(firstLine).Trim()))
                        firstLine++;
                    targetCharIndex = _text.GetCharacterIndexFromLineIndex(firstLine);
                    lineTextToAnnounce = _text.GetLineText(firstLine).Trim();
                }
            }

            if (hadFocus)
            {
                FocusTextAt(targetCharIndex);
            }

            if (announce)
            {
                // Eğer odak zaten metin kutusundaysa, FocusTextAt() imleci hedef satıra taşır ve
                // ekran okuyucu (NVDA/JAWS) o satırı doğal olarak otomatik seslendirir.
                // Burada tekrar lineTextToAnnounce eklenirse aynı satır iki defa okunur.
                // Bu nedenle metin kutusu odaklıyken yalnızca sayfa numarası duyurulur.
                if (!hadFocus && !string.IsNullOrWhiteSpace(lineTextToAnnounce))
                    App.Announce($"{_pageInfo.Text}. {lineTextToAnnounce}");
                else
                    App.Announce(_pageInfo.Text);
            }
        }
    }

    private async void Step(int delta, bool announce = true, bool landAtEnd = false)
    {
        if (_doc == null || _loading) return;
        var target = _page + delta;
        if (target < 1) { App.Announce("İlk sayfadasınız"); return; }
        if (target > _doc.TotalPages) { App.Announce("Son sayfadasınız"); return; }
        var wasSpeaking = App.Tts.IsSpeaking;
        if (App.Repo.Settings.PageTurnVoice != "Kapalı")
            PageTurnSounds.Play(App.Repo.Settings.PageTurnVoice, delta > 0);
        await ShowPage(target, autoPlay: wasSpeaking, announce: announce, landAtEnd: landAtEnd);
    }

    private async void GoFromBox()
    {
        if (_doc == null) return;
        if (!int.TryParse(_gotoBox.Text.Trim(), out var n) || n < 1 || n > _doc.TotalPages)
        {
            App.Announce($"Geçersiz sayfa. 1 ile {_doc.TotalPages} arasında bir sayı girin.", true);
            return;
        }
        await ShowPage(n, autoPlay: false);
        FocusTextAt(0);
    }

    private void ToggleTts()
    {
        if (_doc == null || _loading) return;

        if (!App.Tts.IsSpeaking)
        {
            int targetIdx = 0;
            if (_text.IsKeyboardFocusWithin)
            {
                // Metin kutusundayken imlecin olduğu cümleden başla
                var (_, start, _) = GetCurrentLineOrSelection();
                var sentences = App.Tts.Sentences;
                for (int i = 0; i < sentences.Count; i++)
                {
                    if (sentences[i].Start + sentences[i].Length > start)
                    {
                        targetIdx = i;
                        break;
                    }
                }
            }
            _play.Focus(); // Ekran okuyucunun metin alanını tekrar okumasını ve çift ses çıkmasını önle
            App.Tts.StartSpeaking(targetIdx);
        }
        else
        {
            App.Tts.PauseOrResume();
        }
        bool isSpeaking = App.Tts.IsSpeaking;
        App.Announce(isSpeaking ? "Okuma başladı" : "Okuma duraklatıldı");
    }

    private void RestoreCaretAndFocusAfterTts(bool focus = false)
    {
        int cur = App.Tts.CurrentIndex;
        var sentences = App.Tts.Sentences;
        int targetIdx = (cur >= 0 && cur < sentences.Count) ? sentences[cur].Start : _text.CaretIndex;
        if (focus)
        {
            FocusTextAt(targetIdx);
        }
        else if (_text.Text.Length > 0)
        {
            var idx = Math.Clamp(targetIdx, 0, _text.Text.Length);
            _text.CaretIndex = idx;
            _text.Select(idx, 0);
            int lineIdx = _text.GetLineIndexFromCharacterIndex(idx);
            if (lineIdx >= 0) _text.ScrollToLine(lineIdx);
            if (idx == 0) _text.ScrollToHome();
        }
    }

    private void OnSpeakingChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var speaking = App.Tts.IsSpeaking;
            if (speaking)
            {
                _play.Focus();
            }
            else if (!_pageTransitioningWithTts)
            {
                RestoreCaretAndFocusAfterTts(focus: _play.IsKeyboardFocused);
            }
            _play.Content = speaking ? "Duraklat" : (App.Tts.CurrentIndex > 0 ? "Devam Et" : "Oku");
            Ui.Name(_play, speaking ? "Duraklat" : (App.Tts.CurrentIndex > 0 ? "Devam Et" : "Oku"));
        });
    }

    private void OnSentenceChanged(int index)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (index < 0 || index >= App.Tts.Sentences.Count) return;
            var s = App.Tts.Sentences[index];
            if (s.Start >= 0 && s.Start + s.Length <= _text.Text.Length)
            {
                _text.Select(s.Start, s.Length);
            }
        });
    }

    private void OnPageCompleted()
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (_doc == null) return;
            var s = App.Repo.Settings;
            if (s.AutoNextPage && _page < _doc.TotalPages)
            {
                var token = _loadToken;
                if (s.PlayPageTurnSoundOnTtsNextPage && s.PageTurnVoice != "Kapalı")
                {
                    PageTurnSounds.Play(s.PageTurnVoice, true);
                    await Task.Delay(PageTurnSounds.DelayMs(s.PageTurnVoice));
                    if (token != _loadToken) return;
                }
                await ShowPage(_page + 1, autoPlay: true, announce: false);
            }
            else if (_page >= _doc.TotalPages)
            {
                Host.SetStatus("Kitabın sonuna gelindi.", true);
            }
        });
    }

    private void OnTtsError(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Host.SetStatus(message, true);
            MessageBox.Show(Host, message, "Konuşma hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F4 && (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || e.Key == Key.System))
        {
            e.Handled = true;
            Host.Close();
            return;
        }
        var focus = Keyboard.FocusedElement;

        // Ctrl tuşuna tek başına basılıp bırakılmasını izlemek için:
        // Eğer kullanıcı Ctrl'ye basarsa takibi başlatırız; ancak ardından Shift, Ok veya başka bir tuşa basarsa
        // bu artık tek başına Ctrl basımı değildir ve TTS'yi duraklatmaz (böylece Ctrl+Shift+Sağ/Sol gibi kısayollar bozulmaz).
        if (key == Key.LeftCtrl || key == Key.RightCtrl)
        {
            _ctrlPressedSolo = true;
            return;
        }
        _ctrlPressedSolo = false;

        // Kısayol yöneticisi ile özelleştirilebilir kısayol kontrolleri
        if (ShortcutService.Matches("Reader.NextPage", e)) { Step(1, announce: true, landAtEnd: false); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.PrevPage", e)) { Step(-1, announce: true, landAtEnd: false); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.NextParagraph", e)) { GoNextParagraph(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.PrevParagraph", e)) { GoPreviousParagraph(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.NextSentence", e)) { App.Tts.NextSentence(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.PrevSentence", e)) { App.Tts.PreviousSentence(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.GoToPage", e)) { _gotoBox.Focus(); _gotoBox.SelectAll(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.Search", e)) { Search(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.AddBookmark", e)) { AddBookmark(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.ShowBookmarks", e)) { ShowBookmarks(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.Export", e)) { Export(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.SleepTimer", e)) { SleepTimer(); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.RateUp", e)) { _rate.Value = Math.Min(3.0, Math.Round(_rate.Value + 0.1, 1)); App.Announce($"Hız {Fmt.Speed(_rate.Value)}"); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.RateDown", e)) { _rate.Value = Math.Max(0.5, Math.Round(_rate.Value - 0.1, 1)); App.Announce($"Hız {Fmt.Speed(_rate.Value)}"); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.VolUp", e)) { _volume.Value = Math.Min(1.0, _volume.Value + 0.05); App.Announce($"Ses %{(int)Math.Round(_volume.Value * 100)}"); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.VolDown", e)) { _volume.Value = Math.Max(0.0, _volume.Value - 0.05); App.Announce($"Ses %{(int)Math.Round(_volume.Value * 100)}"); e.Handled = true; return; }
        if (ShortcutService.Matches("Reader.TogglePlay", e) && (_text.IsKeyboardFocusWithin || !Ui.IsActivatableTarget(focus)))
        {
            ToggleTts(); e.Handled = true; return;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Kullanıcı başka bir tuşa basmadan yalnızca Ctrl'ye basıp bıraktıysa ve TTS konuşuyorsa duraklat
        if ((key == Key.LeftCtrl || key == Key.RightCtrl) && _ctrlPressedSolo)
        {
            _ctrlPressedSolo = false;
            if (App.Tts.IsSpeaking)
            {
                ToggleTts();
                e.Handled = true;
            }
        }
    }

    private void Search()
    {
        if (_doc == null) return;
        App.Tts.Stop();
        var d = new SearchDialog(Host, _doc);
        if (d.ShowDialog() == true && d.GoToPage is int p) _ = ShowPage(p, autoPlay: false);
        else FocusTextAt(_text.CaretIndex);
    }

    private void AddBookmark()
    {
        if (_doc == null) return;
        App.Tts.Stop();
        var (line, _, _) = GetCurrentLineOrSelection();
        var caret = _text.CaretIndex;
        var suggestion = line.Length > 160 ? line[..160] + "…" : line;
        var d = new AddBookmarkDialog(Host, $"Sayfa {_page} / {_doc.TotalPages}", suggestion);
        if (d.ShowDialog() == true)
        {
            App.Repo.AddBookmark(_book.Id, new Bookmark
            {
                Title = $"Sayfa {_page}", PageNumber = _page, Note = d.Note
            });
            Host.SetStatus($"Yer imi eklendi: sayfa {_page}", true);
        }
        FocusTextAt(caret);
    }

    private void ShowBookmarks()
    {
        App.Tts.Stop();
        var caret = _text.CaretIndex;
        var d = new BookmarksDialog(Host, _book);
        if (d.ShowDialog() == true && d.GoTo is { } bm) _ = ShowPage(bm.PageNumber, autoPlay: false);
        else FocusTextAt(caret);
    }

    private void Export()
    {
        if (_doc == null) return;
        App.Tts.Stop();
        var caret = _text.CaretIndex;
        new ExportDialog(Host, _book, _doc, _page).ShowDialog();
        FocusTextAt(caret);
    }

    private void CopyPage()
    {
        try { Clipboard.SetText(_text.Text); Host.SetStatus("Sayfa metni panoya kopyalandı.", true); }
        catch (Exception e) { Host.SetStatus("Kopyalanamadı: " + e.Message, true); }
    }

    private void SleepTimer()
    {
        var options = new[] { "Kapalı", "5 dakika", "10 dakika", "15 dakika", "30 dakika", "45 dakika", "60 dakika" };
        var mins = new[] { 0, 5, 10, 15, 30, 45, 60 };
        var i = ChoiceDialog.Pick(Host, "Uyku Zamanlayıcısı", options, 0, "Belirtilen süre sonunda okuma otomatik durur.");
        if (i < 0) { InitialFocus?.Focus(); return; }
        App.Tts.StartSleepTimer(mins[i]);
        Host.SetStatus(mins[i] == 0 ? "Uyku zamanlayıcısı kapatıldı." : $"Uyku zamanlayıcısı {mins[i]} dakikaya ayarlandı.", true);
        UpdateSleepStatus();
        _play.Focus();
    }

    private void OnSleep()
    {
        Dispatcher.InvokeAsync(() =>
        {
            Host.SetStatus("Uyku zamanlayıcısı doldu, okuma durduruldu.", true);
            App.Announce("Uyku zamanlayıcısı doldu, okuma durduruldu.", true);
            UpdateSleepStatus();
        });
    }

    private void OnSleepTick() => Dispatcher.InvokeAsync(UpdateSleepStatus);

    private void UpdateSleepStatus()
    {
        int s = App.Tts.SleepSecondsLeft;
        _sleepStatus.Text = s > 0 ? $"Uyku zamanlayıcısı: {s / 60} dakika {s % 60} saniye kaldı" : "";
        _sleepStatus.Visibility = s > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
