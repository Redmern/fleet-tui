<#
.SYNOPSIS
    Checks the installers' progress helpers without installing anything.

.DESCRIPTION
    Loads the helper functions out of install.ps1 and get-fleet.ps1 (through the
    parser, so none of the install steps run) and checks that they fall back to
    plain '==> step' output when stdout is not an interactive console. Runs
    install.sh --uninstall against a throwaway FLEET_BIN_DIR for the sh side.

    Plain PowerShell rather than Pester, so it runs on Windows PowerShell 5.1 and
    pwsh 7 with whatever Pester version is installed (or none). Exits 1 on failure.

.EXAMPLE
    pwsh -NoProfile -File tests\scripts\installer-output.ps1
#>
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '../..')
$shell = (Get-Process -Id $PID).Path
$failures = 0

function Check($name, $condition) {
    if ($condition) { Write-Host "ok   $name" -ForegroundColor Green }
    else { Write-Host "FAIL $name" -ForegroundColor Red; $script:failures++ }
}

# The helper definitions of an installer, as source text: the Fancy/Step*
# assignments and every function, but none of the install steps.
function Get-Helpers($path) {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$null)
    $statements = $ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -or
        ($_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $_.Left.Extent.Text -match '^\$script:(Fancy|StepCount|StepTotal)$')
    }
    ($statements | ForEach-Object { $_.Extent.Text }) -join "`n"
}

function Get-FancyExpression($path) {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$null)
    $assignment = $ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$script:Fancy'
    }
    $assignment.Right.Extent.Text
}

