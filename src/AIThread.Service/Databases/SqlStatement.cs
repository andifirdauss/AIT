using System.Text;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public enum SqlStatementKind
{
    Read = 1,
    Write = 2,
    Refused = 3
}

// What a statement is: a read (SELECT / WITH … SELECT), a DML write on one table (INSERT, UPDATE, DELETE, MERGE), or
// refused, with the reason (for the model). Tables: every table named after FROM/JOIN/INTO/UPDATE/USING, for a rehearsal.
public sealed record SqlClassification(SqlStatementKind Kind, string? Verb, TableName? Target, IReadOnlyList<TableName> Tables, string? Reason)
{
    public static SqlClassification Refuse(string reason) => new(SqlStatementKind.Refused, null, null, [], reason);

    // What the backup of a write must hold (BackupScope); the whole table whenever the statement cannot be read exactly.
    public BackupScope Scope { get; init; } = BackupScope.WholeTable;
}

// Reads one SQL statement without running it: comments and string literals are skipped, quoted names kept exact,
// unquoted names folded the way the engine folds them. Deliberately strict: anything it cannot place is refused, since
// the result decides whether AIT may run the statement at all.
public static class SqlStatement
{
    private static readonly HashSet<string> WriteVerbs = new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE", "MERGE" };

    private static readonly HashSet<string> RefusedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "DROP", "TRUNCATE", "RENAME", "GRANT", "REVOKE", "DENY", "EXEC", "EXECUTE", "CALL", "DO", "BEGIN",
        "COMMIT", "ROLLBACK", "SAVEPOINT", "SET", "USE", "DECLARE", "LOCK", "UNLOCK", "VACUUM", "ANALYZE", "ATTACH", "DETACH",
        "PRAGMA", "COPY", "LOAD", "REPLACE", "UPSERT", "REINDEX", "CLUSTER", "COMMENT", "SHUTDOWN", "KILL", "BACKUP",
        "RESTORE", "DBCC", "BULK", "OPTIMIZE", "REFRESH", "DISCARD", "NOTIFY", "LISTEN", "PREPARE", "DEALLOCATE", "HANDLER",
        "FLUSH", "RESET", "PURGE", "AUDIT", "NOAUDIT", "EXPLAIN", "SHOW", "DESCRIBE", "DESC"
    };

    // Words that end a table reference in FROM/JOIN lists.
    private static readonly HashSet<string> ClauseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "SET", "FROM", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "USING", "GROUP", "ORDER",
        "HAVING", "LIMIT", "OFFSET", "FETCH", "UNION", "EXCEPT", "INTERSECT", "MINUS", "VALUES", "SELECT", "OUTPUT", "RETURNING",
        "WHEN", "THEN", "AS", "WITH", "NATURAL", "LATERAL", "APPLY", "WINDOW", "FOR", "INTO", "DEFAULT", "TOP", "OVER"
    };

    private enum TokenType
    {
        Word,
        Quoted,
        String,
        Number,
        Symbol
    }

    // Words that end the WHERE of an UPDATE/DELETE (what follows limits or returns rows, it does not choose them).
    private static readonly HashSet<string> FilterEndWords = new(StringComparer.OrdinalIgnoreCase) { "ORDER", "LIMIT", "RETURNING", "OPTION", "OFFSET", "FETCH" };

    // In a WHERE, these make the rows it matches differ from one evaluation to the next: the backup's SELECT and the
    // statement could then pick different rows, so such a statement is backed up as a whole table.
    private static readonly HashSet<string> NondeterministicWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "RAND", "RANDOM", "NEWID", "NEWSEQUENTIALID", "UUID", "GEN_RANDOM_UUID", "SYS_GUID", "DBMS_RANDOM", "TOP", "LIMIT",
        "FETCH", "ROWNUM", "SAMPLE", "TABLESAMPLE"
    };

    // MySQL modifiers between the verb and the table (UPDATE LOW_PRIORITY IGNORE t …).
    private static readonly HashSet<string> VerbModifiers = new(StringComparer.OrdinalIgnoreCase) { "LOW_PRIORITY", "HIGH_PRIORITY", "DELAYED", "QUICK", "IGNORE" };

    // Start/End: where the token is in the statement text (End exclusive), so parts of it can be reused verbatim.
    private sealed record Token(TokenType Type, string Text, int Depth, int Start = 0, int End = 0)
    {
        public bool Is(string word) => Type == TokenType.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

        public bool IsName => Type is TokenType.Word or TokenType.Quoted;
    }

    public static SqlClassification Classify(string sql, MemoryStoreProvider provider)
    {
        if (string.IsNullOrWhiteSpace(sql)) return SqlClassification.Refuse("the statement is empty");

        List<Token> tokens;
        try
        {
            tokens = Tokenize(sql);
        }
        catch (FormatException ex)
        {
            return SqlClassification.Refuse(ex.Message);
        }

        // One statement: a trailing semicolon is fine, a second statement is not.
        var semicolon = tokens.FindIndex(t => t.Type == TokenType.Symbol && t.Text == ";");
        if (semicolon >= 0)
        {
            if (tokens.Skip(semicolon + 1).Any(t => !(t.Type == TokenType.Symbol && t.Text == ";")))
                return SqlClassification.Refuse("only one statement is allowed per call (found more than one, separated by ';')");
            tokens = tokens.Take(semicolon).ToList();
        }
        if (tokens.Count == 0) return SqlClassification.Refuse("the statement is empty");

        var first = tokens[0];
        if (first.Type != TokenType.Word) return SqlClassification.Refuse("the statement must start with SELECT, WITH, INSERT, UPDATE, DELETE or MERGE");
        if (RefusedVerbs.Contains(first.Text))
            return SqlClassification.Refuse($"{first.Text.ToUpperInvariant()} is not allowed: only reads (SELECT) and row changes (INSERT, UPDATE, DELETE, MERGE) can run; schema changes go through a migration in the code");

        // The main verb: the first one, or after the CTEs of a WITH (the first top-level word that is not part of them).
        var verbIndex = 0;
        if (first.Is("WITH"))
        {
            // The verbs are reserved words, so none of them can be a CTE name.
            verbIndex = tokens.FindIndex(1, t => t.Depth == 0 && t.Type == TokenType.Word && (t.Is("SELECT") || WriteVerbs.Contains(t.Text)));
            if (verbIndex < 0) return SqlClassification.Refuse("a WITH statement must end in SELECT, INSERT, UPDATE, DELETE or MERGE");
        }

        // Anything that writes or runs code anywhere at the top level (e.g. a SELECT … INTO, or EXEC inside) is refused.
        foreach (var token in tokens.Where(t => t.Type == TokenType.Word))
        {
            if (token.Is("EXEC") || token.Is("EXECUTE") || token.Is("CALL"))
                return SqlClassification.Refuse("running procedures (EXEC/CALL) is not allowed");
        }

        // A write inside a CTE or subquery (PostgreSQL's WITH x AS (DELETE … RETURNING …)) changes a table the backup does
        // not cover. SELECT … FOR UPDATE only locks.
        for (var t = 0; t < tokens.Count; t++)
        {
            var token = tokens[t];
            if (token.Depth == 0 || token.Type != TokenType.Word || !WriteVerbs.Contains(token.Text)) continue;
            if (token.Is("UPDATE") && t > 0 && tokens[t - 1].Is("FOR")) continue;
            return SqlClassification.Refuse("a write inside a subquery or CTE is not allowed: change one table per statement");
        }

        var verb = tokens[verbIndex].Text.ToUpperInvariant();
        if (verb == "INSERT" && verbIndex + 1 < tokens.Count && (tokens[verbIndex + 1].Is("ALL") || tokens[verbIndex + 1].Is("FIRST")))
            return SqlClassification.Refuse("a multi-table INSERT ALL/FIRST changes several tables; change one table per statement");
        var output = tokens.FindIndex(t => t.Depth == 0 && t.Is("OUTPUT"));
        if (output >= 0 && tokens.Skip(output + 1).Any(t => t.Depth == 0 && t.Is("INTO")))
            return SqlClassification.Refuse("OUTPUT … INTO writes a second table; change one table per statement");
        var tables = Tables(tokens, provider);
        if (verb == "SELECT")
        {
            if (tokens.Any(t => t.Depth == 0 && t.Is("INTO")))
                return SqlClassification.Refuse("SELECT … INTO creates or fills a table; use INSERT … SELECT through change_database instead");
            if (tokens.Skip(verbIndex + 1).Any(t => t.Depth == 0 && t.Type == TokenType.Word && WriteVerbs.Contains(t.Text) && !t.Is("UPDATE")))
                return SqlClassification.Refuse("the statement mixes a read with a write");
            return new SqlClassification(SqlStatementKind.Read, verb, null, tables, null);
        }

        if (!WriteVerbs.Contains(verb)) return SqlClassification.Refuse($"{verb} is not allowed");
        var target = WriteTarget(tokens, verbIndex, provider);
        if (target is null) return SqlClassification.Refuse($"the table that {verb} changes could not be read from the statement; name it plainly (e.g. {verb} {(verb == "INSERT" ? "INTO " : verb == "DELETE" ? "FROM " : "")}schema.table …)");
        return new SqlClassification(SqlStatementKind.Write, verb, target, tables.Any(t => t == target) ? tables : [target, .. tables], null)
        {
            Scope = ScopeOf(sql, tokens, verbIndex, verb, first.Is("WITH"), provider)
        };
    }

    // UPDATE/DELETE on one table with a plain WHERE: only the rows it matches (Rows). An INSERT that cannot touch existing
    // rows: none, the keys it adds are recorded when it runs (Insert). Anything else (MERGE, upserts, UPDATE … FROM,
    // DELETE … USING, a CTE, no WHERE, a WHERE that is not deterministic): the whole table.
    private static BackupScope ScopeOf(string sql, List<Token> tokens, int verbIndex, string verb, bool withCte, MemoryStoreProvider provider)
    {
        if (verb == "INSERT")
            return tokens.Any(t => t.Depth == 0 && (t.Is("UPDATE") || t.Is("DUPLICATE") || t.Is("REPLACE"))) ? BackupScope.WholeTable : BackupScope.NewRows;
        if (withCte || verb is not ("UPDATE" or "DELETE")) return BackupScope.WholeTable;

        var i = SkipTop(tokens, verbIndex + 1);
        while (i < tokens.Count && tokens[i].Type == TokenType.Word && VerbModifiers.Contains(tokens[i].Text)) i++;
        if (verb == "DELETE" && i < tokens.Count && tokens[i].Is("FROM")) i++;
        if (i < tokens.Count && tokens[i].Is("ONLY")) i++;
        var nameStart = i;
        if (ReadName(tokens, ref i, provider) is null) return BackupScope.WholeTable;

        // An alias, with or without AS.
        if (i < tokens.Count && tokens[i].Is("AS")) i++;
        if (i < tokens.Count && tokens[i].IsName && !(tokens[i].Type == TokenType.Word && ClauseWords.Contains(tokens[i].Text))) i++;
        var source = sql[tokens[nameStart].Start..tokens[i - 1].End];

        if (i >= tokens.Count || !tokens[i].Is(verb == "UPDATE" ? "SET" : "WHERE")) return BackupScope.WholeTable;
        var rest = tokens.Skip(i).Where(t => t.Depth == 0).ToList();
        if (rest.Any(t => t.Is("FROM") || t.Is("JOIN") || t.Is("USING") || t.Is("APPLY") || t.Is("INTO"))) return BackupScope.WholeTable;
        var wheres = rest.Where(t => t.Is("WHERE")).ToList();
        if (wheres.Count != 1) return BackupScope.WholeTable;

        var w = tokens.IndexOf(wheres[0]);
        if (w + 1 >= tokens.Count || tokens[w + 1].Is("CURRENT")) return BackupScope.WholeTable;
        var end = tokens.FindIndex(w + 1, t => t.Depth == 0 && t.Type == TokenType.Word && FilterEndWords.Contains(t.Text));
        if (end < 0) end = tokens.Count;
        if (end <= w + 1) return BackupScope.WholeTable;
        if (tokens.Skip(w + 1).Take(end - w - 1).Any(t => t.Type == TokenType.Word && NondeterministicWords.Contains(t.Text))) return BackupScope.WholeTable;
        return BackupScope.Matching(source, sql[tokens[w + 1].Start..tokens[end - 1].End]);
    }

    // Past SQL Server's TOP (n) / TOP n.
    private static int SkipTop(List<Token> tokens, int i)
    {
        if (i >= tokens.Count || !tokens[i].Is("TOP")) return i;
        i++;
        if (i < tokens.Count && tokens[i].Text == "(")
        {
            var depth = tokens[i].Depth;
            i++;
            while (i < tokens.Count && !(tokens[i].Text == ")" && tokens[i].Depth == depth)) i++;
        }
        return i + 1;
    }

    // The table named after INSERT INTO / UPDATE / DELETE FROM / MERGE INTO; an alias (SQL Server's UPDATE o … FROM t o)
    // is resolved through the FROM list.
    private static TableName? WriteTarget(List<Token> tokens, int verbIndex, MemoryStoreProvider provider)
    {
        var i = SkipTop(tokens, verbIndex + 1);
        while (i < tokens.Count && tokens[i].Type == TokenType.Word && VerbModifiers.Contains(tokens[i].Text)) i++;
        if (i < tokens.Count && (tokens[i].Is("INTO") || tokens[i].Is("FROM"))) i++;
        if (i < tokens.Count && tokens[i].Is("ONLY")) i++;

        var name = ReadName(tokens, ref i, provider, allowColumnList: true);
        if (name is null) return null;

        // A one-part name that is an alias declared in a later FROM/USING (`UPDATE o SET … FROM Orders o`).
        if (name.Schema is null)
        {
            var aliased = ResolveAlias(tokens, i, name.Name, provider);
            if (aliased is not null) return aliased;
        }
        return name;
    }

    private static TableName? ResolveAlias(List<Token> tokens, int start, string alias, MemoryStoreProvider provider)
    {
        for (var i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Depth != 0 || !(tokens[i].Is("FROM") || tokens[i].Is("JOIN"))) continue;
            var j = i + 1;
            var table = ReadName(tokens, ref j, provider);
            if (table is null) continue;
            if (j < tokens.Count && tokens[j].Is("AS")) j++;
            if (j < tokens.Count && tokens[j].IsName && !ClauseWords.Contains(tokens[j].Text)
                && string.Equals(Unquote(tokens[j], provider), alias, StringComparison.OrdinalIgnoreCase))
                return table;
        }
        return null;
    }

    // Every table after FROM, JOIN, INTO, UPDATE and USING (comma lists of FROM included); subqueries at any depth too.
    private static List<TableName> Tables(List<Token> tokens, MemoryStoreProvider provider)
    {
        var tables = new List<TableName>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!(token.Is("FROM") || token.Is("JOIN") || token.Is("INTO") || token.Is("UPDATE") || token.Is("USING"))) continue;
            var j = i + 1;
            if (j < tokens.Count && tokens[j].Is("ONLY")) j++;
            while (true)
            {
                var name = ReadName(tokens, ref j, provider, allowColumnList: token.Is("INTO"));
                if (name is null) break;
                if (!tables.Contains(name)) tables.Add(name);
                // Skip an alias, then go on through a comma list.
                if (j < tokens.Count && tokens[j].Is("AS")) j++;
                if (j < tokens.Count && tokens[j].IsName && !ClauseWords.Contains(tokens[j].Text)) j++;
                if (j < tokens.Count && tokens[j].Text == "," && token.Is("FROM")) j++;
                else break;
            }
        }
        return tables;
    }

    // schema.table or table (a three-part db.schema.table keeps the last two); null at a subquery or a non-name.
    private static TableName? ReadName(List<Token> tokens, ref int i, MemoryStoreProvider provider, bool allowColumnList = false)
    {
        if (i >= tokens.Count || !tokens[i].IsName || (tokens[i].Type == TokenType.Word && ClauseWords.Contains(tokens[i].Text))) return null;
        var parts = new List<string> { Unquote(tokens[i], provider) };
        i++;
        while (i + 1 < tokens.Count && tokens[i].Text == "." && tokens[i + 1].IsName)
        {
            parts.Add(Unquote(tokens[i + 1], provider));
            i += 2;
        }
        // A function call (FROM generate_series(…)) is not a table; INSERT INTO t (a, b) is a column list.
        if (!allowColumnList && i < tokens.Count && tokens[i].Text == "(") return null;
        return parts.Count == 1 ? new TableName(null, parts[0]) : new TableName(parts[^2], parts[^1]);
    }

    private static string Unquote(Token token, MemoryStoreProvider provider) =>
        token.Type == TokenType.Quoted ? token.Text : SqlDialect.FoldUnquoted(provider, token.Text);


    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var depth = 0;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '-' && Peek(sql, i + 1) == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (c == '#' )
            {
                // MySQL line comment.
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (c == '/' && Peek(sql, i + 1) == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("a /* comment is not closed");
                i = end + 2;
                continue;
            }
            if (c == '\'' || ((c == 'N' || c == 'n' || c == 'E' || c == 'e') && Peek(sql, i + 1) == '\''))
            {
                var literalStart = i;
                if (c != '\'') i++;
                i = SkipQuoted(sql, i, '\'', "a string literal is not closed");
                tokens.Add(new Token(TokenType.String, "'…'", depth, literalStart, i));
                continue;
            }
            if (c == '$' && DollarTag(sql, i) is { } tag)
            {
                // PostgreSQL dollar quoting: $$ … $$ or $tag$ … $tag$.
                var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("a $$ string is not closed");
                var dollarStart = i;
                i = end + tag.Length;
                tokens.Add(new Token(TokenType.String, "'…'", depth, dollarStart, i));
                continue;
            }
            if (c is '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                var builder = new StringBuilder();
                var quotedStart = i;
                i++;
                while (true)
                {
                    if (i >= sql.Length) throw new FormatException("a quoted name is not closed");
                    if (sql[i] == close)
                    {
                        if (Peek(sql, i + 1) == close)
                        {
                            builder.Append(close);
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    builder.Append(sql[i]);
                    i++;
                }
                tokens.Add(new Token(TokenType.Quoted, builder.ToString(), depth, quotedStart, i));
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$' or '@' or '#')) i++;
                tokens.Add(new Token(TokenType.Word, sql[start..i], depth, start, i));
                continue;
            }
            if (char.IsDigit(c))
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '.')) i++;
                tokens.Add(new Token(TokenType.Number, sql[start..i], depth, start, i));
                continue;
            }
            if (c == '(')
            {
                tokens.Add(new Token(TokenType.Symbol, "(", depth, i, i + 1));
                depth++;
                i++;
                continue;
            }
            if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                tokens.Add(new Token(TokenType.Symbol, ")", depth, i, i + 1));
                i++;
                continue;
            }
            tokens.Add(new Token(TokenType.Symbol, c.ToString(), depth, i, i + 1));
            i++;
        }
        return tokens;
    }

    private static int SkipQuoted(string sql, int i, char quote, string error)
    {
        i++;
        while (true)
        {
            if (i >= sql.Length) throw new FormatException(error);
            if (sql[i] == quote)
            {
                if (Peek(sql, i + 1) == quote)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
    }

    private static string? DollarTag(string sql, int i)
    {
        var end = sql.IndexOf('$', i + 1);
        if (end < 0) return null;
        var tag = sql[i..(end + 1)];
        return tag.Length == 2 || tag[1..^1].All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? tag : null;
    }

    private static char Peek(string sql, int i) => i < sql.Length ? sql[i] : '\0';
}
