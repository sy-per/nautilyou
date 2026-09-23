namespace NautilyouShared;

// Application installee sur la machine (meme source que "Applications installees" de Windows).
// Name est le nom affiche, celui que le parent voit et choisit dans le dashboard ; Exes liste les
// noms de process (sans ".exe") a surveiller pour cette application.
public class InstalledApp
{
    public string Name { get; set; } = "";
    public List<string> Exes { get; set; } = new();
}
