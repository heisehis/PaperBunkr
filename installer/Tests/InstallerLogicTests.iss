; Runs shared Pascal helpers and exits during initialization: never installs or writes registry.
[Setup]
AppName=Paperbunkr installer verification
AppVersion=1.0
DefaultDirName={tmp}\PaperbunkrInstallerVerification
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
OutputBaseFilename=InstallerLogicTests
WizardStyle=modern

[Code]
#include "..\InstallerLogic.iss"

var
  Report: String;
  Failures: Integer;

procedure Check(Passed: Boolean; const Name: String);
begin
  if Passed then Report := Report + 'PASS ' + Name + #13#10
  else begin Report := Report + 'FAIL ' + Name + #13#10; Failures := Failures + 1; end;
end;

procedure Compare(const Left, Right: String; Expected: Integer);
var
  Actual: Integer;
begin
  Check(TryCompareReleases(Left, Right, Actual) and (Actual = Expected), Left + ' compared with ' + Right);
end;

function InitializeSetup(): Boolean;
var
  Parts: TReleaseParts;
  Root, Profile, Sentinel: String;
begin
  Report := '';
  Failures := 0;
  Compare('0.7.10-beta', '0.7.9-beta', 1);
  Compare('0.7.9-beta', '0.7.10-beta', -1);
  Compare('1.0.0', '0.99.99-beta', 1);
  Compare('0.7.89-beta-abc', '0.7.89-beta-def', 0);
  Compare('0.7.89.0', '0.7.89-beta+abc', 0);
  Compare('0.1.1-alpha', '0.7.89-beta', -1);
  Compare('0.7', '0.7.0-beta', 0);
  Check(not TryReleaseParts('', Parts), 'empty version rejected');
  Check(not TryReleaseParts('garbage', Parts), 'unknown version rejected');
  Check(not TryReleaseParts('0.7.', Parts), 'trailing dot rejected');
  Check(not TryReleaseParts('0..7', Parts), 'empty component rejected');
  Check(not TryReleaseParts('0.7.65536', Parts), 'overflow rejected');
  Check(not TryReleaseParts('0.7.1.0.1', Parts), 'extra component rejected');
  Check(not TryReleaseParts('-1.7.1', Parts), 'negative component rejected');
  Check(SkipQuickUpdatePage(True, wpWelcome), 'quick upgrade skips welcome');
  Check(SkipQuickUpdatePage(True, wpSelectDir), 'quick upgrade skips destination');
  Check(SkipQuickUpdatePage(True, wpSelectComponents), 'quick upgrade skips shortcuts');
  Check(SkipQuickUpdatePage(True, wpSelectTasks), 'quick upgrade skips tasks');
  Check(SkipQuickUpdatePage(True, wpSelectProgramGroup), 'quick upgrade skips group');
  Check(not SkipQuickUpdatePage(True, wpLicense), 'quick upgrade retains licence');
  Check(not SkipQuickUpdatePage(True, wpReady), 'quick upgrade retains review');
  Check(not SkipQuickUpdatePage(True, wpFinished), 'quick upgrade retains finish');
  Check(not SkipQuickUpdatePage(False, wpSelectDir), 'full flow retains destination');
  Root := ExpandConstant('{param:FIXTURES}');
  Profile := AddBackslash(Root) + 'Paperbunkr';
  Sentinel := AddBackslash(Root) + 'Library\keep.cbz';
  Check(IsSafeProfileTarget(Profile, Root), 'normal fixture profile accepted');
  Check(not IsSafeProfileTarget(Root, Root), 'profile parent refused');
  Check(not IsSafeProfileTarget(AddBackslash(Root) + 'Library', Root), 'library folder refused');
  Check(not IsSafeProfileTarget(AddBackslash(Root) + 'Paperbunkr-other', Root), 'similar sibling refused');
  Check(not IsSafeProfileTarget(AddBackslash(Root) + 'LinkedProfile', Root), 'junction target refused');
  // A separate fixture contains a junction to the library sentinel.
  Check(not IsPlainTree(AddBackslash(Root) + 'WithLink', 0), 'nested junction refused');
  Check(not IsSafeProfileTarget(AddBackslash(Root) + 'LinkedRoot\Paperbunkr',
    AddBackslash(Root) + 'LinkedRoot'), 'profile through a redirected ancestor refused');
  Check(not RemovePlainTree(AddBackslash(Root) + 'WithLink\LinkedLibrary'), 'linked deletion refused');
  Check(FileExists(Sentinel), 'library sentinel survives rejected deletion');
  Check(IsPlainTree(AddBackslash(Root) + 'Empty', 0), 'empty directory accepted');
  Check(not IsPlainTree(AddBackslash(Root) + 'Missing', 0), 'missing directory refused');
  Check(not RemovePlainTree(AddBackslash(Root) + 'ReadOnly'), 'deletion failure reported');
  Check(FileExists(AddBackslash(Root) + 'ReadOnly\keep.txt'), 'failed deletion retains file');
  Check(RemovePlainTree(Profile), 'normal fixture profile deleted');
  Check(not DirExists(Profile), 'fixture deletion completed');
  Check(FileExists(Sentinel), 'library sentinel survives profile deletion');
  Report := Report + 'Failures: ' + IntToStr(Failures) + #13#10;
  SaveStringToFile(ExpandConstant('{param:RESULTS}'), Report, False);
  Result := False;
end;
