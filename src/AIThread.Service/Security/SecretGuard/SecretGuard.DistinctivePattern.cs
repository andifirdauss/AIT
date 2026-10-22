using System.Text.RegularExpressions;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // One alternation of every known distinctive value, longest first, rebuilt when a value was added.
    private Regex? DistinctivePattern()
    {
        var version = Volatile.Read(ref _distinctiveVersion);
        var cached = _distinctivePattern;
        if (cached.Pattern is not null && cached.Version == version) return cached.Pattern;

        var values = _distinctive.Keys.OrderByDescending(v => v.Length).Select(Regex.Escape).ToList();
        if (values.Count == 0) return null;
        var pattern = new Regex(string.Join('|', values), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        _distinctivePattern = (version, pattern);
        return pattern;
    }
}
