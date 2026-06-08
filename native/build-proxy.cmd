@echo off
setlocal enabledelayedexpansion
rem ============================================================================
rem  Builds the native XInput proxy DLL for x64 and x86 using the Visual C++
rem  compiler (cl.exe). Requires the "Desktop development with C++" workload
rem  (or the C++ Build Tools). Outputs:
rem      native\bin\x64\SplitPlay.XInputProxy.dll
rem      native\bin\x86\SplitPlay.XInputProxy.dll
rem  Run this once after installing the C++ tools; re-run if proxy.cpp changes.
rem ============================================================================

pushd "%~dp0\SplitPlay.XInputProxy"

rem --- Locate Visual Studio / Build Tools via vswhere ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [ERROR] vswhere.exe not found. Install Visual Studio or the C++ Build Tools.
    popd & exit /b 1
)

set "VSPATH="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * ^
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"

if not defined VSPATH (
    echo [ERROR] No Visual C++ toolset found. Install the "Desktop development with C++" workload.
    popd & exit /b 1
)

set "VCVARS=%VSPATH%\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%VCVARS%" (
    echo [ERROR] vcvarsall.bat not found at "%VCVARS%".
    popd & exit /b 1
)

set "OUT_X64=%~dp0bin\x64"
set "OUT_X86=%~dp0bin\x86"
if not exist "%OUT_X64%" mkdir "%OUT_X64%"
if not exist "%OUT_X86%" mkdir "%OUT_X86%"

echo.
echo === Building x64 proxy ===
call :build x64 "%OUT_X64%"
if errorlevel 1 ( popd & exit /b 1 )

echo.
echo === Building x86 proxy ===
call :build x86 "%OUT_X86%"
if errorlevel 1 ( popd & exit /b 1 )

rem --- Tidy intermediates ---
del /q *.obj 2>nul
del /q "%OUT_X64%\*.exp" "%OUT_X64%\*.lib" 2>nul
del /q "%OUT_X86%\*.exp" "%OUT_X86%\*.lib" 2>nul

echo.
echo === Done. Proxy DLLs written to native\bin\x64 and native\bin\x86 ===
popd
endlocal
exit /b 0

rem ---------------------------------------------------------------------------
rem  :build <vcvars-arch> <output-dir>
rem  Runs in its own setlocal scope so the vcvarsall environment changes do not
rem  leak between the x64 and x86 builds.
rem ---------------------------------------------------------------------------
:build
setlocal
call "%VCVARS%" %~1 >nul
if errorlevel 1 (
    echo [ERROR] vcvarsall.bat %~1 failed.
    endlocal & exit /b 1
)
cl /nologo /O2 /MT /LD /EHsc /I minhook\include proxy.cpp ^
    minhook\src\buffer.c minhook\src\hook.c minhook\src\trampoline.c ^
    minhook\src\hde\hde32.c minhook\src\hde\hde64.c ^
    /Fe:"%~2\SplitPlay.XInputProxy.dll" /link /DEF:exports.def
if errorlevel 1 (
    echo [ERROR] Compilation for %~1 failed.
    endlocal & exit /b 1
)
endlocal & exit /b 0
