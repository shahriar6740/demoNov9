using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 8 — null handling and translation gotchas, shown failing and then fixed.</summary>
public static class Demo04_NullsAndTranslation
{
    private const double CriticalThresholdCelsius = 4.0;

    // A normal C# method. EF Core cannot translate a call to it inside Where() into SQL.
    private static bool IsCritical(TelemetryRecord r) => r.TemperatureCelsius >= CriticalThresholdCelsius;

    public static async Task RunAsync()
    {
        Out.Title("Demo 4 · Null handling and translation gotchas", "Slide 8");
        await using var db = Db.Create();

        var emptyId = await db.Gateways
            .Where(g => g.DeviceCode == Seeder.EmptyGatewayCode)
            .Select(g => g.Id)
            .SingleAsync();

        Out.Part("A. Average over zero rows into a non-nullable double");
        try
        {
            var avg = await db.Readings
                .Where(r => r.GatewayDeviceId == emptyId)
                .AverageAsync(r => r.TemperatureCelsius);
            Out.Note($"Average = {Out.Format(avg)}");
        }
        catch (Exception ex)
        {
            Out.Bad($"{ex.GetType().Name}: {FirstLine(ex.Message)}");
        }

        Out.Note("Fix: cast the selector to double? so NULL has somewhere to go.");
        var safeAvg = await db.Readings
            .Where(r => r.GatewayDeviceId == emptyId)
            .AverageAsync(r => (double?)r.TemperatureCelsius);
        Out.Good($"Average = {Out.Format(safeAvg)}");

        Out.Part("B. The same bug inside a projection: one empty gateway breaks the whole report");
        // A List (not an array): with C# 14, array.Contains in a query can bind to the span overload.
        var zones = new List<string> { "Cold-Vault-01", Seeder.EmptyZone };
        try
        {
            var report = await db.Gateways
                .Where(g => zones.Contains(g.InstallationZone))
                .OrderBy(g => g.DeviceCode)
                .Select(g => new { g.DeviceCode, AvgTemp = g.Readings.Average(r => r.TemperatureCelsius) })
                .ToListAsync();
            Out.Rows(report, 4);
        }
        catch (Exception ex)
        {
            Out.Bad($"{ex.GetType().Name}: {FirstLine(ex.Message)}");
        }

        var fixedReport = await db.Gateways
            .Where(g => zones.Contains(g.InstallationZone))
            .OrderBy(g => g.DeviceCode)
            .Select(g => new { g.DeviceCode, AvgTemp = g.Readings.Average(r => (double?)r.TemperatureCelsius) })
            .ToListAsync();
        Out.Good("With (double?) the report runs; GW-EMPTY shows NULL:");
        Out.Rows(fixedReport.OrderBy(x => x.AvgTemp.HasValue).ToList(), 4);

        Out.Part("C. A C# method inside Where() cannot be translated to SQL");
        try
        {
            var count = await db.Readings.Where(r => IsCritical(r)).CountAsync();
            Out.Note($"Critical readings = {count}");
        }
        catch (InvalidOperationException ex)
        {
            Out.Bad($"{ex.GetType().Name}: {FirstLine(ex.Message)}");
        }

        Out.Note("Fix: express the rule with operators EF Core understands.");
        var threshold = CriticalThresholdCelsius;
        var critical = await db.Readings.Where(r => r.TemperatureCelsius >= threshold).CountAsync();
        Out.Good($"Critical readings (>= {threshold} °C) = {critical:N0}");

        Out.Part("D. CS8072 — `?.` is not allowed in query lambdas (a compile-time error)");
        Out.Note("Uncomment the line marked LIVE DEMO in Demo04_NullsAndTranslation.cs and run `dotnet build`.");
        Out.Note("Fix: write  r.Gateway == null ? null : r.Gateway.DeviceCode  — EF Core adds the SQL null checks.");

        // LIVE DEMO (CS8072): uncomment the next line, run `dotnet build`, show the error, then comment it again.
        // var broken = db.Readings.Select(r => r.Gateway?.DeviceCode);
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length > 220 ? line[..220] + " …" : line;
    }
}
