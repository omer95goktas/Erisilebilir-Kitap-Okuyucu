using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ErisilebilirKitapOkuyucu.Services;

public class GitHubReleaseInfo
{
    public string TagName { get; set; } = "";
    public string VersionString { get; set; } = "";
    public string Title { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
}

public static class UpdateService
{
    public const string CurrentVersion = "1.0.2";
    public const string GitHubRepo = "omer95goktas/Erisilebilir-Kitap-Okuyucu";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "ErisilebilirKitapOkuyucu-Windows");
    }

    /// <summary>
    /// Uygulamanın MSIX (Microsoft Store) paketi olarak mı yoksa standart .exe olarak mı çalıştığını tespit eder.
    /// </summary>
    public static bool IsRunningAsMsix()
    {
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            return package != null && !string.IsNullOrEmpty(package.Id?.FullName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// GitHub üzerinden güncellemeleri denetler.
    /// </summary>
    public static async Task CheckForUpdatesAsync(bool isManual)
    {
        if (IsRunningAsMsix())
        {
            if (isManual)
            {
                MessageBox.Show(
                    "Uygulamanız Microsoft Store üzerinden kurulmuştur.\nGüncellemeler Microsoft Store tarafından otomatik olarak yönetilmektedir.",
                    "Güncelleme Bilgisi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        try
        {
            var release = await FetchLatestReleaseAsync();
            if (release == null)
            {
                if (isManual)
                {
                    MessageBox.Show(
                        "Güncelleme bilgisi alınamadı. Lütfen internet bağlantınızı kontrol edin veya daha sonra tekrar deneyin.",
                        "Güncelleme Denetimi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (IsNewerVersion(release.VersionString, CurrentVersion))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new UpdateDialog(Application.Current.MainWindow, release);
                    dlg.ShowDialog();
                });
            }
            else if (isManual)
            {
                MessageBox.Show(
                    $"Tebrikler! Erişilebilir Kitap Okuyucu uygulamasının en güncel sürümünü kullanıyorsunuz (v{CurrentVersion}).",
                    "Güncellemeler Tamam", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("UPDATE_CHECK_ERROR: " + ex);
            if (isManual)
            {
                MessageBox.Show(
                    "Güncelleme kontrolü sırasında bir hata oluştu:\n" + ex.Message,
                    "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public static async Task<GitHubReleaseInfo?> FetchLatestReleaseAsync()
    {
        var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
        var res = await Http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

        var info = new GitHubReleaseInfo
        {
            TagName = tagName,
            VersionString = CleanVersion(tagName),
            Title = string.IsNullOrWhiteSpace(name) ? tagName : name,
            ReleaseNotes = body,
            HtmlUrl = htmlUrl
        };

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            // Öncelik: Kurulum.exe
            foreach (var asset in assets.EnumerateArray())
            {
                var aName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                if (aName.EndsWith("_Kurulum.exe", StringComparison.OrdinalIgnoreCase) ||
                    aName.Equals("ErisilebilirKitapOkuyucu_Kurulum.exe", StringComparison.OrdinalIgnoreCase))
                {
                    info.FileName = aName;
                    info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    info.FileSizeBytes = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            }

            // Alternatif: Genel .exe
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var aName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.FileName = aName;
                        info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        info.FileSizeBytes = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                        break;
                    }
                }
            }
        }

        return info;
    }

    public static bool IsNewerVersion(string remoteVer, string localVer)
    {
        if (Version.TryParse(remoteVer, out var rv) && Version.TryParse(localVer, out var lv))
        {
            return rv > lv;
        }

        // Basit kıyaslama yedeği
        return string.Compare(remoteVer, localVer, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string CleanVersion(string tag)
    {
        var clean = tag.Trim().TrimStart('v', 'V');
        var dash = clean.IndexOf('-');
        if (dash > 0) clean = clean.Substring(0, dash);
        return clean;
    }
}

public sealed class UpdateDialog : DialogBase
{
    private readonly GitHubReleaseInfo _release;
    private readonly ProgressBar _progressBar = new() { Height = 18, Margin = new Thickness(0, 10, 0, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock _statusText = new() { Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Button _btnUpdate;
    private readonly Button _btnLater;
    private readonly Button _btnWeb;
    private CancellationTokenSource? _cts;

    public UpdateDialog(Window? owner, GitHubReleaseInfo release) : base(owner, "Yeni Güncelleme Mevcut", 600)
    {
        _release = release;

        var header = new TextBlock
        {
            Text = $"Erişilebilir Kitap Okuyucu v{release.VersionString} Yayınlandı!",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 8),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["AccentBg"]
        };
        Body.Children.Add(header);

        var sizeMb = release.FileSizeBytes > 0 ? $" (Boyut: {release.FileSizeBytes / (1024.0 * 1024.0):0.1} MB)" : "";
        var subText = new TextBlock
        {
            Text = $"Kullandığınız sürüm: v{UpdateService.CurrentVersion}\nYeni sürüm: v{release.VersionString}{sizeMb}",
            Margin = new Thickness(0, 0, 0, 8)
        };
        Body.Children.Add(subText);

        if (!string.IsNullOrWhiteSpace(release.ReleaseNotes))
        {
            Body.Children.Add(new TextBlock { Text = "Sürüm Yenilikleri:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            var notesBox = new TextBox
            {
                Text = release.ReleaseNotes,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 160,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            AutomationProperties.SetName(notesBox, "Sürüm yenilikleri notları");
            Body.Children.Add(notesBox);
        }

        _statusText.Text = "Yeni sürümü hemen indirip yüklemek için 'Şimdi Güncelle ve Kur' butonuna basın.";
        AutomationProperties.SetName(_statusText, "Güncelleme durumu");
        Body.Children.Add(_statusText);

        AutomationProperties.SetName(_progressBar, "İndirme ilerleme çubuğu");
        Body.Children.Add(_progressBar);

        _btnUpdate = Btn("Şimdi Güncelle ve Kur", async (_, _) => await StartDownloadAndInstall(), isDefault: true, help: "Yeni sürümü otomatik indirir, kurar ve uygulamayı yeniden başlatır");
        _btnLater = Btn("Daha Sonra", (_, _) => { CancelDownload(); DialogResult = false; }, help: "Güncellemeyi kapatır");
        _btnWeb = Btn("Tarayıcıda İncele", (_, _) =>
        {
            if (!string.IsNullOrEmpty(release.HtmlUrl))
            {
                try { Process.Start(new ProcessStartInfo { FileName = release.HtmlUrl, UseShellExecute = true }); } catch { }
            }
        }, help: "GitHub sürüm sayfasını web tarayıcınızda açar");

        Body.Children.Add(ButtonRow(_btnWeb, _btnLater, _btnUpdate));
        FocusFirst(_btnUpdate);
    }

    private void CancelDownload()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch { }
    }

    private async Task StartDownloadAndInstall()
    {
        if (string.IsNullOrEmpty(_release.DownloadUrl))
        {
            MessageBox.Show("İndirme bağlantısı bulunamadı. Lütfen tarayıcıdan indirmeyi deneyin.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _btnUpdate.IsEnabled = false;
        _btnLater.Content = "İptal Et";
        _progressBar.Visibility = Visibility.Visible;
        _progressBar.IsIndeterminate = false;
        _progressBar.Value = 0;
        _statusText.Text = "İndirme başlatılıyor...";
        App.Announce("Güncelleme indirmesi başlatılıyor...");

        _cts = new CancellationTokenSource();
        var tempFile = Path.Combine(Path.GetTempPath(), "ErisilebilirKitapOkuyucu_Kurulum.exe");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "ErisilebilirKitapOkuyucu-Windows");

            using var response = await client.GetAsync(_release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? _release.FileSizeBytes;
            using var sourceStream = await response.Content.ReadAsStreamAsync(_cts.Token);
            using var targetStream = File.Create(tempFile);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            int lastPercent = -1;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
            {
                await targetStream.WriteAsync(buffer, 0, bytesRead, _cts.Token);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)((totalRead * 100) / totalBytes);
                    _progressBar.Value = percent;
                    var readMb = totalRead / (1024.0 * 1024.0);
                    var totalMb = totalBytes / (1024.0 * 1024.0);
                    _statusText.Text = $"İndiriliyor: %{percent} ({readMb:0.1} MB / {totalMb:0.1} MB)";

                    if (percent != lastPercent && percent % 25 == 0)
                    {
                        lastPercent = percent;
                        App.Announce($"Güncelleme %{percent} indirildi.");
                    }
                }
            }

            targetStream.Close();

            _statusText.Text = "İndirme tamamlandı! Kurulum başlatılıyor, lütfen bekleyin...";
            App.Announce("İndirme tamamlandı. Kurulum başlatılıyor, uygulama kapanacak.");
            await Task.Delay(1000);

            // Inno Setup kurucusunu çalıştır (/SILENT parametresi sessizce eski dosyaları günceller ve uygulamayı yeniden başlatır)
            var psi = new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/SILENT",
                UseShellExecute = true
            };
            Process.Start(psi);

            // Uygulamayı kapat ki kurulum dosyaları kilitsiz üzerine yazabilsin
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "İndirme iptal edildi.";
            _progressBar.Visibility = Visibility.Collapsed;
            _btnUpdate.IsEnabled = true;
            _btnLater.Content = "Kapat";
            App.Announce("Güncelleme indirmesi iptal edildi.");
        }
        catch (Exception ex)
        {
            AppPaths.Log("DOWNLOAD_INSTALL_ERROR: " + ex);
            _statusText.Text = "Hata oluştu: " + ex.Message;
            _progressBar.Visibility = Visibility.Collapsed;
            _btnUpdate.IsEnabled = true;
            _btnLater.Content = "Kapat";
            MessageBox.Show("Güncelleme indirilirken bir hata oluştu:\n" + ex.Message, "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
