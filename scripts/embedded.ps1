# Runs this checkout's fleet with its own multiplexer (the `embedded` driver),
# next to any installed fleet and without touching it.
#
#   scripts\embedded.ps1                    pick a project; fleetd starts and this terminal attaches
#   scripts\embedded.ps1 -Project techweb   attach straight to a project
#   scripts\embedded.ps1 attach             reattach after a detach
#   scripts\embedded.ps1 build              build a new copy next to the running one (fleetd keeps running)
#   scripts\embedded.ps1 restart            stop fleetd and attach with the newest build; fleetd restores its session
#   scripts\embedded.ps1 stop               stop fleetd and every client of these builds
#   scripts\embedded.ps1 status             which build runs, which is newest, and whether it is current
#
# Every build goes into its own folder under artifacts\embedded\<rid>, and `current` names the newest.
# A running fleetd keeps the build it started from (Windows locks a running exe), so building never
# stops it; `restart` switches deliberately. Old builds that nothing runs are removed, keeping three.
#
# -Isolated uses a separate fleet config (projects, settings, logs) under
# artifacts\embedded-config instead of your real one.

param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'attach', 'build', 'restart', 'stop', 'status')]
    [string]$Command = 'run',
    [string]$Project,
    [switch]$Isolated
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
$exe = $IsWindows ? 'fleet.exe' : 'fleet'
$builds = Join-Path $root "artifacts/embedded/$rid"
$pointer = Join-Path $builds 'current'
$legacy = Join-Path $root "artifacts/publish/$rid/$exe"
$ghostty = Join-Path $root "artifacts/ghostty/$rid/lib"

function Get-CurrentFleet {
    if (Test-Path $pointer) {
        $candidate = Join-Path $builds ((Get-Content $pointer -Raw).Trim()) $exe
        if (Test-Path $candidate) { return $candidate }
    }

    if (Test-Path $legacy) { return $legacy }
    return $null
}

function Get-Fleetd {
    $mine = [IO.Path]::GetFullPath($builds)
    Get-Process fleet -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and ($_.Path.StartsWith($mine, [StringComparison]::OrdinalIgnoreCase) -or $_.Path -eq $legacy)
    }
}

function Stop-Fleetd {
    $running = @(Get-Fleetd)
    if ($running.Count -gt 0) {
        $running | Stop-Process -Force
        Write-Host "stopped $($running.Count) fleet process(es) of these builds"
    }
}

function Remove-OldBuilds {
    $inUse = @(Get-Fleetd | ForEach-Object { Split-Path $_.Path -Parent })
    $current = if (Test-Path $pointer) { (Get-Content $pointer -Raw).Trim() } else { '' }

    Get-ChildItem $builds -Directory | Sort-Object Name -Descending | Select-Object -Skip 3 |
        Where-Object { $_.Name -ne $current -and $inUse -notcontains $_.FullName } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}

function Build-Fleet {
    if (-not (Test-Path $ghostty)) {
        Write-Host 'building libghostty-vt (once, a few minutes)'
        if ($IsWindows) { & (Join-Path $root 'scripts/ghostty/build.ps1') } else { & bash (Join-Path $root 'scripts/ghostty/build.sh') }
        if ($LASTEXITCODE -ne 0) { throw 'libghostty-vt build failed' }
    }

    if ($IsWindows) {
        $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
        if ((Test-Path $installer) -and -not ($env:PATH -split ';' -contains $installer)) {
            $env:PATH = "$installer;$env:PATH"
        }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $out = Join-Path $builds $stamp
    dotnet publish (Join-Path $root 'src/Fleet/Fleet.csproj') -c Release -r $rid -o $out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    Set-Content $pointer $stamp -NoNewline
    Remove-OldBuilds
    Write-Host "built $(Join-Path $out $exe)"

    if (@(Get-Fleetd).Count -gt 0) {
        Write-Host 'fleetd keeps running on its build; `restart` switches to this one (the session is restored)'
    }
}

function Invoke-Fleet([string[]]$Arguments) {
    $fleet = Get-CurrentFleet
    if (-not $fleet) { Build-Fleet; $fleet = Get-CurrentFleet }

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

$attachArgs = @('attach') + $(if ($Project) { @('--project', $Project) } else { @() })

switch ($Command) {
    'build' { Build-Fleet }
    'stop' { Stop-Fleetd }
    'restart' {
        Stop-Fleetd
        Invoke-Fleet $attachArgs
    }
    'status' {
        $fleet = Get-CurrentFleet
        if ($fleet) {
            $built = (Get-Item $fleet).LastWriteTime
            $commit = [datetime](git -C $root log -1 --format=%cI -- src)
            Write-Host "newest build $built ($fleet)"
            Write-Host "last code commit $commit$(if ($commit -gt $built.AddMinutes(1)) { ' (newer: run build)' })"
        }
        else {
            Write-Host 'not built yet: run build'
        }

        $running = @(Get-Fleetd)
        if ($running.Count -eq 0) {
            Write-Host 'fleetd is not running'
        }
        else {
            foreach ($p in $running) {
                $build = Split-Path (Split-Path $p.Path -Parent) -Leaf
                $note = if ($fleet -and $p.Path -ne $fleet) { ' (older than the newest build; `restart` switches)' } else { '' }
                Write-Host "running: pid $($p.Id) on build $build$note"
            }
        }
    }
    'attach' { Invoke-Fleet $attachArgs }
    default { Invoke-Fleet $(if ($Project) { @('attach', '--project', $Project) } else { @() }) }
}
