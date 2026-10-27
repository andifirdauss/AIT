namespace AIThread.Service.Workspace.Web;

// What one web_fetch read. Text is the body as readable text (HTML as Markdown, JSON indented), not yet cut to what a
// tool result holds. Error: nothing could be read (refused address, binary body, network failure, timeout).
// BlockedRedirect: the page redirects to a host AIT may not visit yet; nothing was read from it.
public sealed record WebFetchResult(
    Uri RequestedUrl,
    Uri FinalUrl,
    int? StatusCode = null,
    string? ReasonPhrase = null,
    string? ContentType = null,
    long Bytes = 0,
    bool BodyCut = false,
    string? Title = null,
    string Text = "",
    long DurationMs = 0,
    string? Error = null,
    Uri? BlockedRedirect = null);
