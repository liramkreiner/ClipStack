using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClipStack.Core;

/// <summary>
/// Encryption at rest via Windows DPAPI (CurrentUser scope): only the same Windows account
/// on the same machine can decrypt. Also owns the secret key used to hash content for duplicate
/// detection, so stored hashes can't be used to brute-force short secrets.
/// </summary>
public sealed class Protector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClipStack.v1.history");
    private readonly byte[] _hashKey;

    public Protector(string keyFile)
    {
        _hashKey = LoadOrCreateKey(keyFile);
    }

    public static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);

    public string Hash(ClipKind kind, params ReadOnlySpan<byte[]?> parts)
    {
        using var h = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _hashKey);
        h.AppendData([(byte)kind]);
        foreach (var p in parts)
        {
            var len = BitConverter.GetBytes(p?.Length ?? -1);
            h.AppendData(len);
            if (p is not null) h.AppendData(p);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    private static byte[] LoadOrCreateKey(string keyFile)
    {
        try
        {
            if (File.Exists(keyFile))
                return Unprotect(File.ReadAllBytes(keyFile));
        }
        catch (Exception ex)
        {
            Log.Error("Hash key unreadable; generating a new one", ex);
        }
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, Protect(key));
        return key;
    }
}
