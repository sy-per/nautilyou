using Microsoft.Win32;

namespace NautilyouCompanion;

// Détecte le verrouillage/déverrouillage de session et le transmet au service via le pipe local
// (PipeClient) — remplace la détection par inactivité clavier/souris (idle timeout) comme signal
// de présence : un enfant qui regarde une vidéo ne touche ni souris ni clavier pendant de longues
// minutes, ce qui faisait considérer ce temps comme "absent" et ne comptait pas dans le temps
// d'écran. La session verrouillée (Win+L, mise en veille, écran de verrouillage) est un signal
// bien plus fiable.
//
// S'abonne à Microsoft.Win32.SystemEvents.SessionSwitch, qui nécessite une boucle de messages
// Windows active — c'est pour ça que cette détection vit dans le Companion (qui a la boucle de
// messages du systray, Application.Run()) et pas dans le Service (Session 0, pas de boucle de
// messages utilisable pour ça).
public class SessionLockState
{
    public void AttachToSystemEvents(PipeClient pipeClient)
    {
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                _ = pipeClient.SendSessionLockAsync(true);
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                _ = pipeClient.SendSessionLockAsync(false);
            }
        };
    }
}
