@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem App name (Korean) lives in app.name so this file stays ASCII-only.
set /p APPNAME=<app.name
rem usage: build.bat [outdir]   (default: bin)
set OUT=%~1
if "%OUT%"=="" set OUT=bin
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set CSC=%FW%\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

if not exist res\app.ico (
  echo Generating icon...
  "%CSC%" /nologo /target:exe /out:"%OUT%\MakeIcon.exe" /r:System.Drawing.dll tools\MakeIcon.cs || exit /b 1
  "%OUT%\MakeIcon.exe" res\app.ico || exit /b 1
  del "%OUT%\MakeIcon.exe"
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /win32icon:res\app.ico /win32manifest:res\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:"%OUT%\%APPNAME%.exe" src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

copy /y engine\7z.exe "%OUT%\" >nul
copy /y engine\7z.dll "%OUT%\" >nul
copy /y engine\7zip-License.txt "%OUT%\" >nul
echo BUILD OK: %OUT%\%APPNAME%.exe
