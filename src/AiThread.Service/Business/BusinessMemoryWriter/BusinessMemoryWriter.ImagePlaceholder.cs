using System.Text.RegularExpressions;

namespace AiThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    [GeneratedRegex(@"asset:img(?<n>\d{1,2})\b")]
    private static partial Regex ImagePlaceholder();
}
