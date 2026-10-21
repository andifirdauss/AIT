namespace AIThread.Service.Testing;

// The element an action works on: a ref from the latest snapshot ("e12") or a CSS/Playwright selector. Name is what
// the developer reads in the progress.
public sealed record TestElement(string Name, string? Ref, string? Selector)
{
    public override string ToString() => Name;
}

// One visible browser window for one testing run (ITestBrowserLauncher). Every action answers with the page as the
// tester model reads it: what happened, URL, title, the console errors, failed requests and dialogs since the last
// action, and an accessibility snapshot whose refs the next action can use. Failures are part of that answer, never
// exceptions. Disposing closes the window.
public interface ITestBrowser : IAsyncDisposable
{
    // "Microsoft Edge", "Google Chrome", "Chromium (Playwright)".
    string Name { get; }

    // Console errors and failed requests seen during the whole run, for the report.
    IReadOnlyList<string> Problems { get; }

    Task<string> OpenAsync(Uri url);

    Task<string> SnapshotAsync();

    Task<string> ClickAsync(TestElement element);

    Task<string> FillAsync(TestElement element, string text, bool submit);

    Task<string> SelectAsync(TestElement element, IReadOnlyList<string> values);

    Task<string> PressAsync(string key);

    // text null: just wait the seconds.
    Task<string> WaitAsync(string? text, bool gone, double seconds);
}
