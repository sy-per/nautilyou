<#
.SYNOPSIS
  Installe NautilyouService en tant que vrai service Windows (tourne en LocalSystem, demarre
  automatiquement au boot, avant meme l'ouverture de session).

.DESCRIPTION
  1. Publie NautilyouService en autonome (self-contained, single-file) pour ne pas dependre du
     runtime .NET installe sur la machine cible.
  2. Copie le resultat dans C:\Program Files\Nautilyou\Service.
  3. Enregistre le service via New-Service (demarrage automatique).
  4. Configure le redemarrage automatique en cas de crash (sc.exe failure).
  5. Demarre le service.

  NECESSITE LES DROITS ADMINISTRATEUR (clic droit > "Executer en tant qu'administrateur" sur
  PowerShell, ou depuis un terminal deja eleve).

.NOTES
  Desinstallation : voir uninstall-service.ps1 dans ce meme dossier.
#>

$ErrorActionPreference = "Stop"

$serviceName = "NautilyouService"
$displayName = "Nautilyou Service"
$installDir = "C:\Program Files\Nautilyou\Service"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $PSScriptRoot "..\NautilyouService\NautilyouService.csproj"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Ce script doit etre execute en tant qu'administrateur (droits insuffisants pour installer un service Windows)."
    exit 1
}

Write-Host "1/5 - Publication de NautilyouService (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$env:TEMP\NautilyouServicePublish"
if ($LASTEXITCODE -ne 0) { Write-Error "Echec de la publication."; exit 1 }

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "2/5 - Service existant detecte, arret et suppression..." -ForegroundColor Cyan
    if ($existing.Status -eq "Running") { Stop-Service -Name $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
} else {
    Write-Host "2/5 - Aucun service existant." -ForegroundColor Cyan
}

Write-Host "3/5 - Copie des binaires vers $installDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path "$env:TEMP\NautilyouServicePublish\*" -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir "NautilyouService.exe"
if (-not (Test-Path $exePath)) { Write-Error "Binaire introuvable apres publication : $exePath"; exit 1 }

Write-Host "4/5 - Enregistrement du service..." -ForegroundColor Cyan
New-Service -Name $serviceName -BinaryPathName "`"$exePath`"" -DisplayName $displayName `
    -Description "Nautilyou - controle parental (enforcement : temps d'ecran, filtrage de sites)." `
    -StartupType Automatic | Out-Null

# Redemarrage automatique si le service crashe (New-Service ne gere pas les actions de recuperation).
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Host "5/5 - Demarrage du service..." -ForegroundColor Cyan
Start-Service -Name $serviceName

Write-Host ""
Write-Host "Service '$displayName' installe et demarre." -ForegroundColor Green
Write-Host "ATTENTION : au premier demarrage, le service demandera le pairing (URL serveur + code) sur sa" -ForegroundColor Yellow
Write-Host "   console - or un service Windows n'a pas de console interactive. Voir dev-context :" -ForegroundColor Yellow
Write-Host "   il faut appairer une fois via 'dotnet run' en console AVANT d'installer le service," -ForegroundColor Yellow
Write-Host "   pour que pairing.json/device.key existent deja dans %ProgramData%\Nautilyou." -ForegroundColor Yellow
Write-Host ""
Write-Host "Verifier l'etat : Get-Service $serviceName"
Write-Host "Voir les logs   : Get-EventLog -LogName Application -Source $serviceName -Newest 20"
