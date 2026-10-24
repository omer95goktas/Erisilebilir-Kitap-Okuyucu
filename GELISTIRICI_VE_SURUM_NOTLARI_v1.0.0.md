# Erişilebilir Kitap Okuyucu (Windows) — Geliştirici ve Sürüm Notları (v1.0.0)

Bu belge, **Erişilebilir Kitap Okuyucu** uygulamasının Windows masaüstü sürümünün ilk resmi yayını (**v1.0.0**) kapsamında geliştirilen tüm özellikleri, Android sürümüyle olan mimari farkları, geliştirme sürecinde çözülen kritik sorunları ve gelecekte yeni güncellemeler yayınlarken ihtiyaç duyulacak tüm teknik adımları içermektedir.

---

## 📌 1. Proje Kimliği ve Genel Bakış

* **Uygulama Adı:** Erişilebilir Kitap Okuyucu (Windows)
* **Geliştirici:** Ömer Göktaş ([omergoktas.net](https://omergoktas.net) — mail@omergoktas.net)
* **Resmi Sürüm:** `v1.0.0` (Dosya Sürümü: `1.0.0.0`)
* **Platform:** Windows 10 & Windows 11 (64-bit x64)
* **Çerçeve:** .NET 8.0 (`net8.0-windows10.0.19041.0`), WPF, C# 12
* **Dağıtım Biçimi:** Tek Dosya Taşınabilir (Self-Contained Single-File Executable — Kurulum gerektirmez, doğrudan çalışır)
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
| **Yayıncı & UAC İzinleri** | Play Store İzinleri | `asInvoker` + Authenticode İmza | Windows UAC ve SmartScreen'in sürekli onay istemesini engelleyen tam masaüstü kimliği tanımlandı. |

---

## 🛠️ 4. Çözülen Kritik Hatalar ve Teknik Çözümleri

### 1. Word Belgelerinde Ekran Okuyucu Satır Tekrarı Çözüldü
- **Sorun:** Word belgesini açıp Aşağı/Yukarı ok tuşlarıyla ilerlerken NVDA ekran okuyucusu bir sonraki satırı iki defa üst üste seslendiriyordu.
- **Kök Neden:** WPF `TextBox` kontrolünde arka arkaya çift yeni satır (`\r\n\r\n`) bulunduğunda, NVDA'nın kullandığı UI Automation `TextPattern.ExpandToEnclosingUnit(Line)` metodu boş satırdayken bir sonraki satırın metnini döndürüyordu.
- **Çözüm:** `ReaderPage.ShowPage` içinde ardışık yeni satırlar tekil `\r\n` formatına normalize edildi. Böylece NVDA satırları bire bir ve pürüzsüz okumaya başladı.

### 2. Açılışta Klavye ve Tab Tuşunun Kilitlenmesi (Alt+Tab Zorunluluğu) Çözüldü
- **Sorun:** Program masaüstünden ilk açıldığında NVDA ilk kitabın adını okuyor, fakat Aşağı/Yukarı ok tuşlarına veya Tab tuşuna basıldığında program hiçbir tuşa tepki vermiyordu. `Alt+Tab` yapıp geri dönüldüğünde her şey düzeliyordu.
- **Kök Neden:** `MainWindow.EnsureForeground` fonksiyonundaki `AttachThreadInput(foreThread, appThread, true)` ve hemen ardından çağrılan `AttachThreadInput(..., false)` çağrısı, Windows Explorer ile uygulamanın giriş kuyruklarını kopartıp Win32 düzeyindeki klavye odağını (`WM_SETFOCUS`) sıfırlıyordu.
- **Çözüm:**
  - `AttachThreadInput` çağrıları tamamen kaldırıldı.
  - Windows standart pencere aktivasyonu (`Topmost=true/false`, `Activate()`, `SetFocus(hWnd)`) ve WPF `Keyboard.Focus` zinciri kuruldu.
  - `LibraryPage` yapıcısında `ShowLibrary()` hemen çağrılarak görsel ağacın pencere açılmadan önce hazır olması sağlandı.
  - `ListBox` üzerindeki yön tuşlarını kilitleyen yapay `PreviewKeyDown` kodları temizlendi.

### 3. Sürekli İzin İsteme ve "Bilinmeyen Yayımcı" Sorunu Çözüldü
- **Sorun:** Kullanıcı "Ek bilgi -> Yine de çalıştır" dedikten sonra bile Windows her açılışta veya çalıştırmada izin istiyor ve yayıncı "Bilinmeyen" görünüyordu.
- **Kök Neden:** `app.manifest` dosyasında `<requestedExecutionLevel level="asInvoker" />` tanımlı değildi. Windows, UAC sezgisel motoruyla programı eski veya şüpheli görerek her defasında onay istiyordu.
- **Çözüm:**
  - `app.manifest` içerisine standart `asInvoker` kuralı eklendi.
  - `IncludeNativeLibrariesForSelfExtract` kaldırılarak `%TEMP%` dizinine geçici dosya çıkartılması engellendi.
  - `.csproj` içerisine Yayımcı, Yazar ve Telif Hakları tanımlandı (`Company: Ömer Göktaş`).
  - Uygulama SHA256 ile resmi olarak **Ömer Göktaş** adına dijital olarak imzalandı (`Authenticode`).

---

## 🚀 5. Yeni Güncelleme Yayınlama Rehberi (Adım Adım)

Gelecekte yeni bir sürüm (örneğin `v1.1.0`) yayınlamak istediğinizde izlenecek kesin adımlar şunlardır:

### 1. Adım: Sürüm Numaralarını Güncelleyin
- `windows/ErisilebilirKitapOkuyucu.Windows/ErisilebilirKitapOkuyucu.Windows.csproj`:
  ```xml
  <Version>1.1.0</Version>
  <AssemblyVersion>1.1.0.0</AssemblyVersion>
  <FileVersion>1.1.0.0</FileVersion>
  ```
- `windows/ErisilebilirKitapOkuyucu.Windows/Pages/SettingsPage.cs`:
  Fallback sürümünü `"1.1.0"` yapın.
- `windows/ErisilebilirKitapOkuyucu.Windows/Dialogs.cs`:
  Yardım ekranındaki sürüm metnini `v1.1.0` yapın.

### 2. Adım: Otomatik Testleri Çalıştırın
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project "windows\ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj" -- --selftest "windows\selftest_report.txt"
```
*(44 testin tamamının `[GEÇTİ]` olduğundan emin olun.)*

### 3. Adım: Tek Dosya Release Sürümünü Derleyin
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish "windows\ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "windows\publish"
```

### 4. Adım: .exe Dosyasını Dijital Olarak İmzalayın
```powershell
$cert = Get-Item 'Cert:\CurrentUser\My\66745F66182183264B0634030FAFE6FC3AEA0C7F'
$exe = "windows\publish\ErisilebilirKitapOkuyucu.exe"
Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256
```

### 5. Adım: Masaüstüne ve Proje Dizinine Kopyalayın
```powershell
Copy-Item "windows\publish\ErisilebilirKitapOkuyucu.exe" -Destination "C:\Users\gokta\OneDrive\Desktop\ErisilebilirKitapOkuyucu.exe" -Force
Copy-Item "windows\publish\ErisilebilirKitapOkuyucu.exe" -Destination "windows\ErisilebilirKitapOkuyucu.exe" -Force
```

### 6. Adım: Git'e Gönderin
```powershell
git -C "windows" add .
git -C "windows" commit -m "Erisilebilir Kitap Okuyucu v1.1.0 Guncellemesi"
git -C "windows" push origin main
```

### 7. Adım: GitHub'da Yeni Release Oluşturun
1. GitHub deponuzda **Releases -> Draft a new release** deyin.
2. Tag olarak `v1.1.0` yazın.
3. Başlık olarak `Erişilebilir Kitap Okuyucu v1.1.0 (Windows)` yazın.
4. Masaüstündeki imzalanmış `ErisilebilirKitapOkuyucu.exe` dosyasını sürükleyip bırakın.
5. **Publish release** butonuna basın.

*(Kalıcı indirme linki `.../releases/latest/download/ErisilebilirKitapOkuyucu.exe` olduğu için kullanıcılar hiçbir link değiştirmeden otomatik olarak yeni sürümü indirecektir!)*

---

## 🔮 6. Gelecek Sürümler İçin Fikirler ve Yol Haritası (v1.1.0+)

1. **Otomatik Güncelleme Denetleyicisi:**
   - Program açıldığında veya Ayarlar menüsünden *"Güncellemeleri Denetle"* dendiğinde GitHub API'sine bakıp yeni sürüm varsa kullanıcıya tek tıkla güncelleme imkanı sunma.
2. **Masaüstü ve Başlat Menüsü Kısayolu Ekleme:**
   - Ayarlar sayfasına *"Masaüstüne Kısayol Oluştur"* butonu ekleme.
3. **MOBI / AZW3 Format Desteği:**
   - Kindle formatındaki e-kitapların da doğrudan açılabilmesi.
4. **Gelişmiş Yer İmi Yedekleme:**
   - Yer imlerini ve okuma konumlarını JSON / TXT olarak dışa aktarıp başka bir bilgisayara aktarabilme.

---

## 11. KURULUM (SETUP / INSTALLER) SİSTEMİ VE YAYIMCI MİMARİSİ

### 11.1 Neden Kurulum Programı Hazırlandı?
1. **Windows SmartScreen ve Defender Koruması:** Tekil ve sıkıştırılmış `.exe` dosyaları internetten indiğinde Windows tarafından her çalıştırmada "tanınmayan dosya" veya "geçici klasöre kod açan dosya (dropper)" olarak algılanabiliyordu.
2. **Kalıcı Yerel Kurulum:** Kurulum programı dosyaları Windows'un resmi kullanıcı dizinine (`AppData\Local\Programs\ErisilebilirKitapOkuyucu`) sabitler. Masaüstündeki kısayol bu yerel konumu çalıştırdığı için Windows işletim sistemi uygulamayı güvenli yerel yazılım olarak çalıştırır.
3. **Resmi Yayımcı Bilgisi:** Dosya özelliklerinde ve Windows Denetim Masası'nda yayımcı olarak **`omergoktas.net`** görünür.

### 11.2 Kurulum Mimarisi (Setup.cs)
- **Kaynak Dosya:** `windows/installer/Setup.cs`
- **Bildirim (Manifest):** `windows/installer/setup.manifest` (`asInvoker` seviyesi — yönetici izni/UAC gerektirmez).
- **Yayımcı:** `omergoktas.net`
- **Hedef Dizin:** `%LocalAppData%\Programs\ErisilebilirKitapOkuyucu\`
- **Kısayollar:**
  - Masaüstü: `Erisilebilir Kitap Okuyucu.lnk`
  - Başlat Menüsü: `Programs\Erisilebilir Kitap Okuyucu\Erisilebilir Kitap Okuyucu.lnk`
- **Kayıt Defteri (Uninstall):**
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ErisilebilirKitapOkuyucu`

### 11.3 Yeni Güncellemede Kurulum Paketini Yeniden Derleme Adımları
Gelecekte yeni bir sürüm çıktığında kurulum programını tek komutla oluşturmak için:
```powershell
# 1. Temiz klasör çıktısı al
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish "windows\ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "windows\publish_folder"

# 2. payload.zip oluştur
Compress-Archive -Path "windows\publish_folder\*" -DestinationPath "windows\installer\payload.zip" -Force

# 3. Setup.exe derle
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:"windows\installer\ErisilebilirKitapOkuyucu_Kurulum.exe" /win32manifest:"windows\installer\setup.manifest" /resource:"windows\installer\payload.zip",payload.zip /r:System.Windows.Forms.dll,System.Drawing.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll,Microsoft.CSharp.dll "windows\installer\Setup.cs"
```

### 11.4 Yayınlanan Dağıtım Dosyaları (GitHub Release v1.0.0)
1. **`ErisilebilirKitapOkuyucu_Kurulum.exe` (74.4 MB):** Resmi kurulum sihirbazı (Tavsiye edilen).
2. **`ErisilebilirKitapOkuyucu_v1.0.0.zip` (70.9 MB):** Tarayıcıların indirmesini engellemediği temiz klasör arşivi.
3. **`ErisilebilirKitapOkuyucu.exe` (72.6 MB):** Tek dosya taşınabilir sürüm.

