<#
.SYNOPSIS
    Smoke-test a fleet binary inside a sandbox, so it can never stop the real daemon or touch real config.

.DESCRIPTION
    scripts\sandbox-smoke.ps1 <path to a fleet binary>

    The binary runs as a child process whose environment is cleared first: nothing from the
    caller's environment reaches it. It gets a throwaway HOME / USERPROFILE, APPDATA,
    LOCALAPPDATA, XDG_* dirs, XDG_RUNTIME_DIR, FLEET_CONFIG_HOME and FLEET_ENDPOINT (a unique
    pipe name on Windows, a socket in the sandbox elsewhere), a short PATH (the system folder
    plus the folders of git and nvim) and a handful of system variables Windows needs
    (SystemRoot, ComSpec, ...). Fleet panes export the real FLEET_ENDPOINT; the sandbox never
    uses it. Works under Windows PowerShell 5.1 and pwsh 7 on Windows and on Linux.

    Steps, all inside the sandbox:
      1. fleet --help        must exit 0
      2. fleet daemon        started in the background; the endpoint must appear
      3. fleet doctor        must run to the end (exit 0 or 1) and report this fleetd's pid
      4. fleet daemon stop   must exit 0, the endpoint must go and the process must exit

    Doctor runs while the daemon is up because it starts a daemon of its own (detached, not a
    child of this script) when none answers. Doctor exits 1 when it reports problems such as a
    missing nvim or harness or a keybind file that is not written yet; that says something
    about the machine, not the binary, so it is reported and does not fail the run. Any other
    exit code (a crash, a hang past 60 s) fails it.

    Refuses (exit 2, nothing run) when the sandbox endpoint equals the caller's FLEET_ENDPOINT
    or the default endpoint of the caller's environment, or when the sandbox runtime dir or
    HOME equals the caller's. The sandbox values are always fresh temp paths;
    SANDBOX_FLEET_ENDPOINT, SANDBOX_XDG_RUNTIME_DIR and SANDBOX_HOME override them and exist so
    that the refusal can be tested.

    Windows limit: .NET resolves the profile folder (Environment.GetFolderPath) through the
    shell, not through USERPROFILE, so a fleet verb that writes under the profile (for example
    ~\.claude) would reach the real one. Fleet's own config honours FLEET_CONFIG_HOME, which is
    set, and so are CLAUDE_CONFIG_DIR, CODEX_HOME and PI_CODING_AGENT_DIR, which keeps harness
    config out of the real profile. To make anything that still leaks visible, the script lists
    the top-level files of the real ~\.claude, ~\.codex, ~\.pi and fleet config dirs, plus
    ~\.claude.json and ~\.profiles.psd1, before and after and prints a WARN with the differences.
    It does not fail the run: on a machine where Claude or fleet is running, those files can change
    on their own, so a difference there needs a look, not an exit code.

    The sandbox is removed on exit. First 'fleet daemon stop' runs in it, which also stops a
    daemon that doctor started; a daemon this script started that survives that is killed.

    Exit codes: 0 all steps passed, 1 a step failed, 2 refused or bad arguments.
