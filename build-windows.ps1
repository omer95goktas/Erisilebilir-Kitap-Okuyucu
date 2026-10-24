# Windows sürümünü tek dosyalık, kendi kendine yeten (self-contained) .exe olarak derler.
# Kullanım:  powershell -ExecutionPolicy Bypass -File windows\build-windows.ps1 [-Output "C:\hedef\klasor"]
# Gereksinim: .NET 8 SDK  (https://dotnet.microsoft.com/download/dotnet/8.0)
param(
    [string]$Output = (Join-Path ([Environment]::GetFolderPath('Desktop')) '')
)
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'ErisilebilirKitapOkuyucu.Windows\ErisilebilirKitapOkuyucu.Windows.csproj'
$publish = Join-Path $PSScriptRoot 'publish'

dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }

$exe = Join-Path $publish 'ErisilebilirKitapOkuyucu.exe'
if ($Output) {
    New-Item -ItemType Directory -Force $Output | Out-Null
    Copy-Item $exe (Join-Path $Output 'ErisilebilirKitapOkuyucu.exe') -Force
    Write-Host "Teslim edildi: $(Join-Path $Output 'ErisilebilirKitapOkuyucu.exe')"
}
