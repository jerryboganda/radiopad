. "$PSScriptRoot\lib.ps1"

$payload = Read-HookPayload
$command = Get-HookCommand $payload
$toolName = Get-HookToolName $payload

$riskyCommands = @(
    [pscustomobject]@{
        Pattern = '\brm\s+-(?:[A-Za-z]*r[A-Za-z]*f|[A-Za-z]*f[A-Za-z]*r)[^\n]*(?:\s/|\s~|\$HOME|%USERPROFILE%|[A-Za-z]:\\)'
        Reason = 'recursive forced deletion outside a clearly scoped project path'
    },
    [pscustomobject]@{
        Pattern = '\bgit\s+reset\s+--hard\b'
        Reason = 'hard reset can discard user work'
    },
    [pscustomobject]@{
        Pattern = '\bgit\s+clean\s+-[^\n]*[fdx][^\n]*\b'
        Reason = 'git clean can delete untracked user files'
    },
    [pscustomobject]@{
        Pattern = '\bRemove-Item\b[^\n]*(?:-Recurse|-r)\b[^\n]*(?:-Force|-fo)\b'
        Reason = 'forced recursive deletion can discard user work'
    },
    [pscustomobject]@{
        Pattern = '\b(?:del|erase|rd|rmdir)\b[^\n]*(?:/s|-s)\b[^\n]*(?:[A-Za-z]:\\|%USERPROFILE%|%HOMEPATH%|\\)'
        Reason = 'Windows recursive deletion can discard user work'
    },
    [pscustomobject]@{
        Pattern = '(?:curl|Invoke-WebRequest|iwr)\b[^\n|]*\|\s*(?:sh|bash|powershell|pwsh|iex)\b'
        Reason = 'remote script execution needs explicit human review'
    }
)

$heavyCommands = @(
    [pscustomobject]@{
        Pattern = '\bdotnet\s+build\b'
        Reason = 'a full dotnet build belongs in CI (ci.yml -> backend job), not this laptop'
    },
    [pscustomobject]@{
        Pattern = '\bdotnet\s+test\b(?![^\n]*--filter)'
        Reason = 'the full dotnet suite belongs in CI; locally run `dotnet test --filter <Name>`'
    },
    [pscustomobject]@{
        Pattern = '\bpnpm\s+(?:--filter\s+\S+\s+)?(?:run\s+)?(?:build|typecheck|lint)\b'
        Reason = 'full frontend build/typecheck/lint belongs in CI (ci.yml -> frontend job)'
    },
    [pscustomobject]@{
        Pattern = '\b(?:npx\s+)?next\s+build\b'
        Reason = 'a Next.js production build belongs in CI'
    },
    [pscustomobject]@{
        Pattern = '\btauri\s+build\b'
        Reason = 'desktop bundling runs on GitHub Actions only (desktop-bundle.yml)'
    },
    [pscustomobject]@{
        Pattern = '\bcargo\s+(?:build|test)\b'
        Reason = 'cargo build/test is expensive; CI runs it'
    },
    [pscustomobject]@{
        Pattern = '\bdocker\s+(?:compose\s+)?build\b'
        Reason = 'docker image builds run in CI, never on the laptop or the VPS'
    },
    [pscustomobject]@{
        Pattern = '\bgh\s+run\s+watch\b'
        Reason = 'do not wait on CI - push and stop; the operator monitors runs and reports failures'
    }
)

$match = $null
$isRisky = $false
if ($command) {
    $match = $riskyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
    if ($match) {
        $isRisky = $true
    }
    else {
        $match = $heavyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
    }
}

if ($match) {
    $label = if ($isRisky) { 'safety guard' } else { 'compute-discipline guard' }
    $detail = if ($isRisky) {
        "Potentially destructive command requires confirmation: $($match.Reason)."
    } else {
        "RadioPad compute rule (CLAUDE.md): $($match.Reason)."
    }
    Write-HookResult ([ordered]@{
        continue = $true
        systemMessage = "RadioPad $label flagged $(if ($toolName) { $toolName } else { 'a tool' }): $($match.Reason)."
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'ask'
            permissionDecisionReason = $detail
        }
    })
}
else {
    Write-HookResult ([ordered]@{
        continue = $true
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'allow'
        }
    })
}