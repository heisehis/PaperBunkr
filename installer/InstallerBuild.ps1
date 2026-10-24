# Shared version validation and guarded output preparation for the build and verification scripts.
function Get-InstallerVersion {
    param([Parameter(Mandatory)][string]$ProjectPath, [string]$RequestedVersion, [string]$BuildIdentity)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $assemblyVersion = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ($assemblyVersion -notmatch '^(\d+)\.(\d+)\.(\d+)\.0$') {
        throw "Expected a major.minor.patch.0 <Version> in $ProjectPath; found '$assemblyVersion'."
    }
    $numeric = [version]$assemblyVersion
    if (@($numeric.Major, $numeric.Minor, $numeric.Build) | Where-Object { $_ -gt 65535 }) {
        throw 'Version components must fit the Windows file-version resource (0..65535).'
    }
    $release = "$($numeric.Major).$($numeric.Minor).$($numeric.Build)-beta"
    if ($RequestedVersion) {
        if ($RequestedVersion -notmatch '^(\d+)\.(\d+)\.(\d+)-beta(?:-[A-Za-z0-9]+)?$') {
            throw "Invalid installer version '$RequestedVersion'; expected major.minor.patch-beta[-build]."
        }
        $requestedNumeric = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
        if ($requestedNumeric -ne $numeric) {
            throw "Installer version '$RequestedVersion' does not match the app's $assemblyVersion."
        }
        $release = $RequestedVersion
    } elseif ($BuildIdentity) {
        if ($BuildIdentity -notmatch '^[A-Za-z0-9]+$') { throw 'Invalid local build identity.' }
        $release = "$release-$BuildIdentity"
    }
    [pscustomobject]@{ Display = $release; Numeric = $numeric.ToString(4) }
}

function Reset-InstallerOutput {
    param([Parameter(Mandatory)][string]$InstallerRoot, [Parameter(Mandatory)][string]$RelativePath)
    $root = [IO.Path]::GetFullPath($InstallerRoot)
    $target = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    $allowed = @('publish\win-x64', 'publish\shell-thumbnails') | ForEach-Object {
        [IO.Path]::GetFullPath((Join-Path $root $_))
    }
    if ($target -notin $allowed) { throw "Refusing to clear unexpected installer output: $target" }
    # Check every existing ancestor, including the installer root, before recursive removal.
    $ancestor = $target
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing installer output through a reparse point: $ancestor"
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    if (Test-Path -LiteralPath $target) {
        $links = Get-ChildItem -LiteralPath $target -Force -Recurse -ErrorAction Stop |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
        if ($links) { throw "Refusing installer output containing reparse points: $target" }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
