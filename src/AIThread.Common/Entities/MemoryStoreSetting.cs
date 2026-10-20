using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// Connection of one memory level, stored as columns of SystemSetting (owned type).
public class MemoryStoreSetting
{
    public MemoryStoreProvider Provider { get; set; } = MemoryStoreProvider.Default;

    public string? Host { get; set; }
    public int? Port { get; set; }

    // Database name; Oracle service name; Redis database index; SQLite file path.
    public string? Database { get; set; }

    public string? Username { get; set; }

    // Encrypted with ASP.NET Core Data Protection; never exposed through the API.
    public string? PasswordProtected { get; set; }

    // Extra connection-string keywords ("SSL Mode=Require;Pooling=false").
    public string? Options { get; set; }
}
