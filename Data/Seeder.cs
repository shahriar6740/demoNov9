using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QueryDemos.Infrastructure;

namespace QueryDemos.Data;

public static class Seeder
{
    public static readonly string[] ActiveZones = ["Cold-Vault-01", "Cold-Vault-02", "Dry-Storage-A", "Dock-East"];
    public const string EmptyZone = "Freezer-Annex";
    public const string EmptyGatewayCode = "GW-EMPTY";

    private static readonly string[] Technicians = ["A. Singh", "M. Tremblay", "J. Okafor", "L. Chen", "R. Gagnon"];

    /// <summary>
    /// Drops and recreates the database: 99 gateways with about 50 readings each (last 24 h)
    /// and 3–10 maintenance logs each, plus GW-EMPTY in Freezer-Annex with no readings at all.
    /// </summary>
    public static async Task ResetAsync()
    {
        Out.Title("Setup · recreate the QueryDemos database", "run once before class");
        await using var db = Db.Create();

        using (CommandCounter.Instance.Quiet())
        {
            Out.Note($"Server: {MaskPassword(Db.ConnectionString)}");
            Out.Note("Dropping the database and applying migrations …");
            await db.Database.EnsureDeletedAsync();
            // MigrateAsync (not EnsureCreatedAsync): builds the schema from the Migrations folder and
            // records each one in __EFMigrationsHistory, so later migrations can be applied on top.
            await db.Database.MigrateAsync();

            var rng = new Random(42);
            var now = DateTime.UtcNow;
            int readings = 0, logs = 0;

            for (int i = 1; i <= 99; i++)
            {
                var zone = ActiveZones[(i - 1) % ActiveZones.Length];
                var gateway = new GatewayDevice
                {
                    Id = Guid.CreateVersion7(),            // time-ordered GUIDs suit a clustered primary key
                    DeviceCode = $"GW-{i:000}",
                    InstallationZone = zone,
                    IsOnline = i % 7 != 0
                };

                int readingCount = 40 + rng.Next(21);       // 40–60 readings, one about every 24 minutes
                for (int k = 0; k < readingCount; k++)
                {
                    gateway.Readings.Add(new TelemetryRecord
                    {
                        RecordedAtUtc = now.AddMinutes(-(k * 24) - rng.Next(0, 5)),
                        TemperatureCelsius = Math.Round(BaseTemperature(zone) + rng.NextDouble() * 4 - 2, 2),
                        HumidityPercent = Math.Round(35 + rng.NextDouble() * 30, 1)
                    });
                }

                int logCount = 3 + rng.Next(8);             // 3–10 maintenance logs
                for (int k = 0; k < logCount; k++)
                {
                    gateway.MaintenanceLogs.Add(new MaintenanceLog
                    {
                        PerformedAtUtc = now.AddDays(-rng.Next(1, 365)),
                        Technician = Technicians[rng.Next(Technicians.Length)],
                        Notes = "Routine inspection and sensor calibration"
                    });
                }

                readings += readingCount;
                logs += logCount;
                db.Gateways.Add(gateway);
            }

            // A gateway with no readings at all — the null-handling and LeftJoin demos need it.
            db.Gateways.Add(new GatewayDevice
            {
                Id = Guid.CreateVersion7(),
                DeviceCode = EmptyGatewayCode,
                InstallationZone = EmptyZone,
                IsOnline = false
            });

            await db.SaveChangesAsync();
            Out.Good($"Created 100 gateways, {readings:N0} readings and {logs:N0} maintenance logs.");
        }

        Out.Note("Next: dotnet run -- bigseed   (adds about a million older readings for Demo 9)");
    }

    /// <summary>
    /// Adds a large volume of older readings with one set-based INSERT, so Demo 9's execution plans
    /// behave like production. The new rows are all older than the existing data, so the
    /// "last 24 hours" windows used by Demos 1–8 are unaffected.
    /// </summary>
    public static async Task BigSeedAsync(int count)
    {
        Out.Title($"Big seed · add {count:N0} historical readings", "needed for Demo 9");
        await using var db = Db.Create();

        using (CommandCounter.Instance.Quiet())
        {
            var gatewayCount = await db.Gateways.CountAsync(g => g.DeviceCode != EmptyGatewayCode);
            if (gatewayCount == 0)
            {
                Out.Bad("No gateways found — run `dotnet run -- setup` first.");
                return;
            }

            var before = await db.Readings.LongCountAsync();
            var oldest = await db.Readings.MinAsync(r => (DateTime?)r.RecordedAtUtc) ?? DateTime.UtcNow;
            var anchor = oldest.AddHours(-1);

            Out.Note($"Inserting {count:N0} rows in one statement (this can take a minute) …");
            var sw = Stopwatch.StartNew();

            // Parameterized with FormattableString: {count}, {anchor}, … become SQL parameters.
            await db.Database.ExecuteSqlAsync($"""
                WITH gw AS (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY DeviceCode) - 1 AS k
                    FROM Gateways
                    WHERE DeviceCode <> {EmptyGatewayCode}
                ),
                n AS (
                    SELECT TOP ({count}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
                    FROM sys.all_objects AS a
                    CROSS JOIN sys.all_objects AS b
                )
                INSERT INTO Readings (RecordedAtUtc, TemperatureCelsius, HumidityPercent, GatewayDeviceId)
                SELECT DATEADD(MINUTE, -CAST(n.i / {gatewayCount} AS int) * 50, {anchor}),
                       -2.0 + (ABS(CHECKSUM(NEWID())) % 2400) / 100.0,
                       30.0 + (ABS(CHECKSUM(NEWID())) % 4000) / 100.0,
                       gw.Id
                FROM n
                JOIN gw ON gw.k = n.i % {gatewayCount};
                """);

            sw.Stop();
            var after = await db.Readings.LongCountAsync();
            Out.Good($"Readings: {before:N0} → {after:N0}  ({sw.Elapsed.TotalSeconds:0.0} s)");
            if (after - before < count)
                Out.Warn("Fewer rows than requested were inserted (the number generator ran out). Run bigseed again to add more.");
        }

        Out.Note("Next: dotnet run -- 9");
    }

    private static double BaseTemperature(string zone) => zone switch
    {
        "Cold-Vault-01" or "Cold-Vault-02" => 3.0,
        "Dry-Storage-A" => 18.0,
        "Dock-East" => 11.0,
        _ => -18.0
    };

    private static string MaskPassword(string connectionString) =>
        System.Text.RegularExpressions.Regex.Replace(connectionString, @"(?i)(password|pwd)=[^;]*", "$1=****");
}
