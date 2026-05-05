param(
    [ValidateSet('msi', 'nsis', 'msi,nsis')]
    [string]$Bundles = 'msi,nsis',
    [switch]$SkipFrontendInstall,
    [switch]$SkipTauriCliInstall,
    [switch]$NoFrozenLockfile,
    [switch]$DisableUpdaterArtifacts
)

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repo

Write-Host '== Check Windows desktop build prerequisites ==' -ForegroundColor Cyan
$requiredTools = @(
    @{ Name = 'dotnet'; Hint = 'Install the .NET 8 SDK.' },
    @{ Name = 'node'; Hint = 'Install Node.js 20 LTS or newer.' },
    @{ Name = 'pnpm'; Hint = 'Run corepack enable, then corepack prepare pnpm@9.15.9 --activate.' },
    @{ Name = 'cargo'; Hint = 'Install Rust stable via rustup.rs.' }
)
foreach ($tool in $requiredTools) {
    $command = Get-Command $tool.Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "$($tool.Name) not found on PATH. $($tool.Hint)"
    }
}
if (-not (Get-Command 'cl' -ErrorAction SilentlyContinue)) {
    Write-Warning 'cl.exe was not found. Install Microsoft C++ Build Tools with the Desktop development with C++ workload before building Tauri.'
}
Write-Host ''

Write-Host '== Publish .NET backend sidecar ==' -ForegroundColor Cyan
& dotnet publish backend\RadioPad.Api\src\RadioPad.Api\RadioPad.Api.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish\sidecar-win-x64

New-Item -ItemType Directory -Force desktop\src-tauri\binaries | Out-Null
Copy-Item -Force `
    publish\sidecar-win-x64\RadioPad.Api.exe `
    desktop\src-tauri\binaries\radiopad-api-x86_64-pc-windows-msvc.exe
Write-Host ''

Write-Host '== Install frontend dependencies ==' -ForegroundColor Cyan
if ($SkipFrontendInstall) {
    Write-Host 'Skipped by -SkipFrontendInstall.'
} elseif ($NoFrozenLockfile) {
    & pnpm install --no-frozen-lockfile
} else {
    & pnpm install --frozen-lockfile
}
Write-Host ''

Write-Host '== Build frontend static export ==' -ForegroundColor Cyan
& pnpm --filter '@radiopad/frontend' build
if (-not (Test-Path frontend\out\index.html)) {
    throw 'frontend/out/index.html was not produced. The desktop shell requires a static Next.js export.'
}
Write-Host ''

Write-Host '== Ensure Tauri CLI ==' -ForegroundColor Cyan
if (Get-Command 'cargo-tauri' -ErrorAction SilentlyContinue) {
    Write-Host 'cargo-tauri found.'
} elseif ($SkipTauriCliInstall) {
    throw 'cargo-tauri not found and -SkipTauriCliInstall was specified.'
} else {
    & cargo install tauri-cli --locked
}
Write-Host ''

Write-Host '== Build Windows installer bundle ==' -ForegroundColor Cyan
Push-Location desktop
try {
    $tauriArgs = @('tauri', 'build', '--bundles', $Bundles)
    if ($DisableUpdaterArtifacts) {
        $tauriArgs += @('--config', '{"bundle":{"createUpdaterArtifacts":false}}')
    }
    & cargo @tauriArgs
} finally {
    Pop-Location
}
Write-Host ''

Write-Host '== Installer output ==' -ForegroundColor Cyan
$bundleRoot = Join-Path $repo 'desktop\src-tauri\target\release\bundle'
if (-not (Test-Path $bundleRoot)) {
    throw "Bundle output folder missing: $bundleRoot"
}
Get-ChildItem $bundleRoot -Recurse -Include *.msi,*.exe |
    Sort-Object LastWriteTime -Descending |
    Select-Object FullName, Length, LastWriteTime |
    Format-Table -AutoSize
Write-Host ''

Write-Host 'Windows 8 note: RadioPad desktop targets the repo stack (Tauri 2 + ASP.NET Core/.NET 8). Current Microsoft .NET support excludes Windows 8.1 and Windows 7; validate on Windows 10/11 for supported behavior.' -ForegroundColor Yellow
