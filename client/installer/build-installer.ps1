<#
.SYNOPSIS
  Construit l'installateur Windows Nautilyou-Setup-<version>.exe (dossier output\).

.DESCRIPTION
  1. Publie NautilyouService et NautilyouCompanion en autonome (self-contained, win-x64, fichier unique)
     dans staging\ : la machine cible n'a pas besoin d'installer .NET.
  2. Compile Nautilyou.iss avec Inno Setup 6 (deja installe : %LocalAppData%\Programs\Inno Setup 6).

.PARAMETER Version
  Numero de version affiche par l'installateur (defaut 1.0.0).
#>
param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$client = Split-Path -Parent $here
$staging = Join-Path $here "staging"

$iscc = @(
    "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Error "Inno Setup 6 introuvable (https://jrsoftware.org/isinfo.php)."; exit 1 }

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

Write-Host "1/3 - Publication du Service..." -ForegroundColor Cyan
dotnet publish (Join-Path $client "NautilyouService\NautilyouService.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $staging "service")
if ($LASTEXITCODE -ne 0) { Write-Error "Echec de la publication du Service."; exit 1 }

Write-Host "2/3 - Publication du Companion..." -ForegroundColor Cyan
dotnet publish (Join-Path $client "NautilyouCompanion\NautilyouCompanion.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $staging "companion")
if ($LASTEXITCODE -ne 0) { Write-Error "Echec de la publication du Companion."; exit 1 }

Write-Host "3/3 - Compilation de l'installateur (Inno Setup)..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" (Join-Path $here "Nautilyou.iss")
if ($LASTEXITCODE -ne 0) { Write-Error "Echec de la compilation Inno Setup."; exit 1 }

Write-Host ""
Write-Host "Installateur pret : $(Join-Path $here "output\Nautilyou-Setup-$Version.exe")" -ForegroundColor Green
