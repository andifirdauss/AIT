using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static NineRouterException NotInstalled(Exception? inner) => CliFailure.NotInstalled(DisplayName, Command, "ClaudeCli", inner);
}
