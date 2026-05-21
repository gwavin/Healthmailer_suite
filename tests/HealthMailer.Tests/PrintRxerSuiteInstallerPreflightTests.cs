using PrintRxerSuiteInstaller;
using Xunit;

namespace HealthMailer.Tests;

public sealed class PrintRxerSuiteInstallerPreflightTests
{
    [Fact]
    public void Preflight_reports_all_required_release_bundle_paths()
    {
        string root = CreateMinimalBundle();

        IReadOnlyList<BundlePreflightResult> results = BundlePreflight.Check(root);

        Assert.All(results, result => Assert.True(result.Exists, result.RelativePath));
        Assert.Contains(results, result => result.RelativePath == "INSTALL-BUNDLE-README.txt");
        Assert.Contains(results, result => result.RelativePath == @"payload\installers\printRxer\printRxerSetup.exe");
        Assert.Contains(results, result => result.RelativePath == @"payload\tools\New-PrintRxerSupportBundle.ps1");
    }

    [Fact]
    public void Preflight_reports_missing_release_bundle_paths()
    {
        string root = Path.Combine(Path.GetTempPath(), "printRxer-suite-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        IReadOnlyList<BundlePreflightResult> results = BundlePreflight.Check(root);

        Assert.Contains(results, result => result.RelativePath == "INSTALL-BUNDLE-README.txt" && !result.Exists);
        Assert.Contains(results, result => result.RelativePath == @"payload\publish\HealthMailer\HealthMailer.exe" && !result.Exists);
    }

    private static string CreateMinimalBundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "printRxer-suite-preflight-" + Guid.NewGuid().ToString("N"));
        string[] paths =
        {
            "INSTALL-BUNDLE-README.txt",
            "SHA256SUMS.txt",
            @"payload\installers\printRxer\printRxerSetup.exe",
            @"payload\installers\HealthMailer\HealthMailerSetup.exe",
            @"payload\tools\Test-PrintRxerSuiteHealth.ps1",
            @"payload\tools\New-PrintRxerSupportBundle.ps1",
            @"payload\tools\Install-PrintRxerCapturePrinter.ps1",
            @"payload\publish\printRxer\printRxer.exe",
            @"payload\publish\HealthMailer\HealthMailer.exe"
        };

        foreach (string path in paths)
        {
            string fullPath = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "test");
        }

        return root;
    }
}
