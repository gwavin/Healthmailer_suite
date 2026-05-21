namespace PrintRxerSuiteInstaller;

internal sealed record BundlePreflightResult(string RelativePath, bool Exists);

internal static class BundlePreflight
{
    public static IReadOnlyList<BundlePreflightResult> Check(string bundleRoot)
    {
        string[] requiredPaths =
        {
            "INSTALL-BUNDLE-README.txt",
            "SHA256SUMS.txt",
            "printRxerSetup.exe",
            "HealthMailerSetup.exe",
            @"payload\tools\Test-PrintRxerSuiteHealth.ps1",
            @"payload\tools\New-PrintRxerSupportBundle.ps1",
            @"payload\tools\Install-PrintRxerCapturePrinter.ps1",
            @"payload\publish\printRxer\printRxer.exe",
            @"payload\publish\HealthMailer\HealthMailer.exe"
        };

        return requiredPaths
            .Select(path => new BundlePreflightResult(path, File.Exists(Path.Combine(bundleRoot, path))))
            .ToArray();
    }
}
