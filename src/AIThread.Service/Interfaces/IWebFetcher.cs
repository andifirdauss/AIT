using AIThread.Service.Workspace.Web;

namespace AIThread.Service.Interfaces;

// Reads a web page or API response over HTTP GET for the planner's web_fetch tool, after the caller checked the first
// URL. Every redirect hop is checked again with mayVisit (another host needs its own permission).
public interface IWebFetcher
{
    Task<WebFetchResult> FetchAsync(Uri url, Func<Uri, CancellationToken, Task<bool>> mayVisit, CancellationToken cancellationToken = default);
}
