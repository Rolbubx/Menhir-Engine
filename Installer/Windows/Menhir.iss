; Menhir's Windows installer (Inno Setup 6): the usual wizard (folder, C compiler, shortcuts), Settings > Apps
; entry and uninstaller. Installer/package.sh compiles it on Windows with the files it staged:
;   ISCC.exe /DAppVersion=0.2.1 /DStage=<staged folder> /DOutputDir=<folder> Installer\Windows\Menhir.iss
;
; Command line (besides Inno Setup's own /SILENT, /VERYSILENT, /DIR=..., /TASKS=...):
;   /COMPILER=<path or name>   the C compiler, without asking (the compiler page shows it otherwise)
;   /RELAUNCH=<project folder> start the editor on that project when done (File > Update Editor uses it)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Stage
  #define Stage "..\..\artifacts\stage-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#define Editor "{app}\Editor\MenhirEditor.exe"

[Setup]
AppId={{6F1C2B8E-4D7A-4E59-9B3F-2A8C0D5E7F41}
AppName=Menhir Engine
AppVersion={#AppVersion}
AppVerName=Menhir Engine {#AppVersion}
AppPublisher=Menhir
AppPublisherURL=https://github.com/Rolbubx/Menhir-Engine
AppSupportURL=https://github.com/Rolbubx/Menhir-Engine/issues
AppUpdatesURL=https://github.com/Rolbubx/Menhir-Engine/releases
VersionInfoVersion={#AppVersion}
; For the current user by default (no administrator rights); the wizard offers "for all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\Menhir
DisableProgramGroupPage=yes
WizardStyle=modern
WizardImageFile=wizard-large.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small-2x.bmp
SetupIconFile=menhir.ico
UninstallDisplayIcon={#Editor}
UninstallDisplayName=Menhir Engine
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
; Updates close a running editor (File > Update Editor closes it itself).
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=Menhir-Setup-win-x64
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.CompilerTitle=C compiler
english.CompilerDescription=Which C compiler should Menhir use?
english.CompilerIntro=Menhir compiles your scripts and your games with GCC or Clang. On Windows: a MinGW-w64 build such as MSYS2 (UCRT64, MINGW64, CLANG64), WinLibs, w64devkit, TDM-GCC or LLVM-MinGW. Microsoft's cl.exe (MSVC) does not work.%n%nYou can change it later in the editor, in Project Settings > Build.
english.CompilerLabel=Compiler (a path to gcc.exe or clang.exe, or a name on the PATH such as gcc):
english.CompilerFound=Found on this computer (click to use one):
english.CompilerNone=No GCC or Clang was found. Install one (e.g. MSYS2: https://www.msys2.org), then choose its gcc.exe, or keep "gcc" if it will be on the PATH.
english.CompilerDoesNotRun='%1' does not run as a C compiler (it was not found, or it is not GCC/Clang).%n%nUse it anyway? You can change it later in Project Settings > Build.
english.LaunchEditor=Launch Menhir Editor
english.RemovePreferences=Also remove the editor's preferences (window layout, recent projects)?%n%nYour projects are not removed.
french.CompilerTitle=Compilateur C
french.CompilerDescription=Quel compilateur C Menhir doit-il utiliser ?
french.CompilerIntro=Menhir compile vos scripts et vos jeux avec GCC ou Clang. Sous Windows : une version MinGW-w64 comme MSYS2 (UCRT64, MINGW64, CLANG64), WinLibs, w64devkit, TDM-GCC ou LLVM-MinGW. Le cl.exe de Microsoft (MSVC) ne fonctionne pas.%n%nVous pourrez le changer dans l'éditeur, dans Project Settings > Build.
french.CompilerLabel=Compilateur (chemin vers gcc.exe ou clang.exe, ou un nom du PATH comme gcc) :
french.CompilerFound=Trouvés sur cet ordinateur (cliquez pour en choisir un) :
french.CompilerNone=Aucun GCC ni Clang trouvé. Installez-en un (par ex. MSYS2 : https://www.msys2.org) puis choisissez son gcc.exe, ou gardez « gcc » s'il sera dans le PATH.
french.CompilerDoesNotRun=« %1 » ne fonctionne pas comme compilateur C (introuvable, ou ce n'est pas GCC/Clang).%n%nL'utiliser quand même ? Vous pourrez le changer dans Project Settings > Build.
french.LaunchEditor=Lancer Menhir Editor
french.RemovePreferences=Supprimer aussi les préférences de l'éditeur (disposition des fenêtres, projets récents) ?%n%nVos projets ne sont pas supprimés.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An update replaces the editor and the engine entirely (no files left from the previous version).
Type: filesandordirs; Name: "{app}\Editor"
Type: filesandordirs; Name: "{app}\Engine"

[Files]
Source: "{#Stage}\Editor\*"; DestDir: "{app}\Editor"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Stage}\Engine\*"; DestDir: "{app}\Engine"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Stage}\Docs\*"; DestDir: "{app}\Docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Stage}\README.md"; DestDir: "{app}"; Flags: ignoreversion
; The sample keeps the user's changes when updating.
Source: "{#Stage}\Samples\*"; DestDir: "{app}\Samples"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Menhir Editor"; Filename: "{#Editor}"; WorkingDir: "{app}\Editor"
Name: "{autodesktop}\Menhir Editor"; Filename: "{#Editor}"; WorkingDir: "{app}\Editor"; Tasks: desktopicon

[Run]
Filename: "{#Editor}"; Description: "{cm:LaunchEditor}"; WorkingDir: "{app}\Editor"; Flags: nowait postinstall skipifsilent
; After an update from the editor: back to the project that was open.
Filename: "{#Editor}"; Parameters: "--project ""{param:RELAUNCH}"""; WorkingDir: "{app}\Editor"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Editor"
Type: filesandordirs; Name: "{app}\Engine"
Type: files; Name: "{app}\toolchain.json"
Type: filesandordirs; Name: "{app}\Samples\Sandbox\Cache"
Type: filesandordirs; Name: "{app}\Samples\Sandbox\Build"

[Code]
var
  CompilerPage: TInputFileWizardPage;
  FoundList: TNewListBox;
  Found: TStringList;

procedure AddFound(Path: String);
begin
  if (Path <> '') and FileExists(Path) and (Found.IndexOf(Path) < 0) then
    Found.Add(Path);
end;

{ GCC and Clang on the PATH, then where the usual MinGW-w64 distributions put them. }
procedure FindCompilers();
var
  Drives: array[0..1] of String;
  Folders: array[0..6] of String;
  D, F: Integer;
begin
  Found := TStringList.Create;
  AddFound(FileSearch('gcc.exe', GetEnv('PATH')));
  AddFound(FileSearch('clang.exe', GetEnv('PATH')));
  AddFound(FileSearch('x86_64-w64-mingw32-gcc.exe', GetEnv('PATH')));
  Drives[0] := 'C:\';
  Drives[1] := 'D:\';
  Folders[0] := 'msys64\ucrt64\bin';
  Folders[1] := 'msys64\mingw64\bin';
  Folders[2] := 'msys64\clang64\bin';
  Folders[3] := 'w64devkit\bin';
  Folders[4] := 'mingw64\bin';
  Folders[5] := 'TDM-GCC-64\bin';
  Folders[6] := 'llvm-mingw\bin';
  for D := 0 to 1 do
    for F := 0 to 6 do
    begin
      AddFound(Drives[D] + Folders[F] + '\gcc.exe');
      AddFound(Drives[D] + Folders[F] + '\clang.exe');
    end;
  AddFound(ExpandConstant('{%USERPROFILE}\w64devkit\bin\gcc.exe'));
  AddFound(ExpandConstant('{%USERPROFILE}\scoop\apps\gcc\current\bin\gcc.exe'));
  AddFound(ExpandConstant('{%USERPROFILE}\scoop\apps\mingw\current\bin\gcc.exe'));
  AddFound(ExpandConstant('{commonpf}\LLVM\bin\clang.exe'));
end;

procedure FoundListClick(Sender: TObject);
begin
  if FoundList.ItemIndex >= 0 then
    CompilerPage.Values[0] := FoundList.Items[FoundList.ItemIndex];
end;

procedure InitializeWizard();
var
  Caption: TNewStaticText;
  I: Integer;
  Previous: String;
begin
  FindCompilers();
  CompilerPage := CreateInputFilePage(wpSelectDir, CustomMessage('CompilerTitle'),
    CustomMessage('CompilerDescription'), CustomMessage('CompilerIntro'));
  CompilerPage.Add(CustomMessage('CompilerLabel'), 'gcc / clang|gcc.exe;clang.exe;*gcc.exe|Programs|*.exe', '.exe');

  Caption := TNewStaticText.Create(CompilerPage);
  Caption.Parent := CompilerPage.Surface;
  Caption.Top := CompilerPage.Edits[0].Top + CompilerPage.Edits[0].Height + ScaleY(14);
  Caption.Width := CompilerPage.SurfaceWidth;
  Caption.AutoSize := False;
  Caption.WordWrap := True;
  Caption.Height := ScaleY(30);
  if Found.Count > 0 then
    Caption.Caption := CustomMessage('CompilerFound')
  else
    Caption.Caption := CustomMessage('CompilerNone');

  FoundList := TNewListBox.Create(CompilerPage);
  FoundList.Parent := CompilerPage.Surface;
  FoundList.Top := Caption.Top + ScaleY(20);
  FoundList.Width := CompilerPage.SurfaceWidth;
  FoundList.Height := CompilerPage.SurfaceHeight - FoundList.Top;
  FoundList.OnClick := @FoundListClick;
  FoundList.Visible := Found.Count > 0;
  for I := 0 to Found.Count - 1 do
    FoundList.Items.Add(Found[I]);

  { The command line wins, then what was chosen at the previous installation, then the first one found. }
  Previous := GetPreviousData('Compiler', '');
  if ExpandConstant('{param:COMPILER}') <> '' then
    CompilerPage.Values[0] := ExpandConstant('{param:COMPILER}')
  else if Previous <> '' then
    CompilerPage.Values[0] := Previous
  else if Found.Count > 0 then
    CompilerPage.Values[0] := Found[0]
  else
    CompilerPage.Values[0] := 'gcc';
  FoundList.ItemIndex := FoundList.Items.IndexOf(CompilerPage.Values[0]);
end;

function CompilerRuns(Command: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(Command, '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Command: String;
begin
  Result := True;
  if CurPageID = CompilerPage.ID then
  begin
    Command := Trim(CompilerPage.Values[0]);
    if Command = '' then
    begin
      Command := 'gcc';
      CompilerPage.Values[0] := Command;
    end;
    // Silent (an update from the editor): the compiler stays as chosen, without asking.
    if not WizardSilent and not CompilerRuns(Command) then
      Result := MsgBox(FmtMessage(CustomMessage('CompilerDoesNotRun'), [Command]), mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'Compiler', Trim(CompilerPage.Values[0]));
end;

function JsonString(S: String): String;
begin
  StringChangeEx(S, '\', '\\', True);
  StringChangeEx(S, '"', '\"', True);
  Result := '"' + S + '"';
end;

{ toolchain.json: the editor's compiler (Editor/Menhir.Editor.Core/Build/Toolchain.cs). }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\toolchain.json'),
      '{' + #13#10 + '  "compiler": ' + JsonString(Trim(CompilerPage.Values[0])) + #13#10 + '}' + #13#10, False);
end;

function ShouldRelaunch(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH}') <> '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Preferences: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Preferences := ExpandConstant('{userappdata}\Menhir');
    if DirExists(Preferences) and not UninstallSilent() and
       (MsgBox(CustomMessage('RemovePreferences'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Preferences, True, True, True);
  end;
end;
