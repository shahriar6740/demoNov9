using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slides 6–7 — GroupBy + aggregates into a reporting DTO, versus over-fetching.</summary>
public static class Demo03_ZoneReport
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 3 · Zone report: grouping, aggregates and DTO projections", "Slides 6–7");
        await using var db = Db.Create();

        var now = await DemoClock.NowAsync(db);
        var since = now.AddHours(-24);

        Out.Part("A. Group and aggregate in the database, project into ZoneReportDto");
        var inDatabase = await Probe.RunAsync(() => db.Readings
            .Where(r => r.RecordedAtUtc >= since)
            .GroupBy(r => r.Gateway.InstallationZone)
            .Where(z => z.Count() >= 10)                                       // becomes HAVING
            .OrderByDescending(z => z.Average(r => r.TemperatureCelsius))      // order before projecting
            .Select(z => new ZoneReportDto(
                z.Key,
                z.Count(),
                z.Average(r => r.TemperatureCelsius),
                z.Max(r => r.TemperatureCelsius),
                z.Average(r => r.HumidityPercent)))
            .ToListAsync());
        Out.Rows(inDatabase.Result);

        Out.Part("B. Over-fetching: load every reading (with its gateway), then group in C#");
        var inMemory = await Probe.RunAsync(async () =>
        {
            var everything = await db.Readings.AsNoTracking()
                .Include(r => r.Gateway)
                .Where(r => r.RecordedAtUtc >= since)
                .ToListAsync();

            var report = everything
                .GroupBy(r => r.Gateway.InstallationZone)
                .Where(z => z.Count() >= 10)
                .OrderByDescending(z => z.Average(r => r.TemperatureCelsius))
                .Select(z => new ZoneReportDto(
                    z.Key,
                    z.Count(),
                    z.Average(r => r.TemperatureCelsius),
                    z.Max(r => r.TemperatureCelsius),
                    z.Average(r => r.HumidityPercent)))
                .ToList();

            return (Pulled: everything.Count, Report: report);
        }, echoFirst: 1);

        Console.WriteLine();
        Out.Table(
            ["Variant", "Rows from SQL Server", "Elapsed", "Memory allocated"],
            [
                ["GROUP BY in the database", Out.Format(inDatabase.Result.Count), Out.Format(inDatabase.Elapsed), inDatabase.Allocated],
                ["Load everything, group in C#", Out.Format(inMemory.Result.Pulled), Out.Format(inMemory.Elapsed), inMemory.Allocated],
            ]);
        Out.Note("Same report. The database version sends one small row per zone across the network.");

        Out.Part("C. A DTO per device — only the columns the screen needs");
        var statuses = await db.Gateways
            .Where(g => g.IsOnline)
            .OrderBy(g => g.DeviceCode)
            .Select(g => new DeviceStatusDto(
                g.DeviceCode,
                g.InstallationZone,
                g.Readings.Count(),
                g.Readings.Max(r => (DateTime?)r.RecordedAtUtc)))   // nullable: a gateway may have no readings
            .ToListAsync();
        Out.Rows(statuses, 6);
        Out.Note("Projections into a DTO are not change-tracked, and the API never exposes the entity.");
    }
}
