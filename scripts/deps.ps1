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
    )
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

# The installers' entry point. Interactive and ReadAnswer exist for the tests:
# leave them out and the console is detected and asked through Read-Host.
function Invoke-FleetDepsOffer {
    [CmdletBinding()]
    param(
        [switch]$WithDeps,
        [string]$NvimConfig,
        [switch]$SkipConfig,
        [object]$Interactive = $null,
        [scriptblock]$ReadAnswer = { Read-Host '    install them with winget now? [y/N]' }
    )

    try {
        $missing = Get-MissingFleetDeps

        Write-Host '==> Checking git, nvim and yazi' -ForegroundColor Cyan

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
