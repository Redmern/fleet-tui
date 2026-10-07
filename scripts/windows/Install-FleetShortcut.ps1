<#
.SYNOPSIS
    Create the Fleet launcher on Windows: a Start Menu shortcut and a Windows Terminal profile.

.DESCRIPTION
    Copies Start-Fleet.ps1, FleetTaskbar.cs and the icons into the install directory,
    adds a Windows Terminal profile named "Fleet" as a JSON fragment (the user's
    settings.json is never edited) and creates a Start Menu Fleet.lnk carrying the Fleet
    AppUserModelID, so Fleet windows group under it on the taskbar.

    Safe to run again: unchanged files are left alone. A Start Menu Fleet.lnk that this
    script did not make is kept unless -Force is given, the fragment is skipped when
    settings.json already has a hand-made "Fleet" profile, and a taskbar pin is never
    touched. Windows does not let scripts pin to the taskbar, so that step is printed.

.PARAMETER InstallDir
    Where fleet.exe lives; the launcher files are copied next to it.

.PARAMETER AppId
    The AppUserModelID shared by the shortcut and the Fleet windows.

.PARAMETER Force
    Replace a Start Menu Fleet.lnk this script did not make, and write the fragment even
    when settings.json has its own "Fleet" profile.

.PARAMETER Uninstall
    Remove the Start Menu shortcut (only one this script made) and the fragment.

.EXAMPLE
    .\scripts\windows\Install-FleetShortcut.ps1

.EXAMPLE
    .\scripts\windows\Install-FleetShortcut.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\fleet'),
    [string]$StartMenuDir = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
    [string]$FragmentsDir = (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows Terminal\Fragments'),
    [string]$TerminalSettings = (Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json'),
    [string]$AppId = 'Trivium.Fleet',
    [switch]$Force,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$ProfileName  = 'Fleet'
$ProfileGuid  = '{3f6c1d0e-7b2a-4c55-9e1f-5a8d2b7c4e10}'
$Shortcut     = Join-Path $StartMenuDir 'Fleet.lnk'
$FragmentDir  = Join-Path $FragmentsDir 'Fleet'
$Fragment     = Join-Path $FragmentDir 'fleet.json'
$Launcher     = Join-Path $InstallDir 'Start-Fleet.ps1'
$LauncherFiles = 'Start-Fleet.ps1', 'FleetTaskbar.cs', 'fleet.ico', 'fleet-32.png'

function Write-Ok($m)    { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2($m) { Write-Host "    $m" -ForegroundColor Yellow }

function Test-OurShortcut($path) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($path)
    $link.Arguments.Contains($Launcher)
}

# settings.json is JSONC; when it doesn't parse, a "name": "Fleet" anywhere in it
# counts as a hand-made profile, so a parse failure never adds a second one.
function Get-HandMadeProfile {
    if (-not (Test-Path -LiteralPath $TerminalSettings)) { return $null }
    $text = [IO.File]::ReadAllText($TerminalSettings)
    try {
        $json = [regex]::Replace($text, '(?m)^\s*//.*$', '')
        $json = [regex]::Replace($json, ',(\s*[}\]])', '$1')
        $settings = $json | ConvertFrom-Json
    }
    catch {
        if ($text -match "`"name`"\s*:\s*`"$ProfileName`"") { return [pscustomobject]@{ guid = '(unparsed settings.json)' } }
        return $null
    }
    @($settings.profiles.list) | Where-Object { $_.name -eq $ProfileName -and -not $_.source -and $_.guid -ne $ProfileGuid } |
        Select-Object -First 1
}

function Set-FileContent($path, [string]$content) {
    if ((Test-Path -LiteralPath $path) -and [IO.File]::ReadAllText($path) -eq $content) { return 'unchanged' }
    if (-not $PSCmdlet.ShouldProcess($path, 'Write')) { return 'skipped' }
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $content)
    'written'
}

if ($Uninstall) {
    if ((Test-Path -LiteralPath $Shortcut) -and (Test-OurShortcut $Shortcut)) {
        if ($PSCmdlet.ShouldProcess($Shortcut, 'Remove')) { Remove-Item -LiteralPath $Shortcut -Force; Write-Ok "removed $Shortcut" }
    }
    if (Test-Path -LiteralPath $FragmentDir) {
        if ($PSCmdlet.ShouldProcess($FragmentDir, 'Remove')) { Remove-Item -LiteralPath $FragmentDir -Recurse -Force; Write-Ok "removed $FragmentDir" }
    }
    return
}

foreach ($name in $LauncherFiles) {
    $source = Join-Path $PSScriptRoot $name
    $target = Join-Path $InstallDir $name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $target).Hash) { continue }
    if ($PSCmdlet.ShouldProcess($target, 'Copy')) {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}
Write-Ok "launcher files in $InstallDir"

$pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $pwsh) {
    Write-Warn2 'pwsh (PowerShell 7) is not on PATH; skipped the Terminal profile and the shortcut'
    return
}

$handMade = Get-HandMadeProfile
if ($handMade -and -not $Force) {
    Write-Ok "Windows Terminal already has a '$ProfileName' profile $($handMade.guid); no fragment added"
}
else {
    $fragmentJson = [ordered]@{
        profiles = @(
            [ordered]@{
                guid              = $ProfileGuid
                name              = $ProfileName
                commandline       = "`"$pwsh`" -NoExit -Command `"& '$(Join-Path $InstallDir 'fleet.exe')'`""
                icon              = Join-Path $InstallDir 'fleet-32.png'
                tabTitle          = $ProfileName
                startingDirectory = '%USERPROFILE%'
            }
        )
    } | ConvertTo-Json -Depth 4
    switch (Set-FileContent $Fragment $fragmentJson) {
        'written'   { Write-Ok "wrote the '$ProfileName' Terminal profile: $Fragment" }
        'unchanged' { Write-Ok "'$ProfileName' Terminal profile up to date" }
    }
}

if ((Test-Path -LiteralPath $Shortcut) -and -not (Test-OurShortcut $Shortcut) -and -not $Force) {
    Write-Warn2 "kept the existing $Shortcut (not made by this script; -Force replaces it)"
    return
}

$conhost = Join-Path $env:windir 'System32\conhost.exe'
$arguments = "--headless `"$pwsh`" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Launcher`" -AppId $AppId"
$icon = "$(Join-Path $InstallDir 'fleet.ico'),0"

$current = if (Test-Path -LiteralPath $Shortcut) { (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut) }
if ($current -and $current.TargetPath -eq $conhost -and $current.Arguments -eq $arguments -and $current.IconLocation -eq $icon) {
    Write-Ok "Start Menu shortcut up to date: $Shortcut"
}
elseif ($PSCmdlet.ShouldProcess($Shortcut, 'Create shortcut')) {
    New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut)
    $link.TargetPath = $conhost
    $link.Arguments = $arguments
    $link.IconLocation = $icon
    $link.WorkingDirectory = $InstallDir
    $link.WindowStyle = 7
    $link.Description = 'Fleet'
    $link.Save()

    if (-not ('FleetTaskbar' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'FleetTaskbar.cs') }
    [FleetTaskbar]::SetShortcutAppId($Shortcut, $AppId)
    Write-Ok "created the Start Menu shortcut: $Shortcut"
}

Write-Host '    To pin it: open Start, find Fleet, right-click it and choose "Pin to taskbar".' -ForegroundColor Cyan
