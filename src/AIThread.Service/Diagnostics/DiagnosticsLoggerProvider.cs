using AIThread.Service.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Diagnostics;

// Turns every warning or error logged with an exception into an incident of the Log menu, stack trace included, so a
// failure anywhere (a background worker, an API call, a crashed thread) can be exported without reading the console.
// A {ThreadId} / {ProjectId} in the log message ties the incident to its thread. Cancellations are not incidents.
// Registered as ILoggerProvider (framework adapter, like the hosted workers): IDiagnosticsService is resolved on the
// first entry, after the container is built.
public sealed class DiagnosticsLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    [ThreadStatic] private static bool _recording;
    private IDiagnosticsService? _diagnostics;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Record(string category, LogLevel level, string message, Exception exception, Guid? threadId, Guid? projectId)
    {
        if (_recording || IsCancellation(exception)) return;
        try
        {
            _recording = true;
            _diagnostics ??= services.GetService<IDiagnosticsService>();
            _diagnostics?.Record(new DiagnosticReport(level >= LogLevel.Error ? "Error" : "Warning", Short(category), message,
                message, threadId, projectId, exception));
        }
        catch (ObjectDisposedException)
        {
            // Logged while the host shuts down.
        }
        finally
        {
            _recording = false;
        }
    }

    private static bool IsCancellation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is OperationCanceledException) return true;
        return false;
    }

    // "AIThread.Service.CodeCalls.FunctionCallWorker" → "FunctionCallWorker".
    private static string Short(string category) => category[(category.LastIndexOf('.') + 1)..];

    private static Guid? IdOf<TState>(TState state, string key) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> values
            ? values.FirstOrDefault(v => v.Key == key).Value switch { Guid id => id, string text when Guid.TryParse(text, out var id) => id, _ => null }
            : null;

    private sealed class Logger(DiagnosticsLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is null || !IsEnabled(logLevel)) return;
            provider.Record(category, logLevel, formatter(state, exception), exception, IdOf(state, "ThreadId"), IdOf(state, "ProjectId"));
        }
    }
}
