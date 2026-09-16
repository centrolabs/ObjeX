using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ObjeX.Api.Options;
using ObjeX.Core.Interfaces;

namespace ObjeX.Tests.Integration;

public class StorageSpaceServiceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public void Get_ReportsTheVolumeHoldingTheBlobRoot()
    {
        using var scope = factory.CreateScope();

        var status = scope.ServiceProvider.GetRequiredService<IStorageSpaceService>().Get();

        Assert.True(status.FreeBytes > 0);
        Assert.True(status.TotalBytes >= status.FreeBytes);
        Assert.Equal(status.TotalBytes - status.FreeBytes, status.UsedBytes);
    }

    [Fact]
    public void Get_CarriesTheConfiguredMinimum()
    {
        using var scope = factory.CreateScope();
        var configured = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.MinimumFreeDiskBytes;

        var status = scope.ServiceProvider.GetRequiredService<IStorageSpaceService>().Get();

        Assert.Equal(configured, status.MinimumFreeBytes);
    }
}
