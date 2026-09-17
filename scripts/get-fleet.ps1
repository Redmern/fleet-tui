<#
.SYNOPSIS
    Install fleet on Windows from a published release. No .NET SDK needed.

.DESCRIPTION
    Downloads the native binary, puts it on the user PATH and runs 'fleet setup',
    which writes the WezTerm module, wires the WezTerm config and reports whatever
    is still missing.

    Defaults to the upstream release repository. Pass -Repo, or set FLEET_REPO, to
    install from a fork instead.

.PARAMETER WithDeps
    Install WezTerm, Neovim, yazi and git through winget when missing, and clone a
    Neovim config into %LOCALAPPDATA%\nvim. The dependency script is downloaded
    from the same repository, so this works through 'irm | iex' too.

.PARAMETER NvimConfig
    Git URL of the Neovim config to clone with -WithDeps.

.EXAMPLE
    .\get-fleet.ps1

.EXAMPLE
    .\get-fleet.ps1 -WithDeps

.EXAMPLE
    irm https://raw.githubusercontent.com/Redmern/fleet-tui/main/scripts/get-fleet.ps1 | iex

.EXAMPLE
    .\get-fleet.ps1 -Repo <owner>/fleet
#>
[CmdletBinding()]
param(
    [string]$Repo = $(if ($env:FLEET_REPO) { $env:FLEET_REPO } else { 'Redmern/fleet-tui' }),
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
