using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ErisilebilirKitapOkuyucu.Pages;

namespace ErisilebilirKitapOkuyucu;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    public static MainWindow Instance { get; private set; } = null!;
    private PageBase? _current;
    private BookItem? _nowPlayingAudio;
    public BookItem? NowPlayingAudio
    {
        get => _nowPlayingAudio;
        set
        {
            _nowPlayingAudio = value;
            if (NowPlayingPanel != null)
            {
                NowPlayingPanel.Visibility = (value != null && App.Audio.CurrentPath != null) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        try
        {
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_logo.png");
            if (File.Exists(logoPath))
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.Absolute));
                Icon = bi;
                LogoImage.Source = bi;
            }
        }
        catch { }
        BtnNowPlaying.Click += (_, _) => ReturnToAudio();
        PreviewKeyDown += OnKey;
        Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await ImportFilesAsync(files);
        };
        ShowLibrary();
        Loaded += async (_, _) =>
        {
            EnsureForeground();
            if (App.StartupFile != null) await ImportFilesAsync(new[] { App.StartupFile }, openIfSingle: true);
        };
        ContentRendered += (_, _) =>
        {
            EnsureForeground();
        };
        Closing += (_, _) => { _current?.OnHidden(); };
    }

    public void EnsureForeground()
    {
        try
        {
            Topmost = true;
            Topmost = false;
            Activate();
            Focus();
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
                SetFocus(handle);
            }
            var target = _current?.InitialFocus;
            if (target != null)
            {
                FocusManager.SetFocusedElement(this, target);
                target.Focus();
                Keyboard.Focus(target);
            }
        }
        catch { }
    }

    public void SetStatus(string text, bool announce = false)
    {
        StatusText.Text = text;
        if (announce) App.Announce(text);
    }

    private void Navigate(PageBase page)
    {
        _current?.OnHidden();
        _current = page;
        PageHost.Content = page;
        Title = page.PageTitle + " - Erişilebilir Kitap Okuyucu";
        page.OnShown();

        Dispatcher.BeginInvoke(() =>
        {
            var target = page.InitialFocus;
            if (target != null)
            {
                FocusManager.SetFocusedElement(this, target);
                target.Focus();
                Keyboard.Focus(target);
            }
        }, DispatcherPriority.Input);
    }

    public void ShowLibrary() => Navigate(new LibraryPage());
    public void ShowSettings() => Navigate(new SettingsPage());

    public void OpenBook(BookItem book)
    {
        App.Repo.UpdateProgress(book.Id, book.CurrentPage, book.CurrentPositionMs);
        if (book.Type == BookType.AUDIO) { NowPlayingAudio = book; Navigate(new AudioPage(book)); }
        else
        {
            if (NowPlayingAudio != null) { App.Audio.Stop(); NowPlayingAudio = null; }
            Navigate(new ReaderPage(book));
        }
    }

    public void ReturnToAudio()
    {
        if (NowPlayingAudio != null) Navigate(new AudioPage(NowPlayingAudio, resume: true));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { e.Handled = true; new HelpDialog(this).ShowDialog(); return; }
        if (e.Key == Key.Escape && !e.Handled && _current != null)
        {
            if (_current.HandleBack()) e.Handled = true;
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths, bool openIfSingle = false)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return;
        var folder = (_current as LibraryPage)?.CurrentFolder ?? BookItem.RootFolder;
        SetStatus(list.Count == 1 ? "Kitap ekleniyor, lütfen bekleyin…" : $"{list.Count} dosya ekleniyor, lütfen bekleyin…", true);
        Mouse.OverrideCursor = Cursors.Wait;
        var added = new List<BookItem>();
        string? lastMessage = null;
        try
        {
            foreach (var f in list)
            {
                var r = await App.Importer.AddFileAsync(f, folder);
                if (r.Book != null) added.Add(r.Book);
                if (r.Message != null) lastMessage = r.Message;
            }
        }
        finally { Mouse.OverrideCursor = null; }

        if (added.Count > 0)
        {
            SetStatus(added.Count == 1 ? $"\"{added[0].Title}\" kitaplığa eklendi." : $"{added.Count} kitap kitaplığa eklendi.", true);
            if (lastMessage != null && added.Count == 1) SetStatus(lastMessage, true);
            if (_current is LibraryPage lp) lp.Refresh(added[0].Id);
            if (openIfSingle && added.Count == 1) OpenBook(added[0]);
        }
        else if (lastMessage != null)
        {
            SetStatus(lastMessage, true);
            MessageBox.Show(this, lastMessage, "Kitap eklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
