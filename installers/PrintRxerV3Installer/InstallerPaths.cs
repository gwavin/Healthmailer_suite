namespace PrintRxerV3Installer;

internal static class InstallerPaths
{
    public const string DefaultHandoffRoot = @"C:\ProgramData\printRxer\handoff";
    public const string ProgramFilesRoot = @"C:\Program Files\printRxer";
    public const string ProgramDataRoot = @"C:\ProgramData\printRxer";
    public const string ConfigPath = @"C:\ProgramData\printRxer\config\printRxer.settings.json";
    public const string TaskName = "printRxer";

    public static string BundleRoot => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PayloadPublishRoot => Path.Combine(PayloadRoot, "publish", "printRxer");
    public static string PayloadAssetsRoot => Path.Combine(PayloadRoot, "assets");
    public static string PayloadToolsRoot => Path.Combine(PayloadRoot, "tools");
    public static string InstalledExePath => Path.Combine(ProgramFilesRoot, "printRxer.exe");
}
