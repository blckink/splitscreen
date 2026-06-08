<#
.SYNOPSIS
    Downloads the Steam emulator (gbe_fork) and places the DLLs where SplitPlay
    expects them: redist\goldberg\x64\steam_api64.dll and x86\steam_api.dll.

.DESCRIPTION
    Pulls the latest Windows release from https://github.com/Detanup01/gbe_fork,
    extracts it, and copies the "experimental" steam_api DLLs (preferred for local
    co-op) into the layout the app bundles. Run by CI; can also be run locally.
    Requires 7-Zip (the release assets are .7z) and internet access.

.PARAMETER OutDir
    Where to place the DLLs. Defaults to the "goldberg" folder next to this script.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot "goldberg"))

$ErrorActionPreference = "Stop"
$headers = @{ "User-Agent" = "splitplay-ci"; "Accept" = "application/vnd.github+json" }

# GitHub's release/CDN endpoints occasionally return transient 5xx/timeouts.
# Retry network operations with exponential backoff so a flaky download does
# not fail the whole build.
function Invoke-WithRetry {
    param([scriptblock]$Action, [string]$What, [int]$MaxAttempts = 5)
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return & $Action
        }
        catch {
            if ($attempt -eq $MaxAttempts) {
                throw "Failed to $What after $MaxAttempts attempts: $($_.Exception.Message)"
            }
            $delay = [math]::Pow(2, $attempt)
            Write-Host "  $What failed (attempt $attempt/$MaxAttempts): $($_.Exception.Message). Retrying in $delay s..."
            Start-Sleep -Seconds $delay
        }
    }
}

Write-Host "Querying latest gbe_fork release..."
$release = Invoke-WithRetry -What "query the latest release" -Action {
    Invoke-RestMethod "https://api.github.com/repos/Detanup01/gbe_fork/releases/latest" -Headers $headers
}

# Prefer the regular Windows emulator release archive (not debug, not tools).
$asset =
    ($release.assets | Where-Object { $_.name -match 'emu-win.*\.7z$' -and $_.name -notmatch 'debug' } | Select-Object -First 1)
if (-not $asset) {
    $asset = $release.assets | Where-Object { $_.name -match 'win.*\.7z$' -and $_.name -notmatch 'debug|tools' } | Select-Object -First 1
}
if (-not $asset) {
    throw "Could not find a gbe_fork Windows .7z release asset. Assets: $($release.assets.name -join ', ')"
}
Write-Host "  Using asset: $($asset.name)"

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "gbe_fork"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$archive = Join-Path $tmp $asset.name
Invoke-WithRetry -What "download $($asset.name)" -Action {
    Invoke-WebRequest $asset.browser_download_url -OutFile $archive -Headers $headers
}

# Locate 7-Zip.
$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue)?.Source
if (-not $sevenZip) {
    foreach ($c in @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe")) {
        if (Test-Path $c) { $sevenZip = $c; break }
    }
}
if (-not $sevenZip) { throw "7-Zip (7z.exe) not found. Install it (e.g. 'choco install 7zip')." }

$extract = Join-Path $tmp "extracted"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
& $sevenZip x $archive "-o$extract" -y | Out-Null

function Find-Dll([string]$name, [string]$archPattern) {
    $all = Get-ChildItem $extract -Recurse -Filter $name -File
    if (-not $all) { throw "No $name found in the extracted emulator." }
    # Prefer the experimental build for the requested architecture.
    $pick = $all | Where-Object { $_.FullName -match 'experimental' -and $_.FullName -match $archPattern } | Select-Object -First 1
    if (-not $pick) { $pick = $all | Where-Object { $_.FullName -match $archPattern } | Select-Object -First 1 }
    if (-not $pick) { $pick = $all | Select-Object -First 1 }
    return $pick.FullName
}

$api64 = Find-Dll "steam_api64.dll" "x64|win64"
$api32 = Find-Dll "steam_api.dll"  "x32|x86|win32"

New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "x64"), (Join-Path $OutDir "x86") | Out-Null
Copy-Item $api64 (Join-Path $OutDir "x64\steam_api64.dll") -Force
Copy-Item $api32 (Join-Path $OutDir "x86\steam_api.dll") -Force

# LGPL compliance: ship the emulator's own license alongside the binaries, plus a
# short NOTICE that points back to the (unmodified) upstream source. The DLLs are
# used unmodified and dynamically, so this is all the LGPL-3.0 asks of us.
$emuLicense = Get-ChildItem $extract -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING)' } | Select-Object -First 1
if ($emuLicense) {
    Copy-Item $emuLicense.FullName (Join-Path $OutDir "LICENSE-gbe_fork.txt") -Force
}
@"
This folder contains the gbe_fork Steam emulator (a fork of the Goldberg
Steam Emulator), used by SplitPlay UNMODIFIED for single-account local co-op.

  Source / upstream: https://github.com/Detanup01/gbe_fork
  License:           LGPL-3.0 (see LICENSE-gbe_fork.txt, if present, and upstream)

SplitPlay does not modify these binaries. They are loaded dynamically as separate
DLL files. The corresponding source is available at the upstream URL above.
"@ | Set-Content -Path (Join-Path $OutDir "NOTICE.txt") -Encoding UTF8

Write-Host "Steam emulator DLLs placed in $OutDir"
Write-Host "  x64: $api64"
Write-Host "  x86: $api32"
if ($emuLicense) { Write-Host "  license: $($emuLicense.FullName)" }
