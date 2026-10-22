namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // tester-browser\<channel> for the shared profile; tester-browser\accounts\<profile>\<channel> for a test account,
    // beside it rather than inside it (a profile folder inside another one confuses the browser).
    public static string ProfileDirectory(string? channel, string? profile)
    {
        var browser = channel ?? "chromium";
        if (string.IsNullOrWhiteSpace(profile)) return Path.Combine(ProfileRoot, browser);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(profile.Trim().Select(c => invalid.Contains(c) || c is '.' or ' ' ? '_' : char.ToLowerInvariant(c)).ToArray());
        return Path.Combine(ProfileRoot, "accounts", safe.Length > 80 ? safe[..80] : safe, browser);
    }
}
