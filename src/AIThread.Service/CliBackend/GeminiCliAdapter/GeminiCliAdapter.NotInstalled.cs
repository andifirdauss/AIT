using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    private static NineRouterException NotInstalled(Exception? inner) => CliFailure.NotInstalled(DisplayName, Command, "GeminiCli", inner);
}
