using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
