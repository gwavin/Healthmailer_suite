using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerSuiteInstaller;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows")]
    private static int Main()
    {
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
}
