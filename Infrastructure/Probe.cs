using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;

namespace QueryDemos.Infrastructure;

public sealed record Measured<T>(T Result, int Commands, TimeSpan Elapsed, long AllocatedBytes)
{
    public string Allocated => $"{AllocatedBytes / 1024d / 1024d:0.0} MB";
}

public static class Probe
{
    /// <summary>
    /// Runs the action once silently (to warm up query compilation and the connection pool),
    /// then runs it again and measures commands sent, elapsed time and memory allocated.
    /// The first <paramref name="echoFirst"/> SQL commands of the measured run are printed.
    /// </summary>
    public static async Task<Measured<T>> RunAsync<T>(Func<Task<T>> action, int echoFirst = 3)
    {
        var counter = CommandCounter.Instance;

        var echo = counter.Echo;
        counter.Echo = false;
        try { await action(); }
        finally { counter.Echo = echo; }

        var maxEcho = counter.MaxEcho;
        counter.MaxEcho = echoFirst;
        counter.Reset();
        try
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            var sw = Stopwatch.StartNew();
            var result = await action();
            sw.Stop();
            long after = GC.GetTotalAllocatedBytes(precise: true);
            return new Measured<T>(result, counter.Count, sw.Elapsed, after - before);
        }
        finally
        {
            counter.MaxEcho = maxEcho;
        }
    }
}

public static class DemoClock
{
    /// <summary>
    /// "Now" as far as the demo data is concerned: just after the newest reading. Using this instead
    /// of DateTime.UtcNow keeps the "last 24 hours" windows working even if setup ran days ago.
    /// </summary>
    public static async Task<DateTime> NowAsync(WarehouseDb db)
    {
        using var _ = CommandCounter.Instance.Quiet();
        var newest = await db.Readings.MaxAsync(r => (DateTime?)r.RecordedAtUtc);
        return (newest ?? DateTime.UtcNow).AddSeconds(1);
    }
}
