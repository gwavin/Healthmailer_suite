using Xunit;

namespace HealthMailer.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Validate_dry_run_without_send_does_not_require_outlook()
    {
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "handoff"),
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "local"),
            SendMail = false
        };

        Program.ValidateConfiguration(config, validateOutlook: static () => throw new InvalidOperationException("Outlook should not be checked"));
    }

    [Fact]
    public void EnsureDirectories_does_not_create_unc_handoff_root()
    {
        HealthMailerConfig config = new()
        {
            HandoffRoot = @"\\server\HealthMailerDrop$\incoming",
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-unc-" + Guid.NewGuid().ToString("N")),
            SendMail = false
        };

        config.EnsureDirectories();

        Assert.True(Directory.Exists(config.LocalRoot));
    }
}
