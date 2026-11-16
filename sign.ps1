param(
    [string]$File = (Join-Path $PSScriptRoot 'dist\CapsLockLite.exe'),
    [string]$CertSha1 = $env:SIGN_CERT_SHA1,
    [string]$TimestampUrl = $env:SIGN_TIMESTAMP_URL
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CertSha1)) {
    throw 'SIGN_CERT_SHA1 is not set. Set it to the SHA-1 thumbprint of a code-signing certificate.'
}
if (-not (Test-Path -LiteralPath $File)) {
    throw "File not found: $File"
}

$signtool = $env:SIGNTOOL
if ([string]::IsNullOrWhiteSpace($signtool) -or -not (Test-Path -LiteralPath $signtool)) {
    $roots = @()
    $x86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($x86)) { $roots += (Join-Path $x86 'Windows Kits\10\bin') }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $roots += (Join-Path $env:ProgramFiles 'Windows Kits\10\bin') }
    foreach ($root in $roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { $signtool = $candidate.FullName; break }
    }
}
if ([string]::IsNullOrWhiteSpace($signtool) -or -not (Test-Path -LiteralPath $signtool)) {
    throw 'signtool.exe not found. Install the Windows SDK or set SIGNTOOL to its full path.'
}

$arguments = @('sign', '/sha1', $CertSha1, '/fd', 'SHA256')
if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
}
$arguments += @('/v', $File)

& $signtool @arguments
if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE"
}

Write-Output ("Signed: {0}" -f $File)
