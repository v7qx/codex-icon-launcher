@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Windows C# compiler was not found.
  exit /b 1
)

if not exist "dist" mkdir "dist"
"%CSC%" /nologo /target:winexe /optimize+ /reference:Microsoft.CSharp.dll /win32icon:assets\Codex.ico /out:dist\Codex.exe src\CodexLauncher.cs
if errorlevel 1 exit /b %errorlevel%
echo Built dist\Codex.exe successfully.
