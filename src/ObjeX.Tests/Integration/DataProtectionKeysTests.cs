using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Login cookies and antiforgery tokens are protected with the data protection keys. The keys live in the
/// database, so a restart or a new container keeps every session valid.
/// </summary>
public class DataProtectionKeysTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task ProtectedPayload_SurvivesARestart()
    {
        const string purpose = "ObjeX.Tests.Restart";
        var token = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Protect("session");

        // A developer machine has a user profile that would hold the keys too; the container has none.
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync())
            Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync());

        using var restarted = factory.Restart();
        var restored = restarted.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Unprotect(token);

        Assert.Equal("session", restored);
    }
}
