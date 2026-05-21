using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerV3Installer;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool uninstall = args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "printRxerUninstall", StringComparison.OrdinalIgnoreCase);
        bool removeData = args.Any(arg => string.Equals(arg, "--remove-data", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Any(arg => string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase));
        bool smokeTest = args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase));

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                "printRxerInstaller.exe installs printRxer.\nprintRxerUninstall.exe removes printRxer.\n\nRun as administrator for printer installation/removal.",
                "printRxer installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            if (smokeTest)
            {
                if (!Directory.Exists(InstallerPaths.PayloadPublishRoot))
                {
                    Console.WriteLine("MISS " + InstallerPaths.PayloadPublishRoot);
                    return 2;
                }

                Console.WriteLine("OK   " + InstallerPaths.PayloadPublishRoot);
                return 0;
            }

            if (uninstall && (quiet || removeData))
            {
                if (PrintRxerUninstaller.IsInstalled() || (removeData && PrintRxerUninstaller.HasLocalData()))
                {
                    PrintRxerUninstaller.Uninstall(removeData, _ => { });
                }

                if (!quiet)
                {
                    MessageBox.Show("printRxer uninstall completed.", "printRxer uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return 0;
            }

            if (uninstall)
            {
                Application.Run(new UninstallForm());
            }
            else if (quiet)
            {
                string handoffRoot = GetOption(args, "--handoff-root") ?? InstallerPaths.DefaultHandoffRoot;
                PrintRxerInstaller.Install(new InstallOptions(handoffRoot), _ => { });
            }
            else
            {
                Application.Run(new InstallForm());
            }

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "printRxer installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

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
}
