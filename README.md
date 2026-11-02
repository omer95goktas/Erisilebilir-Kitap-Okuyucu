# Erişilebilir Kitap Okuyucu (Windows) - v2.0.0

Görme engelli kullanıcılar ve ekran okuyucu kullanıcıları (NVDA, JAWS, Windows Ekran Okuyucusu) için özel olarak geliştirilmiş; PDF, EPUB, Word (DOCX & DOC), Excel (XLSX), Markdown (MD), Düz Metin (TXT) ve Sesli Kitap (MP3, M4A, WAV, ZIP vb.) okuma uygulaması.

**Geliştirici:** Ömer Göktaş ([omergoktas.net](https://omergoktas.net) — mail@omergoktas.net)

---

## 🌟 v2.0.0 ile Gelen Başlıca Yenilikler

- **📄 DOC ve XLSX Belge Formatı Desteği:**
  - Eski Word (DOC - Word 97-2003 OLE2 / RTF / HTML uyumlu) belgeleri artık eksiksiz açılmaktadır.
  - Microsoft Excel (XLSX) tabloları sayfa sayfa taranıp ekran okuyucular için satır ve sütun formatında erişilebilir metne dönüştürülür.
- **🇦🇿 Azerbaycan Dili Desteği (Çevrim İçi & Açık Kaynak Çevrim Dışı):**
  - **Microsoft Edge TTS:** Azerbaycan dilinde Babek Neural (Erkek) ve Banu Neural (Kadın) sesleri eklendi.
  - **Meta MMS TTS (Açık Kaynak Çevrim Dışı):** Meta'nın açık kaynaklı çok dilli MMS projesinin Azerbaycan dili nöral modeli yerel olarak entegre edildi; internetsiz çalışır.
- **⌨️ Tam Özelleştirilebilir Kısayol Yönetimi:** 35+ kısayolun tamamı klavye alışkanlığınıza göre değiştirilebilir, çakışmalar otomatik algılanır ve tek tıkla varsayılana sıfırlanabilir (`Ctrl + Virgül` -> Klavye Kısayolları).
- **⏱️ Uyku Zamanlayıcısı (Sleep Timer):** İster sesli kitap ister metin okurken `Ctrl + T` ile zamanlayıcı kurabilir, kalan süreyi sesli anonslarla öğrenebilirsiniz.
- **💾 Ayarları Yedekleme ve Geri Yükleme:** Tüm ayarlarınızı ve kısayol haritanızı tek tıkla JSON dosyasına yedekleyebilir ve dilediğiniz bilgisayarda geri yükleyebilirsiniz.
- **🎙️ Açık Kaynak Çevrim Dışı Nöral Ses (Piper - Fahrettin):** İnternet bağlantısı gerektirmeyen, tamamen yerel yüksek kaliteli Türkçe yapay zeka sesi.
- **📑 Gelişmiş Metin ve PDF Reflow Motoru:** Paragraf reflow motoru ile çift katmanlı PDF gölge yazıları ayıklandı, satır tekrarları giderildi ve ekran okuyucu imleciyle kusursuz uyum sağlandı.
- **🎯 Standart Kelime Gezinmesi:** Metin alanında `Ctrl + Sol / Sağ Ok` artık standart kelime kelime gezinme yapar; sayfa geçişleri `PageDown / PageUp` ve `Ctrl + PageDown / PageUp` tuşlarına taşındı.
- **🛑 Tek Tuşla Susturma:** Sesli okuma sırasında tek başına `Ctrl` tuşuna basarak anında duraklatma / susturma desteği.

---

## 🚀 Kurulum ve Çalıştırma

- **Resmi Kurulum Paketi (Tavsiye Edilen):** Inno Setup ile hazırlanan resmi kurulum paketi `Program Files` altına temiz kurulum yapar ve Başlat/Masaüstü kısayollarını hazırlar.
- **Bağımsız Çalışma:** %100 Self-Contained mimarisi sayesinde harici .NET kurulumuna ihtiyaç duymaz.

---

## ⌨️ Temel Klavye Kısayolları

| Kısayol | İşlev |
| :--- | :--- |
| **Aşağı / Yukarı Ok** | Satır satır okuma (sayfa başında/sonunda sayfa değiştirir) |
| **Ctrl + Sol / Sağ Ok** | Metin içinde kelime kelime gezinme |
| **Ctrl + Yukarı / Aşağı Ok** | Önceki / sonraki paragrafa geçiş |
| **PageDown / PageUp** | Sonraki / önceki sayfaya geç (veya Ctrl+PageDown/Up) |
| **Boşluk Tuşu** | Kesintisiz sesli okumayı başlat / duraklat |
| **Ctrl Tuşu (Tek)** | Sesli okumayı anında duraklat / sustur |
| **Alt + Sağ / Sol Ok** | Sesli okumada sonraki / önceki cümleye atla |
| **Ctrl + Enter** | Bulunulan satırdan itibaren sürekli sesli oku |
| **Enter** | Yalnızca bulunulan satırı seslendir / Seçili kitabı aç |
| **Ctrl + O** | Bilgisayardan kitap ekle |
| **Ctrl + U** | Web bağlantısından makale ekle |
| **Ctrl + Shift + V** | Panodaki metni kitap olarak ekle |
| **Ctrl + Virgül** | Ayarlar penceresini aç |
| **F1** | Kullanım kılavuzu ve kısayollar penceresi |
| **Ctrl + T** | Uyku zamanlayıcısı kur (Metin veya Sesli Kitap) |
| **Ctrl + N / Ctrl + B** | Yer imi ve not / alıntı ekle |
| **Ctrl + Shift + B** | Yer imleri ve alıntılar listesi |
| **Ctrl + F** | Belge içinde ara |
| **Ctrl + G** | Sayfa numarasına git |
| **Ctrl + Shift + Yukarı / Aşağı Ok** | Sesli okuma hızını artır / azalt |
| **Ctrl + Shift + Sağ / Sol Ok** | Sesli okuma ses düzeyini artır / azalt (veya Alt+Yukarı/Aşağı) |
| **Esc** | Önceki ekrana dön / Kitaplığa geç |

---

## 🛠️ Kaynak Koddan Derleme

- **Gereksinim:** .NET 8.0 SDK (Windows x64)
- **Komut:**
```powershell
dotnet publish ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```
