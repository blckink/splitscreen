<#
.SYNOPSIS
    One-command release builder for SplitPlay.

.DESCRIPTION
    Produces a ready-to-share installer (installer\output\SplitPlaySetup.exe) and
    installs every tool it needs along the way - nothing has to be installed by
    hand first. Steps:

      1. Make sure the build tools are present (installs missing ones via winget):
           - .NET 8 SDK
           - Visual C++ Build Tools (for the native XInput proxy)
           - Inno Setup 6 (to build the installer)
      2. Build the native XInput proxy (x64 + x86).
      3. Publish SplitPlay self-contained (bundles the .NET runtime, the test
         window and the proxy) so the end user needs no tools at all.
      4. Compile the installer.

    Re-runnable and idempotent. Already-installed tools are detected and skipped.

.PARAMETER SkipBootstrap
    Do not auto-install missing tools (assume they are already present).

.PARAMETER Configuration
    Build configuration (default: Release).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-release.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipBootstrap,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Runtime = "win-x64"

# Keep the machine awake for the whole run - a long tool install must not be
# interrupted by sleep (which can leave the installer hung). Cleared on exit.
try {
    Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@ -ErrorAction SilentlyContinue
    # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
    [void][Win32.Power]::SetThreadExecutionState([uint32]'0x80000003')
} catch { }

# --- Paths -----------------------------------------------------------------
$InstallerDir = $PSScriptRoot
$Root         = Split-Path -Parent $InstallerDir          # repository root
$Staging      = Join-Path $InstallerDir "staging\app"
$OutputDir    = Join-Path $InstallerDir "output"
$AppProj      = Join-Path $Root "src\SplitPlay.App\SplitPlay.App.csproj"
$TestProj     = Join-Path $Root "src\SplitPlay.TestTarget\SplitPlay.TestTarget.csproj"
$ProxyBuild   = Join-Path $Root "native\build-proxy.cmd"
$ProxyBinX64  = Join-Path $Root "native\bin\x64\SplitPlay.XInputProxy.dll"
$ProxyBinX86  = Join-Path $Root "native\bin\x86\SplitPlay.XInputProxy.dll"
$IssFile      = Join-Path $InstallerDir "SplitPlay.iss"

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Test-Winget {
    return [bool](Get-Command winget -ErrorAction SilentlyContinue)
}

function Install-WingetPackage([string]$id, [string]$override = $null) {
    Write-Host "    Installing $id via winget..." -ForegroundColor DarkGray
    $wgArgs = @("install", "--id", $id, "-e",
                "--accept-package-agreements", "--accept-source-agreements")
    if ($override) { $wgArgs += @("--override", $override) }
    winget @wgArgs
    # winget returns non-zero when the package is already installed/up to date;
    # treat that as success and rely on the explicit detection below.
}

# --- Tool discovery --------------------------------------------------------
function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $fallback) { return $fallback }
    return $null
}

function Test-DotnetSdk8([string]$dotnet) {
    if (-not $dotnet) { return $false }
    $sdks = & $dotnet --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match '^\s*8\.' })
}

function Test-VcTools {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { return $false }
    $path = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath 2>$null
    return -not [string]::IsNullOrWhiteSpace($path)
}

