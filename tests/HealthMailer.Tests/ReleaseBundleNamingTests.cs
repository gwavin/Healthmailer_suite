using Xunit;

namespace HealthMailer.Tests;

public sealed class ReleaseBundleNamingTests
{
    [Fact]
    public void Release_bundle_script_uses_final_printRxer_user_facing_names()
    {
        string repoRoot = FindRepoRoot();
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "New-PrintRxerSuiteReleaseBundle.ps1"));

        Assert.Contains("printRxerSuite-", script, StringComparison.Ordinal);
        Assert.Contains("PrintRxerSuiteInstaller.exe", script, StringComparison.Ordinal);
        Assert.Contains("Test-SuiteZipSmoke", script, StringComparison.Ordinal);
        Assert.Contains("--smoke-test", script, StringComparison.Ordinal);
        Assert.Contains("INSTALL-BUNDLE-README.txt", script, StringComparison.Ordinal);
        Assert.Contains("printRxerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("HealthMailerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("New-PrintRxerSupportBundle.ps1", script, StringComparison.Ordinal);
        Assert.Contains("printRxerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("payload\\publish\\printRxer", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PrintRxerV3Setup.exe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PrintRxerV3 install bundle", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_uploads_suite_zip()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release-bundle.yml"));

        Assert.Contains("dist/printRxerSuite-*.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("dist/printRxer-*.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("dist/HealthMailer-*.zip", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Suite_installer_project_keeps_gui_first_actions_visible()
    {
        string repoRoot = FindRepoRoot();
        string form = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerSuiteInstaller", "SuiteInstallerForm.cs"));

        Assert.Contains("Install printRxer printing machine", form, StringComparison.Ordinal);
        Assert.Contains("Install HealthMailer sending machine", form, StringComparison.Ordinal);
        Assert.Contains("Same-machine pilot: install both", form, StringComparison.Ordinal);
        Assert.Contains("Validate installation", form, StringComparison.Ordinal);
        Assert.Contains("Open logs folder", form, StringComparison.Ordinal);
        Assert.Contains("Create support bundle", form, StringComparison.Ordinal);
        Assert.Contains("Advanced / repair", form, StringComparison.Ordinal);
        Assert.Contains("Repair printRxer printer capture", form, StringComparison.Ordinal);
        Assert.Contains("excludes PDF payloads by default", form, StringComparison.Ordinal);
        Assert.Contains("HealthMailer setup will run as the current Windows user", form, StringComparison.Ordinal);
        Assert.Contains("printRxer setup includes printer capture", form, StringComparison.Ordinal);
        Assert.Contains("bool isUninstall = arguments.Contains(\"--uninstall\"", form, StringComparison.Ordinal);
        Assert.Contains("bool elevate = setupKind == SetupKind.PrintRxer && !isUninstall", form, StringComparison.Ordinal);
        Assert.Contains("printRxer uninstall will check whether printRxer is installed", form, StringComparison.Ordinal);
        Assert.Contains("ProcessRunner.Start(setupPath, arguments, elevate: elevate)", form, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessRunner.StartElevated(setupPath, arguments)", form, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(560, 360)", form, StringComparison.Ordinal);
        Assert.Contains("TableLayoutPanel panel", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Suite_health_script_counts_only_ready_packages_in_handoff_root()
    {
        string repoRoot = FindRepoRoot();
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "Test-PrintRxerSuiteHealth.ps1"));

        Assert.Contains("function Get-ReadyPackageCount", script, StringComparison.Ordinal);
        Assert.Contains("$healthReadyCount = Get-ReadyPackageCount $healthConfig.HandoffRoot", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$healthReadyCount = Get-DirectoryCount $healthConfig.HandoffRoot", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Component_installers_expose_enterprise_cli_contract()
    {
        string repoRoot = FindRepoRoot();
        string printRxer = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "Program.cs"));
        string healthMailer = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "Program.cs"));

        foreach (string source in new[] { printRxer, healthMailer })
        {
            Assert.Contains("--quiet", source, StringComparison.Ordinal);
            Assert.Contains("--uninstall", source, StringComparison.Ordinal);
            Assert.Contains("--validate", source, StringComparison.Ordinal);
            Assert.Contains("--help", source, StringComparison.Ordinal);
            Assert.Contains("--handoff-root", source, StringComparison.Ordinal);
            Assert.Contains("MissingRequiredArgument = 2", source, StringComparison.Ordinal);
            Assert.Contains("InsufficientPermissions = 3", source, StringComparison.Ordinal);
            Assert.Contains("ValidationFailed = 7", source, StringComparison.Ordinal);
            Assert.Contains("WriteInstallLog", source, StringComparison.Ordinal);
        }

        Assert.Contains("PrinterCaptureFailed = 6", printRxer, StringComparison.Ordinal);
        Assert.Contains("HealthMailerPrerequisiteFailed = 5", healthMailer, StringComparison.Ordinal);
        Assert.Contains("--send-mail", healthMailer, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_and_docs_do_not_use_legacy_underscored_printRxer_name()
    {
        string repoRoot = FindRepoRoot();
        string[] includedRoots =
        {
            "apps",
            "docs",
            "installers",
            "native",
            "tests",
            "tools"
        };

        IEnumerable<string> files = includedRoots
            .Select(root => Path.Combine(repoRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        string legacyName = "printrxer" + "_v3";
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(legacyName, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Suite_deployment_text_separates_healthmailer_user_context_from_printrxer_admin_context()
    {
        string repoRoot = FindRepoRoot();
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "New-PrintRxerSuiteReleaseBundle.ps1"));
        string healthMailerManifest = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "app.manifest"));
        string printRxerManifest = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "app.manifest"));

        Assert.Contains("Run as the intended Outlook/Healthmail sender user", script, StringComparison.Ordinal);
        Assert.Contains("administrator-capable context", script, StringComparison.Ordinal);
        Assert.Contains("scheduled task principal", script, StringComparison.Ordinal);
        Assert.Contains("level=\"asInvoker\"", healthMailerManifest, StringComparison.Ordinal);
        Assert.Contains("level=\"asInvoker\"", printRxerManifest, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools")) && File.Exists(Path.Combine(directory.FullName, "PrintRxerSuite.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
