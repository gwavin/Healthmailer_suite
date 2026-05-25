using System.Reflection;
using Xunit;

namespace HealthMailer.Tests;

public sealed class MailHandoffTests
{
    [Fact]
    public void ResolveRecipients_throws_when_outlook_resolve_all_returns_false()
    {
        MethodInfo method = typeof(OutlookMailHandoff).GetMethod("ResolveRecipients", BindingFlags.NonPublic | BindingFlags.Static)!;

        TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [new FakeMailItem(false)]));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("could not resolve", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRecipients_allows_true_resolve_all_result()
    {
        MethodInfo method = typeof(OutlookMailHandoff).GetMethod("ResolveRecipients", BindingFlags.NonPublic | BindingFlags.Static)!;

        method.Invoke(null, [new FakeMailItem(true)]);
    }

    public sealed class FakeMailItem(bool resolveResult)
    {
        public FakeRecipients Recipients { get; } = new(resolveResult);
    }

    public sealed class FakeRecipients(bool resolveResult)
    {
        public bool ResolveAll() => resolveResult;
    }
}
