# ===================================================================
# Erişilebilir Kitap Okuyucu - Resmi Windows Derleme & Paketleme Betiği
# KURAL: Her zaman %100 Self-Contained (.NET dahili) derlenir!
# ===================================================================

param (
    [switch]$SkipInno = $false,
    [switch]$SkipMsix = $false,
    [switch]$CopyToDesktop = $false
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$proj = Join-Path $rootDir "ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj"
$publishFolder = Join-Path $rootDir "publish_folder"
$msixStaging = Join-Path $rootDir "msix_staging"
$releaseArtifacts = Join-Path $rootDir "release_artifacts"
$innoScript = Join-Path $rootDir "installer\setup_inno.iss"

# 1. Ortam ve .NET SDK Hazırlığı
$env:DOTNET_ROOT = "C:\Users\gokta\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

# Sürüm tespiti
[xml]$projXml = Get-Content $proj
$appVersion = $projXml.Project.PropertyGroup.Version
if (-not $appVersion) { $appVersion = "1.0.10.0" }
$shortVersion = $appVersion.Substring(0, $appVersion.LastIndexOf('.'))
Write-Host ">>> Derlenen Sürüm: $appVersion ($shortVersion)" -ForegroundColor Cyan

# 2. Dotnet Publish (ZORUNLU Self-Contained)
Write-Host ">>> [1/4] .NET 8 Release (Self-Contained win-x64) derleniyor..." -ForegroundColor Green
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishFolder

# 3. KESİN KURAL KONTROLÜ: runtimeconfig.json kontrolü
$runtimeConfig = Join-Path $publishFolder "ErisilebilirKitapOkuyucu.runtimeconfig.json"
if (-not (Test-Path $runtimeConfig)) {
    Write-Error "HATA: $runtimeConfig bulunamadı!"
    exit 1
}
$configContent = Get-Content $runtimeConfig -Raw
if ($configContent -notmatch "includedFrameworks") {
    Write-Error "KRİTİK HATA: Derleme 'Self-Contained' değil! includedFrameworks eksik. Paketleme iptal edildi!"
    exit 1
}
Write-Host ">>> [Doğrulama Başarılı] .NET çalışma zamanı pakete başarıyla gömüldü (includedFrameworks onaylandı)." -ForegroundColor Green

# 4. MSIX Staging Güncelleme
Write-Host ">>> [2/4] MSIX hazırlık klasörü (msix_staging) güncelleniyor..." -ForegroundColor Green
robocopy $publishFolder $msixStaging /E /XF AppxManifest.xml | Out-Null
if ($LASTEXITCODE -gt 7) {
    Write-Error "Robocopy hatası: $LASTEXITCODE"
    exit 1
}

# 5. MSIX Paketleme
if (-not $SkipMsix) {
    Write-Host ">>> [3/4] Microsoft Store MSIX paketi üretiliyor..." -ForegroundColor Green
    if (-not (Test-Path $releaseArtifacts)) { New-Item -ItemType Directory -Path $releaseArtifacts -Force | Out-Null }
    $outMsix = Join-Path $releaseArtifacts "ErisilebilirKitapOkuyucu_v$shortVersion.msix"
    if (Test-Path $outMsix) { Remove-Item $outMsix -Force }
    
    MSIXHeroCLI.exe pack -d "$msixStaging" -p "$outMsix"
    if (-not (Test-Path $outMsix)) {
        Write-Error "MSIXHeroCLI paket oluşturamadı!"
        exit 1
    }
    $msixSize = (Get-Item $outMsix).Length / 1MB
    Write-Host ">>> MSIX Paketi Hazır: $outMsix ($([math]::Round($msixSize, 2)) MB)" -ForegroundColor Green

    if ($CopyToDesktop) {
        $desktops = @("C:\Users\gokta\OneDrive\Desktop", "C:\Users\gokta\Desktop")
        foreach ($d in $desktops) {
            if (Test-Path $d) {
                Copy-Item $outMsix (Join-Path $d "ErisilebilirKitapOkuyucu_v$shortVersion.msix") -Force
                Write-Host ">>> Masaüstüne kopyalandı: $d" -ForegroundColor Yellow
            }
        }
    }
}

# 6. Inno Setup Derleme (.exe)
if (-not $SkipInno) {
    Write-Host ">>> [4/4] Inno Setup yükleyicisi derleniyor..." -ForegroundColor Green
    $iscc = "C:\Users\gokta\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
    & $iscc $innoScript
    $outExe = Join-Path $releaseArtifacts "ErisilebilirKitapOkuyucu.exe"
    if (Test-Path $outExe) {
        $exeSize = (Get-Item $outExe).Length / 1MB
        Write-Host ">>> Inno Setup Kurulum Exe Hazır: $outExe ($([math]::Round($exeSize, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Error "Inno Setup derlemesi başarısız oldu!"
        exit 1
    }
}

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " Windows Derleme ve Paketleme Süreci Başarıyla Bitti!" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
