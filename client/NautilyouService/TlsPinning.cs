using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NautilyouService;

// Validation TLS pour un serveur auto-heberge : un certificat signe par une autorite reconnue
// (ex. Let's Encrypt derriere un nom de domaine) est accepte normalement ; un certificat
// auto-signe (cas typique d'un serveur sur reseau local) est accepte UNE fois au pairing
// (trust on first use, TOFU) puis epingle par empreinte SHA-256 : toute connexion ulterieure
// presentant un autre certificat est refusee, ce qui bloque un intermediaire qui voudrait
// se faire passer pour le serveur.
public class TlsPinning
{
    private readonly string? _pinned;
    private readonly bool _allowFirstUse;

    // Renseigne seulement si le certificat n'etait pas valide par une autorite (donc a epingler).
    public string? PinToStore { get; private set; }

    public TlsPinning(string? pinnedSha256, bool allowFirstUse)
    {
        _pinned = string.IsNullOrWhiteSpace(pinnedSha256) ? null : pinnedSha256;
        _allowFirstUse = allowFirstUse;
    }

    public bool Validate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (cert is null) return false;

        var fingerprint = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
        if (_pinned is not null) return string.Equals(_pinned, fingerprint, StringComparison.OrdinalIgnoreCase);
        if (!_allowFirstUse) return false;

        PinToStore = fingerprint;
        return true;
    }
}