function Resolve-Iscc {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

# --- 1. Bootstrap tools ----------------------------------------------------
Write-Step "Checking build tools"

$needDotnet = -not (Test-DotnetSdk8 (Resolve-Dotnet))
$needVc     = -not (Test-VcTools)
$needIscc   = -not (Resolve-Iscc)
$needInstall = $needDotnet -or $needVc -or $needIscc

if (-not $SkipBootstrap -and $needInstall) {
    if (-not (Test-Winget)) {
        throw "winget was not found. Update 'App Installer' from the Microsoft Store, then re-run."
    }

    # Installing machine-wide tools needs administrator rights. Re-launch the
    # script elevated automatically so nothing has to be done by hand.
    $isAdmin = ([Security.Principal.WindowsPrincipal]`
        [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltinRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "    Requesting administrator rights to install tools..." -ForegroundColor Yellow
        $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"",
                     "-Configuration", $Configuration)
        if ($SkipBootstrap) { $argList += "-SkipBootstrap" }
        Start-Process powershell -Verb RunAs -ArgumentList $argList
        Write-Host "    Continuing in a new elevated window." -ForegroundColor Yellow
        return
    }

    if ($needDotnet) {
        Install-WingetPackage "Microsoft.DotNet.SDK.8"
    }
    if ($needVc) {
        Write-Host "    Installing Visual C++ Build Tools - this is several GB and can" -ForegroundColor Yellow
        Write-Host "    take 10-20 minutes. A Visual Studio Installer progress window will" -ForegroundColor Yellow
        Write-Host "    appear; let it finish (this script waits for it)." -ForegroundColor Yellow
        Install-WingetPackage "Microsoft.VisualStudio.2022.BuildTools" `
            "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
    }
    if ($needIscc) {
        Install-WingetPackage "JRSoftware.InnoSetup"
    }
}

Write-Host "    .NET 8 SDK: $([bool](Test-DotnetSdk8 (Resolve-Dotnet)))" -ForegroundColor Green
Write-Host "    Visual C++ Build Tools: $([bool](Test-VcTools))" -ForegroundColor Green
Write-Host "    Inno Setup 6: $([bool](Resolve-Iscc))" -ForegroundColor Green

# Re-resolve after any installs.
$Dotnet = Resolve-Dotnet
$Iscc   = Resolve-Iscc
if (-not $Dotnet) { throw "dotnet not found. Open a NEW terminal (so PATH updates) and re-run." }
if (-not $Iscc)   { throw "Inno Setup (ISCC.exe) not found. Open a NEW terminal and re-run." }

# --- 2. Native XInput proxy ------------------------------------------------
Write-Step "Building native XInput proxy"
& cmd /c "`"$ProxyBuild`""
if (-not (Test-Path $ProxyBinX64) -or -not (Test-Path $ProxyBinX86)) {
    throw "Proxy build did not produce the expected DLLs. Is the C++ workload installed? (open a new terminal if it was just installed)"
}

# --- 3. Publish self-contained ---------------------------------------------
Write-Step "Publishing SplitPlay (self-contained)"
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

& $Dotnet publish $AppProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $Staging
if ($LASTEXITCODE -ne 0) { throw "Publishing the app failed." }

& $Dotnet publish $TestProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o (Join-Path $Staging "TestTarget")
if ($LASTEXITCODE -ne 0) { throw "Publishing the test target failed." }

# Bundle the proxy DLLs the launch engine looks for at runtime.
$proxyDestX64 = Join-Path $Staging "XInputProxy\x64"
$proxyDestX86 = Join-Path $Staging "XInputProxy\x86"
New-Item -ItemType Directory -Path $proxyDestX64 -Force | Out-Null
New-Item -ItemType Directory -Path $proxyDestX86 -Force | Out-Null
Copy-Item $ProxyBinX64 $proxyDestX64 -Force
Copy-Item $ProxyBinX86 $proxyDestX86 -Force

# Bundle the Steam emulator (gbe_fork), if it has been fetched. The launch engine
# uses it to run a second instance from a single Steam account.
$goldbergSrc = Join-Path $Root "redist\goldberg"
if (Test-Path $goldbergSrc) {
    $goldbergDest = Join-Path $Staging "Redist\Goldberg"
    New-Item -ItemType Directory -Force -Path $goldbergDest | Out-Null
    Copy-Item (Join-Path $goldbergSrc "*") $goldbergDest -Recurse -Force
    Write-Host "    Bundled Steam emulator from $goldbergSrc" -ForegroundColor Green
} else {
    Write-Host "    NOTE: Steam emulator not found ($goldbergSrc) - co-op via single account will be unavailable. Run redist\fetch-goldberg.ps1." -ForegroundColor Yellow
}

# Bundle the license + third-party notices so the distributed build is compliant
# (SplitPlay is GPL-3.0; it ships LGPL/BSD third-party components).
foreach ($doc in @("LICENSE", "THIRD-PARTY-NOTICES.md")) {
    $docSrc = Join-Path $Root $doc
    if (Test-Path $docSrc) { Copy-Item $docSrc (Join-Path $Staging $doc) -Force }
}

# --- 4. Compile the installer ----------------------------------------------
Write-Step "Building installer"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Read the app version from the shared build props (single source of truth).
$version = "0.1.0"
$propsPath = Join-Path $Root "Directory.Build.props"
if (Test-Path $propsPath) {
    $m = Select-String -Path $propsPath -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

& $Iscc "/DStaging=$Staging" "/DAppVersion=$version" $IssFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $OutputDir "SplitPlaySetup.exe"
Write-Step "Done"
Write-Host "Installer created:" -ForegroundColor Green
Write-Host "  $setup"
Write-Host ""
Write-Host "Share that single file - it installs SplitPlay with no extra tools required."
