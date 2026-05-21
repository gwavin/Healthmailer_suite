using System.Diagnostics;
using System.Text;

namespace PrintRxerSuiteInstaller;

internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments = "", bool requireSuccess = true, bool elevate = false)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = elevate,
                Verb = elevate ? "runas" : string.Empty,
                CreateNoWindow = !elevate,
                RedirectStandardOutput = !elevate,
                RedirectStandardError = !elevate,
                WorkingDirectory = SuitePaths.BundleRoot
            }
        };

        StringBuilder output = new();
        if (!elevate)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        }

        process.Start();
        if (!elevate)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        process.WaitForExit();

        string text = output.ToString().Trim();
        if (requireSuccess && process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(text) ? "No further detail was reported." : text;
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} returned exit code {process.ExitCode}.\n\n{detail}");
        }

        return text;
    }

    public static string PowerShellFile(string scriptPath, string arguments = "", bool requireSuccess = true, bool elevate = false)
    {
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + arguments, requireSuccess, elevate);
    }

    public static void StartElevated(string fileName, string arguments = "")
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = SuitePaths.BundleRoot
            }
        };

        process.Start();
    }
}
