<#
.SYNOPSIS
    Checks scripts/sandbox-smoke.ps1 without running a real fleet binary.

.DESCRIPTION
    Runs the script in a child PowerShell. The refusal, usage and bad-binary cases run on
    every platform; none of them starts the binary (pwsh itself stands in as the file). On
    Linux and macOS a fake fleet, a small sh script, also drives the happy path, the
    cleared-environment check and a failing doctor.

    Plain PowerShell rather than Pester, like the other scripts in this folder. Exits 1 on
    failure.

.EXAMPLE
    pwsh -NoProfile -File tests/scripts/sandbox-smoke.ps1
#>
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '../..')
$target = Join-Path $repo 'scripts/sandbox-smoke.ps1'
$shell = (Get-Process -Id $PID).Path
$onWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$work = Join-Path ([IO.Path]::GetTempPath()) "fleet-smoke-test-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
[void](New-Item -ItemType Directory -Path $work)
$failures = 0

function Check($name, $condition, $detail = '') {
    if ($condition) { Write-Host "ok   $name" -ForegroundColor Green }
    else {
        Write-Host "FAIL $name" -ForegroundColor Red
        if ($detail) { Write-Host $detail }
        $script:failures++
    }
}

function Get-Leftovers {
    @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'fleet-smoke.*' -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationTime -gt $script:started })
}

function Test-Alive([string]$pidFile) {
    if (-not (Test-Path -LiteralPath $pidFile)) { return $false }
    $id = [int](Get-Content -LiteralPath $pidFile)
    return $null -ne (Get-Process -Id $id -ErrorAction SilentlyContinue)
}

