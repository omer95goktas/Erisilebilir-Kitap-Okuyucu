using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

[assembly: AssemblyTitle("Erişilebilir Kitap Okuyucu Kurulum ve Kaldırma")]
[assembly: AssemblyDescription("Erişilebilir Kitap Okuyucu Otomatik Kurulum ve Kaldırma Sihirbazı")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("omergoktas.net")]
[assembly: AssemblyProduct("Erişilebilir Kitap Okuyucu")]
[assembly: AssemblyCopyright("Copyright 2026 omergoktas.net")]
[assembly: AssemblyTrademark("omergoktas.net")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace ErisilebilirKitapOkuyucu.Setup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isUninstall = false;
            bool isQuiet = false;
            foreach (var arg in args)
            {
                if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || arg.Equals("/u", StringComparison.OrdinalIgnoreCase))
                    isUninstall = true;
                if (arg.Equals("/quiet", StringComparison.OrdinalIgnoreCase) || arg.Equals("/q", StringComparison.OrdinalIgnoreCase))
                    isQuiet = true;
            }

            if (AppDomain.CurrentDomain.FriendlyName.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isUninstall = true;
            }

            if (isUninstall)
            {
                PerformUninstall(isQuiet);
                return;
            }

            Application.Run(new SetupForm());
        }

        private static void PerformUninstall(bool isQuiet)
        {
            if (!isQuiet)
            {
                var confirm = MessageBox.Show(
                    "Erişilebilir Kitap Okuyucu uygulamasını bilgisayarınızdan kaldırmak istediğinize emin misiniz?",
                    "Programı Kaldır — omergoktas.net",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
            }

            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "ErisilebilirKitapOkuyucu");

            // 1. Çalışıyorsa programı kapat
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("ErisilebilirKitapOkuyucu"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            // 2. Kısayolları sil (Tüm masaüstü konumları ve eski isim varyasyonları)
            CleanAllShortcuts();

            // 3. Denetim Masası Kayıt Defteri Girişini Sil
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ErisilebilirKitapOkuyucu", false);
            }
            catch { }

            // 4. Kurulum klasörünü kendini kapattıktan sonra sil
            try
            {
                string cmd = string.Format("/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{0}\"", installDir);
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd)
                {
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }

            if (!isQuiet)
            {
                MessageBox.Show(
                    "Erişilebilir Kitap Okuyucu başarıyla bilgisayarınızdan kaldırıldı.",
                    "Kaldırma Tamamlandı",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        public static void CleanAllShortcuts()
        {
            string[] names = new string[] {
                "Erişilebilir Kitap Okuyucu.lnk",
                "Erisilebilir Kitap Okuyucu.lnk"
            };

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] dirs = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(userProfile, "Desktop"),
                Path.Combine(userProfile, "OneDrive", "Desktop")
            };

            foreach (var d in dirs)
            {
                if (Directory.Exists(d))
                {
                    foreach (var n in names)
                    {
                        string target = Path.Combine(d, n);
                        if (File.Exists(target))
                        {
                            try { File.Delete(target); } catch { }
                        }
                    }
                }
            }

            // Başlat menüsü
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erişilebilir Kitap Okuyucu");
            if (Directory.Exists(startMenuDir))
            {
                try { Directory.Delete(startMenuDir, true); } catch { }
            }
            string startMenuOldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erisilebilir Kitap Okuyucu");
            if (Directory.Exists(startMenuOldDir))
            {
                try { Directory.Delete(startMenuOldDir, true); } catch { }
            }
        }
    }

    public class SetupForm : Form
    {
        private Label lblTitle;
        private Label lblPublisher;
        private Label lblStatus;
        private ProgressBar progressBar;
        private Button btnAction;
        private string installDir;
        private bool isUpdate = false;

        public SetupForm()
        {
            installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "ErisilebilirKitapOkuyucu");

            isUpdate = Directory.Exists(installDir) && File.Exists(Path.Combine(installDir, "ErisilebilirKitapOkuyucu.exe"));

            this.Text = (isUpdate ? "Erişilebilir Kitap Okuyucu Güncelleme" : "Erişilebilir Kitap Okuyucu Kurulumu") + " — omergoktas.net";
            this.Size = new Size(540, 320);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            lblTitle = new Label {
                Text = isUpdate ? "Erişilebilir Kitap Okuyucu Güncelleme Sihirbazı" : "Erişilebilir Kitap Okuyucu Kurulum Sihirbazı",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(24, 20),
                Size = new Size(480, 35)
            };

            lblPublisher = new Label {
                Text = "Yayımcı: omergoktas.net\r\nKurulum Konumu: " + installDir,
                Font = new Font("Segoe UI", 10),
                Location = new Point(24, 65),
                Size = new Size(480, 45)
            };

            lblStatus = new Label {
                Text = isUpdate 
                    ? "Mevcut sürüm tespit edildi. Güncellemek için butona tıklayın veya Enter'a basın." 
                    : "Kuruluma başlamak için 'Kurulumu Başlat' butonuna tıklayın veya Enter'a basın.",
                Font = new Font("Segoe UI", 10),
                Location = new Point(24, 125),
                Size = new Size(480, 40)
            };

            progressBar = new ProgressBar {
                Location = new Point(24, 175),
                Size = new Size(475, 25),
                Style = ProgressBarStyle.Continuous,
                Value = 0,
                Visible = false
            };

            btnAction = new Button {
                Text = isUpdate ? "Güncellemeyi Yükle" : "Kurulumu Başlat",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(24, 220),
                Size = new Size(475, 42)
            };
            btnAction.Click += BtnAction_Click;

            this.Controls.Add(lblTitle);
            this.Controls.Add(lblPublisher);
            this.Controls.Add(lblStatus);
            this.Controls.Add(progressBar);
            this.Controls.Add(btnAction);

            this.AcceptButton = btnAction;
        }

        private async void BtnAction_Click(object sender, EventArgs e)
        {
            if (btnAction.Text == "Uygulamayı Başlat ve Kapat")
            {
                string exePath = Path.Combine(installDir, "ErisilebilirKitapOkuyucu.exe");
                if (File.Exists(exePath))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = installDir,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch { }
                }
                this.Close();
                return;
            }

            btnAction.Enabled = false;
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
            
            string startMsg = isUpdate ? "Güncelleme yükleniyor, lütfen bekleyin..." : "Kurulum başlatıldı, dosyalar yükleniyor, lütfen bekleyin...";
            lblStatus.Text = startMsg;
            this.Text = "Kuruluyor... " + (isUpdate ? "Güncelleme" : "Kurulum") + " — omergoktas.net";

            // Ekran okuyucular ve kullanıcı için anında sistem sesi
            System.Media.SystemSounds.Asterisk.Play();

            try
            {
                var progressHandler = new Progress<int>(percent =>
                {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    if (percent % 25 == 0 && percent > 0 && percent < 100)
                    {
                        lblStatus.Text = (isUpdate ? "Güncelleniyor: %" : "Kuruluyor: %") + percent;
                        this.Text = "%" + percent + " Tamamlandı — omergoktas.net";
                    }
                });

                await Task.Run(() => PerformInstall(progressHandler));

                progressBar.Value = 100;
                string doneMsg = isUpdate 
                    ? "Tebrikler! Güncelleme başarıyla tamamlandı. Kitaplarınız ve ayarlarınız korundu." 
                    : "Tebrikler! Kurulum başarıyla tamamlandı. Masaüstünüze ve Başlat menünüze kısayol eklendi.";
                
                lblStatus.Text = doneMsg;
                this.Text = "Kurulum Tamamlandı! — omergoktas.net";
                btnAction.Text = "Uygulamayı Başlat ve Kapat";
                btnAction.Enabled = true;
                btnAction.Focus();

                // Kurulum bitiş bildirim sesi
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                progressBar.Visible = false;
                lblStatus.Text = "Hata oluştu: " + ex.Message;
                this.Text = "Kurulum Hatası — omergoktas.net";
                btnAction.Enabled = true;
                btnAction.Text = isUpdate ? "Tekrar Dene" : "Kurulumu Tekrar Dene";
                System.Media.SystemSounds.Hand.Play();
                MessageBox.Show(
                    "Kurulum sırasında bir hata oluştu:\n\n" + ex.Message + 
                    "\n\nLütfen açık olan 'Erişilebilir Kitap Okuyucu' pencerelerini kapatıp tekrar deneyin.", 
                    "Kurulum Hatası", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error);
            }
        }

        private void PerformInstall(IProgress<int> progress)
        {
            // 1. Eğer uygulama açıksa zorla ve tam olarak kapanmasını bekle
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("ErisilebilirKitapOkuyucu"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(4000);
                    }
                    catch { }
                }
            }
            catch { }

            // Dosya kilitlerinin Windows tarafından tamamen serbest bırakılması için kısa tampon
            System.Threading.Thread.Sleep(500);

            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("payload.zip"))
            {
                if (stream == null) throw new InvalidOperationException("Gömülü kurulum paketi bulunamadı.");
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int current = 0;

                    foreach (var entry in archive.Entries)
                    {
                        current++;
                        if (current % 15 == 0 && progress != null)
                        {
                            progress.Report((int)((current * 90.0) / total));
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(installDir, entry.FullName));
                            continue;
                        }

                        string destPath = Path.Combine(installDir, entry.FullName);
                        string parentDir = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);

                        // Güvenli dosya yazma (kilit varsa 3 defa kısa aralıkla dene)
                        bool written = false;
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            try
                            {
                                entry.ExtractToFile(destPath, true);
                                written = true;
                                break;
                            }
                            catch (IOException)
                            {
                                System.Threading.Thread.Sleep(300);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                System.Threading.Thread.Sleep(300);
                            }
                        }

                        if (!written)
                        {
                            // Son bir defa dene, hata verirse fırlat
                            entry.ExtractToFile(destPath, true);
                        }
                    }
                }
            }

            if (progress != null) progress.Report(92);

            string exePath = Path.Combine(installDir, "ErisilebilirKitapOkuyucu.exe");

            // Kendini Uninstall.exe olarak da yükleme klasörüne kopyala
            string uninstallerPath = Path.Combine(installDir, "Uninstall.exe");
            try
            {
                File.Copy(Assembly.GetExecutingAssembly().Location, uninstallerPath, true);
            }
            catch { }

            if (progress != null) progress.Report(95);

            // 1. Önce TÜM eski kısayol varyasyonlarını (ASCII/Türkçe, Desktop/OneDrive) temizle
            Program.CleanAllShortcuts();

            // 2. Masaüstü kısayolu oluştur (Öncelikle aktif DesktopDirectory'ye, varsa ek masaüstü konumlarına da garanti olsun diye yaz)
            string activeDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(activeDesktop, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string normalDesktop = Path.Combine(userProfile, "Desktop");
            if (Directory.Exists(normalDesktop) && !normalDesktop.Equals(activeDesktop, StringComparison.OrdinalIgnoreCase))
            {
                CreateShortcut(Path.Combine(normalDesktop, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);
            }

            // 3. Başlat Menüsü kısayolu oluştur
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erişilebilir Kitap Okuyucu");
            if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
            CreateShortcut(Path.Combine(startMenuDir, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            // 4. Program Ekle/Kaldır (Denetim Masası) Kaydı
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ErisilebilirKitapOkuyucu"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Erişilebilir Kitap Okuyucu");
                        key.SetValue("DisplayVersion", "1.0.1");
                        key.SetValue("Publisher", "omergoktas.net");
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall");
                        key.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /uninstall /quiet");
                    }
                }
            }
            catch { }

            if (progress != null) progress.Report(100);
        }

        private void CreateShortcut(string shortcutPath, string targetPath, string workDir)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workDir;
                shortcut.Description = "Erişilebilir Kitap Okuyucu — omergoktas.net";
                shortcut.Save();
            }
            catch { }
        }
    }
}
