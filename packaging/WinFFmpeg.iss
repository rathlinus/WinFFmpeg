; The WinFFmpeg setup program, compiled by scripts\release.ps1 with Inno Setup:
;
;   ISCC /DAppVersion=1.2.0 /DSourceDir=<artifacts> /DOutputDir=<artifacts\release> WinFFmpeg.iss
;
; WinFFmpeg is an MSIX package, which Windows itself installs, updates and removes.
; Setup is only the front door to that: it unpacks the package, the certificate and
; Install.ps1 to a temporary folder and runs the script, the same one the zip download
; ships. It copies nothing else and registers no uninstaller of its own, so the app
; shows up once under Settings > Apps and is removed there.

#ifndef AppVersion
  #error Pass the version with /DAppVersion=<major.minor.patch>
#endif
#ifndef SourceDir
  #error Pass the folder with the signed package and the certificate with /DSourceDir=<path>
#endif
#ifndef OutputDir
  #define OutputDir SourceDir
#endif

[Setup]
AppId={{B0C6A6F4-6E0F-4B67-9D0E-3A0C6E5B7C21}
AppName=WinFFmpeg
AppVersion={#AppVersion}
AppVerName=WinFFmpeg {#AppVersion}
AppPublisher=WinFFmpeg
AppPublisherURL=https://github.com/rathlinus/WinFFmpeg
AppSupportURL=https://github.com/rathlinus/WinFFmpeg/issues
VersionInfoVersion={#AppVersion}
VersionInfoDescription=WinFFmpeg Setup

OutputDir={#OutputDir}
OutputBaseFilename=WinFFmpeg-{#AppVersion}-Setup
SetupIconFile=..\src\WinFFmpeg.App\Assets\AppIcon.ico
WizardStyle=modern
WizardImageFile=installer-side.bmp
WizardSmallImageFile=installer-small.bmp

; Setup itself runs as the user who started it, because an MSIX package is installed
; per user. Only trusting the certificate needs an administrator, and Install.ps1 asks
; for that on its own when it is needed.
PrivilegesRequired=lowest
CreateAppDir=no
Uninstallable=no

DisableWelcomePage=no
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
MinVersion=10.0.22000
; The package is already compressed; packing it again only costs time.
Compression=none

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This installs [name/ver] on your computer.%n%nWinFFmpeg is signed with its own certificate. If this computer does not trust that certificate yet, Windows asks for administrator permission once to add it.%n%nWinFFmpeg converts with ffmpeg, which has to be installed separately.
FinishedHeadingLabel=WinFFmpeg is installed
FinishedLabelNoIcons=Right-click a video, audio or image file in Explorer and choose Convert with WinFFmpeg.%n%nTo remove WinFFmpeg later, open Settings, go to Apps, then Installed apps.
ClickFinish=Click Finish to close Setup.
; The welcome page is the only one with this button, and pressing it starts the installation.
ButtonNext=&Install
ClickNext=Click Install to continue, or Cancel to exit Setup.

[Files]
; "dontcopy": these are unpacked to the temporary folder from the code below, not installed.
Source: "{#SourceDir}\WinFFmpeg-signed.msix"; DestName: "WinFFmpeg.msix"; Flags: dontcopy
Source: "{#SourceDir}\WinFFmpeg.cer"; Flags: dontcopy
Source: "Install.ps1"; Flags: dontcopy

[Run]
; Started through its command alias, which Windows creates with the package.
Filename: "{cmd}"; Parameters: "/c start """" winffmpeg.exe"; Description: "Open WinFFmpeg"; Flags: postinstall nowait runhidden skipifsilent

[Code]
// Runs Install.ps1 and waits for it. Returns the reason when it failed, or an empty
// string. The script runs through a small batch file that records its output and its
// exit code, so Setup can keep its window responsive while it waits and can show
// PowerShell's own error message if the installation fails.
function InstallPackage(Page: TOutputMarqueeProgressWizardPage): String;
var
  Folder, ExitFile: String;
  Text: AnsiString;
  ResultCode: Integer;
begin
  Result := '';
  Folder := ExpandConstant('{tmp}');
  ExitFile := Folder + '\install.exit';

  SaveStringToFile(Folder + '\install.cmd',
    '@echo off' + #13#10 +
    'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" > "%~dp0install.log" 2>&1' + #13#10 +
    // The redirection comes first on purpose: "echo 0> file" would read "0>" as a redirection.
    '> "%~dp0install.exit" echo %errorlevel%' + #13#10, False);

  if not Exec(ExpandConstant('{cmd}'), '/c ""' + Folder + '\install.cmd""', Folder, SW_HIDE, ewNoWait, ResultCode) then
  begin
    Result := 'Setup could not start the installation: ' + SysErrorMessage(ResultCode);
    Exit;
  end;

  while not FileExists(ExitFile) do
  begin
    Page.Animate;
    Sleep(50);
  end;

  // The file appears a moment before its content is written.
  Sleep(200);
  if LoadStringFromFile(ExitFile, Text) and (Trim(String(Text)) = '0') then
    Exit;

  Result := 'WinFFmpeg could not be installed.';
  if LoadStringFromFile(Folder + '\install.log', Text) and (Trim(String(Text)) <> '') then
    Result := Result + #13#10 + #13#10 + Trim(String(Text));
end;

// Everything happens here and not in the usual install step, because this is the one
// place where Setup can stop with an error message instead of reporting success.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Page: TOutputMarqueeProgressWizardPage;
begin
  Page := CreateOutputMarqueeProgressPage('Installing WinFFmpeg', 'Please wait while Setup installs WinFFmpeg on your computer.');
  Page.Show;
  try
    Page.SetText('Unpacking...', '');
    Page.Animate;
    ExtractTemporaryFile('WinFFmpeg.msix');
    ExtractTemporaryFile('WinFFmpeg.cer');
    ExtractTemporaryFile('Install.ps1');

    Page.SetText('Installing the app...', 'Windows may ask for permission to trust the certificate.');
    Result := InstallPackage(Page);
  finally
    Page.Hide;
  end;
end;
