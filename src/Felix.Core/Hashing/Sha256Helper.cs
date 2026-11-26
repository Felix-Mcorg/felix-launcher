using System.Security.Cryptography;

namespace Felix.Core.Hashing;

public static class Sha256Helper
{
    /// <summary>SHA-256 d'un fichier, en hexadécimal minuscule.</summary>
    public static async Task<string> ComputeAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
