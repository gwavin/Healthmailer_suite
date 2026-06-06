
namespace HealthMailer.Tests;

public sealed class GovernanceDocumentTests
{
    [Test]
    public void Support_bundle_script_readme_warns_that_non_pdf_evidence_may_contain_phi()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "New-PrintRxerSupportBundle.ps1"));

        Assert.Contains("patient-identifiable information", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HSE-controlled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approved HSE support/governance channels", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void External_release_note_contains_no_personal_local_testing_path()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "healthmailer_release_doc_cleaned.html"));

        Assert.DoesNotContain(@"C:\Users\gavin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"Documents\Testing", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void HealthMailer_gui_installer_makes_live_sending_choice_explicit()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "installers", "HealthMailerInstaller", "InstallForm.cs"));

        Assert.Contains("Enable live Outlook sending", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Successful-send prescription retention", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new InstallOptions(_selectedHandoffRoot, _sendMailCheckBox.Checked, SelectedRetentionDays())", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void HealthMailer_quiet_installer_requires_explicit_send_mail_argument()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "installers", "HealthMailerInstaller", "Program.cs"));

        Assert.Contains("Missing required argument: --send-mail true|false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--sent-prescription-retention-days <days>", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("value = true;" + Environment.NewLine + "            error = null;" + Environment.NewLine + "            return true;", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Support_bundle_script_copies_actual_healthmailer_config_and_hse_transfer_warning()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "New-PrintRxerSupportBundle.ps1"));

        Assert.Contains(@"C:\ProgramData\HealthMailer\healthmailer.settings.json", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"HealthMailer\healthmailer.settings.json", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not email or transfer this bundle except through approved HSE support/governance channels.", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Release_bundle_script_writes_metadata_and_latest_artifact_manifest()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "New-PrintRxerSuiteReleaseBundle.ps1"));

        Assert.Contains("BUILD-METADATA.txt", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LATEST_RELEASE_ARTIFACTS.txt", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CleanOutputRoot", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-FileHash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Outlook_resolve_all_false_fails_before_send()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "HealthMailer", "MailHandoff.cs"));

        Assert.Contains("Outlook could not resolve all recipients.", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolveAll", text, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Current_release_guidance_marks_chart_copy_removed_or_deferred()
    {
        string configuration = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "CONFIGURATION.md"));
        string checklist = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "RELEASE-CHECKLIST.md"));

        Assert.Contains("removed/deferred", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ChartCopy.Enabled", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Chart-copy failure after mail", checklist, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "PrintRxerSuite.slnx")))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
