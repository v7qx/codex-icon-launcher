@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: The .NET Framework C# compiler was not found.
  echo Install or repair .NET Framework 4.x, then run build.cmd again.
  exit /b 1
)

if not exist "dist" mkdir "dist"
if not exist "dist" (
  echo ERROR: Could not create the dist folder. Check write permissions for this folder.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /reference:Microsoft.CSharp.dll /win32icon:assets\Codex.ico /out:dist\Codex.exe src\CodexLauncher.cs src\AppDiscovery.cs
if errorlevel 1 (
  echo ERROR: Build failed. See the compiler messages above.
  exit /b 1
)
echo Built dist\Codex.exe successfully.
