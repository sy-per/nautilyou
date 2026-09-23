using System.Threading;
using NautilyouService;

// Verrou mono-instance (ajoute le 2026-09-23 apres un incident reel : deux instances lancees en
// meme temps - une session console de pairing restee ouverte + le vrai service redemarre - se
// sont battues pour le port DNS 53, laissant le DNS systeme pointer vers un filtre local que
// personne n'ecoutait plus correctement, coupant tout acces internet). Une deuxieme instance
// refuse desormais de demarrer plutot que de risquer ce genre de conflit.
using var singleInstanceMutex = new Mutex(initiallyOwned: true, name: "Global\\NautilyouServiceSingleInstance", out var createdNew);
if (!createdNew)
{
    Console.WriteLine("Une instance de Nautilyou Service tourne deja (console ou service Windows) - fermeture pour eviter un conflit sur le port DNS 53.");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Nautilyou Service");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Filet de sécurité (ajouté le 2026-09-23 suite à un incident réel) : si le process se termine
// de façon moins propre que le chemin normal (console fermée en dehors d'un Ctrl+C géré,
// exception non rattrapée, etc.), Worker.ExecuteAsync's finally n'est pas garanti de s'exécuter
// jusqu'au bout. On retente un DHCP reset ici en dernier recours — idempotent et sans risque si
// le DNS n'avait pas été changé. Ne couvre pas un kill dur (fin de tâche forcée, coupure
// d'alimentation) : pour ça, voir install/restore-dns.ps1 (à lancer manuellement).
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try
    {
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => { });
        DnsAdapterConfigurator.RestoreDhcp(loggerFactory.CreateLogger("ProcessExit"));
    }
    catch
    {
        // dernier recours, on ne peut plus faire grand chose si meme ça echoue
    }
};

host.Run();
