@echo off
set "PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer"
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish ClioLauncher.Desktop\ClioLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o publish\win-x64
echo NATIVE_PUBLISH_EXIT=%ERRORLEVEL%
