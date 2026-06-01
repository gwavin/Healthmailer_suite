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
        Assert.Contains("printRxer_HealthMailer_User_Guide.html", script, StringComparison.Ordinal);
        Assert.Contains("printRxerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("HealthMailerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("payload\\setup\\printRxerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("payload\\setup\\HealthMailerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("The suite installer is the intended front door for IT handoff", script, StringComparison.Ordinal);
        Assert.Contains("New-PrintRxerSupportBundle.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Assert-SelfContainedExecutable", script, StringComparison.Ordinal);
        Assert.Contains("must not require a separate .NET Desktop Runtime install", script, StringComparison.Ordinal);
        Assert.Contains("Target machines should not need a separate .NET Desktop Runtime installation", script, StringComparison.Ordinal);
        Assert.Contains("printRxerSetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("payload\\publish\\printRxer", script, StringComparison.Ordinal);
        Assert.DoesNotContain("The component installers are included at the ZIP root", script, StringComparison.Ordinal);
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
        Assert.Contains("component setup EXEs are internal under payload\\\\setup", form, StringComparison.Ordinal);
        Assert.Contains("Install HealthMailer sending machine", form, StringComparison.Ordinal);
        Assert.Contains("Same-machine pilot: install both", form, StringComparison.Ordinal);
        Assert.Contains("Validate installation", form, StringComparison.Ordinal);
        Assert.Contains("Open logs folder", form, StringComparison.Ordinal);
        Assert.Contains("Select logs folder", form, StringComparison.Ordinal);
        Assert.Contains("Open printRxer logs folder", form, StringComparison.Ordinal);
        Assert.Contains("Open HealthMailer logs folder", form, StringComparison.Ordinal);
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
        Assert.Contains("GetPrintRxerInstallState", form, StringComparison.Ordinal);
        Assert.Contains("printRxer is not installed on this machine. Nothing needs to be removed.", form, StringComparison.Ordinal);
        Assert.Contains("printRxer is not installed; approved ProgramData removal selected.", form, StringComparison.Ordinal);
        Assert.Contains("printRxer ProgramData was preserved.", form, StringComparison.Ordinal);
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
        string printRxerPaths = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "InstallerPaths.cs"));
        string healthMailerPaths = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "InstallerPaths.cs"));

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
            Assert.Contains("ValidateArguments(args)", source, StringComparison.Ordinal);
            Assert.Contains("Only one primary mode may be supplied.", source, StringComparison.Ordinal);
            Assert.Contains("--handoff-root requires a value.", source, StringComparison.Ordinal);
        }

        Assert.Contains("PrinterCaptureFailed = 6", printRxer, StringComparison.Ordinal);
        Assert.Contains("bool useTempLog = uninstall && removeData", printRxer, StringComparison.Ordinal);
        Assert.Contains("Path.GetTempPath()", printRxer, StringComparison.Ordinal);
        Assert.Contains("HealthMailerPrerequisiteFailed = 5", healthMailer, StringComparison.Ordinal);
        Assert.Contains("--send-mail", healthMailer, StringComparison.Ordinal);
        Assert.Contains("--send-mail requires true or false.", healthMailer, StringComparison.Ordinal);
        Assert.Contains("ResolveBundleRoot()", printRxerPaths, StringComparison.Ordinal);
        Assert.Contains("ResolveBundleRoot()", healthMailerPaths, StringComparison.Ordinal);
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
    public void PrintRxer_installer_stops_existing_watcher_before_copying_files()
    {
        string repoRoot = FindRepoRoot();
        string installer = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "PrintRxerInstaller.cs"));

        int stopIndex = installer.IndexOf("Stopping existing printRxer watcher/process before updating application files.", StringComparison.Ordinal);
        int copyIndex = installer.IndexOf("Installing printRxer application files.", StringComparison.Ordinal);

        Assert.True(stopIndex >= 0, "The printRxer installer should stop any existing watcher before updating app files.");
        Assert.True(copyIndex > stopIndex, "The printRxer installer must stop the existing watcher before copying over printRxer.exe.");
        Assert.Contains("Disable-ScheduledTask -TaskName $taskName", installer, StringComparison.Ordinal);
        Assert.Contains("WaitForStoppedProcesses", installer, StringComparison.Ordinal);
        Assert.Contains("printRxer process did not stop before install.", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Process -Name 'printRxer*'", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Process -Name \"printRxer*\"", installer, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'printRxer'", installer, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'PrintRxer.Agent'", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintRxer_installer_registers_watcher_for_all_interactive_users()
    {
        string repoRoot = FindRepoRoot();
        string[] watcherRegistrationSources =
        {
            File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "PrintRxerInstaller.cs")),
            File.ReadAllText(Path.Combine(repoRoot, "apps", "PrintRxerV3", "app", "Program.cs"))
        };

        foreach (string source in watcherRegistrationSources)
        {
            Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", source, StringComparison.Ordinal);
            Assert.DoesNotContain("New-ScheduledTaskTrigger -AtLogOn -User", source, StringComparison.Ordinal);
            Assert.True(
                source.Contains("New-ScheduledTaskPrincipal -GroupId 'BUILTIN\\Users' -RunLevel Limited", StringComparison.Ordinal) ||
                source.Contains("New-ScheduledTaskPrincipal -GroupId 'BUILTIN\\\\Users' -RunLevel Limited", StringComparison.Ordinal),
                "printRxer watcher task should use the BUILTIN\\Users group principal.");
            Assert.DoesNotContain("New-ScheduledTaskPrincipal -UserId", source, StringComparison.Ordinal);
            Assert.Contains("MultipleInstances Parallel", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MultipleInstances IgnoreNew", source, StringComparison.Ordinal);
            Assert.DoesNotContain("watchdogTrigger", source, StringComparison.Ordinal);
        }

        string installer = watcherRegistrationSources[0];
        Assert.DoesNotContain("printRxer scheduled task target user:", installer, StringComparison.Ordinal);
        Assert.Contains("printRxer scheduled task target: all interactive Windows users.", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintRxer_validation_checks_all_users_watcher_task_shape()
    {
        string repoRoot = FindRepoRoot();
        string program = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "Program.cs"));

        Assert.Contains(@"BUILTIN\Users", program, StringComparison.Ordinal);
        Assert.Contains("string.Equals(groupId, \"Users\"", program, StringComparison.Ordinal);
        Assert.Contains("runLevel=Limited", program, StringComparison.Ordinal);
        Assert.Contains("multipleInstances=Parallel", program, StringComparison.Ordinal);
        Assert.Contains("trigger=AtLogOn", program, StringComparison.Ordinal);
        Assert.Contains("printRxer scheduled task is not configured for all interactive users.", program, StringComparison.Ordinal);
        Assert.Contains("printRxer scheduled task is bound to a named Windows user.", program, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintRxer_installer_keeps_owner_sid_matching_enabled()
    {
        string repoRoot = FindRepoRoot();
        string installer = File.ReadAllText(Path.Combine(repoRoot, "installers", "PrintRxerV3Installer", "PrintRxerInstaller.cs"));

        Assert.Contains("RequireJobOwnerMatch = true", installer, StringComparison.Ordinal);
        Assert.Contains("AllowMissingSubmittingSid = false", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthMailer_package_lock_acquisition_does_not_depend_on_file_exists_precheck()
    {
        string repoRoot = FindRepoRoot();
        string processor = File.ReadAllText(Path.Combine(repoRoot, "apps", "HealthMailer", "PackageProcessor.cs"));
        int claimIndex = processor.IndexOf("private PackageClaim? TryClaimPackage", StringComparison.Ordinal);
        int nextMethodIndex = processor.IndexOf("private static DateTimeOffset ReadLockTime", StringComparison.Ordinal);
        string claimBody = processor.Substring(claimIndex, nextMethodIndex - claimIndex);

        Assert.Contains("FileMode.OpenOrCreate", claimBody, StringComparison.Ordinal);
        Assert.Contains("FileShare.None", claimBody, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists(lockPath)", claimBody, StringComparison.Ordinal);
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
    public void HealthMailer_installer_stops_existing_watcher_before_copying_files()
    {
        string repoRoot = FindRepoRoot();
        string installer = File.ReadAllText(Path.Combine(repoRoot, "installers", "HealthMailerInstaller", "HealthMailerInstallerEngine.cs"));

        int stopIndex = installer.IndexOf("Stopping existing HealthMailer watcher/process before updating application files.", StringComparison.Ordinal);
        int copyIndex = installer.IndexOf("Installing HealthMailer application files.", StringComparison.Ordinal);

        Assert.True(stopIndex >= 0, "The HealthMailer installer should stop any existing watcher before updating app files.");
        Assert.True(copyIndex > stopIndex, "The HealthMailer installer must stop the existing watcher before copying over HealthMailer.exe.");
        Assert.Contains("Disable-ScheduledTask -TaskName 'HealthMailer'", installer, StringComparison.Ordinal);
        Assert.Contains("Stop-ScheduledTask -TaskName 'HealthMailer'", installer, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'HealthMailer'", installer, StringComparison.Ordinal);
        Assert.Contains("HealthMailer process did not stop before install.", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Process -Name 'HealthMailer*'", installer, StringComparison.Ordinal);
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
