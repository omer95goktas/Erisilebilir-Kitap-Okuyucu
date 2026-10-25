# Erişilebilir Kitap Okuyucu — v1.0.1 Geliştirme Planı ve Çalışma Protokolü

**Tarih:** 25 Ekim 2026  
**Hedef Sürüm:** `v1.0.1`  
**Geliştirici:** Ömer Göktaş (omergoktas.net)

---

## 🔒 KATI ÇALIŞMA VE TEST KURALI (ŞART)

> **Önce Yerel Kurulum ve Test Onayı:**
> 1. v1.0.1 için tüm geliştirmeler tamamlandıktan sonra **önce yerel kurulum dosyası (`ErisilebilirKitapOkuyucu_Kurulum.exe`) derlenip doğrudan masaüstüne teslim edilecektir.**
> 2. Kullanıcı (Ömer Göktaş) ve test kullanıcısı bilgisayarlarında bizzat kurulumu yapıp özellikleri deneyecektir.
> 3. **Kullanıcı açıkça onay verene kadar ("Tamamdır, onaylıyorum") Git ve GitHub tarafına KESİNLİKLE hiçbir commit atılmayacak, tag açılmayacak ve release yüklenmeyecektir.**

---

## 📋 v1.0.1 Kapsamında Eklenecek Kesinleşen Özellikler

### 1. Paragraf Paragraf Okuma Desteği
- **Klavye Kontrolü:**
  - `Ctrl + Aşağı Ok`: Sonraki paragrafa geçiş ve odaklanma.
  - `Ctrl + Yukarı Ok`: Önceki paragrafa geçiş ve odaklanma.
  - Normal `Aşağı / Yukarı Ok`: Mevcut satır satır okuma işlevini aynen korur.
- **Okuma Modu Tercihi:** Reader ekranında ve Ayarlar'da "Satır Satır" veya "Paragraf Paragraf" varsayılan gezinme seçeneği.
- **TTS Paragraf Okuma:** Sesli okuma yaparken metni duraksamadan paragraf blokları halinde akıcı seslendirebilme.

### 2. Gelişmiş TTS Mimarisi (Çevrim Dışı AI & Çevrim İçi Doğal Ses Motorları)

- **A. Çevrim Dışı (Yerel / Offline) Sesler:**
  1. **Klasik Sistem Sesleri:** Windows OneCore (Microsoft Tolga vb.) ve kurulu SAPI 5 sesleri. (Sıfır gecikme, internet gerektirmez).
  2. **Çevrimdışı Yapay Zeka Sesi (Offline Neural AI):**
     - **Motor:** `sherpa-onnx` (.NET C# tümleşik motoru).
     - **Model:** Meta Türkçe MMS VITS Modeli (`vits-mms-tur` — `model.onnx` ve `tokens.txt`).
     - **İlk Kullanımda Otomatik İndirme:** Model dosyaları `%AppData%\ErisilebilirKitapOkuyucu\models\vits-mms-tur\` dizininde yoksa, ilk kullanımda arka planda indirilir.
     - **%100 Çevrimdışı:** İndirildikten sonra internetsiz CPU üzerinde tamamen yerel çalışır.
     - **Sayfayı Sese Çevir (WAV Kaydet):** İstenen sayfayı/kitabı ayrı bir iş parçacığında (Thread) arayüzü dondurmadan sese çevirip dosyaya kaydetme.

- **B. Çevrim İçi (Bulut / Online) Doğal Ses Motorları:**
  1. **Microsoft Doğal Sesleri (Edge TTS - Ücretsiz & Anahtarsız):**
     - `tr-TR-AhmetNeural` (Erkek) ve `tr-TR-EmelNeural` (Kadın).
     - Stüdyo kalitesinde, son derece akıcı ve insansı tonlama.
  2. **Google Doğal Sesleri (Google TTS):**
     - Hızlı, net ve anlaşılır Türkçe telaffuz altyapısı (kullanıcı dostu, pratik çevrim içi akış).
  - **Akıllı Fallback:** Çevrim içi sesle okurken bağlantı koptuğunda veya yavaşladığında program sessizliğe gömülmeden otomatik olarak yerel sistem sesine (Tolga'ya) geçiş yapar.

### 3. ⚖️ Üçüncü Taraf Servisler, API Anahtarları ve Mali Sorumluluk Reddi (Disclaimer)
1. **Kişisel API Anahtarı ve Mali Sorumluluk:**  
   Uygulama içerisinde kullanıcı tarafından isteğe bağlı olarak girilen tüm üçüncü taraf API anahtarları (Google Cloud TTS vb.) tamamen kullanıcının kendi sorumluluğundadır. İlgili servis sağlayıcıları tarafından belirlenen kotaların aşılması veya faturalandırma sebebiyle oluşabilecek **hiçbir maddi borç, ücret veya mali yükümlülükten uygulama geliştiricisi (Ömer Göktaş) sorumlu tutulamaz.** Kullanıcı, API anahtarını girerek servis sağlayıcının şartlarını kabul etmiş sayılır.
2. **Yerel Güvenlik:** Kullanıcının girdiği API anahtarları hiçbir harici sunucuya iletilmez, yalnızca kullanıcının kendi bilgisayarında yerel saklanır.
3. **Kesintisiz Güvence:** Uygulamanın temel okuma özellikleri ve yerel sesleri (Microsoft Tolga ve yerel Yapay Zeka motoru) hiçbir API anahtarı, internet veya ücret gerektirmeden tamamen ücretsiz ve sınırsız çalışır.

### 4. Akıllı Üzerine Güncelleme (In-Place Update)
- v1.0.0 üzerine v1.0.1 doğrudan kurulur; kullanıcının mevcut kitapları, yer imleri, kaldığı yerler ve ayarları `%AppData%` içinde **%100 korunur**.
- `Setup.cs` kurulu eski sürümü tanıyıp butonda **"Güncellemeyi Yükle"** metnini gösterir.
- Açık olan eski sürümü otomatik ve nazikçe kapatıp dosyaları kilitlenme olmadan günceller.
