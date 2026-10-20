using AiThread.Common.Memory;

namespace AiThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private static string DefaultLabels(string module, string title) =>
        string.Join(", ", MemoryLabels.Keywords($"{module} {title}".Split([' ', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries)));
}
