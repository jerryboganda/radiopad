$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\Administrator\Desktop\Radiopad.com\frontend'
$log = 'C:\Users\Administrator\Desktop\Radiopad.com\iter32_typecheck.log'
Remove-Item $log -ErrorAction SilentlyContinue
& pnpm typecheck *> $log
"EXIT=$LASTEXITCODE" | Out-File -Append $log
