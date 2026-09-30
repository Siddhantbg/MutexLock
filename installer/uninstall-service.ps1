$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script as administrator."
}

$name = "FolderLockGuard"
sc.exe stop $name 2>$null | Out-Null
sc.exe delete $name 2>$null | Out-Null
Write-Host "Guard service uninstalled: $name" -ForegroundColor Green
