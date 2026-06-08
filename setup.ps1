<#
.SYNOPSIS
    One-shot setup + run for SplitPlay.

.DESCRIPTION
    Gets everything ready on a fresh Windows PC and launches the app:
      1. Keeps the machine awake (no sleep-stalled installs).
      2. Installs the .NET 8 SDK via winget if it is missing (self-elevates once).
      3. Builds and runs SplitPlay so you can test it immediately.

    Controller isolation needs the native proxy (C++ tools) - that is a separate,
    longer step; run this with -Installer to do the full pipeline instead, or see
    installer\build-release.cmd. Without the proxy the app still runs; isolation is
    just reported as off.

.PARAMETER Installer
    Run the full release pipeline (installs C++ tools, builds the proxy, produces
    SplitPlaySetup.exe) instead of just building and running the app.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File setup.ps1
#>
[CmdletBinding()]
param([switch]$Installer)

$ErrorActionPreference = "Stop"
$Here   = $PSScriptRoot                       # repository root
$AppProj = Join-Path $Here "src\SplitPlay.App\SplitPlay.App.csproj"

function Step($t) { Write-Host "`n==> $t" -ForegroundColor Cyan }

# --- Keep awake for the whole run ------------------------------------------
try {
    Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@ -ErrorAction SilentlyContinue
    [void][Win32.Power]::SetThreadExecutionState([uint32]'0x80000003')  # CONTINUOUS|SYSTEM|DISPLAY
} catch { }

# --- Helpers ----------------------------------------------------------------
function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $p = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $p) { return $p }
    return $null
}
function Has-DotnetSdk8($dotnet) {
    if (-not $dotnet) { return $false }
    return [bool]((& $dotnet --list-sdks 2>$null) | Where-Object { $_ -match '^\s*8\.' })
}
function Test-Admin {
    return ([Security.Principal.WindowsPrincipal]`
        [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltinRole]::Administrator)
}

# --- Sanity: are we in the right place? ------------------------------------
if (-not (Test-Path $AppProj)) {
    Write-Host "ERROR: Could not find $AppProj" -ForegroundColor Red
    Write-Host "Run this from the root of the SplitPlay repository (the folder that" -ForegroundColor Red
    Write-Host "contains SplitPlay.sln, src\, native\ and installer\)." -ForegroundColor Red
    exit 1
}

# --- 1. Ensure the .NET 8 SDK ----------------------------------------------
Step "Checking the .NET 8 SDK"
if (-not (Has-DotnetSdk8 (Resolve-Dotnet))) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget not found. Update 'App Installer' from the Microsoft Store, then re-run."
    }
    if (-not (Test-Admin)) {
        Write-Host "    Requesting administrator rights to install the .NET SDK..." -ForegroundColor Yellow
        $a = @("-NoProfile","-ExecutionPolicy","Bypass","-File","`"$PSCommandPath`"")
        if ($Installer) { $a += "-Installer" }
        Start-Process powershell -Verb RunAs -ArgumentList $a
        return
    }
    Write-Host "    Installing .NET 8 SDK via winget..." -ForegroundColor DarkGray
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
} else {
    Write-Host "    .NET 8 SDK: OK" -ForegroundColor Green
}

$Dotnet = Resolve-Dotnet
if (-not (Has-DotnetSdk8 $Dotnet)) {
    throw "The .NET 8 SDK still isn't visible. Open a NEW terminal (so PATH updates) and run setup again."
}

# --- 2a. Full installer path (optional) ------------------------------------
if ($Installer) {
    Step "Running the full release pipeline"
    & (Join-Path $Here "installer\build-release.ps1")
    return
}

# --- 2b. Build and run the app ---------------------------------------------
Step "Building and launching SplitPlay"
Write-Host "    (first build downloads NuGet packages - this can take a minute)" -ForegroundColor DarkGray
& $Dotnet run --project $AppProj -c Release