#>
param(
    [Parameter(Position = 0)]
    [string]$Binary,
    [Alias('h')]
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

$script:onWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$script:fails = 0
$script:sandbox = $null
$script:daemon = $null
$script:envMap = $null
$script:bin = $null
$script:endpoint = $null
$script:configDir = $null

function Get-Env([string]$name) {
    $value = [System.Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrEmpty($value)) { return $null }
    return $value
}

function Write-Usage {
    Get-Help $PSCommandPath -Full | Out-String | Write-Host
}

function Write-Ok([string]$text) { Write-Host "ok   $text" }

function Write-Bad([string]$text) {
    Write-Host "FAIL $text"
    $script:fails++
}

function Write-Indented([string]$text) {
    foreach ($line in ($text -split "\r?\n")) {
        if ($line.Length -gt 0) { Write-Host "     | $line" }
    }
}

function ConvertTo-Comparable([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    $v = $value.Trim()
    if ($script:onWindows) {
        $v = $v -replace '^\\\\\.\\pipe\\', ''
        if ($v -match '[\\/:]') {
            try { $v = [System.IO.Path]::GetFullPath($v) } catch { }
        }
        return $v.TrimEnd('\', '/').ToLowerInvariant()
    }
    $resolved = & realpath -m -- $v 2>$null
    if ($LASTEXITCODE -eq 0 -and $resolved) {
        $v = ($resolved | Select-Object -First 1)
    } else {
        try { $v = [System.IO.Path]::GetFullPath($v) } catch { }
    }
    if ($v.Length -gt 1) { $v = $v.TrimEnd('/') }
    return $v
}

function Get-RealEndpoints {
    $found = @()
    $configured = Get-Env 'FLEET_ENDPOINT'
    if ($configured) { $found += (ConvertTo-Comparable $configured.Trim()) }
    if ($script:onWindows) {
        $found += (ConvertTo-Comparable ("fleet-embedded-" + [System.Environment]::UserName))
    } else {
        $runtime = Get-Env 'XDG_RUNTIME_DIR'
        if ($runtime) {
            $found += (ConvertTo-Comparable (Join-Path (Join-Path $runtime 'fleet') 'embedded.sock'))
        } else {
            $tmp = Get-Env 'TMPDIR'
            if (-not $tmp) { $tmp = '/tmp' }
            $found += (ConvertTo-Comparable (Join-Path (Join-Path $tmp ("fleet-" + [System.Environment]::UserName)) 'embedded.sock'))
        }
    }
    return $found
}

function Stop-Refused([string]$why) {
    [Console]::Error.WriteLine("sandbox-smoke: refusing to run: $why")
    return 2
}

function Test-Endpoint([string]$name) {
    if ($script:onWindows) {
        try {
            return [System.IO.Directory]::GetFiles('\\.\pipe\') -contains ('\\.\pipe\' + $name)
        } catch { return $false }
    }
    return Test-Path -LiteralPath $name
}

function New-ProcessStart([string[]]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:bin
    $psi.Arguments = ($Arguments -join ' ')
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables.Clear()
    foreach ($key in $script:envMap.Keys) {
        $psi.EnvironmentVariables[$key] = $script:envMap[$key]
    }
    return $psi
}

function Start-Sandboxed([string[]]$Arguments) {
    $psi = New-ProcessStart $Arguments
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    $p.StandardInput.Close()
    return [pscustomobject]@{
        Process = $p
        Out     = $p.StandardOutput.ReadToEndAsync()
        Err     = $p.StandardError.ReadToEndAsync()
    }
}

function Read-Captured($run) {
    $text = ''
    foreach ($task in @($run.Out, $run.Err)) {
        if ($task.Wait(5000)) { $text += $task.Result }
    }
    return $text
}

function Invoke-Sandboxed([string[]]$Arguments, [int]$TimeoutSeconds = 60) {
    $run = Start-Sandboxed $Arguments
    $timedOut = -not $run.Process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $run.Process.Kill() } catch { }
        [void]$run.Process.WaitForExit(5000)
    }
    $code = if ($timedOut) { 124 } else { $run.Process.ExitCode }
    return [pscustomobject]@{ ExitCode = $code; Output = (Read-Captured $run); TimedOut = $timedOut }
}

function Get-RealProfileSnapshot {
    $files = @()
    $profileDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)
    if ($profileDir) {
        $files += (Join-Path $profileDir '.claude.json')
        $files += (Join-Path $profileDir '.profiles.psd1')
    }
    $folders = @()
    if ($profileDir) {
        foreach ($name in '.claude', '.codex', '.pi') { $folders += (Join-Path $profileDir $name) }
    }
    $realConfig = Get-Env 'FLEET_CONFIG_HOME'
    if (-not $realConfig) {
        $appData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)
        if ($appData) { $realConfig = Join-Path $appData 'fleet' }
    }
    if ($realConfig) { $folders += $realConfig }

    $rows = @()
    foreach ($path in $files) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $item = Get-Item -LiteralPath $path -Force
            $rows += "$path|$($item.Length)|$($item.LastWriteTimeUtc.Ticks)"
        } else {
            $rows += "$path|absent"
        }
    }
    foreach ($path in $folders) {
        if (Test-Path -LiteralPath $path -PathType Container) {
            $rows += "$path|present"
            foreach ($item in (Get-ChildItem -LiteralPath $path -Force -File -ErrorAction SilentlyContinue)) {
                $rows += "$($item.FullName)|$($item.Length)|$($item.LastWriteTimeUtc.Ticks)"
            }
        } else {
            $rows += "$path|absent"
        }
    }
    return ($rows | Sort-Object) -join "`n"
}

