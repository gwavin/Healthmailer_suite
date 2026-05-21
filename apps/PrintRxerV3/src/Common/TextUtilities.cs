using System.Text.RegularExpressions;

namespace PrintRxerV3.Common;

public static class TextUtilities
{
    public static string NormalizeWhitespace(string? value)
    {
        return Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
    }
}
