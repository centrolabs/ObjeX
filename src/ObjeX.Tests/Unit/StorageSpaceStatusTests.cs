using ObjeX.Core.Interfaces;

namespace ObjeX.Tests.Unit;

public class StorageSpaceStatusTests
{
    private const long Min = 500L * 1024 * 1024;
    private const long Total = 100L * 1024 * 1024 * 1024;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(Min - 1)]
    [InlineData(Min)]
    public void AtOrBelowMinimum_IsBelowMinimum_AndNotNear(long free)
    {
        var status = new StorageSpaceStatus(free, Total, Min);

        Assert.True(status.IsBelowMinimum);
        Assert.False(status.IsNearMinimum);
    }

    [Theory]
    [InlineData(Min + 1)]
    [InlineData(Min * 2)]
    public void AboveMinimum_UpToTwiceTheMinimum_IsNearMinimum(long free)
    {
        var status = new StorageSpaceStatus(free, Total, Min);

        Assert.False(status.IsBelowMinimum);
        Assert.True(status.IsNearMinimum);
    }

    [Theory]
    [InlineData(Min * 2 + 1)]
    [InlineData(Total)]
    public void AboveTwiceTheMinimum_IsNeitherBelowNorNear(long free)
    {
        var status = new StorageSpaceStatus(free, Total, Min);

        Assert.False(status.IsBelowMinimum);
        Assert.False(status.IsNearMinimum);
    }

    [Fact]
    public void MinimumOfZero_NeverWarns_WhileAnySpaceIsLeft()
    {
        var status = new StorageSpaceStatus(1, Total, 0);

        Assert.False(status.IsBelowMinimum);
        Assert.False(status.IsNearMinimum);
    }

    [Fact]
    public void FullDisk_WithMinimumOfZero_IsBelowMinimum()
    {
        var status = new StorageSpaceStatus(0, Total, 0);

        Assert.True(status.IsBelowMinimum);
        Assert.False(status.IsNearMinimum);
    }

    [Fact]
    public void Used_IsTheRemainderOfTotal()
    {
        var status = new StorageSpaceStatus(Total / 4, Total, Min);

        Assert.Equal(Total / 4 * 3, status.UsedBytes);
        Assert.Equal(75, status.UsedPercent);
    }

    [Fact]
    public void UnknownTotal_ReportsZeroPercent_InsteadOfDividingByZero()
    {
        var status = new StorageSpaceStatus(0, 0, Min);

        Assert.Equal(0, status.UsedPercent);
    }
}
