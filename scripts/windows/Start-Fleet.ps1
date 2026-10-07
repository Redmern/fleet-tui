<#
.SYNOPSIS
    Open the Windows Terminal "Fleet" profile in a new window, grouped under the Fleet shortcut.

.DESCRIPTION
    Starts 'wt -w new -p Fleet' and tags the window that appears with the Fleet
    AppUserModelID, so the taskbar groups it under the pinned Fleet shortcut (and its
    icon) instead of under Windows Terminal.

    The shortcut runs this script through 'conhost.exe --headless'. When Windows
    Terminal is the default terminal application, a pwsh started straight from a
    shortcut is handed to Terminal and gets a window of its own with the *default*
    profile; '-WindowStyle Hidden' does not hide it. That window used to be the first
    "new" Terminal window this script saw, so it got the Fleet AppUserModelID and was
    what the taskbar button showed. Headless conhost gives the script no window at all,
    and the window hosting this script's own console is skipped in case it has one.

.PARAMETER ProfileName
    The Windows Terminal profile to open.

.PARAMETER AppId
    The AppUserModelID to give the new window. Must match the shortcut's.

.PARAMETER TimeoutSeconds
    How long to wait for the new window.

.EXAMPLE
    .\Start-Fleet.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ProfileName = 'Fleet',
    [string]$AppId = 'Trivium.Fleet',
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'

if (-not ('FleetTaskbar' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'FleetTaskbar.cs')
}

$wt = Get-Command wt.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $wt) { $wt = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\wt.exe' }
$arguments = "-w new -p `"$ProfileName`""

$ownWindow = [FleetTaskbar]::OwnConsoleWindow()
$before = @([FleetTaskbar]::TerminalWindows())
if ($ownWindow) { $before += $ownWindow }

Write-Verbose "wt: $wt $arguments"
Write-Verbose "own console window: $ownWindow; existing Terminal windows: $($before.Count)"

if (-not $PSCmdlet.ShouldProcess("$wt $arguments", 'Start Windows Terminal')) { return }

Start-Process $wt -ArgumentList $arguments

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $new = [FleetTaskbar]::TerminalWindows() | Where-Object { $_ -notin $before }
    if ($new) {
        foreach ($hwnd in $new) { [FleetTaskbar]::SetWindowAppId($hwnd, $AppId) }
        return
    }
    Start-Sleep -Milliseconds 100
}

Write-Warning "no new Windows Terminal window within $TimeoutSeconds s; it is not grouped under the Fleet shortcut"
