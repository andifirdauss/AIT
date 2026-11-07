using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    private static NineRouterException NotInstalled(Exception? inner) => CliFailure.NotInstalled(DisplayName, Command, "AntigravityCli", inner);
}
