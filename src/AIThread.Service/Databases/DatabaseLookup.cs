namespace AIThread.Service.Databases;

// The database a tool call names (by name, case ignored; the only one when there is just one); error tells the model
// what to do instead.
public static class DatabaseLookup
{
    public static DatabaseTarget? Find(IReadOnlyList<DatabaseTarget>? databases, string? name, out string? error)
    {
        error = null;
        if (databases is not { Count: > 0 })
        {
            error = "Error: this project has no databases set up (Project > Detail → Database).";
            return null;
        }
        var found = string.IsNullOrWhiteSpace(name)
            ? databases.Count == 1 ? databases[0] : null
            : databases.FirstOrDefault(d => string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null)
            error = $"Error: {(string.IsNullOrWhiteSpace(name) ? "\"database\" is required" : $"there is no database named '{name}'")}. Databases: {string.Join(", ", databases.Select(d => d.Name))}.";
        return found;
    }
}
