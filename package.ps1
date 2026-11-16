param(
    [string]$Version = 'v1.21.4',
    [string]$Suffix = '',
    [switch]$SkipBuild,
    [string]$OutputDir = 'dist'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SkipBuild) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }
}

$exe = Join-Path $root 'dist\CapsLockLite.exe'
if (-not (Test-Path $exe)) { throw "Missing executable: $exe" }

$dist = Join-Path $root $OutputDir
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$releaseZip = Join-Path $dist ("CapsLockLite-$Version-win64$Suffix.zip")
$sourceZip = Join-Path $dist ("CapsLockLite-$Version-source$Suffix.zip")
$pluginZip = Join-Path $dist ("CapsLockLite-$Version-plugin-screenshot$Suffix.zip")
$exeSums = Join-Path $dist 'CapsLockLite-SHA256.txt'
$zipSums = Join-Path $dist 'SHA256SUMS.txt'

# Older packages are archived, never deleted: dist\archive\vX.Y.Z\ keeps every
# build reproducible (the exe in dist root is overwritten by each build, but the
# archived zips still hold it). Only the current version stays in dist root so a
# user cannot accidentally run an old build.
$archiveRoot = Join-Path $dist 'archive'
$oldZips = @(Get-ChildItem -LiteralPath $dist -Filter 'CapsLockLite-v*-*.zip' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike ("CapsLockLite-$Version-*") })
if ($oldZips.Count -gt 0) {
    $newest = $oldZips | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $newestVersion = 'unversioned'
    if ($newest.Name -match '^CapsLockLite-(v[0-9][^-]*)-') { $newestVersion = $matches[1] }
    foreach ($old in $oldZips) {
        $oldVersion = 'unversioned'
        if ($old.Name -match '^CapsLockLite-(v[0-9][^-]*)-') { $oldVersion = $matches[1] }
        $target = Join-Path $archiveRoot $oldVersion
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Move-Item -LiteralPath $old.FullName -Destination (Join-Path $target $old.Name) -Force
    }
    # The checksum files belong to the build that was just archived.
    foreach ($name in @('CapsLockLite-SHA256.txt', 'SHA256SUMS.txt')) {
        $sumPath = Join-Path $dist $name
        if (Test-Path -LiteralPath $sumPath) {
            $sumTarget = Join-Path $archiveRoot $newestVersion
            New-Item -ItemType Directory -Path $sumTarget -Force | Out-Null
            Move-Item -LiteralPath $sumPath -Destination (Join-Path $sumTarget $name) -Force
        }
    }
    Write-Output ("Archived previous packages: " + $archiveRoot)
}

Remove-Item -LiteralPath $releaseZip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $sourceZip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $pluginZip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $exeSums -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipSums -Force -ErrorAction SilentlyContinue

# Hash the executable before it is archived. Users can compare this value with
# a separately published SHA-256 or with the signed file's digest.
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
("$exeHash  CapsLockLite.exe") | Set-Content -LiteralPath $exeSums -Encoding ASCII

# Release package intentionally contains no preview screenshots.
# It ships PLUGIN.md plus a ready-to-import sample plugin so users can try the
# plugin system without downloading anything else.
Compress-Archive -Path $exe, $exeSums, (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), `
    (Join-Path $root 'PLUGIN.md'), (Join-Path $root 'samples\plugins\screenshot-pro.cs') `
    -DestinationPath $releaseZip -CompressionLevel Optimal -Force

$stageRoot = Join-Path $env:TEMP ('CapsLockLite-package-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $stageRoot 'CapsLockLite-source'
try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $root 'src') $stage -Recurse
    Copy-Item (Join-Path $root 'samples') $stage -Recurse
    New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
    foreach ($name in @('app.ico', 'app-dark.ico', 'logo.png', 'logo-dark.png', 'logo-option-1.png', 'make_final_icon.py')) {
        Copy-Item (Join-Path $root ('assets\' + $name)) (Join-Path $stage ('assets\' + $name))
    }
    foreach ($name in @('app.manifest', 'build.ps1', 'package.ps1', 'sign.ps1', 'PACKAGING.md', 'LICENSE', 'README.md', 'README.en.md', 'SOURCE.md', 'PLUGIN.md', 'CONTRIBUTING.md', 'CHANGELOG.md', 'SECURITY.md', 'CODE_OF_CONDUCT.md', '.gitignore')) {
        $path = Join-Path $root $name
        if (Test-Path $path) { Copy-Item $path $stage }
    }
    if (Test-Path (Join-Path $root 'docs')) {
        Copy-Item (Join-Path $root 'docs') $stage -Recurse
    }

    if (Test-Path (Join-Path $stage 'preview')) { throw 'Source staging unexpectedly contains preview.' }
    Compress-Archive -Path $stage -DestinationPath $sourceZip -CompressionLevel Optimal -Force
}
finally {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# Plugin package: importable source file, the authoring spec and install notes.
# This script stays pure ASCII: Windows PowerShell decodes .ps1 as ANSI, so any
# non-ASCII literal here would be mis-read. The notes live in a UTF-8 data file.
$pluginStageRoot = Join-Path $env:TEMP ('CapsLockLite-plugin-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $pluginStageRoot -Force | Out-Null
    Copy-Item (Join-Path $root 'samples\plugins\screenshot-pro.cs') (Join-Path $pluginStageRoot 'screenshot-pro.cs')
    Copy-Item (Join-Path $root 'PLUGIN.md') (Join-Path $pluginStageRoot 'PLUGIN.md')

    $pluginReadme = [IO.File]::ReadAllText(
        (Join-Path $root 'samples\plugins\README.txt'),
        [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $pluginStageRoot 'README.txt'), $pluginReadme,
        (New-Object System.Text.UTF8Encoding($true)))

    Compress-Archive -Path (Join-Path $pluginStageRoot '*') -DestinationPath $pluginZip -CompressionLevel Optimal -Force
}
finally {
    Remove-Item -LiteralPath $pluginStageRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($zip in @($releaseZip, $sourceZip, $pluginZip)) {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '(^|\\)preview(\\|$)') {
                throw "Package contains preview data: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$sumLines = @()
foreach ($zip in @($releaseZip, $sourceZip, $pluginZip)) {
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $sumLines += ("{0}  {1}" -f $hash, [IO.Path]::GetFileName($zip))
}
$sumLines | Set-Content -LiteralPath $zipSums -Encoding ASCII

Get-Item $releaseZip, $sourceZip, $pluginZip, $zipSums | ForEach-Object {
    Write-Output ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length)
}
