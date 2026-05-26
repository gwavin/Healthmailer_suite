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
        Assert.Contains("Assert-SelfContainedExecutable", script, StringComparison.Ordinal);
        Assert.Contains("must not require a separate .NET Desktop Runtime install", script, StringComparison.Ordinal);
        Assert.Contains("Target machines should not need a separate .NET Desktop Runtime installation", script, StringComparison.Ordinal);
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
        Assert.Contains("bool elevate = setupKind == SetupKind.PrintRxer", form, StringComparison.Ordinal);
        Assert.Contains("RunPrintRxerInstall", form, StringComparison.Ordinal);
        Assert.Contains("--quiet --handoff-root", form, StringComparison.Ordinal);
        Assert.Contains("--uninstall --quiet", form, StringComparison.Ordinal);
        Assert.Contains("--uninstall --remove-data --quiet", form, StringComparison.Ordinal);
        Assert.Contains("RunPrintRxerUninstall", form, StringComparison.Ordinal);
        Assert.Contains("Remove C:\\\\ProgramData\\\\printRxer too?", form, StringComparison.Ordinal);
        Assert.Contains("MessageBoxDefaultButton.Button2", form, StringComparison.Ordinal);
        Assert.Contains("printRxer uninstall selected with ProgramData preserved", form, StringComparison.Ordinal);
        Assert.Contains("printRxer uninstall selected with ProgramData removal", form, StringComparison.Ordinal);
        Assert.Contains("printRxer uninstall will remove the watcher, app files, printer queue, driver, port, and monitor", form, StringComparison.Ordinal);
        Assert.Contains("ComponentDisplayName(setupKind) + \" uninstall is running", form, StringComparison.Ordinal);
        Assert.Contains("Suite buttons are disabled while Windows removes", form, StringComparison.Ordinal);
        Assert.Contains("This window will wait for uninstall to finish", form, StringComparison.Ordinal);
        Assert.Contains("ShowBusyDialog(ProgressTitle(setupKind, isUninstall), ProgressMessage(setupKind, isUninstall))", form, StringComparison.Ordinal);
        Assert.Contains("whileWaiting: PumpBusyUi", form, StringComparison.Ordinal);
        Assert.Contains("Application.DoEvents()", form, StringComparison.Ordinal);
        Assert.Contains("setupKind == SetupKind.HealthMailer ? \"HealthMailer\" : \"printRxer\"", form, StringComparison.Ordinal);
        Assert.Contains("Please wait while Windows removes the HealthMailer scheduled task and app files.", form, StringComparison.Ordinal);
        Assert.Contains("Please wait while Windows installs HealthMailer for the Outlook/Healthmail sender user.", form, StringComparison.Ordinal);
        Assert.Contains("Please wait while Windows installs printRxer, including the watcher, printer queue, driver, port, monitor, and app files.", form, StringComparison.Ordinal);
        Assert.Contains("HealthMailer uninstall finished. Standard uninstall preserves C:\\\\ProgramData\\\\HealthMailer evidence by default.", form, StringComparison.Ordinal);
        Assert.Contains("AppendNewLogLines(SuitePaths.PrintRxerInstallerLogPath", form, StringComparison.Ordinal);
        Assert.Contains("printRxer uninstall log:", form, StringComparison.Ordinal);
        Assert.Contains("ProcessRunner.RunForResult(setupPath, arguments, elevate: elevate, whileWaiting: PumpBusyUi)", form, StringComparison.Ordinal);
        Assert.Contains("ValidatePrintRxerAfterSetup", form, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessRunner.StartElevated(setupPath, arguments)", form, StringComparison.Ordinal);
        Assert.Contains("No printRxer or HealthMailer log folder exists yet", form, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)", form, StringComparison.Ordinal);
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
        Assert.Contains("bool useTempLog = uninstall && removeData", printRxer, StringComparison.Ordinal);
        Assert.Contains("Path.GetTempPath()", printRxer, StringComparison.Ordinal);
        Assert.Contains("HealthMailerPrerequisiteFailed = 5", healthMailer, StringComparison.Ordinal);
        Assert.Contains("--send-mail", healthMailer, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintRxer_uninstaller_does_not_kill_its_own_setup_process()
    {
        string repoRoot = FindRepoRoot();
        string uninstaller = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "PrintRxerUninstaller.cs"));
        string uninstallForm = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "UninstallForm.cs"));

        Assert.DoesNotContain("Get-Process -Name 'printRxer*'", uninstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Process -Name \"printRxer*\"", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'printRxer'", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Initial component state", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Final component state", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Removing scheduled task", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Printer cleanup step finished", uninstaller, StringComparison.Ordinal);
        Assert.Contains("--uninstall --quiet", uninstallForm, StringComparison.Ordinal);
        Assert.Contains("--uninstall --remove-data --quiet", uninstallForm, StringComparison.Ordinal);
        Assert.Contains("complete the already-confirmed uninstall", uninstallForm, StringComparison.Ordinal);
        Assert.Contains("Uninstall is running. Buttons are disabled", uninstallForm, StringComparison.Ordinal);
        Assert.Contains("Uninstalling...", uninstallForm, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthMailer_uninstaller_separates_active_install_from_preserved_data()
    {
        string repoRoot = FindRepoRoot();
        string uninstaller = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "HealthMailerUninstaller.cs"));
        string program = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "Program.cs"));

        string isInstalledBody = uninstaller.Substring(
            uninstaller.IndexOf("public static bool IsInstalled()", StringComparison.Ordinal),
            uninstaller.IndexOf("public static bool HasLocalData()", StringComparison.Ordinal) - uninstaller.IndexOf("public static bool IsInstalled()", StringComparison.Ordinal));

        Assert.DoesNotContain("ConfigPath", isInstalledBody, StringComparison.Ordinal);
        Assert.Contains("public static bool HasLocalData()", uninstaller, StringComparison.Ordinal);
        Assert.Contains("icacls $path /grant:r", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $path -Recurse -Force", uninstaller, StringComparison.Ordinal);
        Assert.Contains("bool useTempLog = uninstall && removeData", program, StringComparison.Ordinal);
        Assert.Contains("HealthMailer uninstall needs review. ProgramData was not fully removed.", program, StringComparison.Ordinal);
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
