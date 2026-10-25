using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ErisilebilirKitapOkuyucu.Services;

namespace ErisilebilirKitapOkuyucu.Pages;

public sealed class SettingsPage : PageBase
{
    private readonly TextBlock _heading = Ui.Text("Ayarlar", "Heading");
    private readonly StackPanel _engineBox = new();
    private readonly ListBox _voices = new() { MaxHeight = 220, MinHeight = 90 };
    private readonly TextBlock _voiceInfo = new();
    private bool _syncing = true;
    private string? _engineId;

    public override string PageTitle => "Ayarlar";
    public override UIElement? InitialFocus => _heading;

    public SettingsPage()
    {
        Ui.Name(_heading, "Ayarlar");
        var s = App.Repo.Settings;
        _engineId = s.SelectedTtsEngine;
        var stack = new StackPanel();

        stack.Children.Add(Ui.Button("Kitaplığa Dön", () => Host.ShowLibrary(), "Esc tuşu. Ayarlar anında kaydedilir"));

        // ---- Konuşma motoru ve ses
        var ttsGroup = new GroupBox { Header = "Konuşma Motoru ve Ses" };
        Ui.Name(ttsGroup, "Konuşma Motoru ve Ses");
        var tts = new StackPanel();
        tts.Children.Add(Ui.Text("Konuşma motoru"));
        _engineBox.Children.Add(Ui.Radio("Otomatik (en uygun motoru seç)", "motor", () => SetEngine(null), s.SelectedTtsEngine == null));
        foreach (var e in App.Tts.Engines)
        {
            var id = e.Id;
            var count = e.GetVoices().Count;
            _engineBox.Children.Add(Ui.Radio($"{e.DisplayName}, {count} ses", "motor", () => SetEngine(id), s.SelectedTtsEngine == id));
        }
        tts.Children.Add(_engineBox);
        var vl = Ui.Text("Ses (sistemde yüklü sesler)", "SubHeading");
        tts.Children.Add(vl);
        Ui.Name(_voices, "Ses listesi");
        _voices.SelectionChanged += (_, _) =>
        {
            if (_syncing || _voices.SelectedItem is not ListBoxItem { Tag: VoiceDescriptor v }) return;
            Update(x => x.SelectedVoiceId = v.Id);
            App.Announce($"{v.Name} seçildi");
        };
        tts.Children.Add(_voices);
        tts.Children.Add(_voiceInfo);
        tts.Children.Add(Ui.Button("Sesi Dene", PreviewVoice, "Seçili sesle örnek bir cümle okur"));
        tts.Children.Add(Slider("Okuma hızı", s.TtsSpeechRate, 0.5, 3.0, 0.1, v => Update(x => x.TtsSpeechRate = (float)v), v => $"{v:0.0}x"));
        tts.Children.Add(Slider("Ses perdesi", s.TtsPitch, 0.5, 2.0, 0.1, v => Update(x => x.TtsPitch = (float)v), v => $"{v:0.0}"));
        ttsGroup.Content = tts;
        stack.Children.Add(ttsGroup);

        // ---- Google Cloud TTS API Anahtarı ve Yasal Sorumluluk Bildirimi
        var googleGroup = new GroupBox { Header = "Google Cloud Seslendirme (İsteğe Bağlı)" };
        Ui.Name(googleGroup, "Google Cloud Seslendirme");
        var gPanel = new StackPanel();

        bool hasKey = !string.IsNullOrWhiteSpace(s.GoogleApiKey);
        var keyStatusText = Ui.Text(hasKey ? "Google Cloud API Anahtarı: Kayıtlı ve aktif" : "Google Cloud API Anahtarı: Henüz tanımlanmamış");
        gPanel.Children.Add(keyStatusText);

        var keyInputContainer = new StackPanel { Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 4, 0, 4) };
        var apiBox = new PasswordBox
        {
            Password = s.GoogleApiKey ?? "",
            Margin = new Thickness(0, 4, 0, 6),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 14
        };
        Ui.Name(apiBox, "Google Cloud API Anahtarı Giriş Alanı");

        var saveKeyBtn = Ui.Button("Anahtarı Kaydet ve Gizle", () =>
        {
            var val = apiBox.Password.Trim();
            Update(x => x.GoogleApiKey = string.IsNullOrWhiteSpace(val) ? null : val);
            if (!string.IsNullOrWhiteSpace(val))
            {
                keyStatusText.Text = "Google Cloud API Anahtarı: Kayıtlı ve aktif";
                keyInputContainer.Visibility = Visibility.Collapsed;
                App.Announce("Google Cloud API anahtarı güvenle kaydedildi ve gizlendi.");
            }
            else
            {
                keyStatusText.Text = "Google Cloud API Anahtarı: Henüz tanımlanmamış";
                App.Announce("API anahtarı temizlendi.");
            }
        }, "Girilen anahtarı güvenle kaydeder ve ekrandan gizler");

        keyInputContainer.Children.Add(Ui.Text("API Anahtarınızı yapıştırın:"));
        keyInputContainer.Children.Add(apiBox);
        keyInputContainer.Children.Add(saveKeyBtn);
        gPanel.Children.Add(keyInputContainer);

