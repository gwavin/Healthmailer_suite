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
        Assert.Contains(results, result => result.RelativePath == "printRxerSetup.exe");
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

    [Fact]
    public void Suite_installer_waits_for_printrxer_setup_and_validates_printer_capture()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "installers",
            "PrintRxerSuiteInstaller",
            "SuiteInstallerForm.cs"));

        Assert.Contains("RunForResult(setupPath, arguments, elevate: elevate)", source);
        Assert.Contains("ValidatePrintRxerAfterSetup", source);
        Assert.Contains("--validate", source);
        Assert.Contains("Windows should show a printer named printRxer", source);
    }

    private static string CreateMinimalBundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "printRxer-suite-preflight-" + Guid.NewGuid().ToString("N"));
        string[] paths =
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

        foreach (string path in paths)
        {
            string fullPath = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "test");
        }

        return root;
    }

    private static string RepoRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "PrintRxerSuite.slnx")))
        {
            directory = Directory.GetParent(directory)?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        return directory;
    }
}
