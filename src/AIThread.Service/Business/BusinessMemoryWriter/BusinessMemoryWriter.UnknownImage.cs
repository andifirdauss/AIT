using System.Text.RegularExpressions;

namespace AIThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    [GeneratedRegex(@"!\[[^\]]*\]\(asset:img\d+[^)]*\)")]
    private static partial Regex UnknownImage();
}
