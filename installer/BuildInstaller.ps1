# Builds the Paperbunkr alpha installer (P7, docs/alpha-todo.md).
#
# Two steps: publish the App project self-contained for win-x64, then hand that output to Inno
# Setup 6. Modeled on _reference/ComicRackCE/BuildInstaller.ps1's shape (locate ISCC.exe, derive a
# version string, invoke the compiler with /D params) but adapted - there's no CE-style
# CurrentCommit.txt here, so the version defaults to the current short commit hash instead.
#
# Usage: pwsh installer\BuildInstaller.ps1 [-Version "1.2.3"]
# Requires Inno Setup 6 (https://jrsoftware.org/isdl.php) installed at one of the usual locations.

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerDir = $PSScriptRoot

# Locate Inno Setup 6.
$innoSetupCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$innoSetupPath = $innoSetupCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $innoSetupPath) {
    Write-Error "Inno Setup 6 is not installed in any of the expected locations: $($innoSetupCandidates -join ', '). Install it from https://jrsoftware.org/isdl.php"
    exit 1
}

# Derive a version string if the caller didn't pass one.
if (-not $Version) {
    $shortCommit = (git -C $repoRoot rev-parse --short HEAD).Trim()
    $Version = "0.1.0-alpha-$shortCommit"
}

Write-Output "Building Paperbunkr installer version $Version"

# Publish self-contained win-x64 - bundles the .NET 8 runtime, so the installer needs no
# prerequisite-detection step the way CE's does for .NET Framework 4.8 (see Installer.iss header).
$publishDir = Join-Path $installerDir "publish\win-x64"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Write-Output "Publishing self-contained win-x64 build to $publishDir..."
dotnet publish (Join-Path $repoRoot "src\Paperbunkr.App\Paperbunkr.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit 1
}

# Compile the installer.
$setupFileParam = "PaperbunkrSetup-$Version"
Write-Output "Running Inno Setup..."
& $innoSetupPath (Join-Path $installerDir "Installer.iss") "/DMyAppVersion=$Version" "/DMyAppSetupFile=$setupFileParam"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed."
    exit 1
}

Write-Output "Installer built: $installerDir\Output\$setupFileParam.exe"
