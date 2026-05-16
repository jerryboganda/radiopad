@echo off
set DOTNET_ROLL_FORWARD=LatestPatch
cd /d "C:\Users\Administrator\Desktop\Radiopad.com\backend\RadioPad.Api"
"C:\Program Files\dotnet\dotnet.exe" test RadioPad.Api.sln --nologo --no-build --filter "FullyQualifiedName~RoutingPreview_Selects_Composite_Winner_And_Requires_Admin" -v:detailed > "C:\Users\Administrator\Desktop\Radiopad.com\iter32-rp.log" 2>&1
echo EXITCODE=%errorlevel%
