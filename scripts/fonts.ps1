<#
.SYNOPSIS
    Install the Nerd Font fleet's icons need on Windows: JetBrainsMono Nerd Font Mono.

.DESCRIPTION
    Dot-source this and call Install-FleetFont (Uninstall-FleetFont under -Purge).
    Both installers use it, which is why it defines functions and does nothing on
    its own besides setting the pins below.

    The font is installed for the current user, without admin rights: the four
    Mono styles are copied to %LOCALAPPDATA%\Microsoft\Windows\Fonts and each is
    registered under HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts.
    The zip is a pinned nerd-fonts release asset, checked against a pinned SHA256
    before anything is extracted.

    When the face is already installed (registry value and file present) nothing
    is downloaded. A failed download, hash check or extraction only warns, so the
    fleet install carries on.

    Windows Terminal's settings.json and HKCU:\Console are never touched; the
    "Fleet" Terminal profile asks for the face itself.

.NOTES
    Runs on Windows PowerShell 5.1 and PowerShell 7. To move to a newer release,
    change $FleetFontVersion and put the asset's SHA256 in $FleetFontSha256.
#>

$FleetFontVersion = 'v3.5.1'
$FleetFontUrl = "https://github.com/ryanoasis/nerd-fonts/releases/download/$FleetFontVersion/JetBrainsMono.zip"
$FleetFontSha256 = 'fab782a66f7d3019da64f6572db9fc5d3a4bcb19f9fa13e2d8a62e3693d6396e'
$FleetFontFace = 'JetBrainsMono Nerd Font Mono'
$FleetFontFiles = @(
    [pscustomobject]@{ File = 'JetBrainsMonoNerdFontMono-Regular.ttf'; Name = "$FleetFontFace (TrueType)" },
    [pscustomobject]@{ File = 'JetBrainsMonoNerdFontMono-Bold.ttf'; Name = "$FleetFontFace Bold (TrueType)" },
    [pscustomobject]@{ File = 'JetBrainsMonoNerdFontMono-Italic.ttf'; Name = "$FleetFontFace Italic (TrueType)" },
    [pscustomobject]@{ File = 'JetBrainsMonoNerdFontMono-BoldItalic.ttf'; Name = "$FleetFontFace Bold Italic (TrueType)" }
)
$FleetFontRegistryPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'

function Get-FleetFontDefaultDir {
    "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"
}

# The registry goes through these three so the tests can stand them in on a
# machine without one.
function Get-FleetFontRegistryValue([string]$Path, [string]$Name) {
    $item = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
    if ($item) { $item.$Name }
}

function Set-FleetFontRegistryValue([string]$Path, [string]$Name, [string]$Value) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
}

function Remove-FleetFontRegistryValue([string]$Path, [string]$Name) {
    Remove-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
}

# Installed means the Regular style is registered and its file is where the
# registry says.
function Test-FleetFontInstalled {
    [CmdletBinding()]
    param(
        [string]$FontDir = (Get-FleetFontDefaultDir),
        [string]$RegistryPath = $FleetFontRegistryPath
    )
    $regular = $FleetFontFiles[0]
    $registered = Get-FleetFontRegistryValue -Path $RegistryPath -Name $regular.Name
    if (-not $registered) { return $false }
    if (-not [IO.Path]::IsPathRooted($registered)) { $registered = Join-Path $FontDir $registered }
    [bool](Test-Path -LiteralPath $registered)
}

function Save-FleetFontZip([string]$Url, [string]$OutFile) {
    $saved = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    }
    finally {
        $ProgressPreference = $saved
    }
}

# Returns $true when the face is installed afterwards, $false (after a warning)
# when it could not be. Never throws. Sha256 and Download exist for the tests.
function Install-FleetFont {
    [CmdletBinding()]
    param(
        [string]$FontDir = (Get-FleetFontDefaultDir),
        [string]$RegistryPath = $FleetFontRegistryPath,
        [string]$Url = $FleetFontUrl,
        [string]$Sha256 = $FleetFontSha256,
        [scriptblock]$Download = { param($Url, $OutFile) Save-FleetFontZip $Url $OutFile }
    )

    if (Test-FleetFontInstalled -FontDir $FontDir -RegistryPath $RegistryPath) {
        Write-Host "    $FleetFontFace already installed" -ForegroundColor Green
        return $true
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) "fleet-font-$([guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $work -Force | Out-Null
        $zip = Join-Path $work 'JetBrainsMono.zip'

        Write-Host "    downloading $FleetFontFace ($FleetFontVersion)" -ForegroundColor DarkGray
        & $Download $Url $zip

        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($actual -ne $Sha256) {
            throw "SHA256 mismatch for $Url (expected $Sha256, got $actual)"
        }

        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $extracted = Join-Path $work 'fonts'
        New-Item -ItemType Directory -Path $extracted -Force | Out-Null
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($font in $FleetFontFiles) {
                $entry = $archive.Entries | Where-Object { $_.FullName -eq $font.File } | Select-Object -First 1
                if (-not $entry) { throw "$($font.File) is not in the zip" }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $extracted $font.File), $true)
            }
        }
        finally {
            $archive.Dispose()
        }

        New-Item -ItemType Directory -Path $FontDir -Force | Out-Null
        foreach ($font in $FleetFontFiles) {
            $target = Join-Path $FontDir $font.File
            Copy-Item -LiteralPath (Join-Path $extracted $font.File) -Destination $target -Force
            Set-FleetFontRegistryValue -Path $RegistryPath -Name $font.Name -Value $target
        }

        Write-Host "    installed $FleetFontFace for this user" -ForegroundColor Green
        $true
    }
    catch {
        Write-Host "    could not install $($FleetFontFace): $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host '    fleet still installs; its icons need this font - re-run the installer to try again' -ForegroundColor Yellow
        $false
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Removes only the files and registry values Install-FleetFont adds. A file in
# use (an open Windows Terminal) only warns.
function Uninstall-FleetFont {
    [CmdletBinding()]
    param(
        [string]$FontDir = (Get-FleetFontDefaultDir),
        [string]$RegistryPath = $FleetFontRegistryPath
    )

    foreach ($font in $FleetFontFiles) {
        $target = Join-Path $FontDir $font.File
        if (Get-FleetFontRegistryValue -Path $RegistryPath -Name $font.Name) {
            Remove-FleetFontRegistryValue -Path $RegistryPath -Name $font.Name
        }
        if (Test-Path -LiteralPath $target) {
            try {
                Remove-Item -LiteralPath $target -Force -ErrorAction Stop
                Write-Host "    removed $target" -ForegroundColor Green
            }
            catch {
                Write-Host "    could not remove $($target): $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }
}
