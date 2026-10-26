namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Enough of a value to recognize it in a list, not enough to use it: a few characters at both ends.
    private static string Preview(string value) => value.Length switch
    {
        <= 4 => new string('•', value.Length),
        <= 8 => $"{value[0]}…{value[^1]}",
        <= 14 => $"{value[..2]}…{value[^2..]}",
        _ => $"{value[..4]}…{value[^3..]}"
    };
}
