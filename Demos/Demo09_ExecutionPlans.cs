using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>
/// Slides 13–14 — execution plans and indexes. For each query this prints the paste-ready SQL,
/// the logical reads (SET STATISTICS IO), the elapsed time and the plan's operators read from
/// SQL Server's plan cache. Use --add-index / --add-time-index / --drop-index to change indexes.
/// </summary>
public static class Demo09_ExecutionPlans
{
    private const string CompositeIndex = "IX_Readings_Gateway_Time";
    private const string TimeIndex = "IX_Readings_Time";

    private static readonly Regex LogicalReads = new(@"(?<!lob )logical reads (\d+)", RegexOptions.Compiled);
    private static readonly XNamespace ShowPlan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static async Task RunAsync(CliOptions options)
    {
        Out.Title("Demo 9 · Execution plans and indexes", "Slides 13–14");
        await using var db = Db.Create();

        long total;
        DateTime now;
        Guid gatewayId;
        using (CommandCounter.Instance.Quiet())
        {
            total = await db.Readings.LongCountAsync();
            now = await DemoClock.NowAsync(db);
            gatewayId = await db.Gateways.Where(g => g.DeviceCode == "GW-001").Select(g => g.Id).SingleAsync();
        }

        Out.Metric("Rows in Readings", total);
        if (total < 200_000)
            Out.Warn("Small table: the optimizer may scan on purpose. Run `dotnet run -- bigseed` first for realistic plans.");

        if (options.DropIndex)
        {
            Out.Part("Dropping the demo indexes");
            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS [IX_Readings_Gateway_Time] ON [Readings]; DROP INDEX IF EXISTS [IX_Readings_Time] ON [Readings];");
        }

        if (options.AddIndex)
        {
            Out.Part($"Adding {CompositeIndex} (what this Fluent API configuration generates):");
            Out.Code("""
                modelBuilder.Entity<TelemetryRecord>()
                    .HasIndex(r => new { r.GatewayDeviceId, r.RecordedAtUtc })
                    .IsDescending(false, true)
                    .IncludeProperties(r => new { r.TemperatureCelsius })
                    .HasDatabaseName("IX_Readings_Gateway_Time");
                """);
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Readings_Gateway_Time' AND object_id = OBJECT_ID('dbo.Readings'))
                    CREATE INDEX [IX_Readings_Gateway_Time]
                        ON [Readings] ([GatewayDeviceId], [RecordedAtUtc] DESC)
                        INCLUDE ([TemperatureCelsius]);
                """);
        }

        if (options.AddTimeIndex)
        {
            Out.Part($"Adding {TimeIndex} for time-only filters");
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Readings_Time' AND object_id = OBJECT_ID('dbo.Readings'))
                    CREATE INDEX [IX_Readings_Time]
                        ON [Readings] ([RecordedAtUtc])
                        INCLUDE ([GatewayDeviceId], [TemperatureCelsius]);
                """);
        }

        Out.Part("Indexes currently on Readings");
        foreach (var line in await ListIndexesAsync(db)) Out.Note(line);

        var weekAgo = now.AddDays(-7);
        var twoHoursAgo = now.AddHours(-2);

        var q1 = await ProfileAsync("Demo9 Q1 gateway dashboard", d => d.Readings
            .Where(r => r.GatewayDeviceId == gatewayId && r.RecordedAtUtc >= weekAgo)
            .OrderByDescending(r => r.RecordedAtUtc)
            .Select(r => new { r.RecordedAtUtc, r.TemperatureCelsius }));
        Print("Q1 · one gateway, last 7 days, newest first (temperature only)", q1);

        var q2 = await ProfileAsync("Demo9 Q2 fleet last two hours", d => d.Readings
            .Where(r => r.RecordedAtUtc >= twoHoursAgo)
            .Select(r => new { r.GatewayDeviceId, r.TemperatureCelsius }));
        Print("Q2 · every gateway, last 2 hours (filters on time only)", q2);

