; Installateur Windows de Nautilyou (client). Compile par build-installer.ps1 (ne pas lancer a la main :
; les binaires publies dans ..\installer\staging doivent exister).
;
; Installe :
;   - NautilyouService.exe : vrai service Windows (LocalSystem, demarrage automatique, relance en cas de crash)
;   - NautilyouCompanion.exe : icone de barre des taches, lancee a l'ouverture de session de TOUS les utilisateurs
; Au premier lancement le Companion affiche le formulaire d'appairage (URL du serveur + code).

#define AppName "Nautilyou"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define ServiceName "NautilyouService"

[Setup]
AppId={{6F2B7C1E-5A3D-4E8B-9C41-2D7A1B0E93F5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Nautilyou
DefaultDirName={autopf}\Nautilyou
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=Nautilyou-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName} (controle parental)
CloseApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "staging\service\NautilyouService.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "staging\companion\NautilyouCompanion.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Lancement du Companion a l'ouverture de session (tous les utilisateurs).
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NautilyouCompanion"; ValueData: """{app}\NautilyouCompanion.exe"""; Flags: uninsdeletevalue

[InstallDelete]
; Ancien raccourci de demarrage cree par install-companion-autostart.ps1 (evite un double lancement).
Type: files; Name: "{userstartup}\Nautilyou.lnk"

[UninstallDelete]
; Etat local (appairage, cle de l'appareil, compteurs) : supprime a la desinstallation.
Type: filesandordirs; Name: "{commonappdata}\Nautilyou"

[Run]
Filename: "{app}\NautilyouCompanion.exe"; Description: "Lancer Nautilyou maintenant"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure RunHidden(const Exe, Params: String);
var
  ResultCode: Integer;
begin
  Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopAndRemoveService();
begin
  RunHidden(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}');
  Sleep(2500);
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM NautilyouCompanion.exe');
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM NautilyouService.exe');
  Sleep(500);
  RunHidden(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}');
  Sleep(1000);
end;

// Remet le DNS en automatique (DHCP) : le Service redirige le DNS vers son filtre local, il ne faut
// jamais laisser la machine pointer vers un filtre qui n'existe plus apres la desinstallation.
procedure RestoreDns();
begin
  RunHidden(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -Command "Get-NetAdapter | Where-Object Status -eq ''Up'' | ForEach-Object { Set-DnsClientServerAddress -InterfaceAlias $_.Name -ResetServerAddresses }; Clear-DnsClientCache"');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopAndRemoveService();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Exe: String;
begin
  if CurStep = ssPostInstall then
  begin
    Exe := ExpandConstant('{app}\NautilyouService.exe');
    RunHidden(ExpandConstant('{sys}\sc.exe'),
      'create {#ServiceName} binPath= "' + Exe + '" DisplayName= "Nautilyou Service" start= auto');
    RunHidden(ExpandConstant('{sys}\sc.exe'),
      'description {#ServiceName} "Nautilyou - controle parental (temps d''ecran, filtrage de sites)."');
    RunHidden(ExpandConstant('{sys}\sc.exe'),
      'failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000');
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAndRemoveService();
    RestoreDns();
  end;
end;
