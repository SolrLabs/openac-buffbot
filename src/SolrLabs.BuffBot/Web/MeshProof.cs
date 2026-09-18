using System.Security.Cryptography;
using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>A spoke sends a fresh nonce with every heartbeat, and only applies the commands a heartbeat response carries if the response's <c>X-BuffBot-Proof</c> header is <c>hex(HMACSHA256(key, nonce))</c>. Both the key and nonce are decoded from hex to raw bytes before hashing.</summary>
internal static class MeshProof
{
    private const int NonceBytes = 16;

    internal static string GenerateNonce()
    {
        Span<byte> raw = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(raw);
        return Convert.ToHexString(raw).ToLowerInvariant();
    }

    /// <summary>Throws for a key that is not itself <see cref="MeshKeyStore.IsValidKey"/>: there is no such thing as a proof over an invalid key.</summary>
    internal static string Compute(string keyHex, string nonceHex)
    {
        if (!MeshKeyStore.IsValidKey(keyHex))
            throw new ArgumentException("The mesh key must be 64 lowercase hex characters.", nameof(keyHex));
        byte[] hash = HMACSHA256.HashData(Convert.FromHexString(keyHex), Convert.FromHexString(nonceHex));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>An invalid key never verifies, whatever <paramref name="proofHex"/> is.</summary>
    internal static bool Verify(string keyHex, string nonceHex, string proofHex)
    {
        if (!MeshKeyStore.IsValidKey(keyHex))
            return false;

        string expected = Compute(keyHex, nonceHex);
        byte[] a = Encoding.ASCII.GetBytes(expected);
        byte[] b = Encoding.ASCII.GetBytes(proofHex);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