        var toggleBtn = Ui.Button(hasKey ? "API Anahtarını Değiştir / Düzenle" : "API Anahtarı Tanımla", () =>
        {
            if (keyInputContainer.Visibility == Visibility.Visible)
            {
                keyInputContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                keyInputContainer.Visibility = Visibility.Visible;
                apiBox.Focus();
            }
        }, "API anahtarı giriş kutusunu açar veya kapatır");
        gPanel.Children.Add(toggleBtn);

        var disclaimerText = new TextBlock
        {
            Text = "Yasal Sorumluluk Bildirimi: Uygulama içerisinde girilen Google Cloud API anahtarları tamamen kullanıcının kendi sorumluluğundadır. İlgili servis sağlayıcıları tarafından belirlenen kotaların aşılması veya ücretlendirme sebebiyle doğabilecek hiçbir maddi borç, fatura veya mali yükümlülükten uygulama geliştiricisi (Ömer Göktaş) sorumlu tutulamaz. Girilen anahtar yalnızca bu bilgisayarda yerel olarak saklanır.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkRed,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 6)
        };
        Ui.Name(disclaimerText, disclaimerText.Text);
        gPanel.Children.Add(disclaimerText);
        googleGroup.Content = gPanel;
        stack.Children.Add(googleGroup);

        // ---- Okuma
        var readGroup = new GroupBox { Header = "Okuma" };
        Ui.Name(readGroup, "Okuma");
        var read = new StackPanel();
        read.Children.Add(Ui.Text("Varsayılan Okuma Modu (TTS ve Gezinme)"));
        read.Children.Add(Ui.Radio("Cümle Cümle Okuma", "okumamodu", () => Update(x => x.TtsReadMode = "Cümle"), s.TtsReadMode != "Paragraf"));
        read.Children.Add(Ui.Radio("Paragraf Paragraf Okuma (Kesintisiz ve Akıcı)", "okumamodu", () => Update(x => x.TtsReadMode = "Paragraf"), s.TtsReadMode == "Paragraf"));

        var auto = new CheckBox { Content = "Sayfa bitince sonraki sayfaya otomatik geç", IsChecked = s.AutoNextPage };
        auto.Checked += (_, _) => Update(x => x.AutoNextPage = true);
        auto.Unchecked += (_, _) => Update(x => x.AutoNextPage = false);
        read.Children.Add(auto);
        read.Children.Add(Ui.Text("Sayfa değişim sesi"));
        foreach (var v in PageTurnSounds.Voices)
        {
            var voice = v;
            read.Children.Add(Ui.Radio(voice, "sayfasesi", () => { Update(x => x.PageTurnVoice = voice); if (!_syncing) PageTurnSounds.Play(voice, true); }, s.PageTurnVoice == voice));
        }
        var ttsSound = new CheckBox { Content = "Sesli okurken sayfa geçişinde de sayfa sesini çal", IsChecked = s.PlayPageTurnSoundOnTtsNextPage };
        ttsSound.Checked += (_, _) => Update(x => x.PlayPageTurnSoundOnTtsNextPage = true);
        ttsSound.Unchecked += (_, _) => Update(x => x.PlayPageTurnSoundOnTtsNextPage = false);
        read.Children.Add(ttsSound);
        readGroup.Content = read;
        stack.Children.Add(readGroup);

        // ---- Sesli kitap
        var audioGroup = new GroupBox { Header = "Sesli Kitap" };
        Ui.Name(audioGroup, "Sesli Kitap");
        var audio = new StackPanel();
        audio.Children.Add(Slider("Varsayılan oynatma hızı", s.AudioSpeed, 0.5, 3.0, 0.25, v => Update(x => x.AudioSpeed = (float)v), v => $"{v:0.##}x"));
        audio.Children.Add(Slider("İleri ve geri sarma süresi (saniye)", s.SeekIntervalSeconds, 5, 60, 5, v => Update(x => x.SeekIntervalSeconds = (int)v), v => $"{(int)v} saniye"));
        audioGroup.Content = audio;
        stack.Children.Add(audioGroup);

        // ---- Görünüm
        var viewGroup = new GroupBox { Header = "Görünüm" };
        Ui.Name(viewGroup, "Görünüm");
        var view = new StackPanel();
        var hc = new CheckBox { Content = "Yüksek karşıtlık modu (siyah zemin, sarı vurgu)", IsChecked = s.HighContrastMode };
        hc.Checked += (_, _) => Update(x => x.HighContrastMode = true);
        hc.Unchecked += (_, _) => Update(x => x.HighContrastMode = false);
        view.Children.Add(hc);
        view.Children.Add(Slider("Yazı boyutu", s.FontSizeSp, 14, 40, 2, v => Update(x => x.FontSizeSp = (int)v), v => $"{(int)v} punto"));
        view.Children.Add(Ui.Text("Windows yüksek karşıtlık teması açıkken uygulama sistem renklerini kullanır."));
        viewGroup.Content = view;
        stack.Children.Add(viewGroup);

