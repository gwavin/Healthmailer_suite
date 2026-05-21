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

        Assert.Contains("Install printRxer", form, StringComparison.Ordinal);
        Assert.Contains("Install HealthMailer", form, StringComparison.Ordinal);
        Assert.Contains("Install printRxer printer capture", form, StringComparison.Ordinal);
        Assert.Contains("Validate installation", form, StringComparison.Ordinal);
        Assert.Contains("Open logs folder", form, StringComparison.Ordinal);
        Assert.Contains("Create support bundle", form, StringComparison.Ordinal);
        Assert.Contains("Uninstall / repair", form, StringComparison.Ordinal);
        Assert.Contains("excludes PDF payloads by default", form, StringComparison.Ordinal);
        Assert.Contains("administrator approval to run this component installer", form, StringComparison.Ordinal);
        Assert.Contains("ProcessRunner.Run(setupPath, arguments, elevate: true)", form, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(560, 360)", form, StringComparison.Ordinal);
        Assert.Contains("TableLayoutPanel panel", form, StringComparison.Ordinal);
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