# Runs the script under test with extra environment variables; returns exit code and output.
function Invoke-Script([string[]]$Arguments, [hashtable]$Environment = @{}) {
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [System.Environment]::GetEnvironmentVariable($key)
        [System.Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    try {
        $ErrorActionPreference = 'Continue'
        $text = & $shell -NoProfile -File $target @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ Code = $LASTEXITCODE; Text = $text }
    } finally {
        foreach ($key in $saved.Keys) { [System.Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

$started = (Get-Date).AddSeconds(-1)

try {
    $realEndpoint = if ($onWindows) { 'fleet-embedded-real' } else { Join-Path $work 'real/fleet/embedded.sock' }

    $r = Invoke-Script @() @{ FLEET_ENDPOINT = $realEndpoint }
    Check 'no argument: usage, exit 2' ($r.Code -eq 2 -and $r.Text -match 'usage') $r.Text

    $r = Invoke-Script @((Join-Path $work 'nope')) @{ FLEET_ENDPOINT = $realEndpoint }
    Check 'missing binary: exit 2, says so' ($r.Code -eq 2 -and $r.Text -match 'not an executable') $r.Text

    $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = $realEndpoint; SANDBOX_FLEET_ENDPOINT = $realEndpoint }
    Check 'sandbox endpoint equal to FLEET_ENDPOINT: refuses, exit 2, runs nothing' `
    ($r.Code -eq 2 -and $r.Text -match 'refusing' -and $r.Text -notmatch 'ok   ') $r.Text
    Check 'refusal leaves no sandbox dir behind' ((Get-Leftovers).Count -eq 0)

    if ($onWindows) {
        $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = '\\.\pipe\Fleet-Embedded-Real'; SANDBOX_FLEET_ENDPOINT = 'fleet-embedded-real' }
        Check 'pipe names compare without case or the \\.\pipe\ prefix' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text

        $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = $null; SANDBOX_FLEET_ENDPOINT = "fleet-embedded-$([System.Environment]::UserName)" }
        Check 'sandbox endpoint equal to the default endpoint: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text
    } else {
        $runtime = Join-Path $work 'real'
        $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = $null; XDG_RUNTIME_DIR = $runtime; SANDBOX_FLEET_ENDPOINT = (Join-Path $runtime 'fleet/embedded.sock') }
        Check 'sandbox endpoint equal to the default under XDG_RUNTIME_DIR: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text

        $r = Invoke-Script @($shell) @{ XDG_RUNTIME_DIR = $runtime; SANDBOX_XDG_RUNTIME_DIR = $runtime }
        Check 'sandbox runtime dir equal to XDG_RUNTIME_DIR: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text
    }

    if (-not $onWindows) {
        $real = Join-Path $work 'real-ws'
        [void](New-Item -ItemType Directory -Force -Path $real)
        [void](New-Item -ItemType SymbolicLink -Path (Join-Path $work 'link-ws') -Target $real)
        $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = (Join-Path $real 'custom.sock'); SANDBOX_FLEET_ENDPOINT = (Join-Path $work 'link-ws/custom.sock') }
        Check 'sandbox endpoint reaching the real one through a symlink: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text
    }

    $r = Invoke-Script @($shell) @{ FLEET_ENDPOINT = $realEndpoint; SANDBOX_FLEET_ENDPOINT = "$realEndpoint " }
    Check 'sandbox endpoint equal to the real one plus trailing whitespace: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text

    $r = Invoke-Script @($shell) @{ HOME = $work; USERPROFILE = $work; SANDBOX_HOME = $work }
    Check 'sandbox HOME equal to the real HOME: refuses' ($r.Code -eq 2 -and $r.Text -match 'refusing') $r.Text

    if (-not $onWindows) {
        $log = Join-Path $work 'calls.log'
        $fake = Join-Path $work 'fleet'
        $template = @'
#!/bin/sh
printf '%s | %s\n' "$*" "$FLEET_ENDPOINT" >>"__LOG__"
case "$*" in
"--help") echo usage; exit 0 ;;
doctor)
    env >"__LOG__.env"
    echo "fleet doctor"
    echo "  config        $FLEET_CONFIG_HOME"
    if [ -e "$FLEET_ENDPOINT" ]; then echo "  fleetd        pid $(cat "$FLEET_ENDPOINT")"; fi
    exit __DOCTOR__ ;;
daemon) echo $$ >"$FLEET_ENDPOINT"; echo $$ >"__LOG__.daemon"; while :; do sleep 1; done ;;
"daemon stop") pid=$(cat "$FLEET_ENDPOINT" 2>/dev/null) && kill "$pid" 2>/dev/null; rm -f "$FLEET_ENDPOINT"; exit 0 ;;
*) exit 64 ;;
esac
'@
        $realRuntime = Join-Path $work 'real-runtime'
        [void](New-Item -ItemType Directory -Force -Path $realRuntime)

        foreach ($doctorExit in 0, 1, 2) {
            $template.Replace('__LOG__', $log).Replace('__DOCTOR__', "$doctorExit") | Set-Content -LiteralPath $fake
            & chmod +x $fake
            Remove-Item -LiteralPath $log, "$log.env", "$log.daemon" -ErrorAction SilentlyContinue
            $r = Invoke-Script @($fake) @{ HOME = (Join-Path $work 'real-home'); XDG_RUNTIME_DIR = $realRuntime; FLEET_ENDPOINT = (Join-Path $realRuntime 'fleet/embedded.sock'); SECRET_TOKEN = 'leak' }
            Start-Sleep -Milliseconds 300
            $daemonAlive = Test-Alive "$log.daemon"
            if ($doctorExit -le 1) {
                Check "fake fleet, doctor exit ${doctorExit}: all steps pass" ($r.Code -eq 0 -and $r.Text -match 'all steps passed') $r.Text
            } else {
                Check 'fake fleet, doctor exit 2: script fails' ($r.Code -eq 1 -and $r.Text -match 'FAIL fleet doctor exited 2') $r.Text
            }
            Check "doctor exit ${doctorExit}: daemon stopped, sandbox removed" (-not $daemonAlive -and (Get-Leftovers).Count -eq 0)
            if ($doctorExit -eq 0) {
                $envText = Get-Content -Raw -LiteralPath "$log.env"
                Check 'the fake saw a cleared environment' ($envText -notmatch 'SECRET_TOKEN|real-home|real-runtime') $envText
                Check 'the fake saw sandbox HOME, runtime dir, endpoint and config home' `
                ($envText -match '(?m)^HOME=.*fleet-smoke\.' -and $envText -match '(?m)^XDG_RUNTIME_DIR=.*fleet-smoke\.' -and
                    $envText -match '(?m)^FLEET_ENDPOINT=.*fleet-smoke\.' -and $envText -match '(?m)^FLEET_CONFIG_HOME=.*fleet-smoke\.' -and
                    $envText -match '(?m)^CLAUDE_CONFIG_DIR=.*fleet-smoke\.' -and $envText -match '(?m)^CODEX_HOME=.*fleet-smoke\.' -and
                    $envText -match '(?m)^PI_CODING_AGENT_DIR=.*fleet-smoke\.') $envText
            }
        }
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    Write-Host "$failures failed" -ForegroundColor Red
    exit 1
}
Write-Host 'all passed' -ForegroundColor Green
