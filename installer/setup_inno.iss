; ===================================================================
; Erişilebilir Kitap Okuyucu - Resmi Inno Setup Kurulum Senaryosu
; Standart, güvenli, %100 erişilebilir Windows kurulum paketi
; ===================================================================

#define MyAppName "Erişilebilir Kitap Okuyucu"
#define MyAppVersion "1.0.10"
#define MyAppPublisher "omergoktas.net"
#define MyAppURL "https://omergoktas.net"
#define MyAppExeName "ErisilebilirKitapOkuyucu.exe"

[Setup]
; Temel Uygulama Tanımlamaları
AppId={{9F8D34B8-B582-4E4D-9345-9856AB762C99}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Yönetici yetkisi ile Program Files kurulumu
PrivilegesRequired=admin
DefaultDirName={autopf}\Omer_Goktas_Net\Erisilebilir Kitap Okuyucu
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Çıktı Ayarları
OutputDir=..\release_artifacts
OutputBaseFilename=ErisilebilirKitapOkuyucu
Compression=lzma2/max
SolidCompression=yes

; Görünüm ve Erişilebilirlik
WizardStyle=modern
SetupIconFile=app.ico
DisableDirPage=yes
DisableReadyPage=yes
AlwaysShowDirOnReadyPage=no
CloseApplications=no
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64

; Türkçe Dil Desteği
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek Kısayollar:"

[Files]
; Derlenmiş uygulamanın tüm dosyalarını dahil et
Source: "..\publish_folder\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#MyAppName} Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Kurulum bittiğinde isteğe bağlı veya doğrudan başlatma
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} uygulamasını başlat"; Flags: nowait postinstall

[Code]
// Kurulum başlamadan önce varsa eski açık uygulamayı güvenle kapat
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/F /IM ErisilebilirKitapOkuyucu.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;
