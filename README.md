# Erişilebilir Kitap Okuyucu (Windows) - v1.0.0

Görme engelli kullanıcılar ve ekran okuyucu kullanıcıları (NVDA, JAWS, Windows Ekran Okuyucusu) için özel olarak geliştirilmiş; PDF, EPUB, Word (DOCX), Düz Metin (TXT) ve Sesli Kitap (MP3, M4A, WAV, ZIP vb.) okuma uygulaması.

**Geliştirici:** Ömer Göktaş ([omergoktas.net](https://omergoktas.net) — mail@omergoktas.net)

---

## 🚀 Doğrudan İndir (Kurulum Gerektirmez)

Uygulama tek parça çalıştırılabilir dosya (`.exe`) olarak derlenmiştir. Herhangi bir kurulum gerektirmez, indirip doğrudan açabilirsiniz:

* **[📥 Erişilebilir Kitap Okuyucu v1.0.0 İndir (ErisilebilirKitapOkuyucu.exe)](https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu/releases/download/v1.0.0/ErisilebilirKitapOkuyucu.exe)**

---

## 🌟 Öne Çıkan Özellikler

- **Ekran Okuyucu Dostu:** NVDA, JAWS ve Windows Ekran Okuyucusu ile %100 uyumlu. Tüm düğmeler, listeler, sayfa içerikleri ve diyaloglar için net erişilebilirlik etiketleri.
- **Zengin Format Desteği:**
  - **PDF:** Metin tabanlı ve taranmış PDF desteği.
  - **Word (.docx):** Satır satır okuma, başlık ve paragraf hiyerarşisi.
  - **EPUB:** Bölüm gezintisi ve temizlenmiş kitap metni.
  - **TXT:** UTF-8 ve Windows-1254 (Türkçe) otomatik kodlama tespiti.
  - **Web Makaleleri:** URL girilerek web makalelerini reklamsız kitap olarak ekleme.
  - **Sesli Kitaplar (MP3, M4A, WAV, ZIP):** Duraklatma, ileri/geri sarma, hız ayarı, uyku zamanlayıcısı.
- **Akıllı Metin Okuma (TTS):**
  - Windows OneCore (Tolga vb. modern doğal sesler) ve SAPI 5 desteği.
  - Cümle cümle ve satır satır okuma modu.
  - Hız, perde ve ses düzeyi ayarları.
  - Metni ses dosyasına (WAV) dönüştürerek dışa aktarma.
- **Kişiselleştirilebilir Görünüm (Az Görenler İçin):**
  - Yazı boyutu büyütme/küçültme.
  - 4 farklı yüksek kontrastlı tema (Siyah üzerine Sarı, Siyah üzerine Beyaz, Beyaz üzerine Siyah, Sarı üzerine Siyah).
- **Yerel ve Güvenli:** Tüm kitaplarınız, okuma konumlarınız ve ayarlarınız yalnızca bilgisayarınızda yerel olarak saklanır. İnternete hiçbir belgeniz yüklenmez.

---

## ⌨️ Temel Klavye Kısayolları

| Kısayol | İşlev |
| :--- | :--- |
| **Aşağı / Yukarı Ok** | Kitaplar veya satırlar arasında gezinme |
| **Enter** | Seçili kitabı aç veya işlemi onayla |
| **Tab / Shift + Tab** | Kontroller ve menüler arasında dolaşma |
| **F6** | Kitap listesi ile Araç çubuğu arasında hızlı geçiş |
| **Ctrl + O** | Bilgisayardan kitap ekle |
| **Ctrl + U** | Web bağlantısından makale ekle |
| **Ctrl + Shift + V** | Panodaki metni kitap olarak ekle |
| **Ctrl + Virgül** | Ayarlar penceresini aç |
| **F1** | Yardım ve kısayollar kılavuzu |
| **Ctrl + Enter** | Belgede bulunulan satırdan sesli okumayı başlat |
| **Boşluk Tuşu** | Sesli okumayı / sesli kitabı duraklat veya devam ettir |
| **Ctrl + Sol / Sağ Ok** | Sesli kitapta 5 saniye geri / ileri sar |
| **Ctrl + Yukarı / Aşağı Ok** | Sesli okuma hızını artır / azalt |
| **Esc** | Önceki sayfaya veya kitaplığa dön |

---

## 🛠️ Kaynak Koddan Derleme

- **Gereksinim:** .NET 8.0 SDK (Windows x64)
- **Komut:**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
Derlenen tek parça dosya `publish/ErisilebilirKitapOkuyucu.exe` konumunda oluşur.
