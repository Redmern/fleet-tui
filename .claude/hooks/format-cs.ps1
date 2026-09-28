# PostToolUse hook: whitespace-format a .cs file Claude just edited, the same rules as
# `dotnet format --verify-no-changes`. --folder mode skips loading the solution (~2 s instead of ~20 s).
# Fails open: any error, a non-.cs file or a path outside the repo exits 0 without output.
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $path = [string]($raw | ConvertFrom-Json).tool_input.file_path
    if (-not $path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { exit 0 }

    $root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { Split-Path (Split-Path $PSScriptRoot) }
    $root = [IO.Path]::GetFullPath($root).TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { exit 0 }
    if (-not (Test-Path -LiteralPath $full)) { exit 0 }

    $relative = [IO.Path]::GetRelativePath($root, $full)
    $null = & dotnet format whitespace $root --folder --include $relative 2>&1
} catch { }
exit 0
