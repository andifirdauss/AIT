using AiThread.Service.Business;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public string Render(string markdown) => BusinessMarkdown.ToHtml(markdown ?? string.Empty);
}
