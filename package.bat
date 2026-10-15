@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set /p APPNAME=<app.name
rem usage: package.bat   -> dist\%APPNAME%-portable.zip (build into dist\app first)
call "%~dp0build.bat" dist\%APPNAME% || exit /b 1
echo portable> dist\%APPNAME%\portable.txt
if exist dist\%APPNAME%-portable.zip del dist\%APPNAME%-portable.zip
pushd dist
"..\engine\7z.exe" a -tzip -mx=9 %APPNAME%-portable.zip %APPNAME% >nul || (popd & exit /b 1)
popd
echo PACKAGE OK: dist\%APPNAME%-portable.zip
