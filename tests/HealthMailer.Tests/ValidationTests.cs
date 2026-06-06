
namespace HealthMailer.Tests;

public sealed class ValidationTests
{
    [Test]
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

    [Test]
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

    [Test]
    public void Validate_rejects_live_sending_without_explicit_approval()
    {
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "handoff"),
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "local"),
            SendMail = true,
            LiveSendingApproved = false
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Program.ValidateConfiguration(config, static () => "ok"));

        Assert.Contains("live sending", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Validate_rejects_live_sending_without_installer_created_marker()
    {
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "handoff"),
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "local"),
            SendMail = true,
            ConfigCreatedByInstaller = false,
            LiveSendingApproved = true
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Program.ValidateConfiguration(config, static () => "ok"));

        Assert.Contains("installer-created", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Validate_allows_installer_created_dry_run_without_outlook_check()
    {
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "handoff"),
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "local"),
            SendMail = false,
            ConfigCreatedByInstaller = true,
            LiveSendingApproved = false
        };

        Program.ValidateConfiguration(config, static () => throw new InvalidOperationException("Outlook should not be checked"));
    }

    [Test]
    public void Validate_allows_installer_created_live_send_after_outlook_check()
    {
        bool checkedOutlook = false;
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "handoff"),
            LocalRoot = Path.Combine(Path.GetTempPath(), "healthmailer-validate-" + Guid.NewGuid().ToString("N"), "local"),
            SendMail = true,
            ConfigCreatedByInstaller = true,
            LiveSendingApproved = true
        };

        Program.ValidateConfiguration(config, () =>
        {
            checkedOutlook = true;
            return "ok";
        });

        Assert.True(checkedOutlook);
    }

    [Test]
    public void Load_missing_config_creates_safe_dry_run_config()
    {
        string configPath = Path.Combine(Path.GetTempPath(), "healthmailer-missing-config-" + Guid.NewGuid().ToString("N"), "healthmailer.settings.json");

        HealthMailerConfig config = HealthMailerConfig.Load(configPath);

        Assert.False(config.SendMail);
        Assert.False(config.LiveSendingApproved);
        Assert.Equal(14, config.SentPrescriptionRetentionDays);
        Assert.True(File.Exists(configPath));
    }
}
