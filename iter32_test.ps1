$ErrorActionPreference = 'Continue'
$env:Path = 'C:\Program Files\dotnet;' + $env:Path
$env:DOTNET_ROLL_FORWARD = 'LatestPatch'
Set-Location 'C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api'
& 'C:\Program Files\dotnet\dotnet.exe' build-server shutdown 2>&1 | Out-Null
$log = 'C:\Users\Administrator\Desktop\Radiopad.com\iter32_test.log'
Remove-Item $log -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' test --filter "FullyQualifiedName~RadioPad.Api.Tests.Kms" --logger "console;verbosity=normal" --nologo *> $log
"EXIT=$LASTEXITCODE" | Out-File -Append $log
