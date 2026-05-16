@echo off
set DOTNET_ROLL_FORWARD=LatestPatch
cd /d "C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api"
"C:\Program Files\dotnet\dotnet.exe" build RadioPad.Api.sln --nologo -clp:ErrorsOnly > "C:\Users\Administrator\Desktop\Radiopad.com\iter32-build.log" 2>&1
echo EXITCODE=%errorlevel%
