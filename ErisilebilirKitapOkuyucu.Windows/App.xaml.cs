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
            r["PanelBg"] = B(SystemColors.WindowColor);
            r["AccentBg"] = B(SystemColors.HighlightColor);
            r["AccentFg"] = B(SystemColors.HighlightTextColor);
            r["LineBrush"] = B(SystemColors.WindowTextColor);
            r["FocusBrush"] = B(SystemColors.HotTrackColor);
            r["DisabledFg"] = B(SystemColors.GrayTextColor);
        }
        else if (s.HighContrastMode)
        {
            r["AppBg"] = B("#000000"); r["AppFg"] = B("#FFFFFF"); r["PanelBg"] = B("#141414");
            r["AccentBg"] = B("#FFD600"); r["AccentFg"] = B("#000000"); r["LineBrush"] = B("#BDBDBD");
            r["FocusBrush"] = B("#00E5FF"); r["DisabledFg"] = B("#9E9E9E");
        }
        else
        {
            r["AppBg"] = B("#FFFFFF"); r["AppFg"] = B("#111111"); r["PanelBg"] = B("#F3F3F3");
            r["AccentBg"] = B("#0B57D0"); r["AccentFg"] = B("#FFFFFF"); r["LineBrush"] = B("#5F6368");
            r["FocusBrush"] = B("#D93025"); r["DisabledFg"] = B("#6B6B6B");
        }
        r["BaseFontSize"] = (double)Math.Clamp(s.FontSizeSp, 14, 40);
    }
}
