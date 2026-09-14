using System.Text;
using ObjeX.Api.Metrics;

namespace ObjeX.Tests.Unit;

public class ObjeXMetricsTests
{
    private static async Task<string> ExportAsync()
    {
        using var stream = new MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task SyncBuckets_RemovesSeriesOfBucketsThatNoLongerExist()
    {
        ObjeXMetrics.SyncBuckets([("metrics-keep", 10, 1), ("metrics-gone", 20, 2)]);
        Assert.Contains("bucket=\"metrics-gone\"", await ExportAsync());

        ObjeXMetrics.SyncBuckets([("metrics-keep", 11, 1)]);

        var text = await ExportAsync();
        Assert.Contains("objex_storage_bytes{bucket=\"metrics-keep\"} 11", text);
        Assert.DoesNotContain("bucket=\"metrics-gone\"", text);
    }
}
