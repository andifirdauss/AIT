using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    // What the model types to name a database: letters, digits, "_", ".", "-".
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")]
    private static partial Regex NamePattern();
}
