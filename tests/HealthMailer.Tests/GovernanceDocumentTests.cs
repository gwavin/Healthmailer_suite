using Xunit;

namespace HealthMailer.Tests;

public sealed class GovernanceDocumentTests
{
    [Fact]
    public void Support_bundle_script_readme_warns_that_non_pdf_evidence_may_contain_phi()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "New-PrintRxerSupportBundle.ps1"));

        Assert.Contains("patient-identifiable information", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HSE-controlled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approved support/governance channels", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_release_note_contains_no_personal_local_testing_path()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "healthmailer_release_doc_cleaned.html"));

        Assert.DoesNotContain(@"C:\Users\gavin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"Documents\Testing", text, StringComparison.OrdinalIgnoreCase);
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
