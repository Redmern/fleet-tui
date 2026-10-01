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
