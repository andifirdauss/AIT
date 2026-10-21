using System.Text;
using Microsoft.Playwright;

namespace AIThread.Service.Testing;

// A visible Chromium-family window (Edge, Chrome or Playwright's own Chromium) on a persistent profile, driven by the
// tester model. Popups and new tabs are followed; dialogs are accepted and reported. Navigation by the model is checked
// by the caller (localhost only); links the page itself follows are not restricted.
public sealed class PlaywrightTestBrowser : ITestBrowser
{
    private const int MaxSnapshotChars = 12_000;
    private const int MaxEventChars = 300;
    private const int MaxProblems = 50;
    private const float NavigationTimeoutMs = 30_000;

    // Pages that keep polling never go idle; the snapshot is taken anyway after this.
    private const float SettleTimeoutMs = 3_000;
    private const float SnapshotTimeoutMs = 10_000;

    private readonly IPlaywright _playwright;
    private readonly IBrowserContext _context;
    private readonly Action _release;
    private readonly List<string> _events = [];
    private readonly List<string> _problems = [];
    private IPage _page;
    private int _disposed;

    private PlaywrightTestBrowser(IPlaywright playwright, IBrowserContext context, IPage page, string name, Action release)
    {
        _playwright = playwright;
        _context = context;
        _page = page;
        _release = release;
        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_events) return _problems.ToList();
        }
    }

    // Takes over the Playwright instance and the context; release runs once the window is closed.
    public static async Task<PlaywrightTestBrowser> CreateAsync(IPlaywright playwright, IBrowserContext context, string name, Action release)
    {
        // A persistent context opens with one blank tab.
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        var browser = new PlaywrightTestBrowser(playwright, context, page, name, release);
        foreach (var open in context.Pages) browser.Watch(open);
        context.Page += (_, opened) => browser.Follow(opened);
        return browser;
    }

    public Task<string> OpenAsync(Uri url) => ActAsync($"Opened {url}", async () =>
    {
        var response = await _page.GotoAsync(url.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = NavigationTimeoutMs });
        return response is null ? null : $"HTTP {response.Status}";
    });

    public Task<string> SnapshotAsync() => StateAsync("Current page");

    public Task<string> ClickAsync(TestElement element) => ActAsync($"Clicked {element}", async () =>
    {
        await Locate(element).ClickAsync();
        return null;
    });

    public Task<string> FillAsync(TestElement element, string text, bool submit) => ActAsync($"Filled {element}", async () =>
    {
        var locator = Locate(element);
        await locator.FillAsync(text);
        if (submit) await locator.PressAsync("Enter");
        return submit ? "and pressed Enter" : null;
    });

    public Task<string> SelectAsync(TestElement element, IReadOnlyList<string> values) => ActAsync($"Selected in {element}", async () =>
    {
        var selected = await Locate(element).SelectOptionAsync(values);
        return $"selected values: {string.Join(", ", selected)}";
    });

    public Task<string> PressAsync(string key) => ActAsync($"Pressed {key}", async () =>
    {
        await _page.Keyboard.PressAsync(key);
        return null;
    });

    public Task<string> WaitAsync(string? text, bool gone, double seconds) =>
        ActAsync(text is null ? $"Waited {seconds:0.#}s" : $"Waited for \"{text}\" to {(gone ? "disappear" : "appear")}", async () =>
        {
            var timeout = (float)(Math.Clamp(seconds, 0.5, 15) * 1000);
            if (text is null)
            {
                await _page.WaitForTimeoutAsync(timeout);
                return null;
            }
            await _page.GetByText(text).First.WaitForAsync(new LocatorWaitForOptions
            {
                State = gone ? WaitForSelectorState.Hidden : WaitForSelectorState.Visible,
                Timeout = timeout
            });
            return null;
        });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            await _context.CloseAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // The developer may have closed the window already.
        }
        finally
        {
            _playwright.Dispose();
            _release();
        }
    }

    // Refs resolve against the latest snapshot of the page (aria-ref is Playwright's engine for them).
    private ILocator Locate(TestElement element) =>
        element.Ref is { } reference ? _page.Locator($"aria-ref={reference.Trim()}") : _page.Locator(element.Selector!);

    private async Task<string> ActAsync(string done, Func<Task<string?>> action)
    {
        string? note;
        try
        {
            note = await action();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Playwright reports a missing, hidden or disabled element as a timeout.
            var hint = ex is TimeoutException ? " The element was not found, not visible or not enabled in time: use a ref from the snapshot below." : string.Empty;
            return await StateAsync($"Error: {Brief(ex.Message)}{hint}");
        }
        return await StateAsync(note is null ? done : $"{done} ({note})");
    }

    private async Task<string> StateAsync(string header)
    {
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = SettleTimeoutMs });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Still loading or polling: describe it as it is now.
        }

        string snapshot;
        try
        {
            snapshot = await _page.AriaSnapshotAsync(new PageAriaSnapshotOptions { Mode = AriaSnapshotMode.Ai, Timeout = SnapshotTimeoutMs });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            snapshot = $"(no snapshot: {Brief(ex.Message)})";
        }

        string title;
        try
        {
            title = await _page.TitleAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            title = string.Empty;
        }

        var builder = new StringBuilder(header).Append('\n');
        builder.Append("URL: ").Append(_page.Url).Append('\n');
        if (title.Length > 0) builder.Append("Title: ").Append(title).Append('\n');

        string[] events;
        lock (_events)
        {
            events = _events.ToArray();
            _events.Clear();
        }
        if (events.Length > 0)
        {
            builder.Append("Since the last action:\n");
            foreach (var line in events) builder.Append("- ").Append(line).Append('\n');
        }

        if (snapshot.Length > MaxSnapshotChars)
            snapshot = snapshot[..MaxSnapshotChars] + $"\n... ({snapshot.Length - MaxSnapshotChars} more characters not shown)";
        builder.Append("Page snapshot:\n").Append(snapshot.TrimEnd()).Append('\n');
        return builder.ToString();
    }

    private void Follow(IPage opened)
    {
        Watch(opened);
        _page = opened;
        Record($"a new tab opened ({opened.Url}); actions now go to it", problem: false);
    }

    private void Watch(IPage page)
    {
        page.Console += (_, message) =>
        {
            if (message.Type == "error") Record($"console error: {Clip(message.Text)}", problem: true);
        };
        page.PageError += (_, error) => Record($"uncaught error: {Clip(error)}", problem: true);
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && IsPageRequest(response.Request))
                Record($"{response.Request.Method} {PathOf(response.Url)} → {response.Status}", problem: true);
        };
        page.RequestFailed += (_, request) =>
        {
            if (IsPageRequest(request)) Record($"{request.Method} {PathOf(request.Url)} failed: {request.Failure}", problem: true);
        };
        page.Dialog += async (_, dialog) =>
        {
            Record($"{dialog.Type} dialog \"{Clip(dialog.Message)}\" — accepted", problem: false);
            try
            {
                await dialog.AcceptAsync();
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // Already handled or the page went away.
            }
        };
        page.Close += (_, closed) =>
        {
            if (ReferenceEquals(_page, closed)) _page = _context.Pages.LastOrDefault(p => !p.IsClosed) ?? closed;
        };
    }

    // The page's own calls and navigations; images, styles and scripts are left out.
    private static bool IsPageRequest(IRequest request) => request.ResourceType is "xhr" or "fetch" or "document";

    private void Record(string line, bool problem)
    {
        lock (_events)
        {
            _events.Add(line);
            if (problem && _problems.Count < MaxProblems && !_problems.Contains(line)) _problems.Add(line);
        }
    }

    private static string PathOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;

    private static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > MaxEventChars ? line[..MaxEventChars] + "…" : line;
    }

    // Playwright messages end with a long call log; the first lines say what went wrong.
    private static string Brief(string message)
    {
        var lines = message.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeWhile(l => !l.StartsWith("Call log", StringComparison.OrdinalIgnoreCase))
            .Take(3);
        var text = string.Join(" ", lines);
        return text.Length > 600 ? text[..600] + "…" : text;
    }
}
