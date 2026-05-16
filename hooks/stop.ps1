. "$PSScriptRoot\lib.ps1"

$null = Read-HookPayload

Write-HookResult ([ordered]@{
    continue = $true
    systemMessage = 'Open Design completion hint: final responses should name changed files, checks run, and any honest limits. Do not omit failing checks or unverified runtime assumptions.'
})