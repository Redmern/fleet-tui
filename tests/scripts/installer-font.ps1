<#
.SYNOPSIS
    Checks the installers' Nerd Font step without touching the network or the real font store.

.DESCRIPTION
    Dot-sources scripts/fonts.ps1 and runs Install-FleetFont / Uninstall-FleetFont
    against a temp font directory and a locally built fake JetBrainsMono.zip. On
    Windows the registry values go under a throwaway HKCU:\Software\FleetTest key;
    elsewhere the three registry helpers are stubbed with a hashtable, so the
    file-placement and flow checks still run.

    Also checks that install.ps1 wires the step in (and purges the font only under
    -Uninstall -Purge) and that the Windows Terminal fragment asks for the same face.

    Plain PowerShell rather than Pester, so it runs on Windows PowerShell 5.1 and
    pwsh 7. Exits 1 on failure.

.EXAMPLE
    pwsh -NoProfile -File tests\scripts\installer-font.ps1
#>
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '../..')
$fontsScript = Join-Path $repo 'scripts/fonts.ps1'
$shortcutScript = Join-Path $repo 'scripts/windows/Install-FleetShortcut.ps1'
$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$failures = 0

# Output, when given, is printed under a failing check so a CI log shows what
# the installer actually said.
function Check($name, $condition, $Output) {
    if ($condition) { Write-Host "ok   $name" -ForegroundColor Green }
    else {
        Write-Host "FAIL $name" -ForegroundColor Red
        if ($Output) { Write-Host (($Output.TrimEnd() -split "`n" | ForEach-Object { "     | $($_.TrimEnd())" }) -join "`n") }
        $script:failures++
    }
}

function New-TempDir([string]$prefix) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) "$prefix-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $dir | Out-Null
    $dir
}

. $fontsScript

$workDir = New-TempDir 'fleet-font-test'

# A stand-in for the nerd-fonts release asset: the Mono files fleet wants, plus
# the files it must leave out (NL variants, a README).
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipSource = Join-Path $workDir 'zip-source'
New-Item -ItemType Directory -Path $zipSource | Out-Null
$zipEntries = @($FleetFontFiles | ForEach-Object { $_.File }) + @(
    'JetBrainsMonoNLNerdFontMono-Regular.ttf', 'JetBrainsMonoNerdFont-Regular.ttf', 'README.md')
foreach ($entry in $zipEntries) {
    [IO.File]::WriteAllText((Join-Path $zipSource $entry), "fake $entry")
}
$fakeZip = Join-Path $workDir 'JetBrainsMono.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($zipSource, $fakeZip)
$fakeHash = (Get-FileHash -LiteralPath $fakeZip -Algorithm SHA256).Hash

$notZip = Join-Path $workDir 'not-a-zip.zip'
# Longer than the 22-byte end-of-central-directory record, so both runtimes search
# for it and say so. A shorter file fails differently: .NET Core reports "Central
# Directory corrupt", while .NET Framework (Windows PowerShell 5.1) lets the
# IOException from seeking before the start of the file escape.
[IO.File]::WriteAllText($notZip, ('not a zip ' * 20))
$notZipHash = (Get-FileHash -LiteralPath $notZip -Algorithm SHA256).Hash

$noMonoSource = Join-Path $workDir 'no-mono-source'
New-Item -ItemType Directory -Path $noMonoSource | Out-Null
[IO.File]::WriteAllText((Join-Path $noMonoSource 'JetBrainsMonoNerdFont-Regular.ttf'), 'not the Mono face')
$noMonoZip = Join-Path $workDir 'no-mono.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($noMonoSource, $noMonoZip)
$noMonoHash = (Get-FileHash -LiteralPath $noMonoZip -Algorithm SHA256).Hash

$registryRoot = "HKCU:\Software\FleetTest\Fonts-$([guid]::NewGuid().ToString('N'))"
if (-not $onWindows) {
    Write-Host 'skip real registry checks (not Windows): the registry helpers are stubbed with a hashtable' -ForegroundColor Yellow
}

