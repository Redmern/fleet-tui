# End-to-end check of the embedded multiplexer on Windows, without opening a window.
#
#   scripts\e2e\windows.ps1                 the newest build of scripts\embedded.ps1
#   scripts\e2e\windows.ps1 -Fleet <exe>    any fleet.exe
#
# Starts its own fleetd (own config under %TEMP%, own pipe) and talks to it as a control
# client and as an attach client that records every frame, so ConPTY panes, Terminal.Gui
# in panes, floats, the warm menu and session restore all run for real. The attach client
# is this script, not `fleet attach`: the Windows console client itself is not covered.
# Only the fleetd this script starts is ever stopped (its panes go with it), so a fleet
# session running from the same build is left alone. Step 6 runs `fleet apply-keybinds` and
# `fleet doctor` against a temp fleet-nvim (XDG_CONFIG_HOME) and Claude home (CLAUDE_CONFIG_DIR).
# Exits with the number of failed checks.

param([string]$Fleet)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

if (-not $Fleet) {
    $pointer = Join-Path $root 'artifacts/embedded/win-x64/current'
    if (-not (Test-Path $pointer)) { throw 'no build yet: run scripts\embedded.ps1 build, or pass -Fleet' }
    $Fleet = Join-Path $root "artifacts/embedded/win-x64/$((Get-Content $pointer -Raw).Trim())/fleet.exe"
}
$Fleet = (Resolve-Path $Fleet).Path

$work = Join-Path ([IO.Path]::GetTempPath()) "fleet-e2e-$(Get-Random)"
New-Item -ItemType Directory -Force "$work\config\projects", "$work\alpha", "$work\xdg\fleet-nvim", "$work\claude" | Out-Null
Set-Content "$work\xdg\fleet-nvim\init.lua" '-- stands in for an installed fleet-nvim'
git -C "$work\alpha" init -q
Set-Content "$work\config\projects\alpha.json" (@{ version = 1; name = 'alpha'; root = "$work\alpha" } | ConvertTo-Json) -NoNewline

$saved = @{ FLEET_MUX = $env:FLEET_MUX; FLEET_CONFIG_HOME = $env:FLEET_CONFIG_HOME; FLEET_ENDPOINT = $env:FLEET_ENDPOINT; XDG_CONFIG_HOME = $env:XDG_CONFIG_HOME; CLAUDE_CONFIG_DIR = $env:CLAUDE_CONFIG_DIR }
$env:FLEET_MUX = 'embedded'
$env:FLEET_CONFIG_HOME = "$work\config"
$env:FLEET_ENDPOINT = "fleet-e2e-$(Get-Random)"
$session = Join-Path "$work\config" "embedded-session-$($env:FLEET_ENDPOINT).json"
$daemons = [Collections.Generic.List[int]]::new()
$script:fails = 0

function Ok([string]$what) { Write-Host "ok   $what" }
function Bad([string]$what) { Write-Host "FAIL $what"; $script:fails++ }
function Check([bool]$passed, [string]$what) { if ($passed) { Ok $what } else { Bad $what } }

function Start-Fleetd {
    $process = Start-Process $Fleet -ArgumentList 'daemon' -WindowStyle Hidden -PassThru
    $daemons.Add($process.Id)
    for ($i = 0; $i -lt 50; $i++) {
        try { $null = Ctl ping; return $process } catch { Start-Sleep -Milliseconds 100 }
    }
    throw 'fleetd did not come up'
}

class Pipe {
    [IO.Pipes.NamedPipeClientStream]$Stream
    [Threading.Tasks.Task[int]]$Pending
    [byte[]]$Head = [byte[]]::new(5)

