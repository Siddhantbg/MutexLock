param(
    [string]$ServiceExe = ""
)

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script as administrator."
}

if ([string]::IsNullOrWhiteSpace($ServiceExe)) {
    $ServiceExe = Join-Path $PSScriptRoot "..\src\FolderLock.App\bin\Release\net8.0-windows\publish\win-x64\FolderLock.Service.exe"
}

$ServiceExe = (Resolve-Path $ServiceExe).Path
$name = "FolderLockGuard"

sc.exe stop $name 2>$null | Out-Null
sc.exe delete $name 2>$null | Out-Null

sc.exe create $name binPath= "$ServiceExe" start= auto DisplayName= "FolderLock Guard Service"
sc.exe description $name "Monitors and automatically restores FolderLock folder locks."
sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start $name

Write-Host "Guard service installed and started: $name" -ForegroundColor Green
