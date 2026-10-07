# Opens the Windows Terminal "Fleet" profile in a new window and tags that window with the Fleet
# AppUserModelID, so the taskbar groups it under the pinned Fleet shortcut instead of Terminal.
$appId = 'Trivium.Fleet'
Add-Type -Path (Join-Path $PSScriptRoot 'FleetTaskbar.cs')

$before = [FleetTaskbar]::TerminalWindows()
Start-Process "$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe" -ArgumentList '-w new -p "Fleet"'

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    $new = [FleetTaskbar]::TerminalWindows() | Where-Object { $_ -notin $before }
    if ($new) {
        foreach ($hwnd in $new) { [FleetTaskbar]::SetWindowAppId($hwnd, $appId) }
        return
    }
    Start-Sleep -Milliseconds 100
}
