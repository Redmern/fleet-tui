<#
.SYNOPSIS
    Install what fleet needs on Windows: WezTerm, Neovim, yazi and a Neovim config.

.DESCRIPTION
    Dot-source this and call Install-FleetDeps. Both installers use it, which is
    why it defines a function and does nothing on its own.

    Everything is skipped when it is already there, so re-running is harmless.

.NOTES
    Package installs go through winget. Without winget the function reports what
    is missing and leaves the machine alone.
#>

# The config cloned when -NvimConfig is not given. Override per machine with
# FLEET_NVIM_CONFIG, or pass -NvimConfig <url>.
$script:DefaultNvimConfig = 'https://github.com/Redmern/nvim_0.12.git'

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

    $tools = @(
        @{ Name = 'wezterm'; Id = 'wez.wezterm' },
        @{ Name = 'nvim'; Id = 'Neovim.Neovim' },
        @{ Name = 'git'; Id = 'Git.Git' },
        @{ Name = 'yazi'; Id = 'sxyazi.yazi' }
    )

    Step 'Installing dependencies'

    foreach ($tool in $tools) {
        if (Get-Command $tool.Name -ErrorAction SilentlyContinue) {
            Ok "$($tool.Name) already installed"
            continue
        }

        if (-not $winget) {
            Warn "$($tool.Name) is missing and winget is not available - install it by hand"
            continue
        }

        Write-Host "    winget install $($tool.Id)" -ForegroundColor DarkGray

        winget install --id $tool.Id --exact --silent `
            --accept-source-agreements --accept-package-agreements

        if ($LASTEXITCODE -eq 0) {
            Ok "installed $($tool.Name)"
        }
        else {
            Warn "winget could not install $($tool.Name) (exit $LASTEXITCODE)"
        }
    }

    # winget puts new tools on the machine PATH, which this process does not see
    # until it restarts. Add the usual locations so 'fleet setup' can find them.
    foreach ($dir in @(
            (Join-Path $env:ProgramFiles 'WezTerm'),
            (Join-Path $env:ProgramFiles 'Neovim\bin'),
            (Join-Path $env:ProgramFiles 'Git\cmd'),
            (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links'))) {
        if ((Test-Path $dir) -and (($env:PATH -split ';') -notcontains $dir)) {
            $env:PATH = "$dir;$env:PATH"
        }
    }

    if ($SkipConfig) {
        return
    }

    $url = if ($NvimConfig) { $NvimConfig }
           elseif ($env:FLEET_NVIM_CONFIG) { $env:FLEET_NVIM_CONFIG }
           else { $script:DefaultNvimConfig }

    Step 'Neovim config'

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
