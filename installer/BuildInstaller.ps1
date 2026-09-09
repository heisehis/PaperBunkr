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
    $Version = "0.3.0-beta-$shortCommit"
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

# Generate the combined license file (Installer.iss's LicenseFile, shown before the install-dir
# step - see docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 3). Inno's
# LicenseFile is a single accept-gate for exactly one document, but the repo has two: LICENSE
# (AGPLv3, the code license) and TERMS.md (usage/liability terms whose own opening line -
# "By downloading, installing, or running Paperbunkr... you agree to the following" - is the sort
# of claim that should be backed by a real install-time accept). Concatenate LICENSE then TERMS.md,
# stripping TERMS.md's markdown down to plain text line by line (Inno's license viewer renders no
# markdown).
$licensePath = Join-Path $repoRoot "LICENSE"
$termsPath = Join-Path $repoRoot "TERMS.md"
$combinedLicensePath = Join-Path $publishDir "..\License.txt"
$licenseLines = @()
if (Test-Path $licensePath) {
    $licenseLines += Get-Content $licensePath
}
if (Test-Path $termsPath) {
    $termsPlain = (Get-Content $termsPath) `
        -replace '^#{1,6}\s+', '' `
        -replace '^>\s?', '' `
        -replace '\*\*(.+?)\*\*', '$1' `
        -replace '`(.+?)`', '$1' `
        -replace '\[(.+?)\]\((.+?)\)', '$1 ($2)' `
        -replace '^[-*]\s+', '  - '
    $licenseLines += @(
        "",
        ("=" * 80),
        "ADDITIONAL TERMS OF USE",
        ("=" * 80),
        ""
    ) + $termsPlain
}
if ($licenseLines.Count -eq 0) {
    $licenseLines = @("See LICENSE and TERMS.md in the installation folder.")
}
Set-Content -Path $combinedLicensePath -Value $licenseLines -Encoding UTF8
Write-Output "Wrote $combinedLicensePath"

# (No InfoAfter.txt / InfoBefore.txt generation any more - the installer has no "Information"
#  pages: release notes moved to the app's first-run Welcome screen, and the post-install
#  quick-start + wiki link are now the Finished page's own FinishedLabel copy and its two
#  postinstall [Run] checkboxes. See Installer.iss.)

# Compile the installer.
$setupFileParam = "PaperbunkrSetup-$Version"
Write-Output "Running Inno Setup..."
& $innoSetupPath (Join-Path $installerDir "Installer.iss") "/DMyAppVersion=$Version" "/DMyAppSetupFile=$setupFileParam"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed."
    exit 1
}

Write-Output "Installer built: $installerDir\Output\$setupFileParam.exe"
