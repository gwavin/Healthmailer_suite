using HealthMailer;

namespace HealthMailer.Tests;

public sealed class HealthMailerLogTests
{
    [Test]
    public void Write_rotates_active_log_and_caps_retained_files()
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-log-" + Guid.NewGuid().ToString("N"));
        HealthMailerLog log = new(root, new LoggingOptions
        {
            MaxLogBytes = 120,
            MaxLogFiles = 2
        });

        for (int index = 0; index < 20; index++)
        {
            log.Write("rotation test line " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.True(File.Exists(Path.Combine(root, "healthmailer.log")));
        Assert.True(File.Exists(Path.Combine(root, "healthmailer.1.log")));
        Assert.True(File.Exists(Path.Combine(root, "healthmailer.2.log")));
        Assert.False(File.Exists(Path.Combine(root, "healthmailer.3.log")));
    }
}
