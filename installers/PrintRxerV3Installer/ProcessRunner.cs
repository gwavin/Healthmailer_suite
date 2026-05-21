using System.Diagnostics;
using System.Text;

namespace PrintRxerV3Installer;

internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments, bool requireSuccess = true)
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
        if (requireSuccess && process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(text)
                ? "No further detail was reported."
                : text;
            string tool = Path.GetFileName(fileName);
            throw new InvalidOperationException($"{tool} could not complete this setup step. Windows returned exit code {process.ExitCode}.\n\n{detail}");
        }

        return text;
    }

    public static string PowerShell(string command, bool requireSuccess = true)
    {
        string escaped = command.Replace("\"", "\\\"", StringComparison.Ordinal);
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + escaped + "\"", requireSuccess);
    }

    public static string PowerShellFile(string scriptPath, string arguments = "", bool requireSuccess = true)
    {
        return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + arguments, requireSuccess);
    }
}