# Runs one fonts.ps1 call in its own scope against a fresh (or given) font dir and
# registry key. Returns the function's result, its Write-Host output, how many
# times the download ran, and the registry state afterwards.
function Invoke-FontCase {
    param(
        [string]$Action = 'Install',
        [string]$FontDir,
        [string]$RegistryPath,
        [hashtable]$Registry,
        [string]$SourceZip = $fakeZip,
        [string]$Sha256 = $fakeHash,
        [switch]$DownloadThrows
    )
    $downloads = [System.Collections.Generic.List[string]]::new()
    $registryState = $Registry
    $result = $null
    $out = & {
        . $fontsScript
        if (-not $onWindows) {
            function Get-FleetFontRegistryValue([string]$Path, [string]$Name) { $registryState["$Path|$Name"] }
            function Set-FleetFontRegistryValue([string]$Path, [string]$Name, [string]$Value) { $registryState["$Path|$Name"] = $Value }
            function Remove-FleetFontRegistryValue([string]$Path, [string]$Name) { $registryState.Remove("$Path|$Name") }
        }
        $download = {
            param($Url, $OutFile)
            $downloads.Add($Url)
            if ($DownloadThrows) { throw 'network down' }
            Copy-Item -LiteralPath $SourceZip -Destination $OutFile
        }
        if ($Action -eq 'Install') {
            $script:caseResult = Install-FleetFont -FontDir $FontDir -RegistryPath $RegistryPath -Sha256 $Sha256 -Download $download
        }
        else {
            $script:caseResult = Uninstall-FleetFont -FontDir $FontDir -RegistryPath $RegistryPath
        }
    } 6>&1 2>&1 | Out-String -Width 4096
    $result = $script:caseResult
    [pscustomobject]@{ Result = $result; Output = $out; Downloads = $downloads.Count }
}

function Get-RegValue([hashtable]$Registry, [string]$Path, [string]$Name) {
    if ($onWindows) {
        $item = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
        if ($item) { $item.$Name }
    }
    else { $Registry["$Path|$Name"] }
}

function Set-RegValue([hashtable]$Registry, [string]$Path, [string]$Name, [string]$Value) {
    if ($onWindows) {
        if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
        New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
    }
    else { $Registry["$Path|$Name"] = $Value }
}

function Test-NothingInstalled($fontDir, $registry, $registryPath) {
    $files = @(Get-ChildItem -LiteralPath $fontDir -ErrorAction SilentlyContinue)
    $values = @($FleetFontFiles | Where-Object { Get-RegValue $registry $registryPath $_.Name })
    $files.Count -eq 0 -and $values.Count -eq 0
}

