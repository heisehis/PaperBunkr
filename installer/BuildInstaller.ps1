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

# Require the .NET SDK before doing anything else - the publish step below needs it, and a missing
# 'dotnet' otherwise fails deep in the script with a less obvious message.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "The .NET SDK ('dotnet') was not found on PATH. Install it from https://dotnet.microsoft.com/download"
    exit 1
}

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

# Derive a version string if the caller didn't pass one. Fall back to "dev" when git isn't
# available or this isn't a git checkout (CI/container builds), rather than aborting.
if (-not $Version) {
    $shortCommit = "dev"
    try {
        $gitHash = (git -C $repoRoot rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitHash)) {
            $shortCommit = $gitHash.Trim()
        }
    } catch {
        # git not on PATH - keep the "dev" fallback.
    }
    $Version = "0.3.0-beta-$shortCommit"
}

# Inno's VersionInfoVersion (the setup exe's own VS_FIXEDFILEINFO resource) needs a pure
# numeric x.y.z.w - the "-beta" / "-beta-<hash>" suffix in $Version is not valid there. Derive it
# from $Version's leading numeric run rather than hand-maintaining a second version string in
# Installer.iss (which CI never overrode, so it silently lagged the csproj on every bump).
if ($Version -match '^(\d+(?:\.\d+){1,3})') {
    $numericParts = $Matches[1].Split('.')
} else {
    $numericParts = @('0', '0', '0')
}
while ($numericParts.Count -lt 4) { $numericParts += '0' }
$VersionNumeric = ($numericParts[0..3] -join '.')

Write-Output "Building Paperbunkr installer version $Version (file-version resource $VersionNumeric)"

# Publish self-contained win-x64 - bundles the .NET 8 runtime, so the installer needs no
# prerequisite-detection step the way CE's does for .NET Framework 4.8 (see Installer.iss header).
$publishDir = Join-Path $installerDir "publish\win-x64"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

# ReadyToRun: AOT-compile the IL to native up front so end users don't pay the cold-start JIT
# tax on first launch (the startup pipeline builds ~30 view-models + the full MainWindow visual
# tree on the UI thread - measured at ~1.6s of pure JIT on a cold run). Costs ~20-30MB of extra
# payload in the already-self-contained bundle and a slower publish; worth it for a desktop app
# whose whole first impression is how fast the window appears.
Write-Output "Publishing self-contained win-x64 build to $publishDir..."
dotnet publish (Join-Path $repoRoot "src\Paperbunkr.App\Paperbunkr.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true `
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
    # Get-Content (no -Raw) yields one string per line, so ^/$ anchor per line without needing
    # (?m). Order matters: strip **bold** before *italic*, and use character classes that can't
    # run past a delimiter so a stray token on a later edit can't swallow the rest of the line.
    $termsPlain = (Get-Content $termsPath) `
        -replace '^\s{0,3}#{1,6}\s+', '' `
        -replace '^\s{0,3}>\s?', '' `
        -replace '\*\*([^*]+?)\*\*', '$1' `
        -replace '__([^_]+?)__', '$1' `
        -replace '(?<![\*\w])\*([^*\r\n]+?)\*(?!\w)', '$1' `
        -replace '`([^`\r\n]+?)`', '$1' `
        -replace '\[([^\]]+?)\]\(([^)]+?)\)', '$1 ($2)' `
        -replace '^\s{0,3}[-*+]\s+', '  - '
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
& $innoSetupPath (Join-Path $installerDir "Installer.iss") "/DMyAppVersion=$Version" "/DMyAppVersionNumeric=$VersionNumeric" "/DMyAppSetupFile=$setupFileParam"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed."
    exit 1
}

Write-Output "Installer built: $installerDir\Output\$setupFileParam.exe"
