<#
.SYNOPSIS
    Install what fleet needs on Windows: Neovim, git and yazi.

.DESCRIPTION
    Dot-source this and call Invoke-FleetDepsOffer (or Install-FleetDeps directly).
    Both installers use it, which is why it defines functions and does nothing on
    its own.

    Invoke-FleetDepsOffer checks which of git, nvim and yazi are missing from PATH.
    With -WithDeps it installs them; on an interactive console it asks first
    (y/N); anywhere else (redirected, -NonInteractive, CI, FLEET_NO_DEPS_PROMPT)
    it prints the winget commands and installs nothing. A failed install only
    warns, so the fleet install carries on.

    With neither nvim nor code on PATH, Invoke-FleetEditorChoice runs first: an
    interactive console picks nvim, VS Code or skip, and the pick is recorded as
    fleet's global default editor in editor.json.

    Everything is skipped when it is already there, so re-running is harmless.

    fleet ships its own Neovim config (nvim\ in the repo, embedded in the binary) and
    'fleet setup' writes it to %LOCALAPPDATA%\fleet-nvim, so no config is cloned here
    unless you ask for one with -NvimConfig or FLEET_NVIM_CONFIG.

.NOTES
    Package installs go through winget. Without winget the functions report what
    is missing, with where to get it, and leave the machine alone.
#>

function Get-FleetDepTools {
    @(
        [pscustomobject]@{ Name = 'nvim'; Id = 'Neovim.Neovim'; Url = 'https://neovim.io (or https://github.com/neovim/neovim/releases)' },
        [pscustomobject]@{ Name = 'git'; Id = 'Git.Git'; Url = 'https://git-scm.com/download/win' },
        [pscustomobject]@{ Name = 'yazi'; Id = 'sxyazi.yazi'; Url = 'https://yazi-rs.github.io/docs/installation' }
    ) | Where-Object { -not ($script:FleetEditorSkipsNvim -and $_.Name -eq 'nvim') }
}

function Get-MissingFleetDeps {
    @(Get-FleetDepTools | Where-Object { -not (Get-Command $_.Name -ErrorAction SilentlyContinue) })
}

# Whether a y/N question can be asked: a real console on both ends, not
# -NonInteractive, not CI, and not opted out with FLEET_NO_DEPS_PROMPT.
function Test-FleetDepsInteractive {
    if ($env:CI -or $env:FLEET_NO_DEPS_PROMPT) { return $false }
    if (-not [Environment]::UserInteractive) { return $false }
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { return $false }
    if (@([Environment]::GetCommandLineArgs() | Where-Object { $_ -match '^-noni' }).Count -gt 0) { return $false }
    $true
}

function Write-FleetDepsManual($missing) {
    Write-Host '    winget is not available - install these by hand:' -ForegroundColor Yellow
    foreach ($tool in $missing) {
        Write-Host "      $($tool.Name): $($tool.Url)" -ForegroundColor Yellow
    }
}

function Write-FleetDepsCommands($missing) {
    Write-Host '    to install them, run:' -ForegroundColor Yellow
    foreach ($tool in $missing) {
        Write-Host "      winget install --id $($tool.Id) -e" -ForegroundColor Yellow
    }
}