        Console.WriteLine();
        Out.Note("Plan operators are listed root first; SSMS draws the same plan right to left.");
        Out.Note("Next steps:  dotnet run -- 9 --add-index      (Q1 should become an Index Seek; Q2 should not change)");
        Out.Note("             dotnet run -- 9 --add-time-index (Q2 seeks too — column order matters)");
        Out.Note("             dotnet run -- 9 --drop-index     (back to the start)");
    }

    private sealed record ProfileResult(string Sql, int Rows, long LogicalReads, TimeSpan Elapsed,
                                        List<string> Operators, bool MissingIndexSuggested);

    private static async Task<ProfileResult> ProfileAsync<T>(string tag, Func<WarehouseDb, IQueryable<T>> build)
    {
        await using var db = Db.Create();
        using var quiet = CommandCounter.Instance.Quiet();

        var query = build(db).TagWith(tag);
        var sql = query.ToQueryString();

        await db.Database.OpenConnectionAsync();
        var connection = (SqlConnection)db.Database.GetDbConnection();
        long reads = 0;

        void OnInfoMessage(object sender, SqlInfoMessageEventArgs e)
        {
            foreach (Match m in LogicalReads.Matches(e.Message))
                reads += long.Parse(m.Groups[1].Value);
        }

        connection.InfoMessage += OnInfoMessage;
        try
        {
            await query.ToListAsync();                                   // warm-up: compile and cache the plan
            await ExecAsync(connection, "SET STATISTICS IO ON;");
            reads = 0;

            var sw = Stopwatch.StartNew();
            var rows = await query.ToListAsync();
            sw.Stop();

            await ExecAsync(connection, "SET STATISTICS IO OFF;");
            var planXml = await CachedPlanAsync(connection, tag);
            var (operators, missing) = ReadPlan(planXml);
            return new ProfileResult(sql, rows.Count, reads, sw.Elapsed, operators, missing);
        }
        finally
        {
            connection.InfoMessage -= OnInfoMessage;
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void Print(string title, ProfileResult r)
    {
        Out.Part(title);
        Out.Metric("Rows returned", r.Rows);
        Out.Metric("Logical reads (8 KB pages)", r.LogicalReads);
        Out.Metric("Elapsed", r.Elapsed);
        Out.Metric("Plan operators", string.Join("  →  ", r.Operators));
        Out.Metric("Missing-index suggestion in the plan", r.MissingIndexSuggested ? "yes" : "no");
        Out.Note("Paste into SSMS, press Ctrl+M (Include Actual Execution Plan), then Execute:");
        Out.Code(r.Sql);
    }

    private static async Task ExecAsync(SqlConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> CachedPlanAsync(SqlConnection connection, string tag)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP (1) CAST(qp.query_plan AS nvarchar(max))
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
            WHERE st.text LIKE @pattern
              AND st.text NOT LIKE '%dm_exec_query_stats%'
            ORDER BY qs.last_execution_time DESC;
            """;
        cmd.Parameters.AddWithValue("@pattern", "%" + tag + "%");
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static (List<string> Operators, bool MissingIndex) ReadPlan(string? planXml)
    {
        if (string.IsNullOrEmpty(planXml))
            return (new List<string> { "(plan not found in the cache — open it in SSMS instead)" }, false);

        var doc = XDocument.Parse(planXml);
        var operators = new List<string>();

        foreach (var relOp in doc.Descendants(ShowPlan + "RelOp"))
        {
            var name = (string?)relOp.Attribute("PhysicalOp") ?? "?";

            var indexScan = relOp.Element(ShowPlan + "IndexScan");
            var lookup = (string?)indexScan?.Attribute("Lookup");
            if (lookup is "1" or "true") name = "Key Lookup";

            var index = (string?)relOp.Elements().Elements(ShowPlan + "Object").FirstOrDefault()?.Attribute("Index");
            operators.Add(index is null ? name : $"{name} {index}");
        }

        var missing = doc.Descendants(ShowPlan + "MissingIndexGroup").Any();
        return (operators, missing);
    }

    private static async Task<List<string>> ListIndexesAsync(WarehouseDb db)
    {
        using var _ = CommandCounter.Instance.Quiet();
        return await db.Database.SqlQuery<string>($"""
            SELECT i.name + '  (' + LOWER(i.type_desc) + ')' AS [Value]
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID('dbo.Readings') AND i.name IS NOT NULL
            """).ToListAsync();
    }
}
