#define ProductVersion "1.0.5"
#define SourceRoot ".."

[Setup]
AppId={{D71D78D5-F131-4C08-B612-1E5F78DB4DB7}
AppName=Timezone Switcher
AppVersion={#ProductVersion}
AppPublisher=Timezone Switcher
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=Timezone Switcher
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
AllowNoIcons=yes
DisableProgramGroupPage=yes
OutputDir=..\..
#ifndef TestBuild
OutputBaseFilename=TimezoneSwitcher-Setup-1.0.5
#endif
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=TimezoneSwitcher.exe
RestartApplications=no
UninstallDisplayIcon={app}\TimezoneSwitcher.exe
SetupIconFile=..\assets\timezone.ico
#ifdef TestBuild
Uninstallable=no
CreateUninstallRegKey=no
OutputBaseFilename=TimezoneSwitcher-Setup-Test
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
Name: "autostart"; Description: "Запускать Timezone Switcher при входе в Windows"; Flags: unchecked

[Files]
; Dependency is installed first, only if absent. It remains independent on uninstall.
Source: "{tmp}\kordon-download.exe"; DestDir: "{code:KordonDir}"; DestName: "kordon.exe"; Flags: external ignoreversion uninsneveruninstall; Check: NeedKordon
Source: "{#SourceRoot}\TimezoneSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\src\CLDR-LICENSE.txt"; DestDir: "{app}\Licenses"; Flags: ignoreversion
Source: "{#SourceRoot}\verification\*"; DestDir: "{app}\verification"; Flags: ignoreversion

#ifndef TestBuild
[Icons]
Name: "{group}\Timezone Switcher"; Filename: "{app}\TimezoneSwitcher.exe"
Name: "{autodesktop}\Timezone Switcher"; Filename: "{app}\TimezoneSwitcher.exe"; Tasks: desktopicon
Name: "{userprograms}\Kordon"; Filename: "{code:KordonExe}"; Flags: uninsneveruninstall; Check: NeedKordon

[Run]
Filename: "{code:KordonExe}"; Description: "Запустить Kordon (при первом запуске потребуется его настройка)"; Flags: postinstall nowait skipifsilent unchecked; Check: CanRunKordon
Filename: "{app}\TimezoneSwitcher.exe"; Description: "Запустить Timezone Switcher"; Flags: postinstall nowait skipifsilent unchecked
#endif

[Code]
var
  KordonBanner: TPanel;
  KordonCheck: TNewCheckBox;
  KordonTitle, KordonDescription, KordonNote: TNewStaticText;
  DownloadPage: TDownloadWizardPage;
  InstallKordon: Boolean;
  ExistingKordon: String;

function DefaultInstallDir(Param: String): String;
begin
  if DirExists('G:\') then Result := 'G:\Programs\TimezoneSwitcher'
  else Result := ExpandConstant('{localappdata}\Programs\TimezoneSwitcher');
end;

function KordonDir(Param: String): String;
begin
#ifdef TestBuild
  Result := ExpandConstant('{param:DIR|}') + '\DependencyTest\Kordon';
#else
  Result := ExpandConstant('{localappdata}\Programs\Kordon');
#endif
end;

function KordonExe(Param: String): String;
begin
  if InstallKordon then Result := AddBackslash(KordonDir('')) + 'kordon.exe'
  else Result := ExistingKordon;
end;

function NeedKordon: Boolean;
begin
  Result := InstallKordon;
end;

function LooksLikeKordon(Path: String): Boolean;
var Name: String;
begin
  Name := Lowercase(ExtractFileName(Path));
  Result := FileExists(Path) and ((Name = 'kordon.exe') or
    (Name = 'kordon-windows-amd64.exe') or (Name = 'kordon-windows-arm64.exe'));
end;

function CanRunKordon: Boolean;
begin
  Result := LooksLikeKordon(KordonExe(''));
end;

function FindKordon: String;
var Locator, Services, Processes, Item: Variant; I: Integer; Path: String;
begin
  Result := ExpandConstant('{param:KORDONPATH|}');
  if LooksLikeKordon(Result) then Exit;
  Result := AddBackslash(KordonDir('')) + 'kordon.exe';
  if LooksLikeKordon(Result) then Exit;
  Result := '';
#ifdef TestBuild
  if ExpandConstant('{param:TESTMISSING|0}') = '1' then Exit;
#endif
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name=''kordon.exe'' OR Name=''kordon-windows-amd64.exe'' OR Name=''kordon-windows-arm64.exe''');
    for I := 0 to Processes.Count - 1 do begin
      Item := Processes.ItemIndex(I);
      if not VarIsNull(Item.ExecutablePath) then begin
        Path := Item.ExecutablePath;
        if LooksLikeKordon(Path) then begin Result := Path; Exit; end;
      end;
    end;
  except
    Log('Running Kordon could not be queried. User can select the existing executable.');
  end;
end;

procedure KordonSelectionChanged(Sender: TObject);
begin
  InstallKordon := KordonCheck.Checked and (ExistingKordon = '');
  if ExistingKordon <> '' then
    KordonNote.Caption := 'Kordon уже найден. Повторная установка не нужна.'
  else if KordonCheck.Checked then
    KordonNote.Caption := 'Официальная загрузка с GitHub, версия 1.4.2. Нужен интернет.'
  else
    KordonNote.Caption := 'Установится только переключатель. Kordon можно добавить позже.';
end;

procedure InitializeWizard;
begin
  ExistingKordon := FindKordon;
  InstallKordon := ExistingKordon = '';
  WizardForm.TasksList.Height := WizardForm.TasksList.Height - ScaleY(156);
  KordonBanner := TPanel.Create(WizardForm);
  KordonBanner.Parent := WizardForm.SelectTasksPage;
  KordonBanner.SetBounds(WizardForm.TasksList.Left, WizardForm.TasksList.Top + WizardForm.TasksList.Height + ScaleY(12), WizardForm.TasksList.Width, ScaleY(144));
  KordonBanner.BevelOuter := bvNone;
  KordonBanner.Color := $00ECF3E6;
  KordonTitle := TNewStaticText.Create(WizardForm);
  KordonTitle.Parent := KordonBanner;
  KordonTitle.SetBounds(ScaleX(14), ScaleY(10), KordonBanner.Width - ScaleX(28), ScaleY(22));
  KordonTitle.Caption := 'Kordon для автоматического режима';
  KordonTitle.Font.Style := [fsBold];
  KordonDescription := TNewStaticText.Create(WizardForm);
  KordonDescription.Parent := KordonBanner;
  KordonDescription.AutoSize := False;
  KordonDescription.WordWrap := True;
  KordonDescription.SetBounds(ScaleX(14), ScaleY(35), KordonBanner.Width - ScaleX(28), ScaleY(35));
  KordonDescription.Caption := 'Помогает определить выход в интернет. Без Kordon доступны часы и ручной выбор Москвы или Берлина.';
  KordonCheck := TNewCheckBox.Create(WizardForm);
  KordonCheck.Parent := KordonBanner;
  KordonCheck.SetBounds(ScaleX(14), ScaleY(76), KordonBanner.Width - ScaleX(28), ScaleY(22));
  KordonCheck.Caption := 'Установить Kordon';
  KordonCheck.Checked := InstallKordon;
  if ExpandConstant('{param:INSTALLKORDON|auto}') = '0' then KordonCheck.Checked := False;
  if (ExpandConstant('{param:INSTALLKORDON|auto}') = '1') and (ExistingKordon = '') then KordonCheck.Checked := True;
  KordonCheck.OnClick := @KordonSelectionChanged;
  KordonNote := TNewStaticText.Create(WizardForm);
  KordonNote.Parent := KordonBanner;
  KordonNote.AutoSize := False;
  KordonNote.WordWrap := True;
  KordonNote.SetBounds(ScaleX(14), ScaleY(105), KordonBanner.Width - ScaleX(28), ScaleY(34));
  KordonSelectionChanged(nil);
  Log(Format('Offer layout: tasksHeight=%d bannerTop=%d bannerHeight=%d pageHeight=%d scale52=%d', [WizardForm.TasksList.Height, KordonBanner.Top, KordonBanner.Height, WizardForm.SelectTasksPage.ClientHeight, ScaleY(52)]));
  DownloadPage := CreateDownloadPage('Загрузка Kordon', 'Официальный выпуск 1.4.2, проверка контрольной суммы', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectTasks then KordonSelectionChanged(nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Hash: String;
begin
  Result := '';
  KordonSelectionChanged(nil);
  if not InstallKordon then begin Log('Kordon download and installation skipped.'); Exit; end;
  // Do not overwrite an installation which appeared after the initial detection.
  if LooksLikeKordon(AddBackslash(KordonDir('')) + 'kordon.exe') then begin
    ExistingKordon := AddBackslash(KordonDir('')) + 'kordon.exe';
    InstallKordon := False;
    Exit;
  end;
  Hash := '5c370955b37f5cce255597017aebfc7eb42df5b89f769e1909d9768933c56c71';
  DownloadPage.Clear;
  DownloadPage.Add('https://github.com/ef1m03/kordon/releases/download/v1.4.2/kordon-windows-amd64.exe', 'kordon-download.exe', Hash);
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if CompareText(GetSHA256OfFile(ExpandConstant('{tmp}\kordon-download.exe')), Hash) <> 0 then
        RaiseException('Контрольная сумма Kordon не совпала.');
    except
      Result := 'Kordon не загружен или не прошёл проверку. Установка переключателя не начата. ' + GetExceptionMessage;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
#ifndef TestBuild
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('autostart') then
    if not RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'TimezoneSwitcher', '"' + ExpandConstant('{app}\TimezoneSwitcher.exe') + '" --tray') then
      MsgBox('Программа установлена, но автозагрузку сохранить не удалось.', mbInformation, MB_OK);
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimezoneSwitcher', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\TimezoneSwitcher.exe') + '" --tray') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimezoneSwitcher');
end;
