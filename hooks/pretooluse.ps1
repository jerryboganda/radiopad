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

$match = $null
if ($command) {
    $match = $riskyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
}

if ($match) {
    Write-HookResult ([ordered]@{
        continue = $true
        systemMessage = "RadioPad safety guard flagged $(if ($toolName) { $toolName } else { 'a tool' }): $($match.Reason)."
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'ask'
            permissionDecisionReason = "Potentially destructive command requires confirmation: $($match.Reason)."
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