. "$PSScriptRoot\lib.ps1"

$null = Read-HookPayload

Write-HookResult ([ordered]@{
    continue = $true
    systemMessage = 'Open Design subagent hint: summarize the returned findings into decisions, changed paths, and risks. Avoid pasting large logs unless the user asked for raw output.'
})