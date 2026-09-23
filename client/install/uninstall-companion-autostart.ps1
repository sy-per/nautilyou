<#
.SYNOPSIS
  Retire le demarrage automatique de NautilyouCompanion et supprime ses binaires installes.
#>

$ErrorActionPreference = "Stop"

$installDir = "$env:LocalAppData\Nautilyou\Companion"
$startupFolder = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startupFolder "Nautilyou.lnk"

Get-Process NautilyouCompanion -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $shortcutPath) {
    Write-Host "Suppression du raccourci de demarrage..." -ForegroundColor Cyan
    Remove-Item -Path $shortcutPath -Force
} else {
    Write-Host "Aucun raccourci de demarrage trouve." -ForegroundColor Yellow
}

if (Test-Path $installDir) {
    Write-Host "Suppression des binaires ($installDir)..." -ForegroundColor Cyan
    Remove-Item -Path $installDir -Recurse -Force
}

Write-Host "Nautilyou Companion desinstalle (ne se relancera plus a l'ouverture de session)." -ForegroundColor Green
