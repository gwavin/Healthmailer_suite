using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;

namespace PrintRxerSuiteInstaller;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSmokeTest();
        }

        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new SuiteInstallerForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "printRxer suite installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunSmokeTest()
    {
        IReadOnlyList<BundlePreflightResult> results = BundlePreflight.Check(SuitePaths.BundleRoot);
        foreach (BundlePreflightResult result in results)
        {
            Console.WriteLine((result.Exists ? "OK   " : "MISS ") + result.RelativePath);
        }

        int exitCode = results.All(result => result.Exists) ? 0 : 2;
        WriteSmokeTestLog(results, exitCode);
        return exitCode;
    }

    private static void WriteSmokeTestLog(IReadOnlyList<BundlePreflightResult> results, int exitCode)
    {
        StringBuilder builder = new();
        builder.AppendLine("Timestamp: " + DateTimeOffset.Now.ToString("O"));
        builder.AppendLine("Bundle root: " + SuitePaths.BundleRoot);
        builder.AppendLine();

        foreach (BundlePreflightResult result in results)
        {
            builder.AppendLine((result.Exists ? "OK   " : "MISS ") + result.RelativePath);
        }

        builder.AppendLine();
        builder.AppendLine(exitCode == 0
            ? "PASS: release bundle layout looks valid."
            : "FAIL: release bundle layout is incomplete.");
        builder.AppendLine("Exit code: " + exitCode);

        string content = builder.ToString();
        string primaryPath = Path.Combine(SuitePaths.BundleRoot, "PrintRxerSuiteInstaller.smoke-test.log");
        if (TryWriteSmokeTestLog(primaryPath, content))
        {
            return;
        }

        try
        {
            string fallbackRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "printRxer",
                "logs");
            Directory.CreateDirectory(fallbackRoot);
            TryWriteSmokeTestLog(Path.Combine(fallbackRoot, "PrintRxerSuiteInstaller.smoke-test.log"), content);
        }
        catch
        {
            // Smoke-test logging must never mask the bundle validation exit code.
        }
    }

    private static bool TryWriteSmokeTestLog(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