function Invoke-Smoke {
    if ($Help -or [string]::IsNullOrEmpty($Binary)) {
        [Console]::Error.WriteLine("usage: $PSCommandPath <path to a fleet binary>")
        if ($Help) { Write-Usage }
        return 2
    }
    if (-not (Test-Path -LiteralPath $Binary -PathType Leaf)) {
        [Console]::Error.WriteLine("sandbox-smoke: $Binary is not an executable file")
        return 2
    }
    $script:bin = (Resolve-Path -LiteralPath $Binary).ProviderPath
    if (-not $script:onWindows) {
        & test -x $script:bin
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("sandbox-smoke: $Binary is not an executable file")
            return 2
        }
    }

    $tempBase = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')
    $name = 'fleet-smoke.' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
    $script:sandbox = Join-Path $tempBase $name
    [void](New-Item -ItemType Directory -Path $script:sandbox)

    $sHome = Get-Env 'SANDBOX_HOME'
    if (-not $sHome) { $sHome = Join-Path $script:sandbox 'home' }
    $sRuntime = Get-Env 'SANDBOX_XDG_RUNTIME_DIR'
    if (-not $sRuntime) { $sRuntime = Join-Path $script:sandbox 'run' }
    $sEndpoint = Get-Env 'SANDBOX_FLEET_ENDPOINT'
    if ($sEndpoint) { $sEndpoint = $sEndpoint.Trim() }
    if (-not $sEndpoint) {
        $sEndpoint = if ($script:onWindows) { $name } else { Join-Path $sRuntime 'fleet.sock' }
    }
    $script:endpoint = $sEndpoint

    $comparable = ConvertTo-Comparable $sEndpoint
    foreach ($real in (Get-RealEndpoints)) {
        if ($real -and $comparable -eq $real) {
            return (Stop-Refused "the sandbox endpoint $sEndpoint is the real daemon's")
        }
    }
    $realRuntime = Get-Env 'XDG_RUNTIME_DIR'
    if ($realRuntime -and (ConvertTo-Comparable $sRuntime) -eq (ConvertTo-Comparable $realRuntime)) {
        return (Stop-Refused "the sandbox runtime dir $sRuntime is the real XDG_RUNTIME_DIR")
    }
    $realHome = Get-Env 'HOME'
    if (-not $realHome) { $realHome = Get-Env 'USERPROFILE' }
    if ($realHome -and (ConvertTo-Comparable $sHome) -eq (ConvertTo-Comparable $realHome)) {
        return (Stop-Refused "the sandbox HOME $sHome is the real HOME")
    }

    $sub = @{}
    foreach ($n in @('config', 'data', 'state', 'cache', 'fleet-config', 'tmp', 'appdata', 'localappdata', 'claude', 'codex', 'pi')) {
        $sub[$n] = Join-Path $script:sandbox $n
    }
    foreach ($dir in (@($sHome, $sRuntime) + @($sub.Values))) {
        [void](New-Item -ItemType Directory -Force -Path $dir)
    }
    $script:configDir = $sub['fleet-config']
    if (-not $script:onWindows) { & chmod 700 $sRuntime }

    $sep = [System.IO.Path]::PathSeparator
    if ($script:onWindows) {
        $sysRoot = Get-Env 'SystemRoot'
        $pathParts = @("$sysRoot\System32", $sysRoot)
    } else {
        $pathParts = @('/usr/local/bin', '/usr/bin', '/bin')
    }
    foreach ($tool in @('git', 'nvim')) {
        $cmd = Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd) {
            $dir = Split-Path -Parent $cmd.Source
            if (-not ($pathParts | Where-Object { $_ -eq $dir })) { $pathParts += $dir }
        }
    }

    $map = [ordered]@{
        'HOME'              = $sHome
        'XDG_CONFIG_HOME'   = $sub['config']
        'XDG_DATA_HOME'     = $sub['data']
        'XDG_STATE_HOME'    = $sub['state']
        'XDG_CACHE_HOME'    = $sub['cache']
        'XDG_RUNTIME_DIR'   = $sRuntime
        'FLEET_CONFIG_HOME' = $sub['fleet-config']
        'CLAUDE_CONFIG_DIR' = $sub['claude']
        'CODEX_HOME'        = $sub['codex']
        'PI_CODING_AGENT_DIR' = $sub['pi']
        'FLEET_ENDPOINT'    = $sEndpoint
        'TMPDIR'            = $sub['tmp']
        'PATH'              = ($pathParts -join $sep)
        'TERM'              = $(if (Get-Env 'TERM') { Get-Env 'TERM' } else { 'xterm-256color' })
        'LANG'              = 'C.UTF-8'
    }
    if ($script:onWindows) {
        $map['USERPROFILE'] = $sHome
        $map['APPDATA'] = $sub['appdata']
        $map['LOCALAPPDATA'] = $sub['localappdata']
        $map['TEMP'] = $sub['tmp']
        $map['TMP'] = $sub['tmp']
        foreach ($keep in @('SystemRoot', 'windir', 'ComSpec', 'PATHEXT', 'ProgramData', 'ProgramFiles', 'ALLUSERSPROFILE',
                'USERNAME', 'USERDOMAIN', 'COMPUTERNAME', 'OS', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS', 'SystemDrive')) {
            if (Get-Env $keep) { $map[$keep] = Get-Env $keep }
        }
    }
    if (Get-Env 'DOTNET_ROOT') { $map['DOTNET_ROOT'] = Get-Env 'DOTNET_ROOT' }
    $script:envMap = $map

    Write-Host "sandbox $script:sandbox"
    Write-Host "endpoint $sEndpoint"

    $before = Get-RealProfileSnapshot

    $r = Invoke-Sandboxed @('--help')
    if ($r.ExitCode -eq 0) { Write-Ok 'fleet --help' } else { Write-Bad "fleet --help exited $($r.ExitCode)"; Write-Indented $r.Output }

    $script:daemon = Start-Sandboxed @('daemon')
    $daemonPid = $script:daemon.Process.Id
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Test-Endpoint $sEndpoint) -and -not $script:daemon.Process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Endpoint $sEndpoint)) {
        Write-Bad "fleet daemon did not create $sEndpoint"
        if ($script:daemon.Process.HasExited) { Write-Indented (Read-Captured $script:daemon) }
        return 1
    }
    Write-Ok "fleet daemon: started (pid $daemonPid)"

    $r = Invoke-Sandboxed @('doctor')
    if ($r.ExitCode -gt 1) {
        Write-Bad "fleet doctor exited $($r.ExitCode)"
        Write-Indented $r.Output
    } elseif (-not $r.Output.Contains($sub['fleet-config'])) {
        Write-Bad "fleet doctor did not report the sandbox config dir $script:configDir"
        Write-Indented $r.Output
    } elseif ($r.Output -notmatch "fleetd\s+pid $daemonPid\b") {
        Write-Bad "fleet doctor does not show fleetd pid $daemonPid"
        Write-Indented $r.Output
    } elseif ($r.ExitCode -eq 0) {
        Write-Ok "fleet doctor: healthy, sees the sandbox fleetd (pid $daemonPid)"
    } else {
        Write-Ok "fleet doctor: sees the sandbox fleetd (pid $daemonPid); exit 1 means it reported problems (below), not a failure"
        Write-Indented $r.Output
    }

    $r = Invoke-Sandboxed @('daemon', 'stop')
    if ($r.ExitCode -eq 0) { Write-Ok 'fleet daemon stop' } else { Write-Bad "fleet daemon stop exited $($r.ExitCode)"; Write-Indented $r.Output }

    if ($script:daemon.Process.WaitForExit(10000)) {
        Write-Ok 'daemon process exited'
    } else {
        Write-Bad "daemon pid $daemonPid still running after daemon stop"
    }
    if (Test-Endpoint $sEndpoint) { Write-Bad "endpoint $sEndpoint still exists after daemon stop" } else { Write-Ok 'endpoint removed' }

    $after = Get-RealProfileSnapshot
    if ($before -eq $after) {
        Write-Ok 'real profile and fleet config untouched'
    } else {
        Write-Host 'WARN the real profile or fleet config changed during the run (live Claude or fleet activity, or a leak out of the sandbox)'
        $was = $before -split "`n"
        $now = $after -split "`n"
        Write-Indented ((Compare-Object $was $now | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join "`n")
    }

    if ($script:fails -eq 0) {
        Write-Host 'sandbox-smoke: all steps passed'
        return 0
    }
    Write-Host "sandbox-smoke: $script:fails step(s) failed"
    return 1
}

