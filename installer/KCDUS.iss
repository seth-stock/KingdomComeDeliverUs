; Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
; GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
; belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
; Modified port of the Kingdom Come: Together installer (https://github.com/DeepFriedDepp/KingdomCome-Together).
;
; Compile with tools\Build-Installer.ps1, not by hand: it publishes the programs into release\KCDUS and builds the mod
; into build\mod first, and this script refuses to compile when either is missing.
;
; What it does:
;   * finds Kingdom Come: Deliverance through Steam (the registry, then every library in libraryfolders.vdf), shows the
;     folder it found and lets the player correct it, and refuses a folder that is not the game;
;   * deploys the mod into <game>\Mods\kcdus (mod.manifest, mod.cfg, Data\kcdus.pak): ONLY these three files, never the sources;
;   * installs the launcher, the agent and the relay into %LocalAppData%\KCDUS (no administrator rights needed);
;   * writes the agent's settings with the game folder it found, so the first start needs no setup;
;   * optionally (a task, ticked by default) blocks the game's remote-console port 4600 from every other computer. That one
;     step asks for administrator permission, and the mod works without it (docs/KCD1-MODDING.md section 8);
;   * touches none of the game's own files and none of the player's saves.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Kingdom Come: Deliver Us"
#define ShortcutName "Kingdom Come Deliver Us"
#define Disclaimer "Unofficial community mod. Not affiliated with or endorsed by Warhorse Studios or Deep Silver. A modified port of Kingdom Come: Together."
#define AppExeName "KcdUsLauncher.exe"
#define AppPublisher "Kingdom Come: Deliver Us contributors"
#define AppUrl "https://github.com/DeepFriedDepp/KingdomCome-Together"

[Setup]
AppId={{6E0C7A52-9B3D-4C1B-A0F4-3D7C1C8A52E9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={localappdata}\KCDUS
PrivilegesRequired=lowest
UsePreviousAppDir=yes
DefaultGroupName={#ShortcutName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\release
OutputBaseFilename=KingdomComeDeliverUs-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%n{#Disclaimer}%n%nClose the game, the launcher and the agent before continuing. Everyone you play with must install this same version.
FinishedLabel=Setup has finished installing [name] on your computer.%n%n{#Disclaimer}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "firewall"; Description: "Block the game's remote-console port (4600) from other computers (asks for administrator permission; recommended)"; GroupDescription: "Safety:"

[Files]
Source: "..\release\KCDUS\*"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly recursesubdirs createallsubdirs
Source: "..\tools\Harden-Firewall.ps1"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly
Source: "..\AUTHORS"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly
Source: "..\docs\PLAYING-TOGETHER.md"; DestDir: "{app}\docs"; Flags: ignoreversion overwritereadonly
Source: "..\docs\KNOWN-LIMITS.md"; DestDir: "{app}\docs"; Flags: ignoreversion overwritereadonly
; The game mod: these three files and nothing else. uninsneveruninstall: the uninstaller must not delete files in the player's game
; folder unasked; removal is the explicit question in CurUninstallStepChanged.
Source: "..\build\mod\kcdus\mod.manifest"; DestDir: "{code:GameModDir}"; Flags: ignoreversion overwritereadonly uninsneveruninstall
Source: "..\build\mod\kcdus\mod.cfg"; DestDir: "{code:GameModDir}"; Flags: ignoreversion overwritereadonly uninsneveruninstall
Source: "..\build\mod\kcdus\Data\kcdus.pak"; DestDir: "{code:GameModDir}\Data"; Flags: ignoreversion overwritereadonly uninsneveruninstall

[Icons]
Name: "{group}\{#ShortcutName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "{#Disclaimer}"
Name: "{group}\Playing together (the guide)"; Filename: "{app}\docs\PLAYING-TOGETHER.md"
Name: "{group}\Uninstall {#ShortcutName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "{#Disclaimer}"

[Registry]
Root: HKCU; Subkey: "Software\KCDUS"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\KCDUS"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\KCDUS"; ValueType: string; ValueName: "GameDir"; ValueData: "{code:GameDirValue}"
Root: HKCU; Subkey: "Software\KCDUS"; ValueType: string; ValueName: "ModDir"; ValueData: "{code:GameModDir}"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Harden-Firewall.ps1"" -Add"; Verb: runas; Flags: shellexec runhidden waituntilterminated; Tasks: firewall; StatusMsg: "Blocking the remote-console port from other computers..."
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  GamePage: TInputDirWizardPage;
  ChosenGameDir: String;

function LooksLikeTheGame(Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + 'Bin\Win64\KingdomCome.exe') and DirExists(AddBackslash(Dir) + 'Data');
end;

function SteamPath(): String;
begin
  Result := '';
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Valve\Steam', 'SteamPath', Result) then
    if not RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', Result) then
      Result := '';
  StringChangeEx(Result, '/', '\', True);
end;

function TryLibrary(Lib: String): String;
var
  D: String;
begin
  Result := '';
  D := AddBackslash(Lib) + 'steamapps\common\KingdomComeDeliverance';
  if LooksLikeTheGame(D) then Result := D;
end;

{ Steam's library list: lines like    "path"    "F:\\SteamLibrary" }
function FindGame(): String;
var
  Steam, Vdf, Line, P: String;
  Lines: TArrayOfString;
  I, A, B: Integer;
begin
  Result := '';
  Steam := SteamPath();
  if Steam = '' then Exit;
  Result := TryLibrary(Steam);
  if Result <> '' then Exit;
  Vdf := AddBackslash(Steam) + 'steamapps\libraryfolders.vdf';
  if not LoadStringsFromFile(Vdf, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Lines[I];
    if Pos('"path"', Line) > 0 then
    begin
      Delete(Line, 1, Pos('"path"', Line) + 5);
      A := Pos('"', Line);
      if A > 0 then
      begin
        Delete(Line, 1, A);
        B := Pos('"', Line);
        if B > 0 then
        begin
          P := Copy(Line, 1, B - 1);
          StringChangeEx(P, '\\', '\', True);
          Result := TryLibrary(P);
          if Result <> '' then Exit;
        end;
      end;
    end;
  end;
end;

{ Silent installs and tests can say where the game is:  /GAMEDIR="F:\SteamLibrary\steamapps\common\KingdomComeDeliverance" }
function GameDirFromCommandLine(): String;
begin
  Result := ExpandConstant('{param:GAMEDIR|}');
end;

procedure InitializeWizard();
var
  Found: String;
begin
  Found := GameDirFromCommandLine();
  if Found = '' then Found := FindGame();
  GamePage := CreateInputDirPage(wpWelcome, 'Kingdom Come: Deliverance',
    'Where is the first game installed?',
    'The mod goes into this folder''s Mods subfolder. This is the original Kingdom Come: Deliverance (not the second game). ' +
    'Setup looked in Steam; correct the folder if it is wrong. It must contain Bin\Win64\KingdomCome.exe.', False, '');
  GamePage.Add('');
  GamePage.Values[0] := Found;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = GamePage.ID then
  begin
    ChosenGameDir := RemoveBackslashUnlessRoot(GamePage.Values[0]);
    if not LooksLikeTheGame(ChosenGameDir) then
    begin
      MsgBox('That folder is not Kingdom Come: Deliverance: Bin\Win64\KingdomCome.exe was not found in it.' + #13#10#13#10 +
             'Install the game in Steam first, or choose the folder that holds it (for example ...\steamapps\common\KingdomComeDeliverance).',
             mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  { a silent install with a valid /GAMEDIR does not ask }
  if (PageID = GamePage.ID) and LooksLikeTheGame(GameDirFromCommandLine()) then
  begin
    ChosenGameDir := RemoveBackslashUnlessRoot(GameDirFromCommandLine());
    Result := True;
  end;
end;

function GameDirValue(Param: String): String;
begin
  Result := ChosenGameDir;
end;

function GameModDir(Param: String): String;
begin
  Result := AddBackslash(ChosenGameDir) + 'Mods\kcdus';
end;

{ The launcher reads this on first start: the game folder is already filled in. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Path, Json: String;
begin
  if CurStep = ssPostInstall then
  begin
    Path := ChosenGameDir;
    StringChangeEx(Path, '\', '\\', True);
    Json := '{' + #13#10 + '  "gameDir": "' + Path + '",' + #13#10 + '  "playerName": "Henry",' + #13#10 + '  "role": "guest"' + #13#10 + '}';
    if not FileExists(ExpandConstant('{app}\kcdus-agent.json')) then
      SaveStringToFile(ExpandConstant('{app}\kcdus-agent.json'), Json, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ModDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKEY_CURRENT_USER, 'Software\KCDUS', 'ModDir', ModDir) and DirExists(ModDir) then
    begin
      { SuppressibleMsgBox obeys /SUPPRESSMSGBOXES and answers Yes there: a scripted uninstall removes what its install put in the game folder }
      if SuppressibleMsgBox('Also remove the mod from your game folder?' + #13#10 + ModDir + #13#10#13#10 +
                'Your saves are not touched either way. Saves made while the mod was installed still load without it.',
                mbConfirmation, MB_YESNO, IDYES) = IDYES then
      begin
        DeleteFile(ModDir + '\Data\kcdus.pak');
        DeleteFile(ModDir + '\mod.manifest');
        DeleteFile(ModDir + '\mod.cfg');
        RemoveDir(ModDir + '\Data');
        RemoveDir(ModDir);
      end;
    end;
  end;
end;
