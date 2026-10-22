using System.Collections.Concurrent;

namespace AIThread.Service.Verification;

// One build or test run per project root at a time: both write build output (bin/, obj/, target/), and two at once fight
// over it. Shared by BuildRunner and TestRunner.
public static class RootGates
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim For(string root) =>
        Gates.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), _ => new SemaphoreSlim(1, 1));
}
