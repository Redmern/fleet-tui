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

# Progress bar and download meter only on an interactive console; anywhere else
# (redirected, CI, TERM=dumb, NO_COLOR, FLEET_NO_ANIMATION) the plain '==> step'
# lines are printed. Inlined rather than dot-sourced so 'irm | iex' keeps working.
$script:Fancy = -not ($env:FLEET_NO_ANIMATION -or $env:NO_COLOR -or $env:CI -or $env:TERM -eq 'dumb' -or
    $Host.Name -ne 'ConsoleHost' -or [Console]::IsOutputRedirected)
$script:StepCount = 0
$script:StepTotal = if ($WithDeps) { 4 } else { 3 }

function Format-Bar([double]$fraction, [int]$width = 24) {
    $filled = [int][Math]::Floor([Math]::Max(0.0, [Math]::Min(1.0, $fraction)) * $width)
    '[' + ('#' * $filled) + ('-' * ($width - $filled)) + ']'
}

function Write-Step($m) {
    $script:StepCount++
    if ($script:Fancy) {
        $bar = Format-Bar (($script:StepCount - 1) / $script:StepTotal)
        Write-Host "$bar $script:StepCount/$script:StepTotal $m" -ForegroundColor Cyan
    }
    else {
        Write-Host "==> $m" -ForegroundColor Cyan
    }
}

function Complete-Steps {
    if ($script:Fancy) { Write-Host "$(Format-Bar 1) $script:StepTotal/$script:StepTotal Done" -ForegroundColor Cyan }
}

function Write-Ok($m)   { Write-Host "    $m" -ForegroundColor Green }

function Get-LineWidth {
    try { [Math]::Max(20, [Console]::WindowWidth - 1) } catch { 79 }
}

function Write-Status($text) {
    $width = Get-LineWidth
    if ($text.Length -gt $width) { $text = $text.Substring(0, $width) }
    [Console]::Write("`r" + $text.PadRight($width))
}

function Clear-Status {
    [Console]::Write("`r" + (' ' * (Get-LineWidth)) + "`r")
    try { [Console]::CursorVisible = $true } catch { }
}

# Streams the download so the console can show a byte bar (or a spinner when the
# server sends no Content-Length). A failed download leaves no partial file.
function Save-Download($Uri, $Path) {
    if (-not $script:Fancy) {
        Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
        return
    }

    $response = $null
    $source = $null
    $target = $null
    $frames = '|/-\'
    $i = 0

    try {
        try { [Console]::CursorVisible = $false } catch { }
        $response = [System.Net.WebRequest]::Create($Uri).GetResponse()
        $total = $response.ContentLength
        $source = $response.GetResponseStream()
        $target = [System.IO.File]::Create($Path)
        $buffer = New-Object byte[] 65536
        $done = 0L
        $clock = [System.Diagnostics.Stopwatch]::StartNew()

        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $target.Write($buffer, 0, $read)
            $done += $read
            if ($clock.ElapsedMilliseconds -lt 100) { continue }
            $clock.Reset()
            $clock.Start()

            $size = '{0:N1} MB' -f ($done / 1MB)
            if ($total -gt 0) {
                Write-Status "    $(Format-Bar ($done / $total)) $([int](100 * $done / $total))% $size"
            }
            else {
                Write-Status "    $($frames[$i++ % $frames.Length]) $size"
            }
        }
    }
    catch {
        if ($target) { $target.Dispose(); $target = $null }
        Remove-Item $Path -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        if ($target) { $target.Dispose() }
        if ($source) { $source.Dispose() }
        if ($response) { $response.Dispose() }
        Clear-Status
    }
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
Save-Download $url $temp
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

Complete-Steps
Write-Host ''
Write-Host "fleet installed. Open a new terminal and run 'fleet'." -ForegroundColor Green
