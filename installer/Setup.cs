using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

[assembly: AssemblyTitle("Erisilebilir Kitap Okuyucu Kurulum Sihirbazi")]
[assembly: AssemblyDescription("Erisilebilir Kitap Okuyucu Otomatik Kurulum")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("omergoktas.net")]
[assembly: AssemblyProduct("Erisilebilir Kitap Okuyucu")]
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
            Application.Run(new SetupForm());
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

        public SetupForm()
        {
            this.Text = "Erisilebilir Kitap Okuyucu Kurulumu - omergoktas.net";
            this.Size = new Size(540, 320);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "ErisilebilirKitapOkuyucu");

            lblTitle = new Label {
                Text = "Erisilebilir Kitap Okuyucu Kurulum Sihirbazi",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(24, 20),
                Size = new Size(480, 35)
            };

            lblPublisher = new Label {
                Text = "Yayimci: omergoktas.net\r\nKurulum Konumu: " + installDir,
                Font = new Font("Segoe UI", 10),
                Location = new Point(24, 65),
                Size = new Size(480, 45)
            };

            lblStatus = new Label {
                Text = "Kuruluma baslamak icin 'Kurulumu Baslat' butonuna tiklayin veya Enter'a basin.",
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
                Text = "Kurulumu Baslat",
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
            if (btnAction.Text == "Uygulamayi Baslat ve Kapat")
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
            lblStatus.Text = "Dosyalar yukleniyor ve kuruluyor, lutfen bekleyin...";

            try
            {
                await Task.Run(() => PerformInstall());

                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                lblStatus.Text = "Tebrikler! Kurulum basariyla tamamlandi. Masaustunuze kisayol eklendi.";
                btnAction.Text = "Uygulamayi Baslat ve Kapat";
                btnAction.Enabled = true;
                btnAction.Focus();
            }
            catch (Exception ex)
            {
                progressBar.Visible = false;
                lblStatus.Text = "Hata olustu: " + ex.Message;
                btnAction.Enabled = true;
                MessageBox.Show("Kurulum sirasinda bir hata olustu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PerformInstall()
        {
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("payload.zip"))
            {
                if (stream == null) throw new InvalidOperationException("Gomulu kurulum paketi bulunamadi.");
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

            // Desktop shortcut
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktopDir, "Erisilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            // OneDrive Desktop shortcut
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string oneDriveDesktop = Path.Combine(userProfile, "OneDrive", "Desktop");
            if (Directory.Exists(oneDriveDesktop))
            {
                CreateShortcut(Path.Combine(oneDriveDesktop, "Erisilebilir Kitap Okuyucu.lnk"), exePath, installDir);
            }

            // Start Menu shortcut
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Erisilebilir Kitap Okuyucu");
            if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
            CreateShortcut(Path.Combine(startMenuDir, "Erisilebilir Kitap Okuyucu.lnk"), exePath, installDir);

            // Add to Add/Remove Programs
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ErisilebilirKitapOkuyucu"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Erisilebilir Kitap Okuyucu");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "omergoktas.net");
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("UninstallString", "cmd.exe /c rmdir /s /q \"" + installDir + "\"");
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
                shortcut.Description = "Erisilebilir Kitap Okuyucu - omergoktas.net";
                shortcut.Save();
            }
            catch { }
        }
    }
}
