@echo off
rem ============================================================================
rem  SplitPlay one-shot setup. Double-click this to install the .NET 8 SDK (if
rem  missing) and build + launch the app. Add the word "installer" after the
rem  file name to build the full installer instead (also installs C++ tools).
rem ============================================================================
set "ARGS="
if /I "%~1"=="installer" set "ARGS=-Installer"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %ARGS%
echo.
pause
