using AIThread.Service.Business;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public string Render(string markdown) => BusinessMarkdown.ToHtml(markdown ?? string.Empty);
}
