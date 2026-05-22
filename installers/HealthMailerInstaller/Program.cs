using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;

namespace HealthMailerInstaller;

internal static class Program
{
    private const int Success = 0;
    private const int GeneralFailure = 1;
    private const int MissingRequiredArgument = 2;
    private const int InsufficientPermissions = 3;
    private const int HandoffUnavailable = 4;
    private const int HealthMailerPrerequisiteFailed = 5;
    private const int ValidationFailed = 7;

    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool uninstall = HasFlag(args, "--uninstall");
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
                if (HealthMailerUninstaller.IsInstalled())
                {
                    HealthMailerUninstaller.Uninstall(removeData, log);
                }

                log("HealthMailer uninstall completed.");
                return Success;
            }

            if (uninstall)
            {
                Application.Run(new UninstallForm());
                return Success;
            }

            if (quiet)
            {
                string? handoffRoot = GetOption(args, "--handoff-root");
                if (string.IsNullOrWhiteSpace(handoffRoot))
                {
                    log("Missing required argument: --handoff-root");
                    return MissingRequiredArgument;
                }

                if (!TryGetBoolOption(args, "--send-mail", out bool sendMail, out string? error))
                {
                    log(error ?? "Invalid --send-mail value.");
                    return MissingRequiredArgument;
                }

                HealthMailerInstallerEngine.Install(new InstallOptions(handoffRoot, sendMail), log);
                log("HealthMailer quiet install completed.");
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
                MessageBox.Show(ex.Message, "HealthMailer setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        if (!File.Exists(InstallerPaths.InstalledExePath)) { failures.Add("Missing HealthMailer executable: " + InstallerPaths.InstalledExePath); }
        if (!File.Exists(InstallerPaths.ConfigPath)) { failures.Add("Missing HealthMailer config: " + InstallerPaths.ConfigPath); }
        if (!Directory.Exists(InstallerPaths.ProgramDataRoot)) { failures.Add("Missing ProgramData root: " + InstallerPaths.ProgramDataRoot); }
        if (!Directory.Exists(Path.Combine(InstallerPaths.ProgramDataRoot, "logs"))) { failures.Add("Missing logs folder: " + Path.Combine(InstallerPaths.ProgramDataRoot, "logs")); }

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

        if (!taskState.Contains("present", StringComparison.OrdinalIgnoreCase)) { failures.Add("HealthMailer scheduled task is not installed."); }

        foreach (string failure in failures)
        {
            log("VALIDATION: " + failure);
        }

        if (failures.Count > 0)
        {
            return ValidationFailed;
        }

        log("HealthMailer validation succeeded.");
        return Success;
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
        if (ex.Message.Contains("Outlook", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("HealthMailer", StringComparison.OrdinalIgnoreCase)) { return HealthMailerPrerequisiteFailed; }
        return GeneralFailure;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
HealthMailerSetup.exe

Usage:
  HealthMailerSetup.exe --quiet --handoff-root <path> [--send-mail true|false]
  HealthMailerSetup.exe --uninstall --quiet [--remove-data]
  HealthMailerSetup.exe --validate
  HealthMailerSetup.exe --help

Exit codes:
  0 success
  1 general failure
  2 missing required argument
  3 insufficient permissions
  4 handoff folder unavailable
  5 Outlook/HealthMailer prerequisite failed
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

    private static bool TryGetBoolOption(string[] args, string name, out bool value, out string? error)
    {
        string? text = GetOption(args, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = true;
            error = null;
            return true;
        }

        if (bool.TryParse(text, out value))
        {
            error = null;
            return true;
        }

        error = name + " must be true or false.";
        return false;
    }

    private static void WriteInstallLog(string message, bool echo)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "logs"));
            string line = "[" + DateTimeOffset.Now.ToString("O") + "] " + message;
            File.AppendAllText(Path.Combine(InstallerPaths.ProgramDataRoot, "logs", "HealthMailerInstaller.log"), line + Environment.NewLine);
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
