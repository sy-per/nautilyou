<#
.SYNOPSIS
  "Bouton panique" : restaure le DNS automatique (DHCP) sur toutes les interfaces actives et vide
  le cache DNS. A utiliser si plus aucun site ne charge apres un test du filtrage DNS Nautilyou
  (le DNS est reste pointe vers 127.0.0.1 sans que rien n'ecoute derriere, generalement parce
  qu'un process NautilyouService/dotnet a ete arrete brutalement sans passer par son nettoyage).

  Ne necessite PAS les droits administrateur pour la commande netsh "dhcp" (contrairement a
  "static"), mais on la lance quand meme sans verif au cas ou.
#>

Write-Host "Restauration du DNS automatique (DHCP) sur toutes les interfaces actives..." -ForegroundColor Cyan
Get-NetAdapter | Where-Object Status -eq "Up" | ForEach-Object {
    Write-Host "  - $($_.Name)"
    netsh interface ip set dns name="$($_.Name)" dhcp | Out-Null
}

Write-Host "Vidage du cache DNS..." -ForegroundColor Cyan
ipconfig /flushdns | Out-Null

Write-Host ""
Write-Host "DNS restaure. Verifie qu'un site charge normalement." -ForegroundColor Green
Write-Host "Si NautilyouService tourne encore et refait la meme chose, arrete-le : Stop-Service NautilyouService"
