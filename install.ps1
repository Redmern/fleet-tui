<#
.SYNOPSIS
    Install fleet: publish the NativeAOT binary, put it on the user PATH.

.DESCRIPTION
    Builds from source. For a machine without the .NET SDK use
    scripts\get-fleet.ps1, which downloads a published binary instead.

    Wiring lives in 'fleet setup', which this script runs at the end, so the
    installer stays a build-and-copy step and the wiring is testable on its own.

.PARAMETER Uninstall
    Remove the binary and the PATH entry. Configuration under %APPDATA%\fleet is
    kept unless -Purge is also given.

.PARAMETER Purge
    With -Uninstall, also delete %APPDATA%\fleet.

.PARAMETER WithDeps
    Install WezTerm, Neovim, yazi and git through winget when they are missing, and
    clone a Neovim config into %LOCALAPPDATA%\nvim.

.PARAMETER NvimConfig
    Git URL of the Neovim config to clone with -WithDeps. Defaults to
    FLEET_NVIM_CONFIG, then to the one in scripts\deps.ps1.

.EXAMPLE
    .\install.ps1

.EXAMPLE
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Purge,
    [switch]$WithDeps,
    [string]$NvimConfig
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = $PSScriptRoot
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\fleet'
$BinPath    = Join-Path $InstallDir 'fleet.exe'
$ConfigDir  = Join-Path $env:APPDATA 'fleet'

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2($m) { Write-Host "    $m" -ForegroundColor Yellow }

function Add-ToUserPath($dir) {
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($current -split ';') -contains $dir) {
        Write-Ok "already on PATH: $dir"
        return
    }

    $updated = if ([string]::IsNullOrWhiteSpace($current)) { $dir } else { "$current;$dir" }
    [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
    Write-Ok "added to user PATH: $dir"
    Write-Warn2 'open a new terminal for PATH to take effect'
}

function Remove-FromUserPath($dir) {
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    $kept = ($current -split ';' | Where-Object { $_ -and $_ -ne $dir }) -join ';'
    if ($kept -ne $current) {
        [Environment]::SetEnvironmentVariable('Path', $kept, 'User')
        Write-Ok "removed from user PATH: $dir"
    }
}

# ---------------------------------------------------------------------------
# Uninstall
# ---------------------------------------------------------------------------

if ($Uninstall) {
    Write-Step 'Uninstalling fleet'

    Get-Process fleet -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Ok 'stopped running fleet processes'

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Ok "removed $InstallDir"
    }

    Remove-FromUserPath $InstallDir

    if ($Purge) {
        if (Test-Path $ConfigDir) {
            Remove-Item $ConfigDir -Recurse -Force
            Write-Ok "purged $ConfigDir"
        }
    }
    else {
        Write-Warn2 'config kept; re-run with -Purge to delete it'
    }

    Write-Host "`nfleet uninstalled." -ForegroundColor Green
    return
}

# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------

if ($WithDeps) {
    . (Join-Path $RepoRoot 'scripts\deps.ps1')
    Install-FleetDeps -NvimConfig $NvimConfig
}

Write-Step 'Checking prerequisites'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is not on PATH. Install the .NET 10 SDK.'
}
Write-Ok "dotnet $(dotnet --version)"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Warn2 'git is not on PATH — fleet needs it at runtime'
}

# NativeAOT links with the MSVC toolchain, which the ILCompiler locates via
# vswhere. vswhere ships with the Visual Studio Installer but is not on PATH by
# default, and without it the link step fails with a confusing
# "'vswhere.exe' is not recognized" followed by MSB3073 exit code 123.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and
    (($env:PATH -split ';') -notcontains $vsInstaller)) {
    $env:PATH = "$vsInstaller;$env:PATH"
    Write-Ok 'added the Visual Studio Installer directory to PATH for this build'
}

Write-Step 'Publishing (NativeAOT)'

$publishDir = Join-Path $RepoRoot 'out'
& dotnet publish (Join-Path $RepoRoot 'src\Fleet') -c Release -o $publishDir | Out-Null

if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $publishDir 'fleet.exe'))) {
    throw 'publish failed. NativeAOT needs Visual Studio Build Tools with the C++ workload.'
}
Write-Ok "published to $publishDir"

Write-Step 'Installing'

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# Windows won't let you overwrite a running executable, but it WILL let you rename
# one. Move the current binary aside (works even while fleets are running) and drop
# the new one in its place, so reinstalling never has to kill live fleets. Running
# fleets keep executing from the renamed file until they are reopened; new launches
# get the new binary.
if (Test-Path $BinPath) {
    $stale = "$BinPath.old-$(Get-Date -Format 'yyyyMMddHHmmss')"
    try {
        Move-Item $BinPath $stale -Force -ErrorAction Stop
    }
    catch {
        # Rename failed (rare) - fall back to stopping the running fleets.
        $running = @(Get-Process fleet -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $BinPath })
        foreach ($p in $running) {
            try { $p.Kill(); $p.WaitForExit(3000) | Out-Null } catch { }
        }
        if ($running.Count -gt 0) {
            Write-Warn2 "could not rename the binary aside; stopped $($running.Count) running fleet(s)"
        }
    }
}

Copy-Item (Join-Path $publishDir 'fleet.exe') $BinPath -Force

# Best-effort cleanup of binaries left by earlier reinstalls. Any still locked by a
# running fleet stay until that process exits, which is harmless.
Get-ChildItem (Join-Path $InstallDir 'fleet.exe.old-*') -ErrorAction SilentlyContinue |
    ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { } }

Write-Ok "installed $BinPath"

Add-ToUserPath $InstallDir

Write-Step 'Setting up'
& $BinPath setup

if ($LASTEXITCODE -ne 0) {
    Write-Host '    something fleet needs is missing - see the list above' -ForegroundColor Yellow
}

Write-Host "`nfleet installed. Run 'fleet' in a new terminal." -ForegroundColor Green
exit 0
