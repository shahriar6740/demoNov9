using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QueryDemos.Infrastructure;

/// <summary>
/// An EF Core interceptor that counts every SQL command sent to the database and prints it.
/// This is what makes round trips (and the N+1 problem) visible during the demos.
/// It can also add artificial latency per command to imitate a cloud database.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public static CommandCounter Instance { get; } = new();

    private int _quietDepth;

    /// <summary>Commands counted since the last Reset().</summary>
    public int Count { get; private set; }

    /// <summary>Print each command's SQL and parameters.</summary>
    public bool Echo { get; set; } = true;

    /// <summary>Print only the first N commands (the rest are still counted).</summary>
    public int MaxEcho { get; set; } = int.MaxValue;

    /// <summary>Artificial delay per command, in milliseconds (0 = off).</summary>
    public int LatencyMs { get; set; }

    public void Reset() => Count = 0;

    /// <summary>Stop counting and printing until the returned object is disposed (for helper queries).</summary>
    public IDisposable Quiet()
    {
        _quietDepth++;
        return new Restore(() => _quietDepth--);
    }

    private void Record(DbCommand command)
    {
        if (_quietDepth > 0) return;

        Count++;
        if (!Echo) return;

        if (Count <= MaxEcho)
            Out.Sql(command, Count);
        else if (MaxEcho > 0 && Count == MaxEcho + 1)
            Out.Dim("    … more commands follow (still counting, no longer printing)");
    }

    private void Delay()
    {
        if (_quietDepth == 0 && LatencyMs > 0) Thread.Sleep(LatencyMs);
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        _quietDepth == 0 && LatencyMs > 0 ? Task.Delay(LatencyMs, cancellationToken) : Task.CompletedTask;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        Delay();
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        await DelayAsync(cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        Delay();
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        await DelayAsync(cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        Delay();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        await DelayAsync(cancellationToken);
        return result;
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
