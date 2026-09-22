@echo off
setlocal
set "publishNoPause="
if "%~1"=="" goto publish
if /I not "%~1"=="-NoPause" goto usage
if not "%~2"=="" goto usage
set "publishNoPause=-NoPause"

:publish
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Portable.ps1" -Mode SelfContained %publishNoPause%
set "publishExitCode=%ERRORLEVEL%"
endlocal & exit /b %publishExitCode%

:usage
echo Usage: %~nx0 [-NoPause]
endlocal & exit /b 2