        // ---- Hakkında
        var aboutGroup = new GroupBox { Header = "Hakkında" };
        Ui.Name(aboutGroup, "Hakkında");
        var aboutPanel = new StackPanel();
        var appVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        aboutPanel.Children.Add(Ui.Text($"Erişilebilir Kitap Okuyucu (Windows v{appVer})", "SubHeading"));
        aboutPanel.Children.Add(Ui.Text("Geliştirici: Ömer Göktaş"));

        var linksPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        linksPanel.Children.Add(Ui.Button("E-Posta Gönder", () => OpenUrl("mailto:mail@omergoktas.net"), "Varsayılan e-posta istemcinizi açarak mail@omergoktas.net adresine ileti hazırlar"));
        linksPanel.Children.Add(Ui.Button("Web Sitesini Ziyaret Et", () => OpenUrl("https://omergoktas.net"), "omergoktas.net web sitesini tarayıcınızda açar"));
        linksPanel.Children.Add(Ui.Button("Gizlilik Politikası", () => new PrivacyDialog(Host).ShowDialog(), "Uygulamanın gizlilik politikasını görüntüler"));
        aboutPanel.Children.Add(linksPanel);

        aboutGroup.Content = aboutPanel;
        stack.Children.Add(aboutGroup);

        stack.Children.Add(Ui.Button("Kitaplığa Dön", () => Host.ShowLibrary(), "Esc tuşu"));

        var root = new DockPanel();
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading);
        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = root;

        RefreshVoices();
        if (App.Tts.ActiveEngine is EdgeTtsEngine edgeInit) edgeInit.PreWarm();
        _syncing = false;
    }

    private FrameworkElement Slider(string label, double value, double min, double max, double step, Action<double> onChange, Func<double, string> fmt)
    {
        var p = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var lbl = Ui.Text(label); lbl.VerticalAlignment = VerticalAlignment.Center; lbl.MinWidth = 280; lbl.Margin = new Thickness(0, 0, 10, 0);
        var val = Ui.Text(fmt(value)); val.VerticalAlignment = VerticalAlignment.Center; val.MinWidth = 100;
        var sl = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = step, LargeChange = step * 2, TickFrequency = step, IsSnapToTickEnabled = true, MinWidth = 220 };
        Ui.Name(sl, label);
        sl.ValueChanged += (_, _) => { val.Text = fmt(sl.Value); if (!_syncing) onChange(sl.Value); };
        DockPanel.SetDock(lbl, Dock.Left); DockPanel.SetDock(val, Dock.Right);
        p.Children.Add(lbl); p.Children.Add(val); p.Children.Add(sl);
        return p;
    }

    private void Update(Action<AppSettings> change)
    {
        if (_syncing) return;
        var s = App.Repo.Settings;
        change(s);
        App.Repo.UpdateSettings(s);
    }

    private void SetEngine(string? id)
    {
        if (_syncing) return;
        _engineId = id;
        Update(x => { x.SelectedTtsEngine = id; x.SelectedVoiceId = null; });
        RefreshVoices();
        if (App.Tts.ActiveEngine is EdgeTtsEngine edge) edge.PreWarm();
        App.Announce("Konuşma motoru değiştirildi, " + App.Tts.ActiveEngine.DisplayName);
    }

    private void RefreshVoices()
    {
        var was = _syncing; _syncing = true;
        _voices.Items.Clear();
        var engine = App.Tts.ActiveEngine;
        var list = engine.GetVoices().OrderByDescending(v => v.IsTurkish).ThenBy(v => v.Name).ToList();
        var active = App.Tts.ActiveVoiceId;
        foreach (var v in list)
        {
            var item = new ListBoxItem { Content = v.Display, Tag = v };
            Ui.Name(item, v.Display);
            _voices.Items.Add(item);
            if (v.Id == active) item.IsSelected = true;
        }
        _voiceInfo.Text = list.Count == 0
            ? $"{engine.DisplayName} motorunda yüklü ses bulunamadı. Windows Ayarlar > Zaman ve dil > Konuşma bölümünden ses ekleyebilirsiniz."
            : $"Kullanılan motor: {engine.DisplayName}. {list.Count} ses bulundu.";
        if (engine is SapiEngine sapi && sapi.UnusableVoices.Count > 0)
            _voiceInfo.Text += $" Windows'ta kayıtlı olduğu hâlde yüklenemeyen {sapi.UnusableVoices.Count} ses gizlendi: {string.Join(", ", sapi.UnusableVoices)}. Bu seslerin kurulumunu onarmanız gerekebilir.";
        _syncing = was;
    }

    private async void PreviewVoice()
    {
        var engine = App.Tts.ActiveEngine;
        var voice = (_voices.SelectedItem as ListBoxItem)?.Tag as VoiceDescriptor;
        await App.Tts.PreviewAsync("Merhaba, bu ses Erişilebilir Kitap Okuyucu tarafından okunuyor.", engine, voice?.Id ?? App.Tts.ActiveVoiceId);
    }

    private static void OpenUrl(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Announce("Bağlantı açılamadı: " + ex.Message, true); }
    }

    public override void OnHidden() => App.Tts.Stop();
}
