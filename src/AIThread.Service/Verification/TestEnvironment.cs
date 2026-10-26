using System.Text.RegularExpressions;
using AIThread.Common.Localization;

namespace AIThread.Service.Verification;

// Recognises a test that failed because this machine lacks what it needs (a database, the network, a config file)
// rather than because of the code: such a test is reported Skipped with the reason, never as a regression.
public static partial class TestEnvironment
{
    public const string Database = "database";
    public const string Network = "network";
    public const string Config = "config";

    // Requirement and the reason (in the UI language, for the developer), or null when the message reads like a real failure.
    public static (string Requirement, string Reason)? Classify(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (DatabaseError().Match(message) is { Success: true } database)
            return (Database, Lang.T($"Butuh database yang tidak tersedia di mesin ini: {Line(message, database.Index)}", $"Needs a database that is not available on this machine: {Line(message, database.Index)}"));
        if (NetworkError().Match(message) is { Success: true } network)
            return (Network, Lang.T($"Butuh koneksi jaringan/layanan yang tidak tersedia di mesin ini: {Line(message, network.Index)}", $"Needs a network connection/service that is not available on this machine: {Line(message, network.Index)}"));
        if (ConfigError().Match(message) is { Success: true } config)
            return (Config, Lang.T($"Butuh konfigurasi yang tidak ada di mesin ini: {Line(message, config.Index)}", $"Needs configuration that does not exist on this machine: {Line(message, config.Index)}"));
        return null;
    }

    // The message line the match is on, shortened.
    private static string Line(string message, int index)
    {
        var start = message.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = message.IndexOf('\n', index);
        var line = (end < 0 ? message[start..] : message[start..end]).Trim();
        return line.Length <= 200 ? line : line[..200] + "…";
    }

    [GeneratedRegex(@"A network-related or instance-specific error|Cannot open database|Login failed for user|SqlException.*(?:timeout|server was not found)|could not connect to server|Unable to connect to any of the specified MySQL hosts|ORA-12(?:154|514|541)|SQLSTATE\[HY000\] \[2002\]|psycopg2?\.OperationalError|sqlalchemy\.exc\.OperationalError|connection to server at .* failed|MongoServerSelectionError|The ConnectionString property has not been initialized|Format of the initialization string does not conform|No connection string named", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseError();

    [GeneratedRegex(@"ECONNREFUSED|ENOTFOUND|EAI_AGAIN|No connection could be made because the target machine actively refused|Connection refused|Name or service not known|No such host is known|getaddrinfo|Connection timed out|HttpRequestException: (?:No such host|Connection refused)", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkError();

    [GeneratedRegex(@"Could not find (?:a part of the path|file) '[^']*(?:appsettings|\.config|\.env)[^']*'|The configuration file '[^']*' was not found|Missing required configuration|KeyError: '[A-Z0-9_]+'.*environ|environment variable [A-Z0-9_]+ (?:is )?not set", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigError();
}
