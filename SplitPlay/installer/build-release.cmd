@echo off
rem ============================================================================
rem  Double-click entry point. Builds the SplitPlay installer end to end and
rem  auto-installs any missing tools (.NET SDK, C++ Build Tools, Inno Setup).
rem  Needs an internet connection the first time (to fetch tools via winget).
rem  Will prompt for administrator rights, which winget/installers require.
rem ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
echo.
pause
