namespace AIThread.Common.Enums;

// Which environment a project database belongs to. Prod changes are never "always allowed", and AIT suggests a local
// rehearsal (a SQLite copy of the touched tables) before running one.
public enum DatabaseEnvironment
{
    Dev = 1,
    Staging = 2,
    Prod = 3
}
