namespace PrintRxerSuiteInstaller;

internal static class SuitePaths
{
    public const string PrintRxerProgramDataRoot = @"C:\ProgramData\printRxer";
    public const string HealthMailerProgramDataRoot = @"C:\ProgramData\HealthMailer";
    public const string PrintRxerLogsRoot = @"C:\ProgramData\printRxer\logs";
    public const string HealthMailerLogsRoot = @"C:\ProgramData\HealthMailer\logs";

    public static string BundleRoot => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PrintRxerSetupPath => Path.Combine(PayloadRoot, "installers", "printRxer", "printRxerSetup.exe");
    public static string HealthMailerSetupPath => Path.Combine(PayloadRoot, "installers", "HealthMailer", "HealthMailerSetup.exe");
    public static string ToolsRoot => Path.Combine(PayloadRoot, "tools");
    public static string ValidationScriptPath => Path.Combine(ToolsRoot, "Test-PrintRxerSuiteHealth.ps1");
    public static string SupportBundleScriptPath => Path.Combine(ToolsRoot, "New-PrintRxerSupportBundle.ps1");
    public static string CaptureInstallScriptPath => Path.Combine(ToolsRoot, "Install-PrintRxerCapturePrinter.ps1");
    public static string SupportOutputRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "printRxer-support-bundles");
}
