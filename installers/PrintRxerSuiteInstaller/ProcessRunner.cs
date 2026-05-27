using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;
using System.Text;

namespace PrintRxerSuiteInstaller;

internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments = "", bool requireSuccess = true, bool elevate = false)
    {
        ProcessResult result = RunForResult(fileName, arguments, elevate);
        if (requireSuccess && result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.Output) ? "No further detail was reported." : result.Output;
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} returned exit code {result.ExitCode}.\n\n{detail}");
        }

        return result.Output;
    }

    public static ProcessResult RunForResult(string fileName, string arguments = "", bool elevate = false, Action? whileWaiting = null)
    {
        bool shouldElevate = elevate && !IsAdministrator();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = shouldElevate,
                Verb = shouldElevate ? "runas" : string.Empty,
                CreateNoWindow = !shouldElevate,
                RedirectStandardOutput = !shouldElevate,
                RedirectStandardError = !shouldElevate,
                WorkingDirectory = SuitePaths.BundleRoot
            }
        };

        StringBuilder output = new();
        if (!shouldElevate)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return ProcessResult.CancelledByUser(
                "Windows did not grant administrator approval for this setup step." +
                Environment.NewLine + Environment.NewLine +
                "If no UAC prompt appeared, Windows policy or the current session may be blocking elevation prompts. Right-click PrintRxerSuiteInstaller.exe and choose Run as administrator, or ask IT to run the installer with administrator rights.");
        }

        if (!shouldElevate)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        while (!process.WaitForExit(100))
        {
            whileWaiting?.Invoke();
        }

        string text = output.ToString().Trim();
        return new ProcessResult(process.ExitCode, text);
    }

    private const int ERROR_CANCELLED = 1223;

    public static string PowerShellFile(string scriptPath, string arguments = "", bool requireSuccess = true, bool elevate = false)
    {
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + arguments, requireSuccess, elevate);
    }

    public static string PowerShell(string command, bool requireSuccess = true)
    {
        string escaped = command.Replace("\"", "\\\"", StringComparison.Ordinal);
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + escaped + "\"", requireSuccess);
    }

    public static void Start(string fileName, string arguments = "", bool elevate = false)
    {
        bool shouldElevate = elevate && !IsAdministrator();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = shouldElevate ? "runas" : string.Empty,
                WorkingDirectory = SuitePaths.BundleRoot
            }
        };

        process.Start();
    }

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, bool Cancelled = false)
{
    public static ProcessResult CancelledByUser(string message) => new(1223, message, Cancelled: true);
}
