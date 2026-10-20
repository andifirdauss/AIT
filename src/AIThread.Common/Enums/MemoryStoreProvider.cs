namespace AIThread.Common.Enums;

// Where short or long memory is kept (menu Setting → Penyimpanan memory).
public enum MemoryStoreProvider
{
    // Built in: short memory in the embedded Garnet server (ConnectionStrings:Redis), long memory in the main database.
    Default = 0,

    // External Redis or any Redis-protocol server. Short memory only.
    Redis = 1,

    SqlServer = 2,
    PostgreSql = 3,
    MySql = 4,
    Oracle = 5,

    // Local file; Database holds the file path.
    Sqlite = 6
}
