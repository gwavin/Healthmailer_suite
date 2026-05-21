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
            string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "PrintRxerV3Uninstall", StringComparison.OrdinalIgnoreCase);
        bool removeData = args.Any(arg => string.Equals(arg, "--remove-data", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Any(arg => string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase));

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                "PrintRxerV3Installer.exe installs PrintRxerV3.\nPrintRxerV3Uninstall.exe removes PrintRxerV3.\n\nRun as administrator for printer installation/removal.",
                "PrintRxerV3 installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            if (uninstall && (quiet || removeData))
            {
                if (PrintRxerUninstaller.IsInstalled() || (removeData && PrintRxerUninstaller.HasLocalData()))
                {
                    PrintRxerUninstaller.Uninstall(removeData, _ => { });
                }

                if (!quiet)
                {
                    MessageBox.Show("PrintRxerV3 uninstall completed.", "PrintRxerV3 uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(ex.Message, "PrintRxerV3 installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
