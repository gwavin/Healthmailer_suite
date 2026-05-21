using System.Drawing;
using System.Runtime.InteropServices;

namespace HealthMailerInstaller;

internal static class InstallerBranding
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon? TryCreateIcon()
    {
        string iconPath = Path.Combine(InstallerPaths.PayloadRoot, "assets", "branding", "mncms.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch
            {
            }
        }

        string imagePath = Path.Combine(InstallerPaths.PayloadRoot, "assets", "branding", "mncms_400x400.jpg");
        if (!File.Exists(imagePath))
        {
            return null;
        }
        try
        {
            using Bitmap source = new(imagePath);
            using Bitmap resized = new(source, new Size(32, 32));
            IntPtr handle = resized.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }
}
