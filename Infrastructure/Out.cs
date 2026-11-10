using System.Data.Common;
using System.Globalization;
using System.Reflection;

namespace QueryDemos.Infrastructure;

/// <summary>Console output helpers sized for a projector: headings, SQL, metrics and small tables.</summary>
public static class Out
{
    /// <summary>Wait for Enter before each part (only when a person is typing in a terminal).</summary>
    public static bool Pause { get; set; }

    public static void Title(string title, string slides)
    {
        Console.WriteLine();
        Write(ConsoleColor.Magenta, new string('═', 78));
        Write(ConsoleColor.Magenta, $" {title}");
        Write(ConsoleColor.DarkMagenta, $" {slides}");
        Write(ConsoleColor.Magenta, new string('═', 78));
    }

    public static void Part(string text)
    {
        if (Pause && !Console.IsInputRedirected)
        {
            Write(ConsoleColor.DarkGray, "  (press Enter for the next part)");
            Console.ReadLine();
        }

        Console.WriteLine();
        Write(ConsoleColor.Yellow, $"▶ {text}");
    }

    public static void Note(string text) => Write(ConsoleColor.Gray, $"  {text}");
    public static void Dim(string text) => Write(ConsoleColor.DarkGray, text);
    public static void Good(string text) => Write(ConsoleColor.Green, $"  ✔ {text}");
    public static void Bad(string text) => Write(ConsoleColor.Red, $"  ✘ {text}");
    public static void Warn(string text) => Write(ConsoleColor.DarkYellow, $"  ! {text}");

    public static void Metric(string label, object? value)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"  {(label + " ").PadRight(44, '.')} ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Format(value));
        Console.ResetColor();
    }

    public static void Code(string text)
    {
        foreach (var line in text.Replace("\r", "").Split('\n'))
            Write(ConsoleColor.Cyan, "    " + line);
    }

    /// <summary>Prints one SQL command the way the database receives it, with its parameters.</summary>
    public static void Sql(DbCommand command, int number)
    {
        Write(ConsoleColor.DarkCyan, $"  ── SQL command #{number} ──");

        var text = command.CommandText;
        if (text.Length > 1800) text = text[..1800] + " …(truncated)";
        Code(text);

        if (command.Parameters.Count > 0)
        {
            var parameters = command.Parameters.Cast<DbParameter>().ToList();
            var shown = parameters.Take(8).Select(p => $"{p.ParameterName} = {Format(p.Value)}");
            var more = parameters.Count > 8 ? $"   (+{parameters.Count - 8} more)" : "";
            Write(ConsoleColor.DarkYellow, $"    parameters: {string.Join(", ", shown)}{more}");
        }
    }

    /// <summary>Prints the first rows of any list of objects (records or anonymous types) as a table.</summary>
    public static void Rows<T>(IReadOnlyList<T> rows, int max = 8)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (props.Length == 0)
        {
            Note($"{rows.Count:N0} rows");
            return;
        }

        var header = props.Select(p => p.Name).ToArray();
        var data = rows.Take(max).Select(r => props.Select(p => Format(p.GetValue(r))).ToArray()).ToList();
        Table(header, data);

        if (rows.Count == 0) Dim("    (no rows)");
        else if (rows.Count > max) Dim($"    … {rows.Count - max:N0} more rows ({rows.Count:N0} in total)");
    }

    public static void Table(string[] header, IReadOnlyList<string[]> rows)
    {
        var widths = header
            .Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))
            .ToArray();

        Write(ConsoleColor.White, "    " + string.Join("   ", header.Select((h, i) => h.PadRight(widths[i]))));
        Write(ConsoleColor.DarkGray, "    " + string.Join("   ", widths.Select(w => new string('─', w))));
        foreach (var row in rows)
            Write(ConsoleColor.Gray, "    " + string.Join("   ", row.Select((c, i) => c.PadRight(widths[i]))));
    }

    public static string Format(object? value) => value switch
    {
        null or DBNull => "NULL",
        double d => d.ToString("0.00", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        Guid g => g.ToString()[..8] + "…",
        TimeSpan ts => $"{ts.TotalMilliseconds:N0} ms",
        bool b => b ? "true" : "false",
        int or long => string.Format(CultureInfo.InvariantCulture, "{0:N0}", value),
        string s => s,
        _ => value.ToString() ?? ""
    };

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
