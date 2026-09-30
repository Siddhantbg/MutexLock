param(
    [string]$Configuration = "Release",
    [string]$CertThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$NoHello
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$appProject = Join-Path $root "src\FolderLock.App\FolderLock.App.csproj"
$serviceProject = Join-Path $root "src\FolderLock.Service\FolderLock.Service.csproj"

$helloFlag = "-p:FolderLockHello=$(-not $NoHello)".ToLower()
$tfm = if ($NoHello) { "net8.0-windows" } else { "net8.0-windows10.0.19041.0" }
$publishDir = Join-Path $root "src\FolderLock.App\bin\$Configuration\$tfm\publish\win-x64"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing app -> $publishDir  ($helloFlag)" -ForegroundColor Cyan
dotnet publish $appProject -c $Configuration -p:PublishProfile=win-x64 $helloFlag -o $publishDir

Write-Host "Publishing guard service -> $publishDir" -ForegroundColor Cyan
dotnet publish $serviceProject -c $Configuration -r win-x64 --self-contained false -o $publishDir

if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    Write-Host "Code-signing executables..." -ForegroundColor Cyan
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        Write-Warning "signtool.exe (part of the Windows SDK) not found; skipping signing."
    }
    else {
        foreach ($exe in Get-ChildItem $publishDir -Filter *.exe) {
            & $signtool sign /sha1 $CertThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exe.FullName
        }
    }
}

Write-Host "Done. Output folder: $publishDir" -ForegroundColor Green
Get-ChildItem $publishDir -Filter *.exe | Select-Object Name, Length
