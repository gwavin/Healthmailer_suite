namespace PrintRxerV3Installer;

internal static class InstallerPaths
{
    public const string DefaultHandoffRoot = @"C:\ProgramData\printrxer_v3\handoff";
    public const string ProgramFilesRoot = @"C:\Program Files\PrintRxerV3";
    public const string ProgramDataRoot = @"C:\ProgramData\printrxer_v3";
    public const string ConfigPath = @"C:\ProgramData\printrxer_v3\config\printrxer_v3.settings.json";
    public const string TaskName = "PrintRxerV3";

    public static string BundleRoot => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PayloadPublishRoot => Path.Combine(PayloadRoot, "publish", "PrintRxerV3");
    public static string PayloadAssetsRoot => Path.Combine(PayloadRoot, "assets");
    public static string PayloadToolsRoot => Path.Combine(PayloadRoot, "tools");
    public static string InstalledExePath => Path.Combine(ProgramFilesRoot, "printrxer_v3.exe");
}