    Pipe([string]$endpoint) {
        $this.Stream = [IO.Pipes.NamedPipeClientStream]::new('.', $endpoint, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
        $this.Stream.Connect(3000)
    }

    [void] Send([byte]$type, [string]$json) {
        $body = [Text.Encoding]::UTF8.GetBytes($json)
        $this.Stream.Write([BitConverter]::GetBytes([uint32]($body.Length + 1)), 0, 4)
        $this.Stream.WriteByte($type)
        $this.Stream.Write($body, 0, $body.Length)
        $this.Stream.Flush()
    }

    [bool] Fill([byte[]]$buffer, [int]$ms) {
        $read = 0
        $deadline = [DateTime]::UtcNow.AddMilliseconds($ms)
        while ($read -lt $buffer.Length) {
            if (-not $this.Pending) { $this.Pending = $this.Stream.ReadAsync($buffer, $read, $buffer.Length - $read) }
            $left = [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds
            if ($read -eq 0 -and ($left -le 0 -or -not $this.Pending.Wait($left))) { return $false }
            $null = $this.Pending.Wait()
            $read += $this.Pending.Result
            $this.Pending = $null
        }
        return $true
    }

    [object] Receive([int]$ms) {
        if (-not $this.Fill($this.Head, $ms)) { return $null }
        $body = [byte[]]::new([BitConverter]::ToUInt32($this.Head, 0) - 1)
        $null = $this.Fill($body, 10000)
        return [pscustomobject]@{ Type = $this.Head[4]; Body = $body }
    }
}

function Ctl([string]$Op, [hashtable]$Fields = @{}) {
    $pipe = [Pipe]::new($env:FLEET_ENDPOINT)
    try {
        $pipe.Send(1, '{"version":1,"role":"control","os":"e2e"}')
        $null = $pipe.Receive(3000)
        $pipe.Send(30, ((@{ id = 1; op = $Op } + $Fields) | ConvertTo-Json -Compress -Depth 5))
        $reply = $pipe.Receive(10000)
        return [Text.Encoding]::UTF8.GetString($reply.Body) | ConvertFrom-Json
    }
    finally { $pipe.Stream.Dispose() }
}

class Client {
    [Pipe]$Pipe
    [Collections.Generic.List[object]]$Frames = [Collections.Generic.List[object]]::new()
    [Diagnostics.Stopwatch]$Clock = [Diagnostics.Stopwatch]::StartNew()

    Client([string]$endpoint, [string]$workspace) {
        $this.Pipe = [Pipe]::new($endpoint)
        $this.Pipe.Send(1, "{`"version`":1,`"role`":`"attach`",`"os`":`"e2e`",`"cols`":120,`"rows`":40,`"workspace`":`"$workspace`"}")
    }

    [void] Pump([int]$ms) {
        $until = $this.Clock.ElapsedMilliseconds + $ms
        while ($this.Clock.ElapsedMilliseconds -lt $until) {
            $m = $this.Pipe.Receive([Math]::Max(1, $until - $this.Clock.ElapsedMilliseconds))
            if ($m -and $m.Type -eq 20) {
                $this.Frames.Add([pscustomobject]@{ At = $this.Clock.ElapsedMilliseconds; Text = [Text.Encoding]::UTF8.GetString($m.Body, 9, $m.Body.Length - 9) })
            }
        }
    }

    [object] WaitFor([string]$text, [int]$ms) {
        $until = $this.Clock.ElapsedMilliseconds + $ms
        while ($this.Clock.ElapsedMilliseconds -lt $until) {
            $hit = $this.Frames | Where-Object { $_.Text.Contains($text) } | Select-Object -First 1
            if ($hit) { return $hit }
            $this.Pump(50)
        }
        return $null
    }

    [void] Command([string]$name) { $this.Pipe.Send(13, "{`"name`":`"$name`"}") }

    [void] Key([string]$text) { $this.Pipe.Send(10, "{`"text`":`"$text`",`"action`":1}") }

    [void] Close() { $this.Pipe.Stream.Dispose() }
}

function Apply-Keybinds {
    foreach ($target in 'nvim', 'claude') {
        & $Fleet apply-keybinds --target $target 2>&1
        if ($LASTEXITCODE) { "exit $LASTEXITCODE from --target $target" }
    }
}

function Hashes([string[]]$Files) { $Files | ForEach-Object { if (Test-Path $_) { (Get-FileHash $_).Hash } else { 'missing' } } }

function Panes([string]$Except) { (Ctl list-panes).panes | Where-Object id -ne $Except | ForEach-Object { "$($_.session)/$($_.tab)" } | Sort-Object }

try {
    Write-Host "fleet: $Fleet"
    Write-Host '== 1. fleetd, a shell and a dashboard'
    $null = Start-Fleetd
    $shell = (Ctl spawn @{ session = 'alpha'; newWindow = $true; cwd = "$work\alpha"; args = @('cmd.exe', '/d', '/q', '/k', 'echo E2E-SHELL') }).pane
    $null = Ctl split @{ pane = $shell; direction = 'right'; percent = 50; cwd = "$work\alpha"; args = @($Fleet, 'dash', '--project', 'alpha') }
    $client = [Client]::new($env:FLEET_ENDPOINT, 'alpha')
    Check ($null -ne $client.WaitFor('E2E-SHELL', 5000)) 'shell pane drawn'
    Check ($null -ne $client.WaitFor('Agents', 8000)) 'dashboard drawn (Terminal.Gui in a ConPTY pane)'

    Write-Host '== 2. warm menu'
    $warm = $false
    for ($i = 0; $i -lt 60 -and -not $warm; $i++) {
        $client.Pump(100)
        $warm = [bool](Select-String "$work\config\fleet.log" -Pattern 'warm menu p\d+ for alpha' -Quiet)
    }
    Check $warm 'a warm menu starts for the shown workspace'
    $client.Pump(1500)
    $client.Frames.Clear()
    $asked = $client.Clock.ElapsedMilliseconds
    $client.Command('menu')
    $menu = $client.WaitFor('fleet menu', 3000)
    Check ($null -ne $menu -and $menu.Text.Contains('Settings')) 'menu drawn with its items in one frame'
    if ($menu) { Check (($menu.At - $asked) -lt 200) "menu shown $($menu.At - $asked) ms after asking (under 200)" }

    Write-Host '== 3. a screen opened from the menu draws once'
    $client.Frames.Clear()
    $client.Key('s')
    $client.Pump(2500)
    $drawn = @($client.Frames | Where-Object { $_.Text.Length -gt 200 })
    Check ($drawn.Count -eq 1 -and $drawn[0].Text.Contains('Rebuild')) "Settings drawn in $($drawn.Count) frame(s) (want 1)"

    Write-Host '== 4. floats'
    $null = Ctl spawn-float @{ session = 'alpha'; cwd = "$work\alpha"; args = @('cmd.exe', '/d', '/q', '/k', 'echo E2E-FLOAT') }
    Check ($null -ne $client.WaitFor('E2E-FLOAT', 5000)) 'float drawn'

    Write-Host '== 5. restore after fleetd is killed'
    $client.Pump(1500)
    Check (Test-Path $session) 'session saved'
    $openMenu = (Select-String "$work\config\fleet.log" -Pattern ': warm menu (p\d+)$' | Select-Object -Last 1).Matches[0].Groups[1].Value
    $before = Panes -Except $openMenu
    $client.Close()
    Stop-Process -Id $daemons[0] -Force
    Start-Sleep -Milliseconds 500
    $null = Start-Fleetd
    Start-Sleep 2
    $after = Panes
    Check ((Compare-Object $before $after) -eq $null) "same panes after restore, less the open menu ($($after.Count))"
    $client = [Client]::new($env:FLEET_ENDPOINT, 'alpha')
    Check ($null -ne $client.WaitFor('Agents', 8000)) 'restored dashboard drawn'
    Check ($null -ne $client.WaitFor('E2E-SHELL', 5000)) 'restored shell drawn'
    Check ([bool](Select-String "$work\config\fleet.log" -Pattern 'restored \d+ of \d+ panes' -Quiet)) 'fleetd logged the restore'
    $client.Close()

    Write-Host '== 6. apply-keybinds writes nvim and Claude keys once; doctor sees drift'
    $env:XDG_CONFIG_HOME = "$work\xdg"
    $env:CLAUDE_CONFIG_DIR = "$work\claude"
    $generated = "$work\xdg\fleet-nvim\lua\fleet\keybinds.generated.lua"
    $files = @($generated, "$work\config\fleet-keys.lua", "$work\claude\keybindings.json")
    $first = @(Apply-Keybinds)
    $first | ForEach-Object { Write-Host "     | $_" }
    Check (-not ($first -match '^exit |FAILED')) 'apply-keybinds succeeds for nvim and claude'
    foreach ($file in $files) {
        $name = Split-Path $file -Leaf
        Check ((Test-Path $file) -and [bool]($first -match "wrote .*$([regex]::Escape($name))")) "wrote $name"
    }
    $before = Hashes $files
    $second = @(Apply-Keybinds)
    Check (-not ($second -match 'wrote|FAILED|^exit ') -and @($second -match 'up to date').Count -eq $files.Count) 'a second run writes nothing'
    Check ((Compare-Object $before (Hashes $files)) -eq $null) 'the files are unchanged by the second run'
    $doctor = (& $Fleet doctor 2>&1) -join "`n"
    Check ($doctor -match 'keybinds\s+nvim and Claude files match the keybind model') 'doctor reports no keybind drift'
    Add-Content $generated '-- edited by hand'
    $doctor = (& $Fleet doctor 2>&1) -join "`n"
    Check ($doctor -match 'keybinds\s+! 1 file\(s\) out of date' -and $doctor -match 'keybinds\.generated\.lua differs from the keybind model') 'doctor reports drift after an edit'
}
catch {
    Bad "script error: $_"
}
finally {
    foreach ($id in $daemons) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    foreach ($name in $saved.Keys) { Set-Item "env:$name" $saved[$name] }
    if (-not $script:fails) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "kept $work (fleet.log is in config)" }
}

Write-Host "== summary: $($script:fails) failure(s)"
exit $script:fails