function Remove-Sandbox {
    if ($script:sandbox -and $script:envMap -and $script:endpoint -and (Test-Endpoint $script:endpoint)) {
        try { [void](Invoke-Sandboxed @('daemon', 'stop') 20) } catch { }
    }
    if ($script:daemon -and -not $script:daemon.Process.HasExited) {
        Start-Sleep -Milliseconds 300
        try { $script:daemon.Process.Kill() } catch { }
        [void]$script:daemon.Process.WaitForExit(5000)
    }
    if ($script:sandbox -and (Test-Path -LiteralPath $script:sandbox)) {
        for ($try = 0; $try -lt 5 -and (Test-Path -LiteralPath $script:sandbox); $try++) {
            Remove-Item -LiteralPath $script:sandbox -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath $script:sandbox) { Start-Sleep -Milliseconds 500 }
        }
        if (Test-Path -LiteralPath $script:sandbox) {
            [Console]::Error.WriteLine("sandbox-smoke: could not remove $script:sandbox; delete it by hand")
        }
    }
}

$code = 1
try {
    $code = Invoke-Smoke | Select-Object -Last 1
} catch {
    [Console]::Error.WriteLine("sandbox-smoke: $($_.Exception.Message)")
    $code = 1
} finally {
    Remove-Sandbox
}
exit $code
