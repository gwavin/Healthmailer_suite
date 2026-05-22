using Xunit;

namespace PrintRxerV3.Tests;

public sealed class InstallerRecipientSourceTests
{
    [Fact]
    public void Installer_seeds_derived_central_recipients_without_overwriting_existing_file()
    {
        string source = ReadRepositoryFile("installers", "PrintRxerV3Installer", "PrintRxerInstaller.cs");

        Assert.Contains("Path.Combine(handoffRoot, \"recipients\")", source, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(centralFolder, \"recipients.csv\")", source, StringComparison.Ordinal);
        Assert.Contains("if (!File.Exists(centralFile))", source, StringComparison.Ordinal);
        Assert.Contains("RecipientCentralAlreadyExists", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy(bundled, centralFile, overwrite: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_always_installs_local_bundled_fallback_and_can_populate_cache()
    {
        string source = ReadRepositoryFile("installers", "PrintRxerV3Installer", "PrintRxerInstaller.cs");

        Assert.Contains("\"bundled-recipients.csv\"", source, StringComparison.Ordinal);
        Assert.Contains("\"recipients.cache.csv\"", source, StringComparison.Ordinal);
        Assert.Contains("RecipientCsvValidator.LoadValidated(centralFile)", source, StringComparison.Ordinal);
        Assert.Contains("TryHardenFile(cacheDestination, FileSystemRights.Modify)", source, StringComparison.Ordinal);
        Assert.Contains("TryHardenFile(Path.Combine(recipientRoot, \"recipients.cache.csv\"), FileSystemRights.Modify)", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintRxerSuite.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray()));
    }
}
