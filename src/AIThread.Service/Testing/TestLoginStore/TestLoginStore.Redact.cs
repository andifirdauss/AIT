namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    // Every password a session gave, replaced by its placeholder: the chat keeps the "login <user> <password>" reply and
    // sends it back with every later prompt.
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || _logins.IsEmpty) return text;
        var now = Now();
        foreach (var login in _logins.Values)
            if (login.ExpiresAt > now) text = login.Value.Mask(text);
        return text;
    }
}
