# Runs this checkout's fleet with its own multiplexer (the `embedded` driver),
# next to any installed fleet and without touching it.
#
#   scripts\embedded.ps1                    pick a project; fleetd starts and this terminal attaches
#   scripts\embedded.ps1 -Project techweb   attach straight to a project
#   scripts\embedded.ps1 attach             reattach after ctrl+b q
#   scripts\embedded.ps1 build              (re)build libghostty-vt if missing, then fleet
#   scripts\embedded.ps1 stop               stop this build's fleetd
#   scripts\embedded.ps1 status             show the binary's age and whether fleetd runs
#
# -Isolated uses a separate fleet config (projects, settings, logs) under
# artifacts\embedded-config instead of your real one.

param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'attach', 'build', 'stop', 'status')]
    [string]$Command = 'run',
    [string]$Project,
    [switch]$Isolated
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
$publish = Join-Path $root "artifacts/publish/$rid"
$fleet = Join-Path $publish ($IsWindows ? 'fleet.exe' : 'fleet')
$ghostty = Join-Path $root "artifacts/ghostty/$rid/lib"

function Get-Fleetd {
    Get-Process fleet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $fleet }
}

function Stop-Fleetd {
    $running = @(Get-Fleetd)
    if ($running.Count -gt 0) {
        $running | Stop-Process -Force
        Write-Host "stopped $($running.Count) fleet process(es) of this build"
    }
}

function Build-Fleet {
    if (-not (Test-Path $ghostty)) {
        Write-Host 'building libghostty-vt (once, a few minutes)'
        if ($IsWindows) { & (Join-Path $root 'scripts/ghostty/build.ps1') } else { & bash (Join-Path $root 'scripts/ghostty/build.sh') }
        if ($LASTEXITCODE -ne 0) { throw 'libghostty-vt build failed' }
    }

    Stop-Fleetd

    if ($IsWindows) {
        $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
        if ((Test-Path $installer) -and -not ($env:PATH -split ';' -contains $installer)) {
            $env:PATH = "$installer;$env:PATH"
        }
    }

    dotnet publish (Join-Path $root 'src/Fleet/Fleet.csproj') -c Release -r $rid -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    Write-Host "built $fleet"
}

function Invoke-Fleet([string[]]$Arguments) {
    if (-not (Test-Path $fleet)) { Build-Fleet }

    $saved = @{ FLEET_MUX = $env:FLEET_MUX; FLEET_CONFIG_HOME = $env:FLEET_CONFIG_HOME }
    try {
        $env:FLEET_MUX = 'embedded'
        if ($Isolated) {
            $env:FLEET_CONFIG_HOME = Join-Path $root 'artifacts/embedded-config'
            New-Item -ItemType Directory -Force $env:FLEET_CONFIG_HOME | Out-Null
        }

        & $fleet @Arguments
    }
    finally {
        $env:FLEET_MUX = $saved.FLEET_MUX
        $env:FLEET_CONFIG_HOME = $saved.FLEET_CONFIG_HOME
    }
}

switch ($Command) {
    'build' { Build-Fleet }
    'stop' { Stop-Fleetd }
    'status' {
        if (Test-Path $fleet) {
            $built = (Get-Item $fleet).LastWriteTime
            $commit = [datetime](git -C $root log -1 --format=%cI -- src)
            Write-Host "binary built $built; last code commit $commit$(if ($commit -gt $built) { ' (newer: run build)' })"
        }
        else {
            Write-Host 'not built yet: run build'
        }

        $running = @(Get-Fleetd)
        Write-Host "fleet processes of this build: $($running.Count)"
    }
    'attach' { Invoke-Fleet (@('attach') + $(if ($Project) { @('--project', $Project) } else { @() })) }
    default { Invoke-Fleet $(if ($Project) { @('attach', '--project', $Project) } else { @() }) }
}
