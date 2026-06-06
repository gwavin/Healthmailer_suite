namespace PrintRxerV3Installer;

internal static class InstallerPaths
{
    public const string DefaultHandoffRoot = @"C:\ProgramData\printRxer\handoff";
    public const string ProgramFilesRoot = @"C:\Program Files\printRxer";
    public const string ProgramDataRoot = @"C:\ProgramData\printRxer";
    public const string ConfigPath = @"C:\ProgramData\printRxer\config\printRxer.settings.json";
    public const string TaskName = "printRxer";

    public static string BundleRoot => ResolveBundleRoot();
    public static string PayloadRoot => Path.Combine(BundleRoot, "payload");
    public static string PayloadPublishRoot => Path.Combine(PayloadRoot, "publish", "printRxer");
    public static string PayloadAssetsRoot => Path.Combine(PayloadRoot, "assets");
    public static string PayloadToolsRoot => Path.Combine(PayloadRoot, "tools");
    public static string InstalledExePath => Path.Combine(ProgramFilesRoot, "printRxer.exe");

    public static void ValidateSecurityBoundaries()
    {
        ValidateSecurityBoundaries(ProgramFilesRoot, ProgramDataRoot, InstalledExePath);
    }

    internal static void ValidateSecurityBoundaries(string programFilesRoot, string programDataRoot, string installedExePath)
    {
        string resolvedProgramFiles = NormalizeDirectory(programFilesRoot);
        string resolvedProgramData = NormalizeDirectory(programDataRoot);
        string resolvedInstalledExe = Path.GetFullPath(installedExePath);
        string nativeProgramFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string x86ProgramFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? x86ProgramFiles = string.IsNullOrWhiteSpace(x86ProgramFilesPath) ? null : NormalizeDirectory(x86ProgramFilesPath);
        string systemProgramData = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        if (!IsUnderOrEqual(resolvedProgramFiles, nativeProgramFiles) &&
            (x86ProgramFiles is null || !IsUnderOrEqual(resolvedProgramFiles, x86ProgramFiles)))
        {
            throw new FatalSecurityException($"Invalid printRxer execution root '{resolvedProgramFiles}'. It must resolve under Program Files.");
        }

        if (!IsUnderOrEqual(resolvedProgramData, systemProgramData))
        {
            throw new FatalSecurityException($"Invalid printRxer data root '{resolvedProgramData}'. It must resolve under ProgramData.");
        }

        if (string.Equals(resolvedProgramFiles, resolvedProgramData, StringComparison.OrdinalIgnoreCase) ||
            IsStrictlyUnder(resolvedProgramFiles, resolvedProgramData) ||
            IsStrictlyUnder(resolvedProgramData, resolvedProgramFiles))
        {
            throw new FatalSecurityException($"Invalid printRxer path boundary. Execution root '{resolvedProgramFiles}' and data root '{resolvedProgramData}' must be separate and non-nested.");
        }

        if (!IsStrictlyUnder(resolvedInstalledExe, resolvedProgramFiles))
        {
            throw new FatalSecurityException($"Invalid installed executable path '{resolvedInstalledExe}'. It must resolve under '{resolvedProgramFiles}'.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnderOrEqual(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) || IsStrictlyUnder(candidate, root);

    private static bool IsStrictlyUnder(string candidate, string root) =>
        candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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
            if (Directory.Exists(Path.Combine(directory.FullName, "payload", "publish", "printRxer")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return baseDirectory;
    }
}
