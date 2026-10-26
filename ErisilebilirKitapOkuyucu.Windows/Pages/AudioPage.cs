using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class AudioPage : PageBase
{
    private readonly BookItem _book;
    private readonly bool _resume;
    private int _chapter;
    private bool _syncing;
    private int _saveCounter;
    private bool _handlersAttached;

    private readonly TextBlock _heading;
    private readonly TextBlock _nowPlaying = new() { FontSize = 20, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _time = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Slider _pos = new() { Minimum = 0, Maximum = 1, SmallChange = 15, LargeChange = 60, MinWidth = 300 };
    private readonly Slider _speed = new() { Minimum = 0.5, Maximum = 3.0, SmallChange = 0.25, LargeChange = 0.5, TickFrequency = 0.25, IsSnapToTickEnabled = true };
    private readonly TextBlock _speedLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly Slider _vol = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock _volLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly CheckBox _loop = new() { Content = "Bu kaydı tekrarla" };
    private readonly Button _play, _back, _fwd, _prevCh, _nextCh;
    private readonly TextBlock _sleepStatus = new();
    private readonly ListBox _chapters = new() { MaxHeight = 220, MinHeight = 80 };

    private bool IsZip => _book.IsZipArchive && _book.Chapters.Count > 0;
    private int Seek => Math.Clamp(App.Repo.Settings.SeekIntervalSeconds, 5, 60);

    public AudioPage(BookItem book, bool resume = false)
    {
        _book = book; _resume = resume;
        _chapter = Math.Clamp(book.CurrentChapterIndex, 0, Math.Max(0, book.Chapters.Count - 1));
        _heading = Ui.Text(book.Title, "Heading");
        Ui.Name(_heading, book.Title);
        Ui.Name(_nowPlaying, "Şu an çalan");
        Ui.Name(_pos, "Konum");
        Ui.Name(_speed, "Oynatma hızı");
        Ui.Name(_vol, "Ses düzeyi");

        _back = Ui.Button($"{Seek} sn Geri", () => App.Audio.SeekBy(-Seek), "Sol ok");
        _play = Ui.Button("Oynat", () => App.Audio.TogglePlayPause(), "Boşluk tuşu. Medya oynat/duraklat tuşu da çalışır", "Oynat", isPrimary: true);
        _fwd = Ui.Button($"{Seek} sn İleri", () => App.Audio.SeekBy(Seek), "Sağ ok");
        _prevCh = Ui.Button("Önceki Ayrım", () => ChangeChapter(-1), "Ctrl+Sol ok");
        _nextCh = Ui.Button("Sonraki Ayrım", () => ChangeChapter(1), "Ctrl+Sağ ok");

        var transport = new WrapPanel();
        if (IsZip) transport.Children.Add(_prevCh);
        transport.Children.Add(_back); transport.Children.Add(_play); transport.Children.Add(_fwd);
        if (IsZip) transport.Children.Add(_nextCh);

        _pos.ValueChanged += (_, _) => { if (!_syncing) App.Audio.SeekTo((long)(_pos.Value * 1000)); };

        var speedRow = new DockPanel();
        var sl = Ui.Text("Çalma hızı:"); sl.VerticalAlignment = VerticalAlignment.Center; sl.MinWidth = 120;
        DockPanel.SetDock(sl, Dock.Left); DockPanel.SetDock(_speedLabel, Dock.Right);
        speedRow.Children.Add(sl); speedRow.Children.Add(_speedLabel); speedRow.Children.Add(_speed);
        _speed.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            App.Audio.SetSpeed(_speed.Value);
            _speedLabel.Text = $"{_speed.Value:0.##}x";
            var s = App.Repo.Settings; s.AudioSpeed = (float)_speed.Value; App.Repo.UpdateSettings(s);
        };

        var volRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var vl = Ui.Text("Ses düzeyi:"); vl.VerticalAlignment = VerticalAlignment.Center; vl.MinWidth = 120;
        DockPanel.SetDock(vl, Dock.Left); DockPanel.SetDock(_volLabel, Dock.Right);
        volRow.Children.Add(vl); volRow.Children.Add(_volLabel); volRow.Children.Add(_vol);
        _vol.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            var v = Math.Clamp(_vol.Value / 100.0, 0.0, 1.0);
            App.Audio.SetVolume(v);
            _volLabel.Text = $"%{Math.Round(_vol.Value)}";
            Ui.Name(_vol, $"Ses düzeyi %{Math.Round(_vol.Value)}");
            var s = App.Repo.Settings; s.AudioVolume = (float)v; App.Repo.UpdateSettings(s);
        };

        _loop.Checked += (_, _) => { if (!_syncing) App.Audio.SetLooping(true); };
        _loop.Unchecked += (_, _) => { if (!_syncing) App.Audio.SetLooping(false); };

        var tools = new WrapPanel();
        tools.Children.Add(Ui.Button("Uyku Zamanlayıcısı", SleepTimer, "Ctrl+T"));
        tools.Children.Add(Ui.Button("Yer İmi Ekle", AddBookmark, "Ctrl+B"));
        tools.Children.Add(Ui.Button("Yer İmleri", ShowBookmarks, "Ctrl+Shift+B"));
        tools.Children.Add(Ui.Button("Ayarlar", () => Host.ShowSettings()));
        tools.Children.Add(Ui.Button("Kitaplığa Dön", () => Host.ShowLibrary(), "Esc tuşu. Ses arka planda çalmaya devam eder"));

        var controls = new StackPanel();
        controls.Children.Add(_nowPlaying);
        controls.Children.Add(transport);
        controls.Children.Add(_pos); controls.Children.Add(_time);
        controls.Children.Add(speedRow);
        controls.Children.Add(volRow);
        controls.Children.Add(_loop);
        controls.Children.Add(_sleepStatus);
        controls.Children.Add(tools);

        var root = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading);
        if (IsZip)
        {
            var chLabel = Ui.Text("Ayrımlar", "SubHeading");
            DockPanel.SetDock(chLabel, Dock.Top);
            Ui.Name(_chapters, "Ayrımlar listesi");
            _chapters.MouseDoubleClick += (_, _) => PlaySelectedChapter();
            _chapters.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; PlaySelectedChapter(); } };
            for (int i = 0; i < _book.Chapters.Count; i++)
            {
                var item = new ListBoxItem { Content = $"{i + 1}. {_book.Chapters[i].Title}", Tag = i };
                Ui.Name(item, $"Ayrım {i + 1}: {_book.Chapters[i].Title}");
                _chapters.Items.Add(item);
            }
            DockPanel.SetDock(_chapters, Dock.Bottom);
            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);
            root.Children.Add(chLabel);
            root.Children.Add(_chapters);
            _chapters.MaxHeight = double.PositiveInfinity;
        }
        else root.Children.Add(controls);
        Content = root;

        PreviewKeyDown += OnKey;
    }

    public override string PageTitle => _book.Title;
    public override UIElement? InitialFocus => _play;

    public override void OnShown()
    {
        Attach();
        var s = App.Repo.Settings;
        _syncing = true;
        _speed.Value = Math.Clamp(_resume ? App.Audio.Speed : s.AudioSpeed, 0.5, 3.0);
        _speedLabel.Text = $"{_speed.Value:0.##}x";
        var volVal = Math.Clamp(s.AudioVolume > 0 ? s.AudioVolume : 1.0f, 0.0f, 1.0f);
        _vol.Value = Math.Round(volVal * 100);
        _volLabel.Text = $"%{Math.Round(_vol.Value)}";
        Ui.Name(_vol, $"Ses düzeyi %{Math.Round(_vol.Value)}");
        _loop.IsChecked = App.Audio.IsLooping;
        _syncing = false;
        App.Audio.SetSpeed(_speed.Value);
        App.Audio.SetVolume(_vol.Value / 100.0);

        if (_resume && App.Audio.CurrentPath != null) { Refresh(); }
        else if (IsZip) StartChapter(_chapter, _book.CurrentPositionMs, announce: false);
        else App.Audio.Play(_book.UriString, _book.CurrentPositionMs, _book.Title);
        Refresh();
        Host.SetStatus($"{_book.Title} oynatılıyor");
        App.Announce($"{_book.Title} oynatılıyor.");
    }

    private void Attach()
    {
        if (_handlersAttached) return;
        _handlersAttached = true;
        App.Audio.StateChanged += Refresh;
        App.Audio.Tick += OnTick;
        App.Audio.Completed += OnCompleted;
        App.Audio.DurationKnown += OnDuration;
        App.Audio.Error += OnError;
        App.Audio.SleepTimerFired += OnSleep;
    }

    public override void OnHidden()
    {
        SaveProgress();
        if (!_handlersAttached) return;
        _handlersAttached = false;
        App.Audio.StateChanged -= Refresh;
        App.Audio.Tick -= OnTick;
        App.Audio.Completed -= OnCompleted;
        App.Audio.DurationKnown -= OnDuration;
        App.Audio.Error -= OnError;
        App.Audio.SleepTimerFired -= OnSleep;
    }

    private void SaveProgress()
    {
        if (IsZip) App.Repo.UpdateZipProgress(_book.Id, _chapter, App.Audio.PositionMs);
        else App.Repo.UpdateProgress(_book.Id, 1, App.Audio.PositionMs);
    }

    private void StartChapter(int index, long pos = 0, bool announce = true)
    {
        index = Math.Clamp(index, 0, _book.Chapters.Count - 1);
        _chapter = index;
        _book.CurrentChapterIndex = index;
        var ch = _book.Chapters[index];
        App.Audio.Play(ch.UriString, pos, $"{_book.Title} - {ch.Title}");
        App.Repo.UpdateZipProgress(_book.Id, index, pos);
        if (announce) App.Announce($"Ayrım {index + 1}: {ch.Title}");
        Refresh();
    }

    private void ChangeChapter(int delta)
    {
        if (!IsZip) return;
        var t = _chapter + delta;
        if (t < 0) { App.Announce("İlk ayrımdasınız"); return; }
        if (t >= _book.Chapters.Count) { App.Announce("Son ayrımdasınız"); return; }
        StartChapter(t);
    }

    private void PlaySelectedChapter()
    {
        if ((_chapters.SelectedItem as ListBoxItem)?.Tag is int i) StartChapter(i);
    }

    private void OnDuration(long ms)
    {
        if (IsZip) App.Repo.UpdateZipDuration(_book.Id, _chapter, ms); else App.Repo.UpdateDuration(_book.Id, ms);
    }

    private void OnTick()
    {
        Refresh();
        if (++_saveCounter % 10 == 0) SaveProgress(); // ~5 sn'de bir
    }

    private void OnCompleted()
    {
        if (IsZip && _chapter + 1 < _book.Chapters.Count) StartChapter(_chapter + 1);
        else
        {
            SaveProgress();
            Host.SetStatus("Sesli kitap bitti.", true);
        }
    }

    private void OnError(string msg)
    {
        Host.SetStatus(msg, true);
        MessageBox.Show(Host, msg, "Oynatma hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnSleep() => Host.SetStatus("Uyku zamanlayıcısı doldu, oynatma durduruldu.", true);

    private void Refresh()
    {
        var a = App.Audio;
        _syncing = true;
        var dur = Math.Max(1, a.DurationMs);
        _pos.Maximum = dur / 1000.0;
        _pos.Value = Math.Min(a.PositionMs / 1000.0, _pos.Maximum);
        _pos.SmallChange = Seek;
        _syncing = false;
        var timeText = $"{Fmt.Time(a.PositionMs)} / {Fmt.Time(a.DurationMs)}";
        _time.Text = timeText;
        Ui.Name(_pos, "Konum: " + Fmt.SpokenTime(a.PositionMs) + ", toplam " + Fmt.SpokenTime(a.DurationMs));
        Ui.Name(_time, timeText);
        _play.Content = a.IsPlaying ? "Duraklat" : "Oynat";
        Ui.Name(_play, a.IsPlaying ? "Duraklat" : "Oynat");
        _back.Content = $"{Seek} sn Geri"; _fwd.Content = $"{Seek} sn İleri";
        Ui.Name(_back, $"{Seek} saniye geri sar"); Ui.Name(_fwd, $"{Seek} saniye ileri sar");
        _nowPlaying.Text = IsZip ? $"Ayrım {_chapter + 1} / {_book.Chapters.Count}: {_book.Chapters[_chapter].Title}" : _book.Title;
        if (IsZip)
        {
            _prevCh.IsEnabled = _chapter > 0; _nextCh.IsEnabled = _chapter < _book.Chapters.Count - 1;
            if (_chapters.SelectedIndex != _chapter) _chapters.SelectedIndex = _chapter;
        }
        _sleepStatus.Text = a.SleepSecondsLeft > 0 ? $"Uyku zamanlayıcısı: {a.SleepSecondsLeft / 60} dakika {a.SleepSecondsLeft % 60} saniye kaldı" : "";
        _sleepStatus.Visibility = a.SleepSecondsLeft > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SleepTimer()
    {
        var options = new[] { "Kapalı", "5 dakika", "10 dakika", "15 dakika", "30 dakika", "45 dakika", "60 dakika" };
        var mins = new[] { 0, 5, 10, 15, 30, 45, 60 };
        var i = ChoiceDialog.Pick(Host, "Uyku Zamanlayıcısı", options, 0, "Belirtilen süre sonunda oynatma otomatik durur.");
        if (i < 0) { InitialFocus?.Focus(); return; }
        App.Audio.StartSleepTimer(mins[i]);
        Host.SetStatus(mins[i] == 0 ? "Uyku zamanlayıcısı kapatıldı." : $"Uyku zamanlayıcısı {mins[i]} dakikaya ayarlandı.", true);
        _play.Focus();
    }

    private void AddBookmark()
    {
        var was = App.Audio.IsPlaying;
        if (was) App.Audio.Pause();
        var pos = App.Audio.PositionMs;
        var where = (IsZip ? $"Ayrım {_chapter + 1}, " : "") + Fmt.SpokenTime(pos);
        var d = new AddBookmarkDialog(Host, where, "");
        if (d.ShowDialog() == true)
        {
            App.Repo.AddBookmark(_book.Id, new Bookmark
            {
                Title = IsZip ? $"{_book.Chapters[_chapter].Title} {Fmt.Time(pos)}" : $"Konum {Fmt.Time(pos)}",
                PageNumber = IsZip ? _chapter + 1 : 1, PositionMs = pos, Note = d.Note
            });
            Host.SetStatus("Yer imi eklendi.", true);
        }
        if (was) App.Audio.TogglePlayPause();
        _play.Focus();
    }

    private void ShowBookmarks()
    {
        var d = new BookmarksDialog(Host, _book);
        if (d.ShowDialog() == true && d.GoTo is { } bm)
        {
            if (IsZip && bm.PageNumber - 1 != _chapter) StartChapter(bm.PageNumber - 1, bm.PositionMs);
            else App.Audio.SeekTo(bm.PositionMs);
            if (!App.Audio.IsPlaying) App.Audio.TogglePlayPause();
        }
        _play.Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var focus = Keyboard.FocusedElement;
        var plain = !(focus is Slider || focus is ListBox || focus is ListBoxItem || Ui.IsTypingTarget(focus));
        if (mods == ModifierKeys.None)
        {
            if (e.Key == Key.Space && !(focus is Button || focus is CheckBox || focus is ListBoxItem)) { App.Audio.TogglePlayPause(); e.Handled = true; }
            else if (e.Key == Key.Left && plain) { App.Audio.SeekBy(-Seek); App.Announce(Fmt.SpokenTime(App.Audio.PositionMs)); e.Handled = true; }
            else if (e.Key == Key.Right && plain) { App.Audio.SeekBy(Seek); App.Announce(Fmt.SpokenTime(App.Audio.PositionMs)); e.Handled = true; }
        }
        else if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Right: ChangeChapter(1); e.Handled = true; break;
                case Key.Left: ChangeChapter(-1); e.Handled = true; break;
                case Key.B: AddBookmark(); e.Handled = true; break;
                case Key.T: SleepTimer(); e.Handled = true; break;
                case Key.Up: _speed.Value = Math.Min(3.0, _speed.Value + 0.25); App.Announce($"Hız {_speed.Value:0.##}"); e.Handled = true; break;
                case Key.Down: _speed.Value = Math.Max(0.5, _speed.Value - 0.25); App.Announce($"Hız {_speed.Value:0.##}"); e.Handled = true; break;
            }
        }
        else if (mods == ModifierKeys.Shift)
        {
            switch (e.Key)
            {
                case Key.Up:
                    _vol.Value = Math.Min(100, _vol.Value + 5);
                    App.Announce($"Ses düzeyi %{Math.Round(_vol.Value)}");
                    e.Handled = true;
                    break;
                case Key.Down:
                    _vol.Value = Math.Max(0, _vol.Value - 5);
                    App.Announce($"Ses düzeyi %{Math.Round(_vol.Value)}");
                    e.Handled = true;
                    break;
            }
        }
        else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.B) { ShowBookmarks(); e.Handled = true; }
    }
}
