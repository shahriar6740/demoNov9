namespace QueryDemos;

/// <summary>Command-line options: a step name plus a few flags.</summary>
public sealed record CliOptions(
    string Step,
    bool Quiet,
    bool Pause,
    bool EfLog,
    int LatencyMs,
    int? Count,
    bool AddIndex,
    bool AddTimeIndex,
    bool DropIndex,
    bool Destructive)
{
    public static CliOptions Parse(string[] args)
    {
        string step = "help";
        bool quiet = false, pause = false, efLog = false, addIndex = false, addTimeIndex = false,
             dropIndex = false, destructive = false;
        int latency = 0;
        int? count = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "--quiet": quiet = true; break;
                case "--pause": pause = true; break;
                case "--ef-log": efLog = true; break;
                case "--add-index": addIndex = true; break;
                case "--add-time-index": addTimeIndex = true; break;
                case "--drop-index": dropIndex = true; break;
                case "--destructive": destructive = true; break;
                case "--latency":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var ms)) { latency = ms; i++; }
                    break;
                case "--count":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { count = n; i++; }
                    break;
                default:
                    if (arg.StartsWith("--")) break;
                    if (step != "help" && int.TryParse(arg, out var positional)) count = positional; // e.g. bigseed 500000
                    else step = arg;
                    break;
            }
        }

        return new CliOptions(step, quiet, pause, efLog, latency, count, addIndex, addTimeIndex, dropIndex, destructive);
    }
}
