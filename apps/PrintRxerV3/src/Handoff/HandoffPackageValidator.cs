using PrintRxerV3.Packaging;

namespace PrintRxerV3.Handoff;

public static class HandoffPackageValidator
{
    public static void ValidatePreparedPdf(string pdfPath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Prepared PDF file not found.", pdfPath);
        }

        if (!LooksLikePdf(pdfPath))
        {
            throw new InvalidOperationException("Prepared attachment is not a PDF. The file must begin with %PDF- before a HealthMailer handoff package can be marked READY.");
        }

        string actualSha256 = Sha256Hasher.HashFile(pdfPath);
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Prepared PDF SHA256 does not match request metadata.");
        }
    }

    public static bool LooksLikePdf(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[5];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(signature) == signature.Length &&
            signature[0] == (byte)'%' &&
            signature[1] == (byte)'P' &&
            signature[2] == (byte)'D' &&
            signature[3] == (byte)'F' &&
            signature[4] == (byte)'-';
    }
}
