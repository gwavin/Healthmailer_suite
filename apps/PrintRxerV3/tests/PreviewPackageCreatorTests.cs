using PrintRxerV3.App;
using System.Runtime.Versioning;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class PreviewPackageCreatorTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void CreateSamplePackage_writes_handoff_package_under_requested_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-preview-" + Guid.NewGuid().ToString("N"));

        string packageDirectory = PreviewPackageCreator.CreateSamplePackage(root);

        Assert.True(Directory.Exists(packageDirectory));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "request.json")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "prescription.pdf")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "request.sha256")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "summary.txt")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "READY")));
    }
}
