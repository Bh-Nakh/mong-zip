@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set /p APPNAME=<app.name
rem usage: setup.bat  ->  dist\%APPNAME%-Setup.exe (installer + context menu + uninstall)
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set CSC=%FW%\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)

call "%~dp0build.bat" dist\payload || exit /b 1
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /win32icon:res\app.ico /win32manifest:res\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /resource:dist\payload\%APPNAME%.exe,%APPNAME%.exe ^
  /resource:dist\payload\7z.exe,7z.exe ^
  /resource:dist\payload\7z.dll,7z.dll ^
  /resource:dist\payload\7zip-License.txt,7zip-License.txt ^
  /out:dist\%APPNAME%-Setup.exe tools\Setup.cs
if errorlevel 1 (
  echo SETUP BUILD FAILED
  exit /b 1
)
echo SETUP OK: dist\%APPNAME%-Setup.exe
