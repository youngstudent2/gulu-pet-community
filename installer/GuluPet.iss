#ifndef AppVersion
  #error AppVersion must be provided by the release publisher.
#endif

#ifndef SourceDir
  #error SourceDir must be provided by the release publisher.
#endif

#ifndef SetupIconPath
  #error SetupIconPath must be provided by the release publisher.
#endif

#ifndef AppIdValue
  #define AppIdValue "9D362A63-DB70-4D63-93D2-47992F1D89C7"
#endif

#ifndef AppNameValue
  #define AppNameValue "Gulu Pet Community"
#endif

#ifndef ShortcutNameValue
  #define ShortcutNameValue "Gulu Pet Community"
#endif

#ifndef AppExeName
  #define AppExeName "GuluPet.Community.exe"
#endif

#ifndef AppMutexValue
  #define AppMutexValue "Local\GuluPet.Community.DesktopPet.SingleInstance.v1"
#endif

#ifndef UserDataDirValue
  #define UserDataDirValue "{localappdata}\GuluPetCommunity"
#endif

#ifndef StartupRunValueName
  #define StartupRunValueName "GuluPetCommunity"
#endif

[Setup]
AppId=GuluPetCommunity.Desktop.{#AppIdValue}
AppName={#AppNameValue}
AppVersion={#AppVersion}
AppVerName={#AppNameValue} {#AppVersion}
AppPublisher=Gulu Pet Community Contributors
DefaultDirName={localappdata}\Programs\GuluPetCommunity
DisableDirPage=auto
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
AppMutex={#AppMutexValue}
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallFilesDir={app}
UninstallDisplayName={#AppNameValue}
UninstallDisplayIcon={app}\app\{#AppExeName}
SetupIconFile={#SetupIconPath}
VersionInfoDescription={#AppNameValue} Setup
VersionInfoProductName={#AppNameValue}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
OutputBaseFilename=GuluPetCommunity-{#AppVersion}-win-x64-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ShortcutNameValue}"; Filename: "{app}\app\{#AppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\{#AppExeName}"
Name: "{autoprograms}\Uninstall {#ShortcutNameValue}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ShortcutNameValue}"; Filename: "{app}\app\{#AppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\app\{#AppExeName}"; WorkingDir: "{app}\app"; Description: "Launch {#AppNameValue}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\.setup-runtime-complete"

[Code]
const
  FileAttributeDirectory = $10;
  FileAttributeReparsePoint = $400;
  InvalidFileAttributes = $FFFFFFFF;
  RunKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = '{#StartupRunValueName}';

var
  DeleteUserData: Boolean;
  MigrateStartupRegistration: Boolean;
  RuntimeBackupCreated: Boolean;
  InstallCompleted: Boolean;

function GetFileAttributesW(FileName: string): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function HasCommandLineParameter(const Value: string): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsSilentInvocation: Boolean;
begin
  Result :=
    HasCommandLineParameter('/SILENT') or
    HasCommandLineParameter('/VERYSILENT');
end;

function NormalizeFileSystemPath(const Value: string): string;
begin
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Value));
end;

function NormalizeConstantPath(const Value: string): string;
begin
  Result := NormalizeFileSystemPath(ExpandConstant(Value));
end;

function IsPathEqualOrChild(const Candidate, Boundary: string): Boolean;
var
  NormalizedCandidate: string;
  NormalizedBoundary: string;
begin
  NormalizedCandidate := Uppercase(NormalizeFileSystemPath(Candidate));
  NormalizedBoundary := Uppercase(NormalizeFileSystemPath(Boundary));
  Result :=
    (CompareText(NormalizedCandidate, NormalizedBoundary) = 0) or
    (Pos(AddBackslash(NormalizedBoundary),
      AddBackslash(NormalizedCandidate)) = 1);
end;

function IsVolumeRoot(const Value: string): Boolean;
var
  Normalized: string;
  Parent: string;
begin
  Normalized := NormalizeFileSystemPath(Value);
  Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Normalized));
  Result := CompareText(Normalized, Parent) = 0;
end;

function HasReparseAncestor(const Value: string): Boolean;
var
  Current: string;
  Parent: string;
  Attributes: LongWord;
begin
  Result := False;
  Current := NormalizeFileSystemPath(Value);
  while Current <> '' do
  begin
    if DirExists(Current) then
    begin
      Attributes := GetFileAttributesW(Current);
      if (Attributes <> InvalidFileAttributes) and
         ((Attributes and FileAttributeReparsePoint) <> 0) then
      begin
        Result := True;
        Exit;
      end;
    end;

    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Current));
    if (Parent = '') or (CompareText(Parent, Current) = 0) then
      Exit;
    Current := Parent;
  end;
end;

function DirectoryHasEntries(const Value: string): Boolean;
var
  Entry: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Value) + '*', Entry) then
  begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function IsExistingCommunityInstall(const Value: string): Boolean;
begin
  Result :=
    FileExists(AddBackslash(Value) + 'app\{#AppExeName}') or
    FileExists(AddBackslash(Value) + 'app.setup-backup\{#AppExeName}') or
    (FileExists(AddBackslash(Value) + 'unins000.exe') and
     FileExists(AddBackslash(Value) + 'unins000.dat'));
end;

function ValidateInstallRoot(const Value: string): string;
var
  InstallRoot: string;
  UserDataRoot: string;
begin
  Result := '';
  InstallRoot := NormalizeFileSystemPath(Value);
  UserDataRoot := NormalizeConstantPath('{#UserDataDirValue}');

  if (Length(InstallRoot) >= 2) and
     (Copy(InstallRoot, 1, 2) = '\\') then
  begin
    Result := 'The install location cannot be a network path. Choose a folder on a local drive.';
    Exit;
  end;

  if IsVolumeRoot(InstallRoot) then
  begin
    Result := 'Do not install directly in a drive root. Choose or create a dedicated folder.';
    Exit;
  end;

  if IsPathEqualOrChild(InstallRoot, UserDataRoot) or
     IsPathEqualOrChild(UserDataRoot, InstallRoot) then
  begin
    Result := 'The program directory cannot overlap the application data directory.';
    Exit;
  end;

  if IsPathEqualOrChild(InstallRoot, ExpandConstant('{win}')) or
     IsPathEqualOrChild(InstallRoot, ExpandConstant('{sys}')) or
     IsPathEqualOrChild(InstallRoot, ExpandConstant('{pf}')) or
     IsPathEqualOrChild(InstallRoot, ExpandConstant('{pf32}')) then
  begin
    Result := 'Choose a directory writable by the current user, outside Windows and Program Files.';
    Exit;
  end;

  if HasReparseAncestor(InstallRoot) then
  begin
    Result := 'The install path cannot contain symbolic links, junctions, or other reparse points.';
    Exit;
  end;

  if DirExists(InstallRoot) and DirectoryHasEntries(InstallRoot) and
     not IsExistingCommunityInstall(InstallRoot) then
  begin
    Result := 'The selected folder is not empty. Choose an empty folder or an existing Gulu Pet Community install.';
    Exit;
  end;
end;

function TreeContainsReparsePoint(const Value: string): Boolean;
var
  Entry: TFindRec;
  Child: string;
begin
  Result := False;
  if not DirExists(Value) then
    Exit;

  if HasReparseAncestor(Value) then
  begin
    Result := True;
    Exit;
  end;

  if FindFirst(AddBackslash(Value) + '*', Entry) then
  begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then
        begin
          Child := AddBackslash(Value) + Entry.Name;
          if (Entry.Attributes and FileAttributeReparsePoint) <> 0 then
          begin
            Result := True;
            Exit;
          end;
          if ((Entry.Attributes and FileAttributeDirectory) <> 0) and
             TreeContainsReparsePoint(Child) then
          begin
            Result := True;
            Exit;
          end;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

procedure DeleteManagedTree(const Value, DisplayName: string);
begin
  if not DirExists(Value) then
    Exit;

  if TreeContainsReparsePoint(Value) then
  begin
    Log('Refusing to delete a managed tree containing a reparse point: ' + Value);
    if not IsSilentInvocation then
      MsgBox(DisplayName + ' contains a linked directory. It was not removed in order to protect other files.',
        mbError, MB_OK);
    Exit;
  end;

  if not DelTree(Value, True, True, True) then
  begin
    Log('Unable to delete managed tree: ' + Value);
    if not IsSilentInvocation then
      MsgBox(DisplayName + ' was not completely removed. You can review it later:' + #13#10 + Value,
        mbError, MB_OK);
  end;
end;

function RuntimeDirectory: string;
begin
  Result := ExpandConstant('{app}\app');
end;

function RuntimeBackupDirectory: string;
begin
  Result := ExpandConstant('{app}\app.setup-backup');
end;

function RuntimeCompleteMarker: string;
begin
  Result := ExpandConstant('{app}\.setup-runtime-complete');
end;

function RecoverInterruptedRuntimeSwap: string;
var
  RuntimePath: string;
  BackupPath: string;
  MarkerPath: string;
begin
  Result := '';
  RuntimePath := RuntimeDirectory;
  BackupPath := RuntimeBackupDirectory;
  MarkerPath := RuntimeCompleteMarker;
  if not DirExists(BackupPath) then
    Exit;

  if TreeContainsReparsePoint(BackupPath) then
  begin
    Result := 'The previous install backup is unsafe. Choose a new install directory.';
    Exit;
  end;

  if FileExists(MarkerPath) and DirExists(RuntimePath) then
  begin
    if TreeContainsReparsePoint(RuntimePath) then
    begin
      Result := 'The existing application directory contains a link. Choose a new install directory.';
      Exit;
    end;
    DeleteManagedTree(BackupPath, 'Previous install backup');
    if DirExists(BackupPath) then
      Result := 'The previous install backup could not be removed. Close applications using it and retry.';
    Exit;
  end;

  DeleteFile(MarkerPath);
  if DirExists(RuntimePath) then
  begin
    if TreeContainsReparsePoint(RuntimePath) then
    begin
      Result := 'The incomplete application directory contains a link. Choose a new install directory.';
      Exit;
    end;
    DeleteManagedTree(RuntimePath, 'Incomplete application files');
    if DirExists(RuntimePath) then
    begin
      Result := 'The incomplete install could not be cleaned up. Close applications using it and retry.';
      Exit;
    end;
  end;

  if not RenameFile(BackupPath, RuntimePath) then
    Result := 'The previous install could not be restored. Close applications using it and retry.';
end;

procedure BeginRuntimeSwap;
var
  RuntimePath: string;
  BackupPath: string;
begin
  RuntimePath := RuntimeDirectory;
  BackupPath := RuntimeBackupDirectory;
  if FileExists(RuntimeCompleteMarker) and
     not DeleteFile(RuntimeCompleteMarker) then
    RaiseException('Setup state could not be prepared. Close applications using the install and retry.');
  if not DirExists(RuntimePath) then
    Exit;

  if TreeContainsReparsePoint(RuntimePath) then
    RaiseException('The existing application directory contains a link, so setup was stopped.');
  if DirExists(BackupPath) then
    RaiseException('An install backup still exists, so setup was stopped.');
  if not RenameFile(RuntimePath, BackupPath) then
    RaiseException('The existing application could not be backed up. Close applications using it and retry.');
  RuntimeBackupCreated := True;
end;

function IsCommunityStartupCommand(const Value: string): Boolean;
var
  Normalized: string;
begin
  Normalized := Lowercase(Trim(Value));
  Result :=
    (Pos(Lowercase('{#AppExeName}') + '"', Normalized) > 0) and
    (Pos(' --startup', Normalized) > 0);
end;

function InstalledStartupCommand: string;
begin
  Result := '"' + ExpandConstant('{app}\app\{#AppExeName}') + '" --startup';
end;

procedure InitializeWizard;
var
  ExistingCommand: string;
begin
  MigrateStartupRegistration :=
    RegQueryStringValue(HKCU, RunKeyPath, RunValueName, ExistingCommand) and
    IsCommunityStartupCommand(ExistingCommand);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: string;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  ErrorMessage := ValidateInstallRoot(WizardDirValue);
  if ErrorMessage <> '' then
  begin
    MsgBox(ErrorMessage, mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  Result := ValidateInstallRoot(WizardDirValue);
  if Result = '' then
    Result := RecoverInterruptedRuntimeSwap;
  if (Result = '') and TreeContainsReparsePoint(RuntimeDirectory) then
    Result := 'The existing application directory contains a link. Choose a new install directory.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    BeginRuntimeSwap;

  if CurStep = ssPostInstall then
  begin
    if not SaveStringToFile(
      RuntimeCompleteMarker, '{#AppVersion}', False) then
      RaiseException('Setup could not save its completion state and will restore the previous version.');
    InstallCompleted := True;
    if RuntimeBackupCreated then
      DeleteManagedTree(RuntimeBackupDirectory, 'Previous application version');

    if MigrateStartupRegistration then
    begin
      if not RegWriteStringValue(
        HKCU, RunKeyPath, RunValueName, InstalledStartupCommand) then
        Log('Unable to migrate the existing startup registration.');
    end;

    if not WizardIsTaskSelected('desktopicon') then
      DeleteFile(ExpandConstant('{autodesktop}\{#ShortcutNameValue}.lnk'));
  end;
end;

procedure DeinitializeSetup;
begin
  if RuntimeBackupCreated and not InstallCompleted then
  begin
    DeleteFile(RuntimeCompleteMarker);
    DeleteManagedTree(RuntimeDirectory, 'Incomplete application files');
    if not DirExists(RuntimeDirectory) then
    begin
      if not RenameFile(RuntimeBackupDirectory, RuntimeDirectory) then
        Log('Unable to restore the previous runtime after Setup failure.');
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if HasCommandLineParameter('/DELETEUSERDATA') then
    DeleteUserData := True
  else if HasCommandLineParameter('/KEEPUSERDATA') or IsSilentInvocation then
    DeleteUserData := False
  else
    DeleteUserData := MsgBox(
      'Also remove settings, diary entries, and progress?' + #13#10 + #13#10 +
      'Choose No to keep these records for a future reinstall.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure RemoveOwnedStartupRegistration;
var
  ExistingCommand: string;
begin
  if RegQueryStringValue(HKCU, RunKeyPath, RunValueName, ExistingCommand) and
     (CompareText(Trim(ExistingCommand), InstalledStartupCommand) = 0) then
    RegDeleteValue(HKCU, RunKeyPath, RunValueName);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOwnedStartupRegistration;

  if CurUninstallStep = usPostUninstall then
  begin
    DeleteFile(RuntimeCompleteMarker);
    DeleteManagedTree(RuntimeDirectory, 'Application files');
    DeleteManagedTree(RuntimeBackupDirectory, 'Previous install backup');
    if DeleteUserData then
      DeleteManagedTree(
        NormalizeConstantPath('{#UserDataDirValue}'), 'Application data');
  end;
end;
