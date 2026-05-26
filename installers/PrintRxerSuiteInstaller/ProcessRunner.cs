using System.Diagnostics;
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

        while (!process.WaitForExit(100))
        {
            whileWaiting?.Invoke();
        }

        string text = output.ToString().Trim();
        return new ProcessResult(process.ExitCode, text);
    }

    public static string PowerShellFile(string scriptPath, string arguments = "", bool requireSuccess = true, bool elevate = false)
    {
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + arguments, requireSuccess, elevate);
    }

    public static void Start(string fileName, string arguments = "", bool elevate = false)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = elevate ? "runas" : string.Empty,
                WorkingDirectory = SuitePaths.BundleRoot
            }
        };

        process.Start();
    }
}

internal sealed record ProcessResult(int ExitCode, string Output);
