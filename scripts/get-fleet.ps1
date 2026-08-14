<#
.SYNOPSIS
    Install fleet on Windows from a published release. No .NET SDK needed.

.DESCRIPTION
    Downloads the native binary, puts it on the user PATH and runs 'fleet setup',
    which writes the WezTerm module, wires the WezTerm config and reports whatever
    is still missing.

    The release repository is not baked in. Pass -Repo, or set FLEET_REPO, e.g.
    'trivium/fleet'.

.PARAMETER WithDeps
    Install WezTerm, Neovim, yazi and git through winget when missing, and clone a
    Neovim config into %LOCALAPPDATA%\nvim. The dependency script is downloaded
    from the same repository, so this works through 'irm | iex' too.

.PARAMETER NvimConfig
    Git URL of the Neovim config to clone with -WithDeps.

.EXAMPLE
    .\get-fleet.ps1 -Repo trivium/fleet

.EXAMPLE
    .\get-fleet.ps1 -Repo trivium/fleet -WithDeps

.EXAMPLE
    $env:FLEET_REPO = 'trivium/fleet'
    irm https://raw.githubusercontent.com/trivium/fleet/main/scripts/get-fleet.ps1 | iex
#>
[CmdletBinding()]
param(
    [string]$Repo = $env:FLEET_REPO,
    [string]$Version = 'latest',
    [switch]$WithDeps,
    [string]$NvimConfig
)

$ErrorActionPreference = 'Stop'

$Asset      = 'fleet-win-x64.exe'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\fleet'
$BinPath    = Join-Path $InstallDir 'fleet.exe'

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "    $m" -ForegroundColor Green }

if ([string]::IsNullOrWhiteSpace($Repo)) {
    throw "no release repository. Pass -Repo <owner/name> or set FLEET_REPO."
}

if ($WithDeps) {
    $depsUrl = "https://raw.githubusercontent.com/$Repo/main/scripts/deps.ps1"
    $depsFile = Join-Path $env:TEMP "fleet-deps-$([guid]::NewGuid().ToString('N')).ps1"

    Write-Step 'Fetching the dependency installer'
    Invoke-WebRequest -Uri $depsUrl -OutFile $depsFile -UseBasicParsing
    Write-Ok $depsUrl

    . $depsFile
    Install-FleetDeps -NvimConfig $NvimConfig
    Remove-Item $depsFile -Force -ErrorAction SilentlyContinue
}

$url = if ($Version -eq 'latest') {
    "https://github.com/$Repo/releases/latest/download/$Asset"
} else {
    "https://github.com/$Repo/releases/download/$Version/$Asset"
}

Write-Step "Downloading $Asset"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$temp = Join-Path $env:TEMP "fleet-$([guid]::NewGuid().ToString('N')).exe"
Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
Write-Ok $url

Write-Step 'Installing'
Get-Process fleet -ErrorAction SilentlyContinue | Stop-Process -Force
Move-Item $temp $BinPath -Force
Write-Ok $BinPath

$current = [Environment]::GetEnvironmentVariable('Path', 'User')

if (($current -split ';') -notcontains $InstallDir) {
    $updated = if ([string]::IsNullOrWhiteSpace($current)) { $InstallDir } else { "$current;$InstallDir" }
    [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
    Write-Ok "added to user PATH: $InstallDir"
} else {
    Write-Ok "already on PATH: $InstallDir"
}

Write-Step 'Setting up'
& $BinPath setup

Write-Host ''
Write-Host "fleet installed. Open a new terminal and run 'fleet'." -ForegroundColor Green
