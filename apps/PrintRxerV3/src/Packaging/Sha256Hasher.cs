using System.Security.Cryptography;

namespace PrintRxerV3.Packaging;

public static class Sha256Hasher
{
    public static string HashFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File path is required.", nameof(path));
        }

        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
