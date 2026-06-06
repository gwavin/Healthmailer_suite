using System.Diagnostics;
using System.Text;

namespace HealthMailerInstaller;

internal static class ProcessRunner
{
    public readonly record struct ProcessResult(int ExitCode, string Output);

    public static string Run(string fileName, string arguments, bool requireSuccess = true)
    {
        ProcessResult result = RunForResult(fileName, arguments);
        if (requireSuccess && result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.Output)
                ? "No further detail was reported."
                : result.Output;
            string tool = Path.GetFileName(fileName);
            throw new InvalidOperationException($"{tool} could not complete this setup step. Windows returned exit code {result.ExitCode}.\n\n{detail}");
        }

        return result.Output;
    }

    public static ProcessResult RunForResult(string fileName, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = InstallerPaths.BundleRoot
            }
        };

        StringBuilder output = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        string text = output.ToString().Trim();
        return new ProcessResult(process.ExitCode, text);
    }

    public static string PowerShell(string command, bool requireSuccess = true)
    {
        string escaped = command.Replace("\"", "\\\"", StringComparison.Ordinal);
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + escaped + "\"", requireSuccess);
    }
}
