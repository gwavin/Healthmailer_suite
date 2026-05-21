using System.Runtime.Versioning;
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

        return results.All(result => result.Exists) ? 0 : 2;
    }
}
