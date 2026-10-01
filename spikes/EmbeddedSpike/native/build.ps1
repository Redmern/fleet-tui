# Builds libghostty-vt as a static library for win-x64, at the commit pinned in
# pins.env, with the Zig version pinned there. Both downloads are checked
# against their SHA-256. Nothing is installed system-wide: Zig and the source
# land in native/.cache, the result in native/out/win-x64.
#
# The zig invocation mirrors herdr's build.rs (Apache-2.0,
# github.com/ogulcancelik/herdr), which ships the same pin on Windows MSVC.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$here = $PSScriptRoot
$pins = @{}
Get-Content (Join-Path $here 'pins.env') | Where-Object { $_ -match '=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $pins[$k.Trim()] = $v.Trim()
}

$cache = Join-Path $here '.cache'
$out = Join-Path $here 'out/win-x64'
New-Item -ItemType Directory -Force $cache, $out | Out-Null

function Get-Pinned($url, $file, $sha) {
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $sha.ToUpper()) {
        Write-Host "downloading $url"
        Invoke-WebRequest $url -OutFile $file
    }
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($actual -ne $sha.ToUpper()) {
        throw "checksum mismatch for $file`: expected $sha, got $actual"
    }
}

$zigName = "zig-x86_64-windows-$($pins.ZIG_VERSION)"
$zigZip = Join-Path $cache "$zigName.zip"
$zigDir = Join-Path $cache $zigName
Get-Pinned "https://ziglang.org/download/$($pins.ZIG_VERSION)/$zigName.zip" $zigZip $pins.ZIG_SHA256_WINDOWS_X64
if (-not (Test-Path (Join-Path $zigDir 'zig.exe'))) {
    Expand-Archive $zigZip -DestinationPath $cache -Force
}
$zig = Join-Path $zigDir 'zig.exe'

$commit = $pins.GHOSTTY_COMMIT
$srcTar = Join-Path $cache "ghostty-$commit.tar.gz"
$srcDir = Join-Path $cache "ghostty-$commit"
Get-Pinned "https://github.com/ghostty-org/ghostty/archive/$commit.tar.gz" $srcTar $pins.GHOSTTY_SHA256
if (-not (Test-Path (Join-Path $srcDir 'build.zig'))) {
    tar -xzf $srcTar -C $cache
    if ($LASTEXITCODE -ne 0) { throw "tar failed extracting $srcTar" }
}

$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $cache 'zig-global'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $cache 'zig-local'
# Zig 0.16 on Windows fails the dependency fetch with FileNotFound when the
# global cache's package directory does not exist yet.
New-Item -ItemType Directory -Force (Join-Path $env:ZIG_GLOBAL_CACHE_DIR 'p') | Out-Null

Push-Location $srcDir
try {
    & $zig build `
        -Demit-lib-vt `
        -Doptimize=ReleaseFast `
        -Dsimd=true `
        -Dtarget=x86_64-windows-msvc `
        "-Dversion-string=$($pins.GHOSTTY_VERSION_STRING)" `
        -Demit-xcframework=false `
        --prefix $out
    if ($LASTEXITCODE -ne 0) { throw "zig build failed with $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$lib = Join-Path $out 'lib/ghostty-vt-static.lib'
if (-not (Test-Path $lib)) { throw "expected $lib after the build" }
Write-Host "built $lib"
