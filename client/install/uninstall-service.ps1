<#
.SYNOPSIS
  Desinstalle NautilyouService (arrete + supprime le service Windows + les binaires publies).
  NECESSITE LES DROITS ADMINISTRATEUR.
#>

$ErrorActionPreference = "Stop"
$serviceName = "NautilyouService"
$installDir = "C:\Program Files\Nautilyou\Service"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Ce script doit etre execute en tant qu'administrateur."
    exit 1
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        Write-Host "Arret du service..." -ForegroundColor Cyan
        Stop-Service -Name $serviceName -Force
    }
    Write-Host "Suppression du service..." -ForegroundColor Cyan
    sc.exe delete $serviceName | Out-Null
} else {
    Write-Host "Aucun service '$serviceName' installe." -ForegroundColor Yellow
}

if (Test-Path $installDir) {
    Write-Host "Suppression des binaires ($installDir)..." -ForegroundColor Cyan
    Remove-Item -Path $installDir -Recurse -Force
}

Write-Host "Nautilyou Service desinstalle." -ForegroundColor Green
Write-Host "Note : %ProgramData%\Nautilyou (pairing, cle privee, etat local) n'est PAS supprime." -ForegroundColor Yellow
Write-Host "Supprime-le manuellement si tu veux repartir a zero (ex. changer de compte enfant)."
