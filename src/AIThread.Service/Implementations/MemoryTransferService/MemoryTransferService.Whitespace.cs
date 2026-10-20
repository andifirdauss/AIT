using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