try {
    # Pins: real values, in one place, consistent with each other.
    Check 'pins: face is JetBrainsMono Nerd Font Mono' ($FleetFontFace -eq 'JetBrainsMono Nerd Font Mono')
    Check 'pins: SHA256 is 64 hex characters' ($FleetFontSha256 -match '^[0-9a-fA-F]{64}$')
    Check 'pins: URL is the pinned nerd-fonts release asset' (
        $FleetFontUrl -eq "https://github.com/ryanoasis/nerd-fonts/releases/download/$FleetFontVersion/JetBrainsMono.zip")
    Check 'pins: the four Mono styles' (
        (@($FleetFontFiles | ForEach-Object { $_.File }) -join ',') -eq
        'JetBrainsMonoNerdFontMono-Regular.ttf,JetBrainsMonoNerdFontMono-Bold.ttf,JetBrainsMonoNerdFontMono-Italic.ttf,JetBrainsMonoNerdFontMono-BoldItalic.ttf')
    Check 'pins: registry names are "<face> <style> (TrueType)"' (
        @($FleetFontFiles | Where-Object { $_.Name -notmatch "^$([regex]::Escape($FleetFontFace))( Bold| Italic| Bold Italic)? \(TrueType\)$" }).Count -eq 0)
    Check 'defaults: per-user font dir and HKCU Fonts key' (
        (Get-FleetFontDefaultDir) -eq "$env:LOCALAPPDATA\Microsoft\Windows\Fonts" -and
        $FleetFontRegistryPath -eq 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts')

    # Fresh install: only the four Mono files land, each registered by full path.
    $registry = @{}
    $fontDir = Join-Path $workDir 'fonts-fresh'
    $fresh = Invoke-FontCase -FontDir $fontDir -RegistryPath $registryRoot -Registry $registry
    $placed = @(Get-ChildItem -LiteralPath $fontDir -ErrorAction SilentlyContinue | ForEach-Object Name | Sort-Object)
    Check 'install: returns true' ($fresh.Result -eq $true)
    Check 'install: says to restart Windows Terminal if the font does not appear' ($fresh.Output -match 'restart Windows Terminal if the font does not appear')
    Check 'install: downloads once' ($fresh.Downloads -eq 1)
    Check 'install: copies only the four Mono files to the font dir' (
        ($placed -join ',') -eq ((@($FleetFontFiles | ForEach-Object { $_.File }) | Sort-Object) -join ','))
    Check 'install: copies the file contents from the zip' (
        [IO.File]::ReadAllText((Join-Path $fontDir 'JetBrainsMonoNerdFontMono-Bold.ttf')) -eq 'fake JetBrainsMonoNerdFontMono-Bold.ttf')
    Check 'install: registers each file by full path' (
        @($FleetFontFiles | Where-Object { (Get-RegValue $registry $registryRoot $_.Name) -ne (Join-Path $fontDir $_.File) }).Count -eq 0)
    Check 'install: Test-FleetFontInstalled agrees' ($(
            if ($onWindows) { Test-FleetFontInstalled -FontDir $fontDir -RegistryPath $registryRoot }
            else { $true }))

    # Already installed: no download at all.
    $again = Invoke-FontCase -FontDir $fontDir -RegistryPath $registryRoot -Registry $registry -DownloadThrows
    Check 'skip: returns true when the face is installed' ($again.Result -eq $true)
    Check 'skip: does not download' ($again.Downloads -eq 0)
    Check 'skip: says it is already installed' ($again.Output -match 'already installed')

    # Registered, but the file is gone: install again.
    Remove-Item -LiteralPath (Join-Path $fontDir 'JetBrainsMonoNerdFontMono-Regular.ttf')
    $repair = Invoke-FontCase -FontDir $fontDir -RegistryPath $registryRoot -Registry $registry
    Check 'skip: a registry value without its file downloads again' ($repair.Downloads -eq 1 -and $repair.Result -eq $true -and
        (Test-Path -LiteralPath (Join-Path $fontDir 'JetBrainsMonoNerdFontMono-Regular.ttf')))

    # A font file that cannot be written (here a directory in its place) warns for
    # that file, registers only the files that landed, and the next run retries.
    $blockedRegistry = @{}
    $blockedKey = "$registryRoot\blocked"
    $blockedDir = Join-Path $workDir 'fonts-blocked'
    $blocked = Join-Path $blockedDir 'JetBrainsMonoNerdFontMono-Italic.ttf'
    New-Item -ItemType Directory -Path $blocked -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $blocked 'keep'), 'in the way')
    $partial = Invoke-FontCase -FontDir $blockedDir -RegistryPath $blockedKey -Registry $blockedRegistry
    $italic = $FleetFontFiles | Where-Object { $_.File -eq 'JetBrainsMonoNerdFontMono-Italic.ttf' }
    $others = @($FleetFontFiles | Where-Object { $_.File -ne $italic.File })
    Check 'copy failure: returns false' ($partial.Result -eq $false)
    Check 'copy failure: warns for that file with how to retry' (
        $partial.Output -match 'JetBrainsMonoNerdFontMono-Italic\.ttf' -and $partial.Output -match 're-run the installer')
    Check 'copy failure: does not register the file it could not copy' (-not (Get-RegValue $blockedRegistry $blockedKey $italic.Name))
    Check 'copy failure: still copies and registers the other files' (
        @($others | Where-Object {
                -not (Test-Path -LiteralPath (Join-Path $blockedDir $_.File) -PathType Leaf) -or
                (Get-RegValue $blockedRegistry $blockedKey $_.Name) -ne (Join-Path $blockedDir $_.File) }).Count -eq 0)
    Remove-Item -LiteralPath $blocked -Recurse -Force
    $retry = Invoke-FontCase -FontDir $blockedDir -RegistryPath $blockedKey -Registry $blockedRegistry
    Check 'copy failure: the next run downloads again and completes' (
        $retry.Downloads -eq 1 -and $retry.Result -eq $true -and
        (Get-RegValue $blockedRegistry $blockedKey $italic.Name) -eq (Join-Path $blockedDir $italic.File))

    # A font file Windows has open (Terminal using it) cannot be replaced; the file
    # already there is still registered.
    if ($onWindows) {
        $lockedRegistry = @{}
        $lockedKey = "$registryRoot\locked"
        $lockedDir = Join-Path $workDir 'fonts-locked'
        New-Item -ItemType Directory -Path $lockedDir | Out-Null
        $lockedFile = Join-Path $lockedDir 'JetBrainsMonoNerdFontMono-Bold.ttf'
        [IO.File]::WriteAllText($lockedFile, 'in use')
        $lock = [IO.File]::Open($lockedFile, 'Open', 'Read', 'None')
        try {
            $locked = Invoke-FontCase -FontDir $lockedDir -RegistryPath $lockedKey -Registry $lockedRegistry
        }
        finally {
            $lock.Dispose()
        }
        Check 'locked file: warns for that file' ($locked.Output -match 'JetBrainsMonoNerdFontMono-Bold\.ttf')
        Check 'locked file: registers every file that is present' (
            @($FleetFontFiles | Where-Object { (Get-RegValue $lockedRegistry $lockedKey $_.Name) -ne (Join-Path $lockedDir $_.File) }).Count -eq 0)
    }
    else {
        Write-Host 'skip locked-file check (file locks are Windows-only)' -ForegroundColor Yellow
    }

    # Purge: removes exactly what the installer added.
    $otherFile = Join-Path $fontDir 'SomeoneElse.ttf'
    [IO.File]::WriteAllText($otherFile, 'not ours')
    Set-RegValue $registry $registryRoot 'Someone Else (TrueType)' $otherFile
    $purge = Invoke-FontCase -Action 'Uninstall' -FontDir $fontDir -RegistryPath $registryRoot -Registry $registry
    Check 'purge: removes the four font files' (
        @($FleetFontFiles | Where-Object { Test-Path -LiteralPath (Join-Path $fontDir $_.File) }).Count -eq 0)
    Check 'purge: removes the four registry values' (
        @($FleetFontFiles | Where-Object { Get-RegValue $registry $registryRoot $_.Name }).Count -eq 0)
    Check 'purge: leaves other fonts alone' (
        (Test-Path -LiteralPath $otherFile) -and (Get-RegValue $registry $registryRoot 'Someone Else (TrueType)') -eq $otherFile)
    Check 'purge: reports what it removed' ($purge.Output -match 'removed')

    # Failures warn with a retry hint, return false and leave nothing behind.
    $cases = @(
        @{ Name = 'hash mismatch'; Args = @{ Sha256 = $FleetFontSha256 }; Expect = 'SHA256' },
        @{ Name = 'download failure'; Args = @{ DownloadThrows = $true }; Expect = 'network down' },
        @{ Name = 'extraction failure'; Args = @{ SourceZip = $notZip; Sha256 = $notZipHash }; Expect = 'End\s+of\s+Central\s+Directory\s+record\s+could\s+not\s+be\s+found' },
        @{ Name = 'zip without the Mono files'; Args = @{ SourceZip = $noMonoZip; Sha256 = $noMonoHash }; Expect = 'JetBrainsMonoNerdFontMono-Regular\.ttf is not in the zip' }
    )
    foreach ($case in $cases) {
        $registry = @{}
        $dir = Join-Path $workDir "fonts-$($case.Name -replace ' ', '-')"
        $caseArgs = $case.Args
        $failed = Invoke-FontCase -FontDir $dir -RegistryPath $registryRoot -Registry $registry @caseArgs
        Check "$($case.Name): returns false" ($failed.Result -eq $false)
        Check "$($case.Name): warns with the cause and how to retry" (
            $failed.Output -match 're-run the installer' -and $failed.Output -match $case.Expect) $failed.Output
        Check "$($case.Name): installs nothing" (Test-NothingInstalled $dir $registry $registryRoot)
    }

    # Never edits Windows Terminal's settings.json or the console defaults (the
    # help text may say so; the code may not mention them).
    $fontsText = [IO.File]::ReadAllText($fontsScript)
    $tokens = $null
    [System.Management.Automation.Language.Parser]::ParseFile($fontsScript, [ref]$tokens, [ref]$null) | Out-Null
    $fontsCode = ($tokens | Where-Object { "$($_.Kind)" -ne 'Comment' } | ForEach-Object { $_.Text }) -join ' '
    Check 'fonts.ps1 never touches settings.json or HKCU:\Console' ($fontsCode -notmatch 'settings\.json|HKCU:\\Console')

    # Windows PowerShell 5.1 has no ?? / ?. / ternary; CI also runs this file there.
    $pwshOnly = @($tokens | Where-Object { "$($_.Kind)" -match '^(QuestionQuestion|QuestionQuestionEquals|QuestionDot|QuestionLBracket|QuestionMark)$' })
    Check 'fonts.ps1 uses no pwsh-only operators' ($pwshOnly.Count -eq 0 -and $fontsText -notmatch '-AsHashtable')

    # The Terminal fragment asks for the same face (the script is copied on its own,
    # so it carries the face as a literal).
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($shortcutScript, [ref]$null, [ref]$null)
    $fontTables = @($ast.FindAll({
                param($n)
                $n -is [System.Management.Automation.Language.HashtableAst] -and
                @($n.KeyValuePairs | Where-Object { $_.Item1.Extent.Text -eq 'face' }).Count -gt 0
            }, $true))
    $faceLiteral = if ($fontTables.Count -eq 1) {
        $pair = $fontTables[0].KeyValuePairs | Where-Object { $_.Item1.Extent.Text -eq 'face' }
        $pair.Item2.Extent.Text.Trim("'", '"')
    }
    Check 'fragment: font.face matches fonts.ps1' ($faceLiteral -eq $FleetFontFace)
    Check 'Install-FleetShortcut.ps1 never touches HKCU:\Console' ([IO.File]::ReadAllText($shortcutScript) -notmatch 'HKCU:\\Console')

    if ($onWindows -and (Get-Command pwsh.exe -ErrorAction SilentlyContinue)) {
        $fragments = Join-Path $workDir 'fragments'
        $settings = Join-Path $workDir 'settings.json'
        foreach ($run in 1, 2) {
            & $shortcutScript -InstallDir (Join-Path $workDir 'install') -StartMenuDir (Join-Path $workDir 'start') `
                -FragmentsDir $fragments -TerminalSettings $settings 6>&1 | Out-Null
            $fragment = Get-Content -Raw (Join-Path $fragments 'Fleet\fleet.json') | ConvertFrom-Json
            Check "fragment: run $run writes font.face" ($fragment.profiles[0].font.face -eq $FleetFontFace)
        }
        Check 'fragment: settings.json is left alone' (-not (Test-Path -LiteralPath $settings))
    }
    else {
        Write-Host 'skip running Install-FleetShortcut.ps1 (needs Windows and pwsh.exe)' -ForegroundColor Yellow
    }

    # install.ps1 runs the font step on every install and purges the font only
    # under -Uninstall -Purge.
    $installAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'install.ps1'), [ref]$null, [ref]$null)
    function Find-Command($name) {
        @($installAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq $name }.GetNewClosure(), $true))
    }
    function Get-IfConditions($node) {
        $conditions = @()
        $p = $node.Parent
        $child = $node
        while ($p) {
            if ($p -is [System.Management.Automation.Language.IfStatementAst]) {
                foreach ($clause in $p.Clauses) {
                    if ($clause.Item2.Extent.StartOffset -le $child.Extent.StartOffset -and $clause.Item2.Extent.EndOffset -ge $child.Extent.EndOffset) {
                        $conditions += $clause.Item1.Extent.Text
                    }
                }
            }
            $child = $p
            $p = $p.Parent
        }
        $conditions
    }
    $installs = Find-Command 'Install-FleetFont'
    $uninstalls = Find-Command 'Uninstall-FleetFont'
    Check 'install.ps1 dot-sources fonts.ps1' ([IO.File]::ReadAllText((Join-Path $repo 'install.ps1')) -match "scripts\\fonts\.ps1")
    Check 'install.ps1 installs the font on every install' ($installs.Count -eq 1 -and @(Get-IfConditions $installs[0]).Count -eq 0)
    Check 'install.ps1 removes the font only under -Uninstall -Purge' (
        $uninstalls.Count -eq 1 -and (Get-IfConditions $uninstalls[0]) -contains '$Purge' -and (Get-IfConditions $uninstalls[0]) -contains '$Uninstall')
    Check 'install.ps1 never touches settings.json or HKCU:\Console' (
        [IO.File]::ReadAllText((Join-Path $repo 'install.ps1')) -notmatch 'settings\.json|HKCU:\\Console')
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($onWindows -and (Test-Path -LiteralPath $registryRoot)) { Remove-Item -LiteralPath $registryRoot -Recurse -Force }
    if ($onWindows -and (Test-Path -LiteralPath 'HKCU:\Software\FleetTest') -and
        -not (Get-ChildItem -LiteralPath 'HKCU:\Software\FleetTest')) {
        Remove-Item -LiteralPath 'HKCU:\Software\FleetTest' -Force
    }
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'all checks passed' -ForegroundColor Green
