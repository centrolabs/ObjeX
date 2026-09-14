using Prometheus;

namespace ObjeX.Api.Metrics;

public static class ObjeXMetrics
{
    private static readonly Gauge StorageBytes = Prometheus.Metrics
        .CreateGauge("objex_storage_bytes", "Total stored bytes per bucket.", "bucket");

    private static readonly Gauge ObjectsTotal = Prometheus.Metrics
        .CreateGauge("objex_objects_total", "Total object count per bucket.", "bucket");

    /// <summary>Sets the per-bucket gauges and drops the series of buckets that no longer exist.</summary>
    public static void SyncBuckets(IEnumerable<(string Name, long TotalSize, long ObjectCount)> buckets)
    {
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, totalSize, objectCount) in buckets)
        {
            StorageBytes.WithLabels(name).Set(totalSize);
            ObjectsTotal.WithLabels(name).Set(objectCount);
            current.Add(name);
        }

        foreach (var labels in StorageBytes.GetAllLabelValues())
        {
            if (current.Contains(labels[0])) continue;
            StorageBytes.RemoveLabelled(labels);
            ObjectsTotal.RemoveLabelled(labels);
        }
    }
}
