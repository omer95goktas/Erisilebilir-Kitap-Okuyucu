# Erişilebilir Kitap Okuyucu (Windows) — Geliştirici ve Sürüm Rehberi (v1.0.0)

Bu belge, **Erişilebilir Kitap Okuyucu** uygulamasının Windows masaüstü sürümünün ilk resmi yayını (**v1.0.0**) kapsamında geliştirilen tüm özellikleri, Android sürümüyle olan mimari farkları, geliştirme sürecinde çözülen kritik sorunları ve gelecekte yeni güncellemeler yayınlarken hiçbir aşamada takılmamanız için gereken **adım adım teknik prosedürü** içermektedir.

---

## 📌 1. Proje Kimliği ve Genel Bakış

* **Uygulama Adı:** Erişilebilir Kitap Okuyucu (Windows)
* **Geliştirici & Yayıncı:** Ömer Göktaş ([omergoktas.net](https://omergoktas.net) — mail@omergoktas.net)
* **Resmi Sürüm:** `v1.0.0` (Assembly/Dosya Sürümü: `1.0.0.0`)
* **Platform:** Windows 10 & Windows 11 (64-bit x64)
* **Çerçeve:** .NET 8.0 (`net8.0-windows10.0.19041.0`), WPF, C# 12
* **Kurulum Mimarisi:** Dedicated Setup Wizard (.NET Framework 4.8 / WinForms, manifest `asInvoker`, hedef: `%LocalAppData%\Programs\ErisilebilirKitapOkuyucu\`)
* **Açık Kaynak Deposu:** [https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu](https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu)
* **Kalıcı İndirme Linki:** [https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu/releases/latest/download/ErisilebilirKitapOkuyucu.exe](https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu/releases/latest/download/ErisilebilirKitapOkuyucu.exe)

---

## 🌟 2. v1.0.0 Kapsamındaki Tüm Özellikler Envanteri

### A. Belge Okuma ve Format Desteği (`DocumentService.cs`, `ReaderPage.cs`)
1. **Microsoft Word (.docx):**
   - Word XML gövdesindeki paragrafları ve başlıkları hiyerarşik olarak ayıklar.
   - Ekran okuyucuların (özellikle NVDA) boş satırlarda sonraki satırı çift okuma hatasını engelleyen akıllı satır normalizasyonu içerir.
   - Aşağı / Yukarı ok tuşlarıyla satır satır kesintisiz gezinme sağlar.
2. **PDF Desteği (PdfPig + Dahili OCR):**
   - Standart dijital PDF'lerdeki metinleri sayfa sayfa ayıklar.
   - Taranmış / resim formatındaki PDF'lerde Windows'un yerleşik OCR teknolojisini (`Windows.Media.Ocr`) kullanarak sayfaları saniyeler içinde okunabilir metne dönüştürür.
3. **EPUB Kitaplar:**
   - `container.xml` -> `content.opf` -> `spine` sırasına göre tüm bölümleri çözer.
   - Reklam, script ve gereksiz etiketleri temizler; bölüm adları ve sayfa içeriklerini pürüzsüz aktarır.
4. **Düz Metin (TXT):**
   - Hem modern **UTF-8** hem de Türkçe Windows sistemlerinde sıkça kullanılan **Windows-1254 (CP1254)** kodlamalarını otomatik tespit eder; "ı, ğ, ü, ş, ö, ç" harflerini asla bozmaz.
5. **Web Makaleleri (Ctrl + U):**
   - `HtmlAgilityPack` ile herhangi bir web sayfasının linki verildiğinde; reklamları, kenar çubuklarını ve menüleri temizleyip makalenin yalnızca ana metnini kitaplığa kitap olarak ekler.
6. **Panodan Metin Yapıştırma (Ctrl + Shift + V):**
   - Panoda kopyalanmış herhangi bir metni tek tıkla başlık ve sayfalama yaparak kitaplığa kaydeder.
7. **Belge İçi Arama ve Gezinme:**
   - `Ctrl + F`: Arama kutusu, `F3`: Sonrakini bul, `Shift + F3`: Öncekini bul.
   - `Ctrl + G`: Doğrudan sayfaya git.
   - `Ctrl + B` / `B`: Bulunulan sayfaya ve konuma metin yer imi ekleme / yer imlerini listeleme.

### B. Sesli Kitap Oynatıcı (`AudioServices.cs`, `AudioPage.cs`)
1. **Geniş Ses Formatı Desteği:**
   - MP3, M4A, WAV, AAC, WMA, OGG dosyalarını çalar (`NAudio` ve Media Foundation tabanlı).
2. **Çok Bölümlü ZIP Sesli Kitapları:**
   - Bir ZIP arşivinde toplanmış birden çok sesli kitap ayrımını (Ayrım 1, Ayrım 2...) tek bir kitap olarak tanır; bölüm listesini gösterir, bölüm geçişlerini otomatik yapar.
3. **Oynatma Kontrolleri ve Kısayollar:**
   - `Boşluk Tuşu`: Oynat / Duraklat.
   - `Ctrl + Sol / Sağ Ok`: 5 saniye geri / ileri sar.
   - `Ctrl + Yukarı / Aşağı Ok`: Ses seviyesini artır / azalt.
   - Oynatma hızı ayarı (0.75x, 1.0x, 1.25x, 1.5x, 2.0x).
   - Uyku zamanlayıcısı (15, 30, 45, 60 dakika).
   - **Çalan Sesli Kitaba Dön:** Kütüphanede veya ayarlarda gezinirken arka planda çalan sesli kitaba tek tıkla veya kısayolla dönme imkanı.

### C. Akıllı Metin Okuma (TTS) Motoru (`TtsEngines.cs`, `TtsService.cs`)
1. **Windows OneCore Sesleri:**
   - Windows 10/11'in modern, doğal Türkçe seslerini (Microsoft Tolga vb.) doğrudan destekler.
2. **SAPI 5 Sesleri:**
   - Kullanıcının sisteminde kurulu klasik Türkçe veya yabancı dildeki SAPI 5 seslerini listeler ve kullanır.
3. **Esnek Okuma Modları:**
   - Satır satır okuma veya cümle cümle okuma.
   - Konuşma hızı, ses perdesi (pitch) ve ses düzeyi ayarları.
4. **Sesi Dosyaya Aktarma (WAV Export):**
   - Herhangi bir kitabın metnini doğrudan sesli kitap dosyası (WAV) olarak dışa aktarabilme.

### D. Kitaplık ve Dosya Yönetimi (`BookRepository.cs`, `LibraryPage.cs`)
1. **Klasörleme:**
   - Kitapları klasörlere ayırma (Yeni Klasör, Yeniden Adlandır, Sil, Klasöre Taşı).
   - Klasör silindiğinde içindeki kitaplar kaybolmaz, güvenle "Ana Dizin"e aktarılır.
2. **Filtreler ve Arama:**
   - "Tümü", "PDF ve Metin", "Sesli Kitaplar" filtreleri.
   - Gerçek zamanlı arama kutusu.
3. **Sürükle-Bırak (Drag & Drop):**
   - Masaüstünden veya herhangi bir klasörden belgeleri pencerenin içine bırakarak anında ekleme.

### E. Görünüm, Temalar ve Erişilebilirlik (`ThemeManager.cs`, `SettingsPage.cs`)
1. **Az Görenler İçin Yüksek Kontrast:**
   - Siyah üzerine Sarı
   - Siyah üzerine Beyaz
   - Beyaz üzerine Siyah
   - Sarı üzerine Siyah
2. **Dinamik Yazı Boyutu:**
   - 14 pt'den 36 pt'ye kadar tüm arayüzü ve okuma metinlerini tek hamlede büyütebilme.
3. **Ses Efektleri:**
   - Sayfa değiştirme ve bildirim sesleri (kapatılabilir/açılabilir).
4. **Landmark ve Klavye Standartları:**
   - Standart Windows `F6` tuşu ile Kitap Listesi ve Araç Çubuğu arasında geçiş.
   - Başlık ve statik etiketlerin Tab sırasını tıkamaması (`KeyboardNavigation.SetIsTabStop = false`).

### F. Yerel Gizlilik ve Güvenlik
- Tüm ayarlar ve kitap kütüphanesi yalnızca `%APPDATA%\ErisilebilirKitapOkuyucu\` dizininde (`library.json`, `settings.json`) saklanır.
- İnternete hiçbir belge, kitap veya veri yüklenmez; uygulama tamamen yerel ve çevrimdışıdır.

---

## 🔄 3. Android Sürümü ile Karşılaştırma (Neler Alındı, Neler Uyarlandı?)

| Özellik | Android Sürümü | Windows Sürümü | Durum ve Tercih Nedeni |
| :--- | :--- | :--- | :--- |
| **Kamera ile Kitap Tarama (Camera OCR)** | Mevcut (ML Kit) | Yok (Yerine Taranmış PDF OCR konuldu) | Masaüstü bilgisayarlarda kamera ile fiziksel kitap tutmak ergonomik olmadığından çıkarıldı; yerine taranmış PDF'leri doğrudan açan yerleşik Windows OCR motoru entegre edildi. |
| **Sesli Yer İmleri (Sesli Not)** | Mevcut (Mikrofon kaydı) | Yok (Sade metin ve sayfa yer imi) | Bilgisayar ortamında klavye ile hızlı not ve sayfa imi almak çok daha ergonomik olduğu için sesli not özelliği kullanıcı isteğiyle sadeleştirildi. |
| **Word (.docx) Okuma** | Paragraf bazlı aktarım | Satır satır okuma motoru | Ekran okuyucuların yön tuşlarıyla okurken satır tekrarı yapmasını engelleyen özel masaüstü satır mimarisi geliştirildi. |
| **Ses Motorları (TTS)** | Android TTS | OneCore (Tolga vb.) + SAPI 5 + WAV Aktarma | Windows'un en yüksek kaliteli doğal sesleri ve kitabı sese dökme özelliği eklendi. |
| **Klavye Kontrolleri** | Sınırlı (Dokunmatik odaklı) | Tam Klavye (Oklar, Tab, F6, F1, Enter...) | Fareye hiç dokunmadan tüm programı sadece klavyeden yönetme imkanı sağlandı. |
| **Kurulum & Yetki** | Play Store İzinleri | Dedicated Setup + `asInvoker` | Windows UAC ve SmartScreen'in sürekli onay istemesini engelleyen tam masaüstü kimliği tanımlandı. |

---

## 🛠️ 4. Çözülen Kritik Hatalar ve Mimari Kararlar

### 1. Windows Defender'ın Her Yeniden Başlatmada Tekrar Ortaya Çıkması ve Çözümü (Kritik Ders)
- **Sorun:** Tek parça taşınabilir `.exe` (`PublishSingleFile=true` ve `IncludeNativeLibrariesForSelfExtract=true`) kullanıldığında, kullanıcı ilk açılışta SmartScreen'e onay verse bile, bilgisayarı yeniden başlattığında veya başka bir gün açtığında Windows Defender tekrar uyarı veriyordu.
- **Kök Neden:** .NET 8 single-file self-extract mekanizması, her çalıştırmada yerel DLL'leri `%TEMP%\.net\ErisilebilirKitapOkuyucu\<rastgele-hash>\` dizinine dinamik olarak açar. Windows Defender sezgisel (heuristics) motoru, internetten indirilmiş bir programın her açılışta geçici klasöre rastgele DLL'ler bırakmasını tipik zararlı yazılım ("dropper") davranışı olarak değerlendirip tekrar bloke ediyordu.
- **Kesin Çözüm:**
  1. Tek parça self-extract yerine **resmi bir Kurulum Sihirbazı (`Setup.cs`)** geliştirildi.
  2. Kurulum sihirbazı, uygulamanın tüm dosyalarını bir defaya mahsus Windows'un standart kullanıcı yazılım dizini olan `%LocalAppData%\Programs\ErisilebilirKitapOkuyucu\` klasörüne açar.
  3. Masaüstüne ve Başlat menüsüne bu sabit yerel konumu gösteren kısayollar yerleştirir.
  4. Böylece kullanıcı günlük olarak programı masaüstündeki kısayoldan açtığında hiçbir geçici dosya çıkarma işlemi gerçekleşmez; Windows Defender bu konumu güvenli kabul eder ve **bir daha asla uyarı vermez**.

### 2. Word Belgelerinde Ekran Okuyucu Satır Tekrarı Çözüldü
- **Sorun:** Word belgesini açıp Aşağı/Yukarı ok tuşlarıyla ilerlerken NVDA ekran okuyucusu bir sonraki satırı iki defa üst üste seslendiriyordu.
- **Kök Neden:** WPF `TextBox` kontrolünde arka arkaya çift yeni satır (`\r\n\r\n`) bulunduğunda, NVDA'nın kullandığı UI Automation `TextPattern.ExpandToEnclosingUnit(Line)` metodu boş satırdayken bir sonraki satırın metnini döndürüyordu.
- **Çözüm:** `ReaderPage.ShowPage` içinde ardışık yeni satırlar tekil `\r\n` formatına normalize edildi. Böylece NVDA satırları bire bir ve pürüzsüz okumaya başladı.

### 3. Açılışta Klavye ve Tab Tuşunun Kilitlenmesi (Alt+Tab Zorunluluğu) Çözüldü
- **Sorun:** Program masaüstünden ilk açıldığında NVDA ilk kitabın adını okuyor, fakat Aşağı/Yukarı ok tuşlarına veya Tab tuşuna basıldığında program hiçbir tuşa tepki vermiyordu. `Alt+Tab` yapıp geri dönüldüğünde düzeliyordu.
- **Kök Neden:** `MainWindow.EnsureForeground` fonksiyonundaki `AttachThreadInput` Win32 API çağrısı, Windows Explorer ile uygulamanın giriş kuyruklarını kopartıp Win32 düzeyindeki klavye odağını (`WM_SETFOCUS`) sıfırlıyordu.
- **Çözüm:**
  - `AttachThreadInput` çağrıları tamamen kaldırıldı.
  - Windows standart pencere aktivasyonu (`Topmost=true/false`, `Activate()`, `SetFocus(hWnd)`) ve WPF `Keyboard.Focus` zinciri kuruldu.
  - `LibraryPage` yapıcısında `ShowLibrary()` hemen çağrılarak görsel ağacın pencere açılmadan önce hazır olması sağlandı.

---

## 📦 5. Kurulum (Setup) ve Dağıtım Dosyaları Yapısı

Tüm dağıtım dosyaları `windows\release_artifacts\` klasöründe toplanır:

| Dosya Adı | Boyut | Amacı ve Kullanımı |
| :--- | :--- | :--- |
| **`ErisilebilirKitapOkuyucu_Kurulum.exe`** | ~74.4 MB | **Resmi Kurulum Sihirbazı:** Yayıncı `omergoktas.net`, manifest `asInvoker`. Çift tıklandığında uygulamayı kurar, masaüstü simgesi oluşturur ve Program Ekle/Kaldır kaydı açar. |
| **`ErisilebilirKitapOkuyucu.exe`** | ~74.4 MB | **Kurulum Dosyasının Eş Kopyası:** Duyuru metinlerindeki eski veya doğrudan bağlantılardan gelen kullanıcıların yanlışlıkla eski taşınabilir dosyayı değil, doğrudan yeni kurulum sihirbazını indirmesini sağlar. |
| **`ErisilebilirKitapOkuyucu_v1.0.0.zip`** | ~73.7 MB | **Alternatif İndirme Paketi:** Şirket ağları veya tarayıcıları doğrudan `.exe` indirmeye izin vermeyen kullanıcılar içindir. İçinde `ErisilebilirKitapOkuyucu_Kurulum.exe` ve Türkçe `KURULUM_KILAVUZU.txt` bulunur. |
| **`Erisilebilir_Kitap_Okuyucu_Windows_Duyuru_Metni.docx`** | ~16 KB | Kullanıcılara gönderilmeye hazır, güncel kurulum adımlarını ve indirme bağlantılarını içeren Word duyuru metni. |

---

## 🚀 6. Gelecekte Yeni Bir Sürüm Çıkarma Rehberi (Adım Adım)

Gelecekte yeni bir sürüm (örneğin `v1.1.0` veya `v2.0.0`) yayınlamak istediğinizde, hiçbir noktada hata yapmadan ilerlemenizi sağlayan kesin süreç aşağıdadır:

### 1. Adım: Sürüm Numaralarını Güncelleyin
İlgili dosyalardaki sürüm numaralarını yeni sürüme çekin:
1. `windows\ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj`:
   ```xml
   <Version>1.1.0</Version>
   <AssemblyVersion>1.1.0.0</AssemblyVersion>
   <FileVersion>1.1.0.0</FileVersion>
   ```
2. `windows\installer\Setup.cs`:
   ```csharp
   [assembly: AssemblyVersion("1.1.0.0")]
   [assembly: AssemblyFileVersion("1.1.0.0")]
   ```
3. `windows\ErisilebilirKitapOkuyucu.Windows\Pages\SettingsPage.cs` ve `Dialogs.cs` içindeki sürüm etiketleri.

### 2. Adım: Otomatik Testleri Çalıştırın
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project "windows\ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj" -- --selftest "windows\selftest_report.txt"
```
*(44 testin tamamının `[GEÇTİ]` olduğunu teyit edin.)*

### 3. Adım: Tek Komutla Otomatik Derleme ve Paketleme
Artık derleme ve paketleme süreci tamamen tek bir PowerShell betiğinde otomatikleştirilmiştir:
```powershell
powershell -ExecutionPolicy Bypass -File windows\build-windows.ps1 -Version "1.1.0"
```
**Bu betik sırasıyla:**
1. .NET 8 WPF uygulamasını temiz `publish_folder` içine derler.
2. Uygulama dosyalarını `installer\payload.zip` olarak sıkıştırır.
3. `csc.exe` ile `Setup.cs`'i derleyerek resmi `ErisilebilirKitapOkuyucu_Kurulum.exe` kurulum sihirbazını üretir (Yayıncı: `omergoktas.net`).
4. Eski linkler için `ErisilebilirKitapOkuyucu.exe` adıyla da bir kopyasını hazırlar.
5. İçinde kurulum sihirbazı ve Türkçe kılavuz olan `ErisilebilirKitapOkuyucu_v1.1.0.zip` arşivini oluşturur.
6. Tüm bu dosyaları `windows\release_artifacts\` altına yerleştirir.

### 4. Adım: Git Deponuzu Güncelleyin
```powershell
git -C "windows" add .
git -C "windows" commit -m "Erisilebilir Kitap Okuyucu v1.1.0 Sürümü"
git -C "windows" push origin main
```

### 5. Adım: GitHub Releases Üzerinde Yeni Sürüm Açın
1. GitHub deponuzda ([github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu](https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu)) **Releases -> Draft a new release** seçeneğine tıklayın.
2. **Tag:** `v1.1.0` (yeni tag oluşturun).
3. **Release title:** `Erişilebilir Kitap Okuyucu v1.1.0 (Windows)`.
4. `windows\release_artifacts\` klasöründeki 3 dosyayı sürükleyip yükleyin:
   - `ErisilebilirKitapOkuyucu_Kurulum.exe`
   - `ErisilebilirKitapOkuyucu.exe`
   - `ErisilebilirKitapOkuyucu_v1.1.0.zip`
5. **Publish release** butonuna basın.

*(Not: Kalıcı indirme linki `https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu/releases/latest/download/ErisilebilirKitapOkuyucu.exe` olduğu için, duyuru metinlerini değiştirmeseniz bile kullanıcılar otomatik olarak en güncel sürümün kurulumunu indirecektir!)*

---

## 🔮 7. Gelecek Sürümler İçin Yol Haritası ve Fikirler

1. **Otomatik Güncelleme Denetleyicisi:**
   - Program açılışında veya Ayarlar sayfasından *"Güncellemeleri Denetle"* seçeneğiyle GitHub API'sine bakıp yeni sürüm varsa kullanıcıyı tek tıkla bilgilendirme.
2. **MOBI / AZW3 Format Desteği:**
   - Amazon Kindle formatındaki e-kitapların da doğrudan kütüphaneye eklenebilmesi.
3. **Gelişmiş Kütüphane / Yer İmi Yedekleme:**
   - Kitap listesini, yer imlerini ve okuma konumlarını JSON / ZIP olarak dışa aktarıp başka bir bilgisayara aktarabilme.
