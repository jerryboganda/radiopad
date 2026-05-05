param(
    [switch]$Commit,
    [switch]$Push,
    [switch]$UseGenericIdentity,
    [switch]$Yes,
    [string]$Message = 'ci: add RadioPad desktop cloud build'
)

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repo

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git is not installed or is not on PATH.'
}

$branch = (& git branch --show-current).Trim()
if (-not $branch) {
    throw 'Could not determine the current git branch.'
}

$origin = (& git remote get-url origin 2>$null).Trim()
if (-not $origin) {
    throw 'No git origin remote is configured. Add a GitHub remote first, then rerun this script.'
}

Write-Host 'RadioPad desktop cloud-build publisher' -ForegroundColor Cyan
Write-Host "Repository: $repo"
Write-Host "Branch    : $branch"
Write-Host "Origin    : $origin"
Write-Host ''

if (-not $Yes) {
    Write-Host 'This will stage the RadioPad project folders needed by GitHub Actions.' -ForegroundColor Yellow
    Write-Host 'It will not stage generated publish/, desktop binaries/, or Tauri target/ outputs.' -ForegroundColor Yellow
    $answer = Read-Host 'Continue? Type YES to continue'
    if ($answer -ne 'YES') {
        Write-Host 'Cancelled.'
        exit 0
    }
}

$paths = @(
    '.gitignore',
    '.github/workflows/desktop-windows-test-build.yml',
    'backend',
    'frontend',
    'desktop',
    'mobile',
    'cli',
    'docs',
    'openapi',
    'rulebooks',
    'templates',
    'scripts/build-radiopad-desktop-windows.ps1',
    'scripts/prepare-desktop-cloud-build-commit.ps1',
    'package.json',
    'pnpm-workspace.yaml',
    'pnpm-lock.yaml',
    'PROGRESS.md',
    'AGENTS.md',
    'CLAUDE.md'
)

$addArgs = @('add', '--') + $paths
Invoke-Git @addArgs

Write-Host ''
Write-Host 'Staged changes:' -ForegroundColor Cyan
$statusArgs = @('status', '--short', '--') + $paths
Invoke-Git @statusArgs

if ($Commit) {
    $configuredNameValue = & git config --get user.name 2>$null
    $configuredEmailValue = & git config --get user.email 2>$null
    $configuredName = if ($configuredNameValue) { $configuredNameValue.Trim() } else { '' }
    $configuredEmail = if ($configuredEmailValue) { $configuredEmailValue.Trim() } else { '' }
    if (-not $configuredName -or -not $configuredEmail) {
        if ($UseGenericIdentity) {
            Invoke-Git config user.name 'RadioPad Builder'
            Invoke-Git config user.email 'radiopad-builder@users.noreply.github.com'
            Write-Host 'Configured repo-local Git identity: RadioPad Builder <radiopad-builder@users.noreply.github.com>' -ForegroundColor Yellow
        } else {
            throw 'Git commit identity is not configured. Rerun with -UseGenericIdentity, or set repo-local user.name and user.email first.'
        }
    }

    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'No staged changes to commit.' -ForegroundColor Yellow
    } else {
        Invoke-Git commit -m $Message
    }
}

if ($Push) {
    Invoke-Git push origin $branch
    Write-Host ''
    Write-Host 'Pushed. GitHub Actions should start desktop-windows-test-build automatically.' -ForegroundColor Green
    Write-Host 'Open the Actions tab for the repository and download the radiopad-windows-unsigned artifact when the run completes.' -ForegroundColor Green
}