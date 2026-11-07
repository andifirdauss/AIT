namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // One key of a .env file (the last assignment wins, as in dotenv); null when it is not there or cannot be read.
    private static string? EnvValue(string file, string key)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? value = null;
        foreach (var line in lines)
        {
            if (EnvAssignment().Match(line) is { Success: true } match && match.Groups["key"].Value == key) value = match.Groups["value"].Value;
        }
        return value;
    }
}
