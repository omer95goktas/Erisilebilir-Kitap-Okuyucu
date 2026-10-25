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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

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

            // 2. Kısayolları sil
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk1 = Path.Combine(desktop, "Erişilebilir Kitap Okuyucu.lnk");
                if (File.Exists(lnk1)) File.Delete(lnk1);

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string oneDriveLnk = Path.Combine(userProfile, "OneDrive", "Desktop", "Erişilebilir Kitap Okuyucu.lnk");
                if (File.Exists(oneDriveLnk)) File.Delete(oneDriveLnk);

                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erişilebilir Kitap Okuyucu");
                if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
            }
            catch { }

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
                    System.Diagnostics.Process.Start(exePath);
                }
                this.Close();
                return;
            }

            btnAction.Enabled = false;
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = isUpdate ? "Güncelleme yükleniyor, lütfen bekleyin..." : "Dosyalar yükleniyor ve kuruluyor, lütfen bekleyin...";

            try
            {
                await Task.Run(() => PerformInstall());

                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                lblStatus.Text = isUpdate 
                    ? "Tebrikler! Güncelleme başarıyla tamamlandı. Kitaplarınız ve ayarlarınız korundu." 
                    : "Tebrikler! Kurulum başarıyla tamamlandı. Masaüstünüze kısayol eklendi.";
                btnAction.Text = "Uygulamayı Başlat ve Kapat";
                btnAction.Enabled = true;
                btnAction.Focus();
            }
            catch (Exception ex)
            {
                progressBar.Visible = false;
                lblStatus.Text = "Hata oluştu: " + ex.Message;
                btnAction.Enabled = true;
                MessageBox.Show("İşlem sırasında bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PerformInstall()
        {
            // Eğer uygulama açıksa önce kapat
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("ErisilebilirKitapOkuyucu"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

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
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(installDir, entry.FullName));
                            continue;
                        }
                        string destPath = Path.Combine(installDir, entry.FullName);
                        string parentDir = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }

            string exePath = Path.Combine(installDir, "ErisilebilirKitapOkuyucu.exe");

            // Kendini Uninstall.exe olarak da yükleme klasörüne kopyala
            string uninstallerPath = Path.Combine(installDir, "Uninstall.exe");
            try
            {
                File.Copy(Assembly.GetExecutingAssembly().Location, uninstallerPath, true);
            }
            catch { }

            // Masaüstü kısayolu
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktopDir, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            // OneDrive Masaüstü kısayolu
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string oneDriveDesktop = Path.Combine(userProfile, "OneDrive", "Desktop");
            if (Directory.Exists(oneDriveDesktop))
            {
                CreateShortcut(Path.Combine(oneDriveDesktop, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);
            }

            // Başlat Menüsü kısayolu
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erişilebilir Kitap Okuyucu");
            if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
            CreateShortcut(Path.Combine(startMenuDir, "Erişilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            // Program Ekle/Kaldır (Denetim Masası) Kaydı
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ErisilebilirKitapOkuyucu"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Erişilebilir Kitap Okuyucu");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "omergoktas.net");
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall");
                        key.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /uninstall /quiet");
                    }
                }
            }
            catch { }
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
