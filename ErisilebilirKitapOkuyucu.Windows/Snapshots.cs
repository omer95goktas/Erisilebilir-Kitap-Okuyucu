using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErisilebilirKitapOkuyucu;
using ErisilebilirKitapOkuyucu.Pages;
using ErisilebilirKitapOkuyucu.Services;

public static class Snapshots
{
    public static void Run(App app)
    {
        try
        {
            var repo = App.Repo;
            var nutukPath = Path.Combine(AppPaths.ImportedDir, "nutuk.txt");
            var audioPath = Path.Combine(AppPaths.ImportedDir, "ornek_sesli_kitap.mp3");

            var b1 = new BookItem { Title = "Nutuk - Mustafa Kemal Atatürk", Type = BookType.TXT, UriString = nutukPath, TotalPages = 24, CurrentPage = 1, FolderName = "Tarih" };
            var b2 = new BookItem { Title = "Kürk Mantolu Madonna - Sabahattin Ali", Type = BookType.PDF, UriString = nutukPath, TotalPages = 160, CurrentPage = 18, FolderName = "Edebiyat" };
            var b3 = new BookItem { Title = "Saatleri Ayarlama Enstitüsü - Ahmet Hamdi Tanpınar", Type = BookType.DOCX, UriString = nutukPath, TotalPages = 280, CurrentPage = 1, FolderName = "Edebiyat" };
            var b4 = new BookItem { Title = "Küçük Prens (Sesli Kitap) - Antoine de Saint-Exupéry", Type = BookType.AUDIO, UriString = audioPath, TotalDurationMs = 7200000, CurrentPositionMs = 1540000, FolderName = "Sesli Kitaplar" };

            if (repo.Books.Count == 0)
            {
                repo.AddBook(b1);
                repo.AddBook(b2);
                repo.AddBook(b3);
                repo.AddBook(b4);
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var outDir = Path.Combine(desktop, "Magaza_Ekran_Goruntuleri");
            Directory.CreateDirectory(outDir);

            int w = 1366;
            int h = 768;

            var win = MainWindow.Instance;
            win.Width = w; win.Height = h;

            // 1. Kitaplık
            win.ShowLibrary();
            Capture(win, w, h, Path.Combine(outDir, "01_Kitaplik_Ana_Ekran.png"));

            // 2. Ayarlar
            win.ShowSettings();
            Capture(win, w, h, Path.Combine(outDir, "04_Ayarlar_Ve_Gelistirici.png"));

            // 3. Okuyucu
            win.OpenBook(b1);
            Capture(win, w, h, Path.Combine(outDir, "02_Belge_Okuma_Ekrani.png"));

            // 4. Sesli Kitap
            win.OpenBook(b4);
            Capture(win, w, h, Path.Combine(outDir, "03_Sesli_Kitap_Calici.png"));

            Console.WriteLine("SNAPSHOTS_ALL_DONE");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR: " + ex);
            Environment.Exit(1);
        }
    }

    private static void Capture(Window win, int w, int h, string file)
    {
        win.Measure(new Size(w, h));
        win.Arrange(new Rect(0, 0, w, h));
        win.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(win);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        Console.WriteLine("Captured: " + file);
    }
}
