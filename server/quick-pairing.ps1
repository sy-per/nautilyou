# Script tout-en-un pour tester le pairing sans jongler entre plusieurs terminaux/commandes.
# 1. Demarre le serveur Nautilyou s'il n'est pas deja lance (dans une fenetre separee, visible).
# 2. Cree l'enfant "Papa" s'il n'existe pas encore.
# 3. Genere un code de pairing et l'affiche en gros.
#
# Usage : lancer depuis n'importe quel dossier :
#   powershell -ExecutionPolicy Bypass -File "chemin\vers\Nautilyou\server\quick-pairing.ps1"
# Options :
#   -ChildName "Papa"   (nom de l'enfant a utiliser/creer, par defaut "Papa")
#   -Port 4100          (port du serveur, par defaut 4100)
#   -Username / -Password  (compte parent du dashboard ; demandes si absents)

param(
    [string]$ChildName = "Papa",
    [int]$Port = 4100,
    [string]$Username,
    [string]$Password
)

$ErrorActionPreference = "Stop"
$ServerDir = $PSScriptRoot
$BaseUrl = "http://localhost:$Port"

function Test-ServerUp {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/api/health" -Method Get -TimeoutSec 2 | Out-Null
        return $true
    } catch {
        return $false
    }
}

Write-Host ""
if (Test-ServerUp) {
    Write-Host "Serveur deja demarre sur $BaseUrl" -ForegroundColor Green
} else {
    Write-Host "Demarrage du serveur Nautilyou ($ServerDir)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$ServerDir'; `$env:PORT='$Port'; node src/index.js" -WindowStyle Normal

    $attempts = 0
    while (-not (Test-ServerUp)) {
        $attempts++
        if ($attempts -gt 20) {
            Write-Host "Le serveur ne repond toujours pas apres 20s. Verifie la fenetre qui vient de s'ouvrir pour voir l'erreur." -ForegroundColor Red
            exit 1
        }
        Start-Sleep -Seconds 1
    }
    Write-Host "Serveur pret sur $BaseUrl" -ForegroundColor Green
}

# L'API du dashboard exige une session parent : connexion avec le compte cree au premier acces.
if (-not $Username) { $Username = Read-Host "Identifiant parent" }
if (-not $Password) {
    $secure = Read-Host "Mot de passe" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json
Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody -SessionVariable session | Out-Null

$children = (Invoke-RestMethod -Uri "$BaseUrl/api/children" -Method Get -WebSession $session).children
$child = $children | Where-Object { $_.name -eq $ChildName } | Select-Object -First 1

if (-not $child) {
    Write-Host "Creation de l'enfant '$ChildName'..." -ForegroundColor Cyan
    $child = (Invoke-RestMethod -Uri "$BaseUrl/api/children" -Method Post -ContentType "application/json" -Body (@{ name = $ChildName } | ConvertTo-Json) -WebSession $session).child
} else {
    Write-Host "Enfant '$ChildName' deja existant (id: $($child.id))" -ForegroundColor Green
}

$pairing = Invoke-RestMethod -Uri "$BaseUrl/api/children/$($child.id)/pairing-code" -Method Post -ContentType "application/json" -Body "{}" -WebSession $session

Write-Host ""
Write-Host "==================================================" -ForegroundColor Yellow
Write-Host "  URL du serveur : $BaseUrl"
Write-Host "  Code de pairing : $($pairing.code)"
Write-Host "  Expire a : $($pairing.expiresAt)"
Write-Host "==================================================" -ForegroundColor Yellow
Write-Host ""
