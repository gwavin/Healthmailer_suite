using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;

namespace PrintRxerV3Installer;

internal static class Program
{
    private const int Success = 0;
    private const int GeneralFailure = 1;
    private const int MissingRequiredArgument = 2;
    private const int InsufficientPermissions = 3;
    private const int HandoffUnavailable = 4;
    private const int PrinterCaptureFailed = 6;
    private const int ValidationFailed = 7;

    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool uninstall = HasFlag(args, "--uninstall") ||
            string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "printRxerUninstall", StringComparison.OrdinalIgnoreCase);
        bool removeData = HasFlag(args, "--remove-data");
        bool quiet = HasFlag(args, "--quiet");
        bool smokeTest = HasFlag(args, "--smoke-test");
        bool validate = HasFlag(args, "--validate");
        bool help = HasFlag(args, "--help") || HasFlag(args, "/?");
        bool nonInteractive = quiet || validate || smokeTest || help;
        Action<string> log = message => WriteInstallLog(message, nonInteractive);

        try
        {
            if (help)
            {
                WriteHelp();
                return Success;
            }

            if (smokeTest)
            {
                return SmokeTest();
            }

            if (validate)
            {
                return Validate(log);
            }

            if (uninstall && (quiet || removeData))
            {
                if (!IsAdministrator())
                {
                    log("printRxer uninstall requires administrator rights.");
                    return InsufficientPermissions;
                }

                if (PrintRxerUninstaller.IsInstalled() || (removeData && PrintRxerUninstaller.HasLocalData()))
                {
                    PrintRxerUninstaller.Uninstall(removeData, log);
                }

                log("printRxer uninstall completed.");
                return Success;
            }

            if (uninstall)
            {
                Application.Run(new UninstallForm());
                return Success;
            }

            if (quiet)
            {
                if (!IsAdministrator())
                {
                    log("printRxer quiet install requires administrator rights because printer capture is installed in this release.");
                    return InsufficientPermissions;
                }

                string handoffRoot = GetOption(args, "--handoff-root") ?? InstallerPaths.DefaultHandoffRoot;
                PrintRxerInstaller.Install(new InstallOptions(handoffRoot), log);
                log("printRxer quiet install completed.");
                return Success;
            }

            Application.Run(new InstallForm());
            return Success;
        }
        catch (Exception ex)
        {
            log("ERROR: " + ex);
            if (!nonInteractive)
            {
                MessageBox.Show(ex.Message, "printRxer installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return MapExitCode(ex);
        }
    }

    private static int SmokeTest()
    {
        if (!Directory.Exists(InstallerPaths.PayloadPublishRoot))
        {
            Console.WriteLine("MISS " + InstallerPaths.PayloadPublishRoot);
            return MissingRequiredArgument;
        }

        Console.WriteLine("OK   " + InstallerPaths.PayloadPublishRoot);
        return Success;
    }

    private static int Validate(Action<string> log)
    {
        List<string> failures = new();
        log("Validation running as Environment user: " + Environment.UserDomainName + "\\" + Environment.UserName);
        log("Validation running as Windows identity: " + WindowsIdentity.GetCurrent().Name);
        if (!File.Exists(InstallerPaths.InstalledExePath)) { failures.Add("Missing printRxer executable: " + InstallerPaths.InstalledExePath); }
        if (!File.Exists(InstallerPaths.ConfigPath)) { failures.Add("Missing printRxer config: " + InstallerPaths.ConfigPath); }
        if (!Directory.Exists(InstallerPaths.ProgramDataRoot)) { failures.Add("Missing ProgramData root: " + InstallerPaths.ProgramDataRoot); }
        if (!Directory.Exists(Path.Combine(InstallerPaths.ProgramDataRoot, "logs"))) { failures.Add("Missing logs folder: " + Path.Combine(InstallerPaths.ProgramDataRoot, "logs")); }
        if (!File.Exists(Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients", "bundled-recipients.csv"))) { failures.Add("Missing bundled recipient fallback."); }

        string? handoffRoot = TryReadConfigString(InstallerPaths.ConfigPath, "HandoffRoot");
        if (!string.IsNullOrWhiteSpace(handoffRoot))
        {
            log("Configured handoff root: " + handoffRoot);
            if (!Directory.Exists(handoffRoot))
            {
                failures.Add("Configured handoff folder is not reachable: " + handoffRoot);
            }
        }

        string taskState = GetScheduledTaskPrincipal(InstallerPaths.TaskName);
        if (!string.IsNullOrWhiteSpace(taskState))
        {
            log("Scheduled task principal for " + InstallerPaths.TaskName + ": " + taskState);
        }

        if (!taskState.Contains("present", StringComparison.OrdinalIgnoreCase)) { failures.Add("printRxer scheduled task is not installed."); }

        string printerState = ProcessRunner.PowerShell(@"
if (Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue) { 'printer' }
if (Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue) { 'port' }
if (Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue) { 'driver' }
", requireSuccess: false);
        if (!printerState.Contains("printer", StringComparison.OrdinalIgnoreCase)) { failures.Add("printRxer printer queue is not installed."); }
        if (!printerState.Contains("port", StringComparison.OrdinalIgnoreCase)) { failures.Add("printrx: printer port is not installed."); }
        if (!printerState.Contains("driver", StringComparison.OrdinalIgnoreCase)) { failures.Add("PrintRxer XPS Driver is not installed."); }

        foreach (string failure in failures)
        {
            log("VALIDATION: " + failure);
        }

        if (failures.Count > 0)
        {
            return ValidationFailed;
        }

        log("printRxer validation succeeded.");
        return Success;
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetScheduledTaskPrincipal(string taskName)
    {
        string escaped = taskName.Replace("'", "''", StringComparison.Ordinal);
        return ProcessRunner.PowerShell(@"
$task = Get-ScheduledTask -TaskName '" + escaped + @"' -ErrorAction SilentlyContinue
if ($task) {
  'present user=' + $task.Principal.UserId + ' logonType=' + $task.Principal.LogonType + ' runLevel=' + $task.Principal.RunLevel
}
", requireSuccess: false);
    }

    private static string? TryReadConfigString(string path, string propertyName)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int MapExitCode(Exception ex)
    {
        if (ex is UnauthorizedAccessException) { return InsufficientPermissions; }
        if (ex is DirectoryNotFoundException or FileNotFoundException) { return MissingRequiredArgument; }
        if (ex.Message.Contains("handoff", StringComparison.OrdinalIgnoreCase) && ex is IOException) { return HandoffUnavailable; }
        if (ex.Message.Contains("printer", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("port", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("spooler", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterCaptureFailed;
        }

        return GeneralFailure;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
printRxerSetup.exe

Usage:
  printRxerSetup.exe --quiet [--handoff-root <path>]
  printRxerSetup.exe --uninstall --quiet [--remove-data]
  printRxerSetup.exe --validate
  printRxerSetup.exe --help

Quiet install includes the printRxer app, watcher task, recipient cache handling,
native port monitor, PrintRxer XPS driver, and local printer queue named printRxer.

Exit codes:
  0 success
  1 general failure
  2 missing required argument
  3 insufficient permissions
  4 handoff folder unavailable
  6 printer capture install failed
  7 validation failed
  8 cancelled by user
""");
    }

    private static bool HasFlag(string[] args, string name) => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void WriteInstallLog(string message, bool echo)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "logs"));
            string line = "[" + DateTimeOffset.Now.ToString("O") + "] " + message;
            File.AppendAllText(Path.Combine(InstallerPaths.ProgramDataRoot, "logs", "printRxerInstaller.log"), line + Environment.NewLine);
            if (echo)
            {
                Console.WriteLine(message);
            }
        }
        catch
        {
            if (echo)
            {
                Console.WriteLine(message);
            }
        }
    }
}
