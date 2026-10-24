param([string]$Compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$installerRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $installerRoot 'InstallerBuild.ps1')
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('PaperbunkrInstallerTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$passed = 0
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name (artifacts: $scratch)" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Throws([scriptblock]$Action, [string]$Name) {
    $thrown = $false
    try { & $Action | Out-Null } catch { $thrown = $true }
    Check $thrown $Name
}
function Compile([string]$Script, [string[]]$Defines, [string]$Output) {
    $log = & $Compiler /Qp "/O$Output" @Defines $Script 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Inno compilation failed:`n$($log -join "`n")" }
}

# Scratch is retained for inspection, especially if a test fails. Never recursively remove it:
# the fixtures intentionally include links to sentinel files.
$fixtureProject = Join-Path $scratch 'Fixture.csproj'
Set-Content -LiteralPath $fixtureProject '<Project><PropertyGroup><Version>0.7.10.0</Version></PropertyGroup></Project>'
$version = Get-InstallerVersion -ProjectPath $fixtureProject -BuildIdentity 'abc123'
Check ($version.Display -eq '0.7.10-beta-abc123' -and $version.Numeric -eq '0.7.10.0') 'project-derived local version'
$version = Get-InstallerVersion -ProjectPath $fixtureProject -RequestedVersion '0.7.10-beta'
Check ($version.Display -eq '0.7.10-beta') 'explicit matching release version'
Throws { Get-InstallerVersion -ProjectPath $fixtureProject -RequestedVersion '0.3.0-beta' } 'mismatched override rejected'
Throws { Get-InstallerVersion -ProjectPath $fixtureProject -RequestedVersion 'nonsense' } 'malformed override rejected'
Throws { Get-InstallerVersion -ProjectPath $fixtureProject -BuildIdentity '..\bad' } 'unsafe build identity rejected'
Throws { Reset-InstallerOutput -InstallerRoot $scratch -RelativePath '..' } 'output escape rejected'
New-Item -ItemType Directory -Path (Join-Path $scratch 'publish\win-x64') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $scratch 'publish\win-x64\stale.txt') 'stale'
Reset-InstallerOutput -InstallerRoot $scratch -RelativePath 'publish\win-x64'
Check (-not (Test-Path -LiteralPath (Join-Path $scratch 'publish\win-x64'))) 'known stale output cleared'

$fixtures = Join-Path $scratch 'fixtures'
foreach ($relative in @('Paperbunkr\cache', 'Library', 'WithLink', 'Empty', 'ReadOnly', 'Redirected\Paperbunkr')) {
    New-Item -ItemType Directory -Path (Join-Path $fixtures $relative) -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $fixtures 'Paperbunkr\paperbunkr.db') 'fixture'
Set-Content -LiteralPath (Join-Path $fixtures 'Paperbunkr\cache\cover.png') 'fixture'
Set-Content -LiteralPath (Join-Path $fixtures 'Paperbunkr\cache\読書.png') 'unicode fixture'
Set-Content -LiteralPath (Join-Path $fixtures 'ReadOnly\keep.txt') 'read-only fixture'
(Get-Item -LiteralPath (Join-Path $fixtures 'ReadOnly\keep.txt')).IsReadOnly = $true
Set-Content -LiteralPath (Join-Path $fixtures 'Library\keep.cbz') 'do not delete'
New-Item -ItemType Junction -Path (Join-Path $fixtures 'WithLink\LinkedLibrary') -Target (Join-Path $fixtures 'Library') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $fixtures 'LinkedProfile') -Target (Join-Path $fixtures 'Library') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $fixtures 'LinkedRoot') -Target (Join-Path $fixtures 'Redirected') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $scratch 'publish\win-x64') -Force | Out-Null
New-Item -ItemType Junction -Path (Join-Path $scratch 'publish\win-x64\linked') -Target (Join-Path $fixtures 'Library') | Out-Null
Throws { Reset-InstallerOutput -InstallerRoot $scratch -RelativePath 'publish\win-x64' } 'linked build output refused'
Check (Test-Path -LiteralPath (Join-Path $fixtures 'Library\keep.cbz')) 'build cleanup preserves external sentinel'
Compile (Join-Path $PSScriptRoot 'InstallerLogicTests.iss') @() $scratch
$report = Join-Path $scratch 'results.txt'
$process = Start-Process -FilePath (Join-Path $scratch 'InstallerLogicTests.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=`"$(Join-Path $scratch 'native.log')`"", "/FIXTURES=`"$fixtures`"", "/RESULTS=`"$report`"") -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Native verification timed out.' }
Check ($process.ExitCode -ne 0) 'initialization rejection returns a failure exit status'
Check (Test-Path -LiteralPath $report) 'native helper test report exists'
$nativeReport = Get-Content -LiteralPath $report -Raw
Write-Output $nativeReport
Check ($nativeReport -match 'Failures: 0\b' -and $nativeReport -notmatch '(?m)^FAIL ') 'native shared Pascal behavior checks'

# Compile the complete production script against a tiny fixture payload in both variants.
# The fixture is only a source file: neither compiled installer is executed.
$payload = Join-Path $scratch 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Set-Content -LiteralPath (Join-Path $payload 'Paperbunkr.App.exe') 'fixture - do not run'
$relativePayload = [IO.Path]::GetRelativePath($installerRoot, $payload)
$fixtureLicense = Join-Path $scratch 'License.txt'
Set-Content -LiteralPath $fixtureLicense 'Installer compilation fixture licence. Not for distribution.'
$defines = @('/DMyAppVersion=0.7.10-beta', '/DMyAppVersionNumeric=0.7.10.0', "/DPublishDir=$relativePayload", "/DLicensePath=$fixtureLicense")
Compile (Join-Path $installerRoot 'Installer.iss') $defines (Join-Path $scratch 'without-thumbnails')
Check $true 'production script compiles without thumbnail handler'
Set-Content -LiteralPath (Join-Path $payload 'Paperbunkr.ShellThumbnails.dll') 'fixture'
Compile (Join-Path $installerRoot 'Installer.iss') $defines (Join-Path $scratch 'with-thumbnails')
Check $true 'production script compiles with thumbnail handler'
$missingVersionLog = & $Compiler /Qp (Join-Path $installerRoot 'Installer.iss') 2>&1
Check ($LASTEXITCODE -ne 0 -and ($missingVersionLog -join "`n") -match 'Supply /DMyAppVersion') 'direct compile refuses stale version fallback'
$mismatchLog = & $Compiler /Qp /DMyAppVersion=0.7.9-beta /DMyAppVersionNumeric=0.7.10.0 (Join-Path $installerRoot 'Installer.iss') 2>&1
Check ($LASTEXITCODE -ne 0 -and ($mismatchLog -join "`n") -match 'same beta release') 'direct compile refuses conflicting version identities'
Write-Output "Installer verification passed: $passed PowerShell/build checks. Native checks listed above. Artifacts: $scratch"
