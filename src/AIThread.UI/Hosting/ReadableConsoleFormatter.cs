using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace AIThread.UI.Hosting;

public sealed class ReadableConsoleFormatterOptions : ConsoleFormatterOptions
{
    // ANSI color on the level tag; off when the log goes to a file.
    public bool UseColors { get; set; }

    // Window in which an identical warning (same category, message and error) is printed only once. Zero turns it off.
    public TimeSpan RepeatWindow { get; set; } = TimeSpan.FromMinutes(5);
}

// Console log meant to be read by a person: one header line per entry (time, level, short category, message), and for a
// warning only the error messages of the exception chain instead of the stack trace. Errors and critical entries keep
// the full stack trace. A warning repeated within RepeatWindow is held back, and its next print says how often it came.
public sealed class ReadableConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "aithread";

    private const string Indent = "    ";
    private const int MaxRepeatEntries = 512;

    private readonly IDisposable? _optionsReloadToken;
    private readonly Dictionary<string, Repeat> _repeats = new(StringComparer.Ordinal);
    private ReadableConsoleFormatterOptions _options;

    private sealed class Repeat(DateTime firstAt)
    {
        public DateTime FirstAt { get; set; } = firstAt;
        public int Suppressed { get; set; }
    }

    public ReadableConsoleFormatter(IOptionsMonitor<ReadableConsoleFormatterOptions> options) : base(FormatterName)
    {
        _optionsReloadToken = options.OnChange(o => _options = o);
        _options = options.CurrentValue;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        var exception = logEntry.Exception;
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var level = logEntry.LogLevel;
        var errors = exception is null ? [] : Messages(exception);
        var now = DateTime.Now;
        string? repeatNote = null;
        if (level == LogLevel.Warning && !ShouldPrint($"{logEntry.Category}\n{message}\n{string.Join('\n', errors)}", now, out repeatNote)) return;

        var line = new StringBuilder();
        line.Append(now.ToString(_options.TimestampFormat ?? "HH:mm:ss")).Append(' ');
        AppendLevel(line, level);
        line.Append(' ').Append(ShortCategory(logEntry.Category)).Append(": ");
        AppendIndented(line, message ?? "", Indent);
        if (repeatNote is not null) line.Append(' ').Append(repeatNote);
        line.AppendLine();

        if (exception is not null)
        {
            if (level >= LogLevel.Error)
            {
                line.Append(Indent);
                AppendIndented(line, exception.ToString(), Indent);
                line.AppendLine();
            }
            else
            {
                foreach (var error in errors)
                {
                    line.Append(Indent).Append("→ ");
                    AppendIndented(line, error, Indent + "  ");
                    line.AppendLine();
                }
            }
        }
        textWriter.Write(line.ToString());
    }

    public void Dispose() => _optionsReloadToken?.Dispose();

    // "Type: message" for every distinct message down the inner-exception chain (aggregate wrappers skipped).
    private static List<string> Messages(Exception exception)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is AggregateException { InnerExceptions.Count: 1 }) continue;
            var text = ex.Message.Trim();
            if (text.Length == 0 || !seen.Add(text)) continue;
            result.Add($"{ex.GetType().Name}: {text}");
        }
        return result;
    }

    private bool ShouldPrint(string key, DateTime now, out string? repeatNote)
    {
        repeatNote = null;
        var window = _options.RepeatWindow;
        if (window <= TimeSpan.Zero) return true;

        lock (_repeats)
        {
            if (_repeats.TryGetValue(key, out var repeat) && now - repeat.FirstAt < window)
            {
                repeat.Suppressed++;
                return false;
            }

            if (repeat is { Suppressed: > 0 })
                repeatNote = $"({repeat.Suppressed}x serupa disembunyikan sejak {repeat.FirstAt:HH:mm:ss})";

            if (_repeats.Count >= MaxRepeatEntries)
                foreach (var stale in _repeats.Where(r => now - r.Value.FirstAt >= window).Select(r => r.Key).ToList())
                    _repeats.Remove(stale);
            _repeats[key] = new Repeat(now);
            return true;
        }
    }

    private void AppendLevel(StringBuilder line, LogLevel level)
    {
        var (tag, color) = level switch
        {
            LogLevel.Trace => ("trce", "\x1b[90m"),
            LogLevel.Debug => ("dbug", "\x1b[90m"),
            LogLevel.Information => ("info", "\x1b[32m"),
            LogLevel.Warning => ("WARN", "\x1b[33m"),
            LogLevel.Error => ("FAIL", "\x1b[31m"),
            LogLevel.Critical => ("CRIT", "\x1b[1;31m"),
            _ => ("    ", "")
        };
        if (_options.UseColors && color.Length > 0) line.Append(color).Append(tag).Append("\x1b[0m");
        else line.Append(tag);
    }

    // Own categories by class name (AIThread.Service.Memory.MemoryRelabeler → MemoryRelabeler); framework categories in
    // full, their last segment alone says too little (Microsoft.EntityFrameworkCore.Database.Command).
    private static string ShortCategory(string category) =>
        category.StartsWith("AIThread.", StringComparison.Ordinal) && category.LastIndexOf('.') is var dot and > 0
            ? category[(dot + 1)..]
            : category;

    private static void AppendIndented(StringBuilder line, string text, string indent)
    {
        var lines = text.TrimEnd().Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) line.AppendLine().Append(indent);
            line.Append(lines[i].TrimEnd('\r'));
        }
    }
}
