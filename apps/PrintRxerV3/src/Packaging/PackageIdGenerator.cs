using System.Globalization;
using System.Security.Cryptography;

namespace PrintRxerV3.Packaging;

public static class PackageIdGenerator
{
    public static string Create(DateTimeOffset timestamp)
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(6);
        string suffix = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" + suffix;
    }
}
