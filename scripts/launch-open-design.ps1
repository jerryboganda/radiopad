# Launcher for Open Design — invoked by the Desktop shortcut.
# Self-elevates to Administrator (in case the shortcut's "Run as admin" bit
# was lost), then starts `npm run dev:all` in the repo directory and opens
# the browser once the web server is responding.

$ErrorActionPreference = 'Stop'

# --- self-elevate -----------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process -FilePath 'powershell.exe' `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") `
        -Verb RunAs
    exit
}

# --- repo dir ---------------------------------------------------------------
$repo = 'C:\Users\Dr Faisal Maqsood PC\.gemini\antigravity\scratch\open-design'
if (-not (Test-Path $repo)) {
    Write-Host "Open Design folder not found at: $repo" -ForegroundColor Red
    Write-Host 'Press any key to exit.' -ForegroundColor Yellow
    [void][System.Console]::ReadKey($true)
    exit 1
}
Set-Location -Path $repo
$Host.UI.RawUI.WindowTitle = 'Open Design — daemon + web'
Write-Host '== Open Design launcher ==' -ForegroundColor Cyan
Write-Host "Repo : $repo"
Write-Host "User : $env:USERNAME (elevated)"
Write-Host ''

# --- ensure npm exists ------------------------------------------------------
$npm = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npm) {
    Write-Host 'npm not found on PATH. Install Node.js first: https://nodejs.org/' -ForegroundColor Red
    Write-Host 'Press any key to exit.' -ForegroundColor Yellow
    [void][System.Console]::ReadKey($true)
    exit 1
}

# --- background browser opener ---------------------------------------------
# Polls localhost:3000 every second; when it responds, opens the default
# browser. Runs as a job so we don't block the dev server's stdout.
$openerJob = Start-Job -ScriptBlock {
    $url = 'http://localhost:3000'
    for ($i = 0; $i -lt 90; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -lt 500) {
                Start-Process $url
                return
            }
        } catch { }
        Start-Sleep -Seconds 1
    }
}

try {
    Write-Host 'Starting daemon + Next.js dev server (Ctrl+C twice to stop)...' -ForegroundColor Green
    Write-Host ''
    & npm run dev:all
}
finally {
    if ($openerJob) {
        Stop-Job $openerJob -ErrorAction SilentlyContinue | Out-Null
        Remove-Job $openerJob -Force -ErrorAction SilentlyContinue | Out-Null
    }
    Write-Host ''
    Write-Host 'Open Design stopped. Press any key to close this window.' -ForegroundColor Yellow
    [void][System.Console]::ReadKey($true)
}
