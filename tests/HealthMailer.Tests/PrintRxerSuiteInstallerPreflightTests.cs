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
        Assert.Contains(results, result => result.RelativePath == @"payload\setup\printRxerSetup.exe");
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

        Assert.Contains("RunForResult(setupPath, arguments, elevate: elevate, whileWaiting: PumpBusyUi)", source);
        Assert.Contains("RunPrintRxerInstall", source);
        Assert.Contains("SetupRoot", File.ReadAllText(Path.Combine(RepoRoot(), "installers", "PrintRxerSuiteInstaller", "SuitePaths.cs")));
        Assert.Contains("--quiet --handoff-root", source);
        Assert.Contains("ValidatePrintRxerAfterSetup", source);
        Assert.Contains("--validate", source);
        Assert.Contains("Windows should show a printer named printRxer", source);
        Assert.Contains("whileWaiting: PumpBusyUi", source);
        Assert.Contains("ProgressTitle(SetupKind.PrintRxer, uninstall: false)", source);
    }

    [Fact]
    public void Suite_installer_reports_uac_cancellation_as_user_cancelled_not_crash()
    {
        string runner = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "installers",
            "PrintRxerSuiteInstaller",
            "ProcessRunner.cs"));
        string form = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "installers",
            "PrintRxerSuiteInstaller",
            "SuiteInstallerForm.cs"));

        Assert.Contains("Win32Exception", runner);
        Assert.Contains("ERROR_CANCELLED", runner);
        Assert.Contains("Windows administrator approval was cancelled.", runner);
        Assert.Contains("setupResult.Cancelled", form);
        Assert.Contains("printRxer install was cancelled before Windows made changes.", form);
    }

    [Fact]
    public void Suite_installer_printrxer_handoff_prompt_uses_healthmailer_style_layout()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "installers",
            "PrintRxerSuiteInstaller",
            "SuiteInstallerForm.cs"));

        Assert.Contains("Use the default local handoff folder", source);
        Assert.Contains("Use a shared or custom handoff folder", source);
        Assert.Contains("CreateDialogButton(\"Install\", DialogResult.OK)", source);
        Assert.Contains("CreateDialogButton(\"Cancel\", DialogResult.Cancel)", source);
        Assert.Contains("dialog.AcceptButton = ok", source);
        Assert.Contains("dialog.CancelButton = cancel", source);
        Assert.Contains("Width = 120", source);
        Assert.Contains("Height = 44", source);
        Assert.Contains("native port monitor, driver, and local printer queue named printRxer", source);
    }

    [Fact]
    public void Component_installers_can_resolve_suite_bundle_root_from_payload_setup()
    {
        string repoRoot = RepoRoot();
        string printRxerPaths = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "InstallerPaths.cs"));
        string healthMailerPaths = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "InstallerPaths.cs"));

        Assert.Contains("ResolveBundleRoot()", printRxerPaths);
        Assert.Contains("payload\", \"publish\", \"printRxer", printRxerPaths);
        Assert.Contains("ResolveBundleRoot()", healthMailerPaths);
        Assert.Contains("payload\", \"publish\", \"HealthMailer", healthMailerPaths);
    }

    private static string CreateMinimalBundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "printRxer-suite-preflight-" + Guid.NewGuid().ToString("N"));
        string[] paths =
        {
            "INSTALL-BUNDLE-README.txt",
            "SHA256SUMS.txt",
            @"payload\setup\printRxerSetup.exe",
            @"payload\setup\HealthMailerSetup.exe",
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
