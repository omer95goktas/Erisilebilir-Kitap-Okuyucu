# ===================================================================
# ERİŞİLEBİLİR KİTAP OKUYUCU - WINDOWS OTOMATİK DERLEME VE PAKETLEME
# ===================================================================
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File windows\build-windows.ps1 [-Version "1.0.0"]
#
# Bu betik:
#   1. .NET 8 WPF uygulamasını win-x64 için publish_folder içine derler.
#   2. Uygulama paketini installer\payload.zip olarak sıkıştırır.
#   3. csc.exe ile Setup.cs'i derleyip resmi kurulum sihirbazını oluşturur (Yayıncı: omergoktas.net).
#   4. Kullanıcılar için temiz bir ZIP paketi (Kurulum + Türkçe Kılavuz) hazırlar.
#   5. Tüm dağıtım dosyalarını windows\release_artifacts\ dizinine yerleştirir.
# ===================================================================

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$rootDir = (Get-Item $scriptDir).Parent.FullName

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  Erişilebilir Kitap Okuyucu v$Version Derleme Başlıyor  " -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

# 1. dotnet CLI yolunu tespit et
$dotnet = "dotnet"
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
}

# 2. csc.exe yolunu tespit et (.NET Framework 4.8 / 64-bit)
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "csc.exe bulunamadı (.NET Framework 4.x gereklidir)."
}

$projPath = Join-Path $scriptDir "ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj"
$publishFolder = Join-Path $scriptDir "publish_folder"
$installerDir = Join-Path $scriptDir "installer"
$artifactsDir = Join-Path $scriptDir "release_artifacts"

if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

# 3. .NET 8 Uygulamasını Derle
Write-Host "[1/4] .NET 8 masaüstü uygulaması derleniyor..." -ForegroundColor Yellow
if (Test-Path $publishFolder) { Remove-Item -Path $publishFolder -Recurse -Force }

& $dotnet publish $projPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishFolder
if ($LASTEXITCODE -ne 0) { throw ".NET Publish başarısız oldu." }

# Çevrim dışı yapay zeka ses modelini (Piper / Fahrettin FP32) dağıtım paketine dahil et
$sourceModels = "C:\Users\gokta\.gemini\antigravity\scratch\models"
$destModels = Join-Path $publishFolder "models"
if (Test-Path $sourceModels) {
    Write-Host "Çevrim dışı yüksek kaliteli ses modeli (Fahrettin FP32) kopyalanıyor..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $destModels -Force | Out-Null
    Copy-Item (Join-Path $sourceModels "vits-piper-tr_TR-fahrettin-medium") $destModels -Recurse -Force
}

# 4. payload.zip Oluştur
Write-Host "[2/4] Uygulama paketi (payload.zip) sıkıştırılıyor..." -ForegroundColor Yellow
$payloadZip = Join-Path $installerDir "payload.zip"
if (Test-Path $payloadZip) { Remove-Item -Path $payloadZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishFolder, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# 5. Setup.cs'i csc.exe ile derle
Write-Host "[3/4] Kurulum sihirbazı (ErisilebilirKitapOkuyucu_Kurulum.exe) derleniyor..." -ForegroundColor Yellow
$setupOut = Join-Path $artifactsDir "ErisilebilirKitapOkuyucu_Kurulum.exe"
$setupManifest = Join-Path $installerDir "setup.manifest"
$setupSource = Join-Path $installerDir "Setup.cs"

& $csc /target:winexe /out:"$setupOut" /win32manifest:"$setupManifest" `
    /resource:"$payloadZip",payload.zip `
    /r:System.Windows.Forms.dll,System.Drawing.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll,Microsoft.CSharp.dll `
    "$setupSource"
if ($LASTEXITCODE -ne 0) { throw "Setup.cs derlemesi başarısız oldu." }

# windows/ köküne de güncel kurulum dosyasını kopyala
Copy-Item $setupOut (Join-Path $scriptDir "ErisilebilirKitapOkuyucu_Kurulum.exe") -Force

# Eski bağlantılardan gelenler için ErisilebilirKitapOkuyucu.exe adıyla da bir kopya hazırla
Copy-Item $setupOut (Join-Path $artifactsDir "ErisilebilirKitapOkuyucu.exe") -Force

# 6. Dağıtım ZIP Paketini Hazırla
Write-Host "[4/4] Dağıtım ZIP paketi hazırlanıyor..." -ForegroundColor Yellow
$zipStaging = Join-Path $artifactsDir "zip_staging"
if (Test-Path $zipStaging) { Remove-Item -Path $zipStaging -Recurse -Force }
New-Item -ItemType Directory -Path $zipStaging -Force | Out-Null

Copy-Item $setupOut (Join-Path $zipStaging "ErisilebilirKitapOkuyucu_Kurulum.exe") -Force

$readmeText = @"
===================================================================
      ERİŞİLEBİLİR KİTAP OKUYUCU - WINDOWS SÜRÜMÜ (v$Version)
===================================================================
Geliştirici / Yayıncı : omergoktas.net (Ömer Göktaş)
Web Sitesi            : https://omergoktas.net
GitHub Proje Deposu   : https://github.com/omer95goktas/Erisilebilir-Kitap-Okuyucu
İletişim & Destek     : mail@omergoktas.net

KOLAY KURULUM ADIMLARI:
-------------------------------------------------------------------
1. Bu arşivdeki 'ErisilebilirKitapOkuyucu_Kurulum.exe' dosyasını çalıştırın.
2. Windows Defender / SmartScreen ilk kurulumda "Windows kişisel bilgisayarınızı korudu" 
   uyarısı verirse "Ek bilgi" seçeneğine tıklayıp "Yine de çalıştır" butonuna basın.
   (Yayıncı: omergoktas.net). Bu güvenlik onayı yalnızca ilk kuruluma özel 1 defa istenir.
3. Açılan kurulum sihirbazında "Kurulumu Başlat" butonuna tıklayın.
4. Birkaç saniye içinde kurulum tamamlanacak, masaüstünüze ve Başlat menünüze 
   'Erişilebilir Kitap Okuyucu' simgesi eklenecektir.
5. Kurulum bittikten sonra uygulamayı her zaman masaüstündeki simgeden hiçbir uyarı 
   ve gecikme olmadan doğrudan açıp kullanabilirsiniz.

Keyifli ve engelsiz okumalar dileriz!
===================================================================
"@
[System.IO.File]::WriteAllText((Join-Path $zipStaging "KURULUM_KILAVUZU.txt"), $readmeText, [System.Text.Encoding]::UTF8)

$releaseZip = Join-Path $artifactsDir "ErisilebilirKitapOkuyucu_v$Version.zip"
if (Test-Path $releaseZip) { Remove-Item -Path $releaseZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($zipStaging, $releaseZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -Path $zipStaging -Recurse -Force

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host "  DERLEME VE PAKETLEME BAŞARIYLA TAMAMLANDI!          " -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
Write-Host "Üretilen Dağıtım Dosyaları ($artifactsDir):" -ForegroundColor Cyan
Get-ChildItem -Path $artifactsDir -File | Format-Table Name, Length -AutoSize
