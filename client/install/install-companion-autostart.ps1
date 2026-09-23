<#
.SYNOPSIS
  Installe NautilyouCompanion (systray) et le fait demarrer automatiquement a l'ouverture de
  session Windows de l'utilisateur courant.

.DESCRIPTION
  1. Publie NautilyouCompanion en autonome (self-contained, single-file).
  2. Copie le resultat dans %LocalAppData%\Nautilyou\Companion (pas besoin d'admin, par utilisateur).
  3. Cree un raccourci dans le dossier Demarrage de l'utilisateur courant
     (%AppData%\Microsoft\Windows\Start Menu\Programs\Startup) qui lance NautilyouCompanion.exe
     a chaque ouverture de session.

  NE necessite PAS les droits administrateur (installation par utilisateur, pas systeme).
  ATTENTION : modifie le dossier de demarrage reel de la session Windows courante - l'app se relancera
  a chaque connexion tant que le raccourci existe. Voir uninstall-companion-autostart.ps1 pour
  revenir en arriere.
#>

$ErrorActionPreference = "Stop"

$installDir = "$env:LocalAppData\Nautilyou\Companion"
$projectPath = Join-Path $PSScriptRoot "..\NautilyouCompanion\NautilyouCompanion.csproj"
$startupFolder = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startupFolder "Nautilyou.lnk"

Write-Host "1/3 - Publication de NautilyouCompanion (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$env:TEMP\NautilyouCompanionPublish"
if ($LASTEXITCODE -ne 0) { Write-Error "Echec de la publication."; exit 1 }

Write-Host "2/3 - Copie des binaires vers $installDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path "$env:TEMP\NautilyouCompanionPublish\*" -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir "NautilyouCompanion.exe"
if (-not (Test-Path $exePath)) { Write-Error "Binaire introuvable apres publication : $exePath"; exit 1 }

Write-Host "3/3 - Creation du raccourci de demarrage ($shortcutPath)..." -ForegroundColor Cyan
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = "Nautilyou - temps d'ecran (systray)"
$shortcut.Save()

Write-Host ""
Write-Host "Companion installe. Il se lancera automatiquement a la prochaine ouverture de session." -ForegroundColor Green
Write-Host "Pour le lancer tout de suite sans redemarrer la session : & '$exePath'"
