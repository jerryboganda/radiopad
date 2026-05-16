. "$PSScriptRoot\lib.ps1"

$payload = Read-HookPayload
$touchedPaths = Get-HookPathStrings $payload
$validationSensitive = $false

foreach ($filePath in $touchedPaths) {
    $normalized = $filePath -replace '\\', '/'
    if ($normalized -match '^(src|app|daemon|tests|scripts)/' -or $normalized -match '(^|/)(package\.json|pnpm-lock\.yaml|tsconfig\.json|next\.config\.ts|vitest\.config\.ts)$') {
        $validationSensitive = $true
        break
    }
}

if ($validationSensitive) {
    Write-HookResult ([ordered]@{
        continue = $true
        systemMessage = 'Open Design validation hint: source, test, script, or config files changed. Run `pnpm typecheck` and the narrowest relevant `pnpm test` before finalizing, or state why they could not run.'
    })
}
else {
    Write-HookResult ([ordered]@{ continue = $true })
}