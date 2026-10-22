using System.Text.RegularExpressions;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Known distinctive values wherever they appear, also where no rule sees a secret (a pasted password, a diff).
    private string MaskKnown(string text, bool count)
    {
        if (_distinctive.IsEmpty || string.IsNullOrEmpty(text)) return text;
        if (DistinctivePattern() is not { } pattern) return text;
        try
        {
            return pattern.Replace(text, m => _distinctive.TryGetValue(m.Value, out var label) ? Mask(label, m.Value, count) : m.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }
}
