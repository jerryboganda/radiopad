@echo off
set DOTNET_ROLL_FORWARD=LatestPatch
cd /d "C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api"
"C:\Program Files\dotnet\dotnet.exe" test RadioPad.Api.sln --nologo --no-build -v:q > "C:\Users\Administrator\Desktop\Radiopad.com\iter32-fulltest.log" 2>&1
echo EXITCODE=%errorlevel%
