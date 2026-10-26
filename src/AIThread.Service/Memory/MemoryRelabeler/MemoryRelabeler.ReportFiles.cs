using AIThread.Common.Localization;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private void ReportFiles(string label, int done, int total, string suffix = "")
    {
        _detail = $"{label} · {done.ToString("N0", Lang.Culture)}/{total.ToString("N0", Lang.Culture)} file{suffix}";
        PublishProgress();
    }
}
