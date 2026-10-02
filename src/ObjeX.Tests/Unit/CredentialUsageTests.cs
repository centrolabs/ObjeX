using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class CredentialUsageTests
{
    static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ANewCredential_MayStayUnusedForAWeek()
    {
        Assert.Null(CredentialUsage.Warning(Now.AddDays(-6), null, Now));
        Assert.Equal("Never used", CredentialUsage.Warning(Now.AddDays(-7), null, Now));
    }

    [Fact]
    public void ACredentialUsedWithin90Days_IsInUse()
    {
        Assert.Null(CredentialUsage.Warning(Now.AddDays(-400), Now.AddDays(-89), Now));
        Assert.Equal("Unused 90+ days", CredentialUsage.Warning(Now.AddDays(-400), Now.AddDays(-90), Now));
    }

    // SQLite hands timestamps back with Kind Unspecified; they are UTC all the same.
    [Fact]
    public void AnUnspecifiedKind_IsReadAsUtc() =>
        Assert.Equal("Never used", CredentialUsage.Warning(DateTime.SpecifyKind(Now.AddDays(-8), DateTimeKind.Unspecified), null, Now));
}
