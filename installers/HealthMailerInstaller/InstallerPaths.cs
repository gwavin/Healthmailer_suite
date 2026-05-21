namespace HealthMailerInstaller;

internal static class InstallerPaths
{
    public const string DefaultHandoffRoot = @"C:\ProgramData\printRxer\handoff";
    public const string ProgramFilesRoot = @"C:\Program Files\HealthMailer";
    public const string ProgramDataRoot = @"C:\ProgramData\HealthMailer";
    public const string ConfigPath = @"C:\ProgramData\HealthMailer\healthmailer.settings.json";
    public const string TaskName = "HealthMailer";

    public static string BundleRoot => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PayloadPublishRoot => Path.Combine(PayloadRoot, "publish", "HealthMailer");
    public static string InstalledExePath => Path.Combine(ProgramFilesRoot, "HealthMailer.exe");
}
