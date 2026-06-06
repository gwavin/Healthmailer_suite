
namespace HealthMailer.Tests;

public sealed class HealthMailerArgumentValidationTests
{

    [TestCase("--install", "--validate")]
    [TestCase("--process-once", "--status")]
    [TestCase("--watch", "--status")]
    public void ValidateArguments_rejects_conflicting_primary_modes(string first, string second)
    {
        string? error = Program.ValidateArguments([first, second]);

        Assert.NotNull(error);
        Assert.Contains("Only one primary mode", error, StringComparison.OrdinalIgnoreCase);
    }


    [TestCase("--config")]
    [TestCase("--config", "--status")]
    public void ValidateArguments_rejects_missing_config_value(params string[] args)
    {
        string? error = Program.ValidateArguments(args);

        Assert.NotNull(error);
        Assert.Contains("--config requires a value", error, StringComparison.OrdinalIgnoreCase);
    }


    [TestCase("--help")]
    [TestCase("--validate")]
    [TestCase("--status")]
    [TestCase("--process-once", "--config", "C:\\ProgramData\\HealthMailer\\healthmailer.settings.json")]
    [TestCase("--watch")]
    public void ValidateArguments_allows_existing_valid_modes(params string[] args)
    {
        Assert.Null(Program.ValidateArguments(args));
    }
}
