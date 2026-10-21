using System.Text.RegularExpressions;

namespace AIThread.Service.Testing;

// A login the developer gave for one test, in the chat or on the thread page. It is never written to disk and never
// sent to a model: the tester writes the placeholders and AIT puts the values in (Fill), then masks the password again
// in everything the model, the logs and the report see (Mask).
public sealed partial record TestLogin(string Username, string Password)
{
    public const string UsernamePlaceholder = "{{username}}";
    public const string PasswordPlaceholder = "{{password}}";

    // A labeled login is a short reply; a longer message that names a username and a password is a request.
    private const int MaxLabeledReplyChars = 200;

    // Shorter passwords would mask unrelated text.
    private const int MinMaskedChars = 3;

    // "login admin rahasia123" — exactly three words, so a request that starts with "login" is not taken for one — or a
    // short reply with labeled values such as "username: admin" and "password: rahasia" in any order. Null when the
    // reply is not a login.
    public static TestLogin? Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var text = reply.Trim();
        if (Inline().Match(text) is { Success: true } inline) return Create(inline.Groups[1].Value, inline.Groups[2].Value);
        if (text.Length > MaxLabeledReplyChars) return null;

        var user = LabeledUser().Match(text);
        var password = LabeledPassword().Match(text);
        return user.Success && password.Success ? Create(user.Groups[1].Value, password.Groups[1].Value) : null;
    }

    // From the thread page, where both fields are optional.
    public static TestLogin? From(string? username, string? password) => Create(username ?? string.Empty, password ?? string.Empty);

    // The real values in place of the placeholders.
    public string Fill(string text) =>
        text.Replace(PasswordPlaceholder, Password, StringComparison.Ordinal).Replace(UsernamePlaceholder, Username, StringComparison.Ordinal);

    // The placeholder in place of the password.
    public string Mask(string text) =>
        Password.Length < MinMaskedChars ? text : text.Replace(Password, PasswordPlaceholder, StringComparison.Ordinal);

    // The generated one would print the password into logs.
    public override string ToString() => $"TestLogin {{ Username = {Username} }}";

    private static TestLogin? Create(string username, string password)
    {
        var user = Clean(username);
        var secret = Clean(password);
        return user.Length == 0 || secret.Length == 0 ? null : new TestLogin(user, secret);
    }

    private static string Clean(string value) => value.Trim().Trim('`', '"', '\'');

    [GeneratedRegex(@"^\s*login\s+(\S+)\s+(\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Inline();

    [GeneratedRegex(@"(?:^|[\s,;])(?:user(?:name)?|e-?mail|akun)\s*[:=]\s*([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledUser();

    [GeneratedRegex(@"(?:^|[\s,;])(?:pass(?:word)?|pwd|sandi|kata\s+sandi)\s*[:=]\s*([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledPassword();
}
