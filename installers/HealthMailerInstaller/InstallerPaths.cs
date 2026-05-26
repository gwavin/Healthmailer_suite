namespace HealthMailerInstaller;

internal static class InstallerPaths
{
    public const string DefaultHandoffRoot = @"C:\ProgramData\printRxer\handoff";
    public const string ProgramFilesRoot = @"C:\ProgramData\HealthMailer\app";
    public const string LegacyProgramFilesRoot = @"C:\Program Files\HealthMailer";
    public const string ProgramDataRoot = @"C:\ProgramData\HealthMailer";
    public const string ConfigPath = @"C:\ProgramData\HealthMailer\healthmailer.settings.json";
    public const string TaskName = "HealthMailer";

    public static string BundleRoot => ResolveBundleRoot();
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PayloadPublishRoot => Path.Combine(PayloadRoot, "publish", "HealthMailer");
    public static string InstalledExePath => Path.Combine(ProgramFilesRoot, "HealthMailer.exe");

    private static string ResolveBundleRoot()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(baseDirectory, "payload")))
        {
            return baseDirectory;
        }

        DirectoryInfo? directory = new(baseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "payload", "publish", "HealthMailer")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return baseDirectory;
    }
}
