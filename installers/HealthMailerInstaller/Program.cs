using System.Runtime.Versioning;
using System.Windows.Forms;

namespace HealthMailerInstaller;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool uninstall = args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase));
        bool removeData = args.Any(arg => string.Equals(arg, "--remove-data", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Any(arg => string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase));
        bool smokeTest = args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase));
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
                if (HealthMailerUninstaller.IsInstalled())
                {
                    HealthMailerUninstaller.Uninstall(removeData, _ => { });
                }

                if (!quiet)
                {
                    MessageBox.Show("HealthMailer uninstall completed.", "HealthMailer uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return 0;
            }

            Application.Run(uninstall ? new UninstallForm() : new InstallForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "HealthMailer setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
