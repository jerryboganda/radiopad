$ErrorActionPreference = 'Continue'
$env:Path = 'C:\Program Files\dotnet;' + $env:Path
Set-Location 'C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api'
& 'C:\Program Files\dotnet\dotnet.exe' build-server shutdown 2>&1 | Out-Null
$log = 'C:\Users\Administrator\Desktop\Radiopad.com\iter32_build.log'
Remove-Item $log -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' build /clp:ErrorsOnly /clp:NoSummary *> $log
"EXIT=$LASTEXITCODE" | Out-File -Append $log
