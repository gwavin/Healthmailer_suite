namespace HealthMailerInstaller;

internal sealed record InstallOptions(string HandoffRoot, bool SendMail = false);