foreach ($installer in 'install.ps1', 'scripts/get-fleet.ps1') {
    $helpers = Get-Helpers (Join-Path $repo $installer)
    $probe = Join-Path ([IO.Path]::GetTempPath()) "fleet-installer-probe-$([guid]::NewGuid().ToString('N')).ps1"

    Set-Content -Path $probe -Encoding ASCII -Value @"
`$WithDeps = `$false
$helpers
"fancy=`$script:Fancy"
Write-Step 'First'
Write-Ok 'detail'
Write-Step 'Second'
Complete-Steps
"bar=`$(Format-Bar 0.5 10)"
"@

    try {
        $out = & $shell -NoProfile -NonInteractive -File $probe 2>&1 | Out-String
    }
    finally {
        Remove-Item $probe -Force
    }

    Check "$installer is plain when redirected" ($out -match 'fancy=False')
    Check "$installer prints '==> step' lines" ($out -match '(?m)^==> First\r?$' -and $out -match '(?m)^==> Second\r?$')
    $steps = ($out -split "`n" | Where-Object { $_ -notmatch '^bar=' }) -join "`n"
    Check "$installer prints no bar or cursor codes" (-not ($steps -match '\[#|\[-|\x1b|\r(?!\n)'))
    Check "$installer Format-Bar" ($out -match 'bar=\[#####-----\]')

    # The detection expression with the console checks pinned to "interactive", so
    # each opt-out variable can be checked on its own.
    $detect = (Get-FancyExpression (Join-Path $repo $installer)).
        Replace("`$Host.Name -ne 'ConsoleHost'", '$false').
        Replace('[Console]::IsOutputRedirected', '$false')
    $saved = @{}
    foreach ($name in 'FLEET_NO_ANIMATION', 'NO_COLOR', 'CI', 'TERM') {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    try {
        Check "$installer animates on an interactive console" (& ([scriptblock]::Create($detect)))
        foreach ($case in @(@('FLEET_NO_ANIMATION', '1'), @('NO_COLOR', '1'), @('CI', 'true'), @('TERM', 'dumb'))) {
            [Environment]::SetEnvironmentVariable($case[0], $case[1])
            Check "$installer is plain with $($case[0])=$($case[1])" (-not (& ([scriptblock]::Create($detect))))
            [Environment]::SetEnvironmentVariable($case[0], $null)
        }
    }
    finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}

. ([scriptblock]::Create((Get-Helpers (Join-Path $repo 'install.ps1'))))
$script:Fancy = $false
$result = Invoke-WithSpinner 'probe' $shell @('-NoProfile', '-Command', 'Write-Output captured-out; [Console]::Error.WriteLine(''captured-err''); exit 3')
Check 'Invoke-WithSpinner passes the exit code through' ($result.ExitCode -eq 3)
Check 'Invoke-WithSpinner captures stdout and stderr' ($result.Output -match 'captured-out' -and $result.Output -match 'captured-err')

# deps.ps1 detect/offer flow, with Get-Command, winget and the prompt stubbed so
# nothing is installed. Each case runs in its own scope and returns its Write-Host
# output plus the winget calls it made.
function Invoke-DepsCase([string[]]$Present, [bool]$Winget = $true, [int]$WingetExit = 0, [hashtable]$Offer = @{}) {
    $deps = Join-Path $repo 'scripts/deps.ps1'
    $calls = [System.Collections.Generic.List[string]]::new()
    $out = & {
        . $deps
        function Get-Command {
            [CmdletBinding()] param([string]$Name)
            if ($Name -eq 'winget') { if ($Winget) { [pscustomobject]@{ Name = 'winget' } } ; return }
            if ($Present -contains $Name) { [pscustomobject]@{ Name = $Name } }
        }
        function winget {
            $calls.Add($args -join ' ')
            if ($WingetExit -lt 0) { throw 'winget exploded' }
            $global:LASTEXITCODE = $WingetExit
        }
        Invoke-FleetDepsOffer @Offer
    } 6>&1 2>&1 | Out-String
    [pscustomobject]@{ Output = $out; Calls = @($calls) }
}

$all = Invoke-DepsCase -Present 'git', 'nvim', 'yazi' -Offer @{ Interactive = $true; ReadAnswer = { throw 'asked' } }
Check 'deps: all present asks nothing and installs nothing' ($all.Calls.Count -eq 0 -and $all.Output -notmatch 'asked|\[y/N\]|missing')

$plain = Invoke-DepsCase -Present 'git' -Offer @{ Interactive = $false; ReadAnswer = { throw 'asked' } }
Check 'deps: names the missing tools' ($plain.Output -match 'missing: nvim, yazi')
Check 'deps: non-interactive installs nothing' ($plain.Calls.Count -eq 0 -and $plain.Output -notmatch 'asked')
Check 'deps: non-interactive prints the winget commands' (
    $plain.Output -match 'winget install --id Neovim\.Neovim -e' -and $plain.Output -match 'winget install --id sxyazi\.yazi -e' -and
    $plain.Output -notmatch 'Git\.Git')

$with = Invoke-DepsCase -Present 'nvim' -Offer @{ WithDeps = $true; Interactive = $true; ReadAnswer = { throw 'asked' } }
Check 'deps: -WithDeps installs only the missing tools without asking' (
    $with.Output -notmatch 'asked' -and $with.Output -match 'missing: git, yazi' -and $with.Calls.Count -eq 2 -and
    ($with.Calls -match 'Git\.Git').Count -eq 1 -and ($with.Calls -match 'sxyazi\.yazi').Count -eq 1)

$yes = Invoke-DepsCase -Present 'git', 'nvim' -Offer @{ Interactive = $true; ReadAnswer = { 'y' } }
Check 'deps: interactive yes installs the missing tool' ($yes.Output -match 'yazi' -and $yes.Calls.Count -eq 1 -and $yes.Calls[0] -match 'sxyazi\.yazi')

foreach ($answer in 'n', '') {
    $no = Invoke-DepsCase -Present 'git', 'nvim' -Offer @{ Interactive = $true; ReadAnswer = [scriptblock]::Create("'$answer'") }
    Check "deps: interactive answer '$answer' installs nothing and prints the command" (
        $no.Calls.Count -eq 0 -and $no.Output -match 'winget install --id sxyazi\.yazi -e')
}

$nowinget = Invoke-DepsCase -Present @() -Winget $false -Offer @{ Interactive = $true; ReadAnswer = { throw 'asked' } }
Check 'deps: without winget prints manual guidance and asks nothing' (
    $nowinget.Calls.Count -eq 0 -and $nowinget.Output -notmatch 'asked' -and
    $nowinget.Output -match 'missing: nvim, git, yazi' -and $nowinget.Output -match 'winget is not available' -and
    $nowinget.Output -match 'neovim\.io' -and $nowinget.Output -match 'git-scm\.com' -and $nowinget.Output -match 'yazi')

$nowingetWith = Invoke-DepsCase -Present @() -Winget $false -Offer @{ WithDeps = $true; SkipConfig = $true }
Check 'deps: -WithDeps without winget prints manual guidance' ($nowingetWith.Calls.Count -eq 0 -and $nowingetWith.Output -match 'neovim\.io')

$failed = Invoke-DepsCase -Present @() -WingetExit 1 -Offer @{ WithDeps = $true; SkipConfig = $true }
Check 'deps: a failing winget install warns and carries on' ($failed.Calls.Count -eq 3 -and $failed.Output -match 'could not install yazi')

$threw = Invoke-DepsCase -Present @() -WingetExit -1 -Offer @{ WithDeps = $true; SkipConfig = $true }
Check 'deps: a throwing winget install warns and carries on' ($threw.Calls.Count -eq 3 -and $threw.Output -match 'could not install yazi')

# The editor choice: with neither nvim nor code on PATH an interactive console is
# asked nvim / VS Code / skip; the answer is installed and recorded in editor.json
# (the bytes JsonSettingsStore reads); anything else prints both commands.
function Invoke-EditorCase([string[]]$Present, [hashtable]$Offer) {
    $home_ = Join-Path ([IO.Path]::GetTempPath()) "fleet-editor-$([guid]::NewGuid().ToString('N'))"
    try {
        $case = Invoke-DepsCase -Present $Present -Offer ($Offer + @{ ConfigHome = $home_ })
        $file = Join-Path $home_ 'editor.json'
        $case | Add-Member -NotePropertyName Editor -NotePropertyValue $(if (Test-Path $file) { [IO.File]::ReadAllText($file) } else { $null })
        $case
    }
    finally {
        Remove-Item $home_ -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function EditorJson($editor) { "{`n  `"version`": 1,`n  `"editor`": `"$editor`"`n}`n" }

$noEditor = Invoke-EditorCase -Present 'git', 'yazi' -Offer @{ Interactive = $false; ReadAnswer = { throw 'asked' }; EditorAnswer = { throw 'asked' } }
Check 'editor: non-interactive with no editor prints both commands (AC-10)' (
    $noEditor.Output -match 'no editor found' -and $noEditor.Output -match 'winget install --id Neovim\.Neovim -e' -and
    $noEditor.Output -match 'winget install --id Microsoft\.VisualStudioCode -e')
Check 'editor: non-interactive asks, installs and writes nothing (AC-10)' (
    $noEditor.Output -notmatch 'asked' -and $noEditor.Calls.Count -eq 0 -and $null -eq $noEditor.Editor)

$withCode = Invoke-EditorCase -Present 'git', 'yazi', 'code' -Offer @{ Interactive = $true; ReadAnswer = { 'n' }; EditorAnswer = { throw 'asked' } }
Check 'editor: code on PATH does not ask for an editor (AC-11)' (
    $withCode.Output -notmatch 'asked|no editor found' -and $null -eq $withCode.Editor -and $withCode.Output -match 'missing: nvim')

$withNvim = Invoke-EditorCase -Present 'git', 'yazi', 'nvim' -Offer @{ Interactive = $true; ReadAnswer = { throw 'asked' }; EditorAnswer = { throw 'asked' } }
Check 'editor: nvim on PATH does not ask for an editor (AC-11)' ($withNvim.Output -notmatch 'asked|no editor found' -and $null -eq $withNvim.Editor)

$vscode = Invoke-EditorCase -Present 'git', 'yazi' -Offer @{ Interactive = $true; ReadAnswer = { throw 'asked' }; EditorAnswer = { '2' } }
Check 'editor: VS Code is installed with winget and nvim is not offered (AC-10)' (
    $vscode.Calls.Count -eq 1 -and $vscode.Calls[0] -match 'Microsoft\.VisualStudioCode' -and
    $vscode.Output -notmatch 'asked|missing: nvim')
Check 'editor: VS Code is recorded as the global default (AC-10)' ($vscode.Editor -eq (EditorJson 'vscode'))

$nvim = Invoke-EditorCase -Present 'git', 'yazi' -Offer @{ Interactive = $true; ReadAnswer = { 'y' }; EditorAnswer = { '1' } }
Check 'editor: nvim is installed through the deps offer and recorded (AC-10)' (
    $nvim.Calls.Count -eq 1 -and $nvim.Calls[0] -match 'Neovim\.Neovim' -and $nvim.Editor -eq (EditorJson 'nvim'))

$skip = Invoke-EditorCase -Present 'git', 'yazi' -Offer @{ Interactive = $true; ReadAnswer = { throw 'asked' }; EditorAnswer = { '3' } }
Check 'editor: skip installs and records nothing (AC-10)' (
    $skip.Calls.Count -eq 0 -and $null -eq $skip.Editor -and $skip.Output -notmatch 'asked|missing: nvim')

$saved = @{}
foreach ($name in 'CI', 'FLEET_NO_DEPS_PROMPT') { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    . (Join-Path $repo 'scripts/deps.ps1')
    foreach ($name in 'CI', 'FLEET_NO_DEPS_PROMPT') {
        foreach ($n in $saved.Keys) { [Environment]::SetEnvironmentVariable($n, $null) }
        [Environment]::SetEnvironmentVariable($name, '1')
        Check "deps: $name=1 is not interactive" (-not (Test-FleetDepsInteractive))
    }
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
$redirected = & $shell -NoProfile -Command ". '$(Join-Path $repo 'scripts/deps.ps1')'; Test-FleetDepsInteractive" 2>&1 | Out-String
Check 'deps: a redirected run is not interactive' ($redirected.Trim() -eq 'False')

# Both installers run the offer on every install, not only under -WithDeps.
foreach ($installer in 'install.ps1', 'scripts/get-fleet.ps1') {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $installer), [ref]$null, [ref]$null)
    $offers = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-FleetDepsOffer' }, $true))
    $guarded = @($offers | Where-Object {
        $p = $_.Parent
        while ($p -and -not ($p -is [System.Management.Automation.Language.IfStatementAst] -and $p.Clauses[0].Item1.Extent.Text -eq '$WithDeps')) { $p = $p.Parent }
        $p
    })
    Check "$installer offers the deps on every install" ($offers.Count -eq 1 -and $guarded.Count -eq 0 -and $offers[0].Extent.Text -match '-WithDeps:\$WithDeps')
}

# get-fleet.ps1 font step: fonts.ps1 is fetched like deps.ps1 (irm | iex has no
# sibling files), and nothing about the font can fail the install. The fetch is
# stubbed, so each case writes its own fonts.ps1 or throws.
$getFleet = Join-Path $repo 'scripts/get-fleet.ps1'
$getFleetHelpers = Get-Helpers $getFleet

function Invoke-FontStepCase([scriptblock]$Fetch) {
    $marker = Join-Path ([IO.Path]::GetTempPath()) "fleet-font-step-$([guid]::NewGuid().ToString('N'))"
    $script:fontMarker = $marker
    $out = & {
        $ErrorActionPreference = 'Stop'
        $WithDeps = $false
        . ([scriptblock]::Create($getFleetHelpers))
        $script:Fancy = $false
        Invoke-FleetFontStep -Repo 'owner/fleet' -Fetch $Fetch
        'step returned'
    } 6>&1 2>&1 | Out-String
    $called = Test-Path -LiteralPath $marker
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    [pscustomobject]@{ Output = $out; Called = $called }
}

$fetchedUrl = $null
$ok = Invoke-FontStepCase {
    param($Uri, $OutFile)
    $script:fetchedUrl = $Uri
    Set-Content -LiteralPath $OutFile -Value "function Install-FleetFont { New-Item -ItemType File -Path '$($script:fontMarker -replace "'", "''")' | Out-Null; `$true }"
}
Check 'get-fleet: fetches fonts.ps1 from the same repository as deps.ps1' ($fetchedUrl -eq 'https://raw.githubusercontent.com/owner/fleet/main/scripts/fonts.ps1')
Check 'get-fleet: runs Install-FleetFont from the fetched fonts.ps1' ($ok.Called)
Check 'get-fleet: the font step prints no result value and carries on' ($ok.Output -notmatch '(?m)^True\r?$' -and $ok.Output -match 'step returned')

$noFetch = Invoke-FontStepCase { param($Uri, $OutFile) throw 'offline' }
Check 'get-fleet: a failed fonts.ps1 fetch warns and carries on' (
    $noFetch.Output -match 'could not install JetBrainsMono Nerd Font' -and $noFetch.Output -match 'offline' -and
    $noFetch.Output -match 're-run the installer' -and $noFetch.Output -match 'step returned')

$broken = Invoke-FontStepCase {
    param($Uri, $OutFile)
    Set-Content -LiteralPath $OutFile -Value "function Install-FleetFont { throw 'font exploded' }"
}
Check 'get-fleet: a throwing font install warns and carries on' (
    $broken.Output -match 'font exploded' -and $broken.Output -match 're-run the installer' -and $broken.Output -match 'step returned')

$failed = Invoke-FontStepCase {
    param($Uri, $OutFile)
    Set-Content -LiteralPath $OutFile -Value "function Install-FleetFont { Write-Host '    could not install it'; `$false }"
}
Check 'get-fleet: a font install that reports failure carries on' ($failed.Output -match 'could not install it' -and $failed.Output -match 'step returned')

$getFleetAst = [System.Management.Automation.Language.Parser]::ParseFile($getFleet, [ref]$null, [ref]$null)
$top = @($getFleetAst.EndBlock.Statements)
$stepAt = -1; $setupAt = -1; $doneAt = -1
for ($i = 0; $i -lt $top.Count; $i++) {
    $text = $top[$i].Extent.Text
    if ($text -match '^Invoke-FleetFontStep\b') { $stepAt = $i }
    if ($text -match '^& \$BinPath setup') { $setupAt = $i }
    if ($text -eq 'Complete-Steps') { $doneAt = $i }
}
$fontCalls = @($getFleetAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-FleetFontStep' }, $true))
Check 'get-fleet: installs the font on every install, after fleet setup' (
    $fontCalls.Count -eq 1 -and $stepAt -ge 0 -and $setupAt -ge 0 -and $stepAt -gt $setupAt -and $stepAt -lt $doneAt -and
    $top[$stepAt - 1].Extent.Text -match "^Write-Step 'Installing JetBrainsMono Nerd Font'")

$steps = @($getFleetAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Write-Step' }, $true)).Count
$totals = foreach ($deps in $true, $false) {
    & {
        $WithDeps = $deps
        $DepsMissing = @()
        . ([scriptblock]::Create($getFleetHelpers))
        $script:StepTotal
    }
}
Check 'get-fleet: the step total counts the font step' ($totals[0] -eq $steps -and $totals[1] -eq $steps - 1)

# The default execution policy on Windows clients (Restricted) refuses to dot-source
# a .ps1 file, and irm | iex works around it, so fetched scripts run as script blocks.
$dotSourcedFiles = @($getFleetAst.FindAll({
            param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.InvocationOperator -eq 'Dot' -and
            $n.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst]
        }, $true))
Check 'get-fleet: runs fetched scripts without dot-sourcing a .ps1 file' ($dotSourcedFiles.Count -eq 0)

Check 'get-fleet: never touches settings.json or HKCU:\Console' (
    [IO.File]::ReadAllText($getFleet) -notmatch 'settings\.json|HKCU:\\Console')

$sh = Get-Command sh -ErrorAction SilentlyContinue
if ($sh) {
    $bin = Join-Path ([IO.Path]::GetTempPath()) "fleet-installer-bin-$([guid]::NewGuid().ToString('N'))"
    $env:FLEET_BIN_DIR = $bin
    try {
        $out = & $sh.Source (Join-Path $repo 'install.sh') --uninstall 2>&1 | Out-String
    }
    finally {
        Remove-Item Env:FLEET_BIN_DIR
    }
    Check 'install.sh is plain when piped' ($out -match '(?m)^==> Uninstalling\r?$' -and -not ($out -match '\[#|\x1b'))
}
else {
    Write-Host 'skip install.sh (no sh on PATH)' -ForegroundColor Yellow
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'all checks passed' -ForegroundColor Green
