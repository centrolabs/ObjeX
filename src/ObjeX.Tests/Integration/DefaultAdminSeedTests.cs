using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Models;

namespace ObjeX.Tests.Integration;

/// <summary>
/// admin/admin stays valid forever unless the seed forces a change. An operator who configured a
/// real DefaultAdmin:Password is not forced.
/// </summary>
public class DefaultAdminSeedTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private static async Task<User> AdminAsync(ObjeXFactory f)
    {
        using var scope = f.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByNameAsync("admin");
        Assert.NotNull(admin);
        return admin;
    }

    [Fact]
    public async Task BuiltInPassword_ForcesAPasswordChange()
    {
        var admin = await AdminAsync(factory);

        Assert.True(admin.MustChangePassword);
        Assert.Null(admin.TemporaryPasswordExpiresAt); // a fresh install must not lock itself out
    }

    [Fact]
    public async Task ConfiguredPassword_DoesNotForceAPasswordChange()
    {
        await using var configured = new ConfiguredAdminFactory();

        var admin = await AdminAsync(configured);

        Assert.False(admin.MustChangePassword);
    }
}

/// <summary>Its own temp directory and database, so the seed runs against an empty user table.</summary>
file class ConfiguredAdminFactory : ObjeXFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("DefaultAdmin:Password", "configured-by-the-operator");
    }
}
