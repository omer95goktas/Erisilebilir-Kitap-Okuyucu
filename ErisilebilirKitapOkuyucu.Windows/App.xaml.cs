using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu;

public partial class App : Application
{
    public static BookRepository Repo { get; private set; } = null!;
    public static TtsService Tts { get; private set; } = null!;
    public static AudioPlayerService Audio { get; private set; } = null!;
    public static ImportService Importer { get; private set; } = null!;
    public static string? StartupFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Bazı ekran kartı sürücülerinde (örn. AMD) çıkışta çökme olabildiği için yazılım işleme kullanılır; metin tabanlı arayüz için fark edilmez.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // windows-1254 (Türkçe) için gerekli

        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log("UNHANDLED: " + args.Exception);
            Announce("Beklenmeyen bir hata oluştu: " + args.Exception.Message);
            MessageBox.Show("Beklenmeyen bir hata oluştu:\n" + args.Exception.Message + "\n\nAyrıntılar: " + AppPaths.LogFile,
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { AppPaths.Log("UNOBSERVED: " + args.Exception); args.SetObserved(); };

        Repo = new BookRepository();
        ThemeManager.Apply(Repo.Settings);

        var win = new MainWindow();
        win.Show();
        win.Activate();

        Tts = new TtsService(Repo);
        Audio = new AudioPlayerService();
        Importer = new ImportService(Repo);
        Repo.SettingsChanged += () => ThemeManager.Apply(Repo.Settings);

        StartupFile = e.Args.FirstOrDefault(a => File.Exists(a));

        var st = Array.IndexOf(e.Args, "--selftest");
        if (st >= 0 && st + 1 < e.Args.Length)
        {
            var report = e.Args[st + 1];
            var pdfDir = st + 2 < e.Args.Length ? e.Args[st + 2] : null;
            _ = Dispatcher.InvokeAsync(async () => { var code = await SelfTest.RunAsync(report, pdfDir); Shutdown(code); });
        }

        if (Array.IndexOf(e.Args, "--snapshots") >= 0)
        {
            _ = Dispatcher.InvokeAsync(() => Snapshots.Run(this));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tts?.Dispose();
        Audio?.Dispose();
        base.OnExit(e);
        // Taranmış PDF OCR'ı Direct2D'yi yüklediyse, bazı ekran kartı sürücüleri (örn. AMD) normal çıkışta çöküp
        // "uygulama çalışmayı durdurdu" iletisi gösterebilir. Tüm veriler zaten kaydedildiği için süreci doğrudan sonlandırıyoruz.
        if (Services.DocumentSession.OcrUsed) TerminateProcess(GetCurrentProcess(), (uint)e.ApplicationExitCode);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);

    /// <summary>Ekran okuyucuya (NVDA, JAWS, Narrator) UI Automation bildirimi gönderir.</summary>
    public static void Announce(string message, bool important = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (Current?.MainWindow is not Window w) return;
                var peer = UIElementAutomationPeer.FromElement(w) ?? UIElementAutomationPeer.CreatePeerForElement(w);
                peer?.RaiseNotificationEvent(
                    AutomationNotificationKind.Other,
                    important ? AutomationNotificationProcessing.ImportantAll : AutomationNotificationProcessing.ImportantMostRecent,
                    message, "KitapOkuyucuDuyuru");
            }
            catch (Exception ex) { AppPaths.Log("announce: " + ex.Message); }
        }, DispatcherPriority.Background);
    }
}

public static class ThemeManager
{
    private static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static SolidColorBrush B(Color c) => new(c);

    public static void Apply(AppSettings s)
    {
        var r = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            // Windows yüksek karşıtlık teması etkinse sistem renklerine tam uyum
            r["AppBg"] = B(SystemColors.WindowColor);
            r["AppFg"] = B(SystemColors.WindowTextColor);
            r["SubTextFg"] = B(SystemColors.WindowTextColor);
            r["PanelBg"] = B(SystemColors.WindowColor);
            r["PanelHeaderBg"] = B(SystemColors.WindowColor);
            r["AccentBg"] = B(SystemColors.HighlightColor);
            r["AccentHover"] = B(SystemColors.HighlightColor);
            r["AccentFg"] = B(SystemColors.HighlightTextColor);
            r["LineBrush"] = B(SystemColors.WindowTextColor);
            r["ButtonBg"] = B(SystemColors.WindowColor);
            r["ButtonHoverBg"] = B(SystemColors.HighlightColor);
            r["ButtonBorder"] = B(SystemColors.WindowTextColor);
            r["FocusBrush"] = B(SystemColors.HotTrackColor);
            r["DisabledFg"] = B(SystemColors.GrayTextColor);
            r["BadgeBg"] = B(SystemColors.WindowColor);
            r["BadgeFg"] = B(SystemColors.WindowTextColor);
        }
        else if (s.HighContrastMode)
        {
            // Manuel Yüksek Karşıtlık (Siyah Zemin, Sarı Vurgu)
            r["AppBg"] = B("#000000");
            r["AppFg"] = B("#FFFFFF");
            r["SubTextFg"] = B("#DDDDDD");
            r["PanelBg"] = B("#121212");
            r["PanelHeaderBg"] = B("#181818");
            r["AccentBg"] = B("#FFD600");
            r["AccentHover"] = B("#FFE033");
            r["AccentFg"] = B("#000000");
            r["LineBrush"] = B("#888888");
            r["ButtonBg"] = B("#1E1E1E");
            r["ButtonHoverBg"] = B("#FFD600");
            r["ButtonBorder"] = B("#BDBDBD");
            r["FocusBrush"] = B("#00E5FF");
            r["DisabledFg"] = B("#757575");
            r["BadgeBg"] = B("#2A2A2A");
            r["BadgeFg"] = B("#FFD600");
        }
        else
        {
            // Modern Aydınlık / Fluent Tasarım (Gözü yormayan, zarif ve modern)
            r["AppBg"] = B("#F8FAFC");
            r["AppFg"] = B("#0F172A");
            r["SubTextFg"] = B("#475569");
            r["PanelBg"] = B("#FFFFFF");
            r["PanelHeaderBg"] = B("#FFFFFF");
            r["AccentBg"] = B("#2563EB");
            r["AccentHover"] = B("#1D4ED8");
            r["AccentFg"] = B("#FFFFFF");
            r["LineBrush"] = B("#E2E8F0");
            r["ButtonBg"] = B("#FFFFFF");
            r["ButtonHoverBg"] = B("#F1F5F9");
            r["ButtonBorder"] = B("#CBD5E1");
            r["FocusBrush"] = B("#2563EB");
            r["DisabledFg"] = B("#94A3B8");
            r["BadgeBg"] = B("#EFF6FF");
            r["BadgeFg"] = B("#1D4ED8");
        }
        r["BaseFontSize"] = (double)Math.Clamp(s.FontSizeSp, 14, 40);
    }
}
