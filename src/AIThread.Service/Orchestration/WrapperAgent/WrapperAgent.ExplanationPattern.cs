using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    [GeneratedRegex(@"\?|\b(jelaskan|terangkan|kenapa|mengapa|bagaimana|gimana|explain|describe|why|how)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplanationPattern();
}
