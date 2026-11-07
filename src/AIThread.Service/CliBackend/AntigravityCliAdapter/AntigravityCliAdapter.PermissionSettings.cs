using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // --settings of every run (never saved): every permission action denied, in case a version ignores the agent's empty
    // tool list. Deny rules win over the user's allow rules; managed policy stays above them.
    public static string PermissionSettings() => new JsonObject
    {
        ["permissions"] = new JsonObject
        {
            ["deny"] = new JsonArray("command(*)", "read_file(*)", "write_file(*)", "read_url(*)", "execute_url(*)", "mcp(*)")
        },
        ["disableSlashCommands"] = true
    }.ToJsonString();
}