# The editor agents open. With neither nvim nor code on PATH an interactive console
# (not -WithDeps) is asked nvim / VS Code / skip and the answer becomes fleet's
# global default in editor.json, the file 'fleet' reads. VS Code is installed here;
# nvim is left to the deps offer that follows, and is dropped from it for VS Code
# or skip. Anywhere else both winget commands are printed and nothing is written.
function Invoke-FleetEditorChoice {
    param(
        [switch]$WithDeps,
        [object]$Interactive = $null,
        [scriptblock]$ReadAnswer = { Read-Host '    which editor? nvim / VS Code / skip [1/2/3]' },
        [string]$ConfigHome
    )

    $script:FleetEditorSkipsNvim = $false

    if ((Get-Command nvim -ErrorAction SilentlyContinue) -or (Get-Command code -ErrorAction SilentlyContinue)) {
        return
    }

    Write-Host '    no editor found: fleet opens agents in nvim or VS Code' -ForegroundColor Yellow

    $winget = [bool](Get-Command winget -ErrorAction SilentlyContinue)
    $ask = if ($null -eq $Interactive) { Test-FleetDepsInteractive } else { [bool]$Interactive }

    if ($WithDeps -or -not $ask -or -not $winget) {
        Write-Host '      nvim:    winget install --id Neovim.Neovim -e' -ForegroundColor Yellow
        Write-Host '      VS Code: winget install --id Microsoft.VisualStudioCode -e' -ForegroundColor Yellow
        return
    }

    $editor = switch -Regex ("$(& $ReadAnswer)".Trim()) {
        '^(1|n|nvim)$' { 'nvim' }
        '^(2|v|code|vscode)$' { 'vscode' }
        default { $null }
    }

    if ($editor -ne 'nvim') {
        $script:FleetEditorSkipsNvim = $true
    }

    if (-not $editor) {
        Write-Host "    no editor chosen; set one per project with E in fleet menu > settings > fleet config, or write editor.json in fleet's config folder" -ForegroundColor Yellow
        return
    }

    if ($editor -eq 'vscode') {
        Write-Host '    winget install Microsoft.VisualStudioCode' -ForegroundColor DarkGray
        try {
            winget install --id Microsoft.VisualStudioCode --exact --silent `
                --accept-source-agreements --accept-package-agreements
            if ($LASTEXITCODE -ne 0) {
                Write-Host "    winget could not install VS Code (exit $LASTEXITCODE)" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Host "    winget could not install VS Code: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    $dir = if ($ConfigHome) { $ConfigHome }
    elseif ($env:FLEET_CONFIG_HOME) { $env:FLEET_CONFIG_HOME }
    else { Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'fleet' }

    try {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir 'editor.json'), "{`n  `"version`": 1,`n  `"editor`": `"$editor`"`n}`n")
        Write-Host "    default editor: $editor (in $(Join-Path $dir 'editor.json'))" -ForegroundColor Green
    }
    catch {
        Write-Host "    could not record the editor: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# The installers' entry point. Interactive and ReadAnswer exist for the tests:
# leave them out and the console is detected and asked through Read-Host.
function Invoke-FleetDepsOffer {
    [CmdletBinding()]
    param(
        [switch]$WithDeps,
        [string]$NvimConfig,
        [switch]$SkipConfig,
        [object]$Interactive = $null,
        [scriptblock]$ReadAnswer = { Read-Host '    install them with winget now? [y/N]' },
        [scriptblock]$EditorAnswer,
        [string]$ConfigHome
    )

    try {
        $editorArgs = @{ WithDeps = $WithDeps; Interactive = $Interactive; ConfigHome = $ConfigHome }
        if ($EditorAnswer) { $editorArgs.ReadAnswer = $EditorAnswer }

        Write-Host '==> Checking git, nvim and yazi' -ForegroundColor Cyan

        Invoke-FleetEditorChoice @editorArgs

        $missing = @(Get-MissingFleetDeps)

        if ($missing.Count -eq 0) {
            Write-Host '    git, nvim and yazi are installed' -ForegroundColor Green
        }
        else {
            Write-Host "    missing: $(($missing | ForEach-Object Name) -join ', ')" -ForegroundColor Yellow
        }

        if ($WithDeps) {
            Install-FleetDeps -NvimConfig $NvimConfig -SkipConfig:$SkipConfig
            return
        }

        if ($missing.Count -eq 0) {
            return
        }

        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            Write-FleetDepsManual $missing
            return
        }

        $ask = if ($null -eq $Interactive) { Test-FleetDepsInteractive } else { [bool]$Interactive }

        if ($ask -and ("$(& $ReadAnswer)".Trim() -match '^(y|yes)$')) {
            Install-FleetDeps -SkipConfig
            return
        }

        Write-FleetDepsCommands $missing
        if (-not $ask) {
            Write-Host '    or re-run the installer with -WithDeps' -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "    could not check or install git, nvim and yazi: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Get-FleetConfigHome {
    if ($env:FLEET_CONFIG_HOME) { return $env:FLEET_CONFIG_HOME }
    Join-Path $env:APPDATA 'fleet'
}

# installed-deps.json lists what the installers installed, so 'fleet uninstall' can
# offer to remove it. One entry per tool: a new one replaces it, other entries stay.
# A file it cannot read is left alone; nothing here fails the install.
function Save-FleetDepsManifest($installed) {
    $installed = @($installed)
    if ($installed.Count -eq 0) { return }

    $path = Join-Path (Get-FleetConfigHome) 'installed-deps.json'

    try {
        $names = @($installed | ForEach-Object Name)
        $deps = [System.Collections.Generic.List[object]]::new()

        if (Test-Path -LiteralPath $path) {
            $existing = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            if ($null -eq $existing -or $null -eq $existing.PSObject.Properties['deps']) {
                throw "$path is not a fleet dependency manifest"
            }
            foreach ($dep in @($existing.deps)) {
                if ($names -contains $dep.tool) { continue }
                $at = $dep.installedAt
                if ($at -is [DateTime]) { $at = $at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }
                $deps.Add([ordered]@{ tool = $dep.tool; manager = $dep.manager; package = $dep.package; sudo = [bool]$dep.sudo; installedAt = "$at" })
            }
        }

        $now = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        foreach ($tool in $installed) {
            $deps.Add([ordered]@{ tool = $tool.Name; manager = 'winget'; package = $tool.Id; sudo = $false; installedAt = $now })
        }

        $json = ConvertTo-Json -InputObject ([ordered]@{ version = 1; deps = $deps.ToArray() }) -Depth 4 -Compress
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        [IO.File]::WriteAllText($path, $json)
    }
    catch {
        Write-Host "    could not record the installed tools in ${path}: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Install-FleetDeps {
    [CmdletBinding()]
    param(
        [string]$NvimConfig,
        [switch]$SkipConfig
    )

    function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
    function Ok($m) { Write-Host "    $m" -ForegroundColor Green }
    function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }

    $winget = [bool](Get-Command winget -ErrorAction SilentlyContinue)

    Step 'Installing dependencies'

    $missing = @()
    $installed = @()

    foreach ($tool in Get-FleetDepTools) {
        if (Get-Command $tool.Name -ErrorAction SilentlyContinue) {
            Ok "$($tool.Name) already installed"
            continue
        }

        if (-not $winget) {
            $missing += $tool
            continue
        }

        Write-Host "    winget install $($tool.Id)" -ForegroundColor DarkGray

        try {
            winget install --id $tool.Id --exact --silent `
                --accept-source-agreements --accept-package-agreements

            if ($LASTEXITCODE -eq 0) {
                Ok "installed $($tool.Name)"
                $installed += $tool
            }
            else {
                Warn "winget could not install $($tool.Name) (exit $LASTEXITCODE)"
            }
        }
        catch {
            Warn "winget could not install $($tool.Name): $($_.Exception.Message)"
        }
    }

    if ($missing.Count -gt 0) {
        Write-FleetDepsManual $missing
    }

    Save-FleetDepsManifest $installed

    # winget puts new tools on the machine PATH, which this process does not see
    # until it restarts. Add the usual locations so 'fleet setup' can find them.
    foreach ($dir in @(
            "$env:ProgramFiles\Neovim\bin",
            "$env:ProgramFiles\Git\cmd",
            "$env:LOCALAPPDATA\Microsoft\WinGet\Links")) {
        if ($dir -notmatch '^\\' -and (Test-Path $dir) -and (($env:PATH -split ';') -notcontains $dir)) {
            $env:PATH = "$dir;$env:PATH"
        }
    }

    if ($SkipConfig) {
        return
    }

    $url = if ($NvimConfig) { $NvimConfig } else { $env:FLEET_NVIM_CONFIG }

    Step 'Neovim config'

    if (-not $url) {
        Ok "fleet's own config ships with fleet; 'fleet setup' writes it to $(Join-Path $env:LOCALAPPDATA 'fleet-nvim')"
        return
    }

    $target = Join-Path $env:LOCALAPPDATA 'nvim'

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Warn 'git is not available, so the config cannot be cloned'
        return
    }

    if (Test-Path (Join-Path $target '.git')) {
        $remote = (git -C $target remote get-url origin 2>$null)

        if ($remote -eq $url) {
            git -C $target pull --ff-only | Out-Null
            Ok "updated $target"
        }
        else {
            Warn "$target already holds a different config ($remote) - left alone"
        }
    }
    elseif ((Test-Path $target) -and (Get-ChildItem $target -Force | Select-Object -First 1)) {
        Warn "$target exists and is not a git checkout - left alone"
    }
    else {
        git clone --depth 1 $url $target
        if ($LASTEXITCODE -eq 0) { Ok "cloned $url into $target" } else { Warn 'clone failed' }
    }

    if (Test-Path (Join-Path $target 'bootstrap.sh')) {
        Warn 'this config ships bootstrap.sh for extra tooling - run it yourself if you want it'
    }
}
