using System.Security.Cryptography;

namespace NautilyouService;

// Génère et conserve la paire de clés RSA de l'appareil pour l'authentification WebSocket
// (remplace le GUID aléatoire de la V1 — voir dev-context, section Sécurité du pairing).
// La clé privée ne quitte jamais la machine et est chiffrée au repos via DPAPI (portée machine,
// car le client tournera à terme en service SYSTEM, pas sous une session utilisateur précise).
public static class DeviceKeyStore
{
    private static string KeyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nautilyou", "device.key");

    public static (RSA Rsa, string PublicKeyBase64) LoadOrCreate()
    {
        var rsa = RSA.Create(2048);

        if (File.Exists(KeyPath))
        {
            var encrypted = File.ReadAllBytes(KeyPath);
            var decrypted = ProtectedDataUnprotect(encrypted);
            rsa.ImportRSAPrivateKey(decrypted, out _);
        }
        else
        {
            var privateKeyBytes = rsa.ExportRSAPrivateKey();
            var encrypted = ProtectedDataProtect(privateKeyBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllBytes(KeyPath, encrypted);
        }

        var publicKeyBase64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return (rsa, publicKeyBase64);
    }

    public static byte[] Sign(RSA rsa, byte[] data) =>
        rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    private static byte[] ProtectedDataProtect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            return System.Security.Cryptography.ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
        }
        return data; // hors Windows (dev cross-plateforme) : pas de chiffrement au repos, TODO si portage Linux/macOS
    }

    private static byte[] ProtectedDataUnprotect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            return System.Security.Cryptography.ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
        }
        return data;
    }
}
