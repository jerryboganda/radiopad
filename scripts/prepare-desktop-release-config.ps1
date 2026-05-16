param(
    [string]$UpdaterPubkey,
    [string]$WindowsCertificateThumbprint,
    [string]$MacSigningIdentity,
    [switch]$RequireUpdaterPubkey
)

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$configPath = Join-Path $repo 'desktop\src-tauri\tauri.conf.json'
$config = Get-Content $configPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($UpdaterPubkey)) {
    if ($RequireUpdaterPubkey) {
        throw 'TAURI_SIGNING_PUBLIC_KEY is required for production desktop release builds.'
    }
} else {
    $config.plugins.updater.pubkey = $UpdaterPubkey.Trim()
}

if (-not [string]::IsNullOrWhiteSpace($WindowsCertificateThumbprint)) {
    $config.bundle.windows.certificateThumbprint = $WindowsCertificateThumbprint.Trim()
}

if (-not [string]::IsNullOrWhiteSpace($MacSigningIdentity)) {
    $config.bundle.macOS.signingIdentity = $MacSigningIdentity.Trim()
}

if ($RequireUpdaterPubkey -and [string]::IsNullOrWhiteSpace($config.plugins.updater.pubkey)) {
    throw 'desktop/src-tauri/tauri.conf.json still has an empty updater pubkey.'
}

$json = $config | ConvertTo-Json -Depth 64
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Prepared desktop release config at $configPath"
