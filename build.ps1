$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = $env:CSC
if ([string]::IsNullOrWhiteSpace($csc) -or -not (Test-Path $csc)) {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { $csc = $candidate; break }
    }
}
if ([string]::IsNullOrWhiteSpace($csc) -or -not (Test-Path $csc)) {
    throw 'Cannot find csc.exe. Install .NET Framework 4.8 or set the CSC environment variable.'
}
$framework = Split-Path -Parent $csc
$out = Join-Path $root 'dist\CapsLockLite.exe'

New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null

$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw 'No source files found.' }

$refs = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'System.Web.dll'),
    (Join-Path $framework 'System.IO.Compression.dll'),
    (Join-Path $framework 'System.IO.Compression.FileSystem.dll')
)

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/langversion:5',
    '/optimize+',
    '/debug-',
    ('/out:' + $out),
    ('/win32icon:' + (Join-Path $root 'assets\app.ico')),
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/resource:' + (Join-Path $root 'assets\app.ico') + ',CapsLockLite.app.ico'),
    ('/resource:' + (Join-Path $root 'assets\app-dark.ico') + ',CapsLockLite.app-dark.ico'),
    ('/resource:' + (Join-Path $root 'assets\logo.png') + ',CapsLockLite.logo.png'),
    ('/resource:' + (Join-Path $root 'assets\logo-dark.png') + ',CapsLockLite.logo-dark.png')
)
foreach ($r in $refs) { $arguments += ('/r:' + $r) }
$arguments += $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

if (-not [string]::IsNullOrWhiteSpace($env:SIGN_CERT_SHA1)) {
    & (Join-Path $root 'sign.ps1') -File $out
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed with exit code $LASTEXITCODE" }
}

$info = Get-Item $out
Write-Output ("Built: {0} ({1:N0} bytes)" -f $info.FullName, $info.Length)
