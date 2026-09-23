namespace NautilyouShared;

// Protocole du named pipe entre le service (NautilyouService, Session 0, pas d'UI) et l'appli
// compagnon (NautilyouCompanion, tourne dans la session utilisateur, affiche le systray). Un
// service Windows tourne en Session 0, isolée de la session interactive — il ne peut PAS afficher
// d'icône systray lui-même. Voir dev-context pour le détail de cette contrainte et la décision de
// scinder l'architecture en deux processus reliés par ce pipe local.
public static class PipeProtocol
{
    public const string PipeName = "NautilyouStatusPipe";
}

// Service -> Companion : état courant à afficher dans le systray.
public class StatusMessage
{
    public int RemainingMinutes { get; set; }
    public bool QuotaEnabled { get; set; } = true;
    public Dictionary<string, int> AppMinutes { get; set; } = new();
    public bool ConnectedToServer { get; set; }
}

// Service -> Companion : notification à afficher (bulle systray).
public class BalloonMessage
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

// Companion -> Service : l'enfant a cliqué "Demander du temps" dans le systray.
public class TimeRequestMessage
{
    public int Minutes { get; set; }
}

// Companion -> Service : changement d'état de verrouillage de session, détecté via
// Microsoft.Win32.SystemEvents.SessionSwitch (nécessite une boucle de messages Windows, que seul
// le Companion a — le service en Session 0 n'en a pas).
public class SessionLockMessage
{
    public bool Locked { get; set; }
}

// Service -> Companion : "pas encore appairé, affiche le formulaire de pairing au lieu du statut".
// Diffusé périodiquement tant que le service attend un pairing (voir dev-context, section
// "Interface de pairing dans le Companion").
public class PairingNeededMessage
{
}

// Companion -> Service : le parent a rempli le formulaire de pairing dans le systray.
public class PairRequestMessage
{
    public string ServerUrl { get; set; } = "";
    public string Code { get; set; } = "";
}

// Service -> Companion : résultat de la tentative de pairing (répondu sur la même connexion que
// la demande, pas diffusé à tout le monde).
public class PairResultMessage
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

// Service -> Companion : un site vient d'etre bloque par le filtre DNS. Domain est l'entree de la
// liste noire qui a declenche le blocage (ou le domaine racine en mode liste blanche), donc
// directement utilisable pour une demande d'acces que le parent peut approuver.
public class BlockedSiteMessage
{
    public string Domain { get; set; } = "";
}

// Companion -> Service : l'enfant demande l'acces a un site bloque (bouton de la notification ou du menu).
public class AccessRequestMessage
{
    public string Domain { get; set; } = "";
}

// Enveloppe générique envoyée sur le pipe : {type, payload} en JSON, une ligne par message
// (delimiteur '\n'), même format que les messages WebSocket client<->serveur pour rester cohérent.
public class PipeEnvelope
{
    public string Type { get; set; } = "";
    public System.Text.Json.JsonElement Payload { get; set; }
}
