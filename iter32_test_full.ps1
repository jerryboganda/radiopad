$ErrorActionPreference = 'Continue'
$env:Path = 'C:\Program Files\dotnet;' + $env:Path
$env:DOTNET_ROLL_FORWARD = 'LatestPatch'
Set-Location 'C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api'
$log = 'C:\Users\Administrator\Desktop\Radiopad.com\iter32_test_full.log'
Remove-Item $log -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' test --no-build --logger "console;verbosity=minimal" --nologo *> $log
"EXIT=$LASTEXITCODE" | Out-File -Append $log
