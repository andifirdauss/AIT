using System.Text;
using System.Text.RegularExpressions;

namespace AIThread.Service.Security;

// What counts as a secret in a text and which part of it is the value: private key blocks, well-known token formats,
// passwords inside connection strings and URLs, and values assigned to a key that names a secret (Password, ApiKey,
// DB_PASSWORD, Jwt:Key, ...). Only the value is replaced, so the model still sees which setting exists. Pure functions:
// the placeholder comes from the caller (mask), which remembers the value so it can be written back (SecretGuard).
public static partial class SecretRules
{
    public const string PlaceholderPrefix = "{{AIT_SECRET:";
    public const int MinValueChars = 4;
    public const int MaxValueChars = 512;
    private const int MaxLabelChars = 48;

    // Words that make a key a secret on their own, as one segment of the key ("DB_PASSWORD", "smtpPassword").
    private static readonly HashSet<string> SecretWords = new(StringComparer.Ordinal)
    {
        "pwd", "pass", "passwd", "passphrase", "credential", "credentials", "pat", "apikey", "accesskey", "privatekey",
        "signingkey", "encryptionkey", "masterkey", "authkey", "licensekey", "appkey", "secretkey", "accountkey",
        "sharedaccesskey", "hmackey"
    };

    // The word before "key" that makes "<word> key" a secret ("ApiKey", "Jwt:Key", "APP_KEY").
    private static readonly HashSet<string> KeyPrefixes = new(StringComparer.Ordinal)
    {
        "api", "access", "private", "signing", "encryption", "master", "auth", "license", "app", "secret", "account",
        "hmac", "jwt", "client", "subscription", "consumer", "shared", "storage", "webhook"
    };

    // A last segment that makes the key a setting about the secret, not the secret ("PasswordLength", "TokenUrl").
    private static readonly HashSet<string> SettingSuffixes = new(StringComparer.Ordinal)
    {
        "length", "policy", "policies", "hint", "label", "placeholder", "field", "fields", "url", "uri", "endpoint",
        "expiry", "expiration", "expires", "expire", "lifetime", "ttl", "type", "name", "path", "file", "dir", "header",
        "prefix", "format", "regex", "pattern", "message", "text", "title", "id", "enabled", "enable", "required", "count",
        "minutes", "seconds", "hours", "days", "timeout", "size", "mode", "provider", "validator", "validation", "options",
        "option", "version", "location", "rule", "rules", "strength", "algorithm", "column", "param", "parameter",
        "property", "attribute", "variable", "var", "env", "store", "manager", "service", "handler", "factory", "generator",
        "hasher", "reset", "changed", "confirmed", "issuer", "audience", "scheme", "kind", "source", "input"
    };

    // Values that are a type, a flag or the key's own name rather than a secret.
    private static readonly HashSet<string> NotSecretValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "none", "nil", "true", "false", "yes", "no", "undefined", "string", "str", "int", "bool", "boolean",
        "number", "any", "object", "required", "optional", "empty", "secret", "password", "token", "secretstr", "bytes",
        "unknown", "default", "redacted", "masked", "hidden"
    };

    private static readonly (Regex Pattern, string Label, Func<string, bool> Accept)[] Tokens =
    [
        (AwsAccessKey(), "aws-access-key", _ => true),
        (GitHubToken(), "github-token", _ => true),
        (GitLabToken(), "gitlab-token", _ => true),
        (AnthropicKey(), "anthropic-key", _ => true),
        (OpenAiKey(), "openai-key", LooksRandom),
        (SlackToken(), "slack-token", _ => true),
        (GoogleApiKey(), "google-api-key", _ => true),
        (NpmToken(), "npm-token", _ => true),
        (StripeKey(), "stripe-key", _ => true),
        (Jwt(), "jwt", _ => true),
        (TelegramToken(), "telegram-bot-token", _ => true)
    ];

    // The placeholder of a value: the label tells the model what it stands for, the id lets AIT write the value back.
    public static string Placeholder(string label, string id) => $"{PlaceholderPrefix}{Label(label)}:{id}}}}}";

    // config: the text is a whole config file (.yml, .ini, .properties, .toml, .env, ...), so unquoted "key: value"
    // lines count too; in code or prose they would catch type hints and variables. extraKeys: key names the project
    // marks as secret (.aithread/project.json "secrets": { "keys": [...] }).
    public static string Redact(string text, bool config, IReadOnlyCollection<string>? extraKeys, Func<string, string, string> mask)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            text = PrivateKeyBlock().Replace(text, m => mask("private-key", m.Value));
            foreach (var (pattern, label, accept) in Tokens)
                text = pattern.Replace(text, m => accept(m.Value) ? mask(label, m.Value) : m.Value);

            text = ConnectionPart().Replace(text, m => ReplaceValue(m, m.Groups["key"].Value, mask, minChars: 1));
            text = UrlCredentials().Replace(text, m => ReplaceValue(m, "url-password", mask, minChars: 1));
            text = XmlAddKey().Replace(text, m => IsSecretKey(m.Groups["key"].Value, extraKeys) ? ReplaceValue(m, m.Groups["key"].Value, mask) : m.Value);
            text = XmlElement().Replace(text, m => IsSecretKey(m.Groups["key"].Value, extraKeys) ? ReplaceValue(m, m.Groups["key"].Value, mask) : m.Value);
            var source = text;
            text = QuotedPair().Replace(source, m => IsSecretKey(Qualified(source, m, yaml: false), extraKeys) ? ReplaceValue(m, m.Groups["key"].Value, mask) : m.Value);
            text = EnvLine().Replace(text, m => IsSecretKey(m.Groups["key"].Value, extraKeys) ? ReplaceValue(m, m.Groups["key"].Value, mask) : m.Value);
            if (config)
            {
                source = text;
                text = ConfigLine().Replace(source, m => IsSecretKey(Qualified(source, m, yaml: true), extraKeys) ? ReplaceValue(m, m.Groups["key"].Value, mask) : m.Value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological text: what was replaced so far stays replaced; the vault's known values still apply.
        }
        return text;
    }

    // The placeholders in a text, with their ids.
    public static IEnumerable<(string Placeholder, string Id)> Placeholders(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(PlaceholderPrefix, StringComparison.Ordinal)) yield break;
        foreach (Match match in PlaceholderPattern().Matches(text)) yield return (match.Value, match.Groups["id"].Value);
    }

    public static string ReplacePlaceholders(string text, Func<string, string, string> replace) =>
        string.IsNullOrEmpty(text) || !text.Contains(PlaceholderPrefix, StringComparison.Ordinal)
            ? text
            : PlaceholderPattern().Replace(text, m => replace(m.Value, m.Groups["id"].Value));

    // Config files whose unquoted "key: value" / "key=value" lines are settings, not code.
    public static bool IsConfigFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var name = Path.GetFileName(path);
        if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return true;
        return Path.GetExtension(name).ToLowerInvariant() is ".yml" or ".yaml" or ".ini" or ".properties" or ".toml" or ".conf" or ".cfg"
            or ".env" or ".envrc" or ".editorconfig";
    }

    // A key names a secret when one of its words does ("DB_PASSWORD", "smtpPassword", "ApiKey", "Jwt:Key") and its last
    // word is not about the secret ("PasswordLength", "TokenEndpoint").
    public static bool IsSecretKey(string key, IReadOnlyCollection<string>? extraKeys = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (extraKeys is { Count: > 0 })
        {
            var last = key.Split(['.', ':', '/', '_', '-'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? key;
            if (extraKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) || k.Equals(last, StringComparison.OrdinalIgnoreCase))) return true;
        }

        var segments = Segments(key);
        if (segments.Count == 0 || SettingSuffixes.Contains(segments[^1])) return false;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (SecretWords.Contains(segment)
                || segment.Contains("password", StringComparison.Ordinal)
                || segment.Contains("secret", StringComparison.Ordinal)
                || segment == "tokens" || segment.EndsWith("token", StringComparison.Ordinal))
                return true;
            if (i + 1 < segments.Count && KeyPrefixes.Contains(segment) && segments[i + 1] is "key" or "keys") return true;
        }
        return false;
    }

    // A value that stands for a secret instead of being one: a reference (${DB_PASS}, %PASS%, {{password}}), a mask
    // (*****), a type or flag, or text with spaces.
    public static bool IsPlaceholderLike(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Contains(PlaceholderPrefix, StringComparison.Ordinal)) return true;
        if (trimmed.Any(char.IsWhiteSpace)) return true;
        if (trimmed.StartsWith("${", StringComparison.Ordinal) || trimmed.StartsWith("$(", StringComparison.Ordinal)
            || trimmed.StartsWith("#{", StringComparison.Ordinal) || trimmed.StartsWith("{{", StringComparison.Ordinal)
            || trimmed.StartsWith("@{", StringComparison.Ordinal) || trimmed.StartsWith("<%", StringComparison.Ordinal)
            || trimmed.StartsWith("%(", StringComparison.Ordinal))
            return true;
        if (trimmed.Length > 1 && trimmed[0] == '$' && char.IsLetter(trimmed[1])) return true;
        if (trimmed.Length > 2 && ((trimmed[0] == '%' && trimmed[^1] == '%') || (trimmed[0] == '<' && trimmed[^1] == '>'))) return true;
        if (trimmed.All(c => c == trimmed[0])) return true;
        return NotSecretValues.Contains(trimmed);
    }

    // Long enough and mixed enough that replacing it wherever it appears does not hit ordinary words or code. A name
    // made of letters only (LunchClaimRateHeaderDto, NewESS.Common.Dto, ref_token) reads as code: masked everywhere, it
    // would hide class and file names from the planner, and the files behind them could no longer be read or edited.
    public static bool IsDistinctive(string value)
    {
        if (value.Length < 8 || value.Length > MaxValueChars || value.Any(char.IsWhiteSpace)) return false;
        if (CodeName().IsMatch(value)) return false;
        var classes = 0;
        if (value.Any(char.IsLower)) classes++;
        if (value.Any(char.IsUpper)) classes++;
        if (value.Any(char.IsDigit)) classes++;
        if (value.Any(c => !char.IsLetterOrDigit(c))) classes++;
        return classes >= 2;
    }

    // A bare "Key" says nothing on its own; nested settings name the secret in the object around it:
    // "Jwt": { "Key": "…" } (JSON, C# object initializers) or jwt:\n  key: … (YAML) read as "Jwt:Key".
    private static string Qualified(string text, Match match, bool yaml)
    {
        var key = match.Groups["key"].Value;
        if (!key.Equals("key", StringComparison.OrdinalIgnoreCase) && !key.Equals("keys", StringComparison.OrdinalIgnoreCase)) return key;
        var parent = yaml ? YamlParent(text, match.Index) : BraceParent(text, match.Index);
        return parent is null ? key : $"{parent}:{key}";
    }

    // The name before the "{" that encloses index: "Jwt": {, Jwt = new {, jwt: {.
    private static string? BraceParent(string text, int index)
    {
        const int maxLookBack = 4000;
        var depth = 0;
        var brace = -1;
        for (var i = index - 1; i >= Math.Max(0, index - maxLookBack); i--)
        {
            if (text[i] == '}') depth++;
            else if (text[i] == '{' && depth-- == 0)
            {
                brace = i;
                break;
            }
        }
        if (brace < 0) return null;

        var end = brace - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        if (end >= 3 && text.AsSpan(end - 2, 3).SequenceEqual("new")) end -= 3;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        if (end < 0 || text[end] is not (':' or '=')) return null;
        end--;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        if (end >= 0 && text[end] is '"' or '\'') end--;

        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] is '_' or '-' or '.' or '$')) start--;
        return end > start ? text[(start + 1)..(end + 1)] : null;
    }

    // The nearest line above with less indentation that opens a mapping ("jwt:").
    private static string? YamlParent(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var indent = 0;
        while (lineStart + indent < text.Length && text[lineStart + indent] is ' ' or '\t') indent++;

        var cursor = lineStart - 1;
        while (cursor > 0)
        {
            var previousStart = text.LastIndexOf('\n', cursor - 1) + 1;
            var line = text[previousStart..cursor].TrimEnd('\r');
            cursor = previousStart - 1;
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            if (line.Length - trimmed.Length >= indent) continue;
            return YamlMapping().Match(trimmed) is { Success: true } mapping ? mapping.Groups["key"].Value : null;
        }
        return null;
    }

    // "DB_PASSWORD" → db, password; "smtpPassword" → smtp, password; "Jwt:Key" → jwt, key; "APIKey" → api, key.
    private static List<string> Segments(string key)
    {
        var spaced = CamelBoundary().Replace(key, " ");
        return Separator().Split(spaced)
            .Where(s => s.Length > 0)
            .Select(s => s.ToLowerInvariant())
            .ToList();
    }

    // Replaces only the "value" group of a match; minChars guards rules where short values are likely not secrets.
    private static string ReplaceValue(Match match, string label, Func<string, string, string> mask, int minChars = MinValueChars)
    {
        var group = match.Groups["value"];
        var value = group.Value;
        var unquoted = value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'' ? value[1..^1] : value;
        if (unquoted.Length < minChars || unquoted.Length > MaxValueChars || IsPlaceholderLike(unquoted)) return match.Value;

        var start = group.Index - match.Index + (unquoted.Length == value.Length ? 0 : 1);
        return string.Concat(match.Value.AsSpan(0, start), mask(label, unquoted), match.Value.AsSpan(start + unquoted.Length));
    }

    private static bool LooksRandom(string value) =>
        value.Count(char.IsDigit) >= 2 && value.Any(char.IsUpper) && value.Any(char.IsLower);

    private static string Label(string label)
    {
        var builder = new StringBuilder(Math.Min(label.Length, MaxLabelChars));
        foreach (var c in label.Length > MaxLabelChars ? label[^MaxLabelChars..] : label)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '_');
        return builder.Length == 0 ? "secret" : builder.ToString();
    }

    [GeneratedRegex(@"\{\{AIT_SECRET:[A-Za-z0-9_.\-]{0,64}:(?<id>[0-9a-f]{10})\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"-----BEGIN (?<kind>[A-Z0-9 ]*)PRIVATE KEY-----[\s\S]{16,16384}?-----END \k<kind>PRIVATE KEY-----", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{22,255})\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bglpat-[A-Za-z0-9_\-]{20,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex GitLabToken();

    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_\-]{20,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"\bsk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_\-]{20,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex OpenAiKey();

    [GeneratedRegex(@"\bxox[abposr]-[A-Za-z0-9\-]{10,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_\-]{35}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex GoogleApiKey();

    [GeneratedRegex(@"\bnpm_[A-Za-z0-9]{36}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex NpmToken();

    [GeneratedRegex(@"\b(?:sk|rk)_live_[A-Za-z0-9]{20,}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex StripeKey();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{8,}\.eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{16,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b\d{8,10}:AA[A-Za-z0-9_\-]{33}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TelegramToken();

    // "Server=.;Password=abc;" — no spaces around "=", so "password = GetIt();" in code is left alone.
    [GeneratedRegex(@"(?<=^|[;""'`\s(])(?<key>Password|Pwd|AccountKey|SharedAccessKey|SharedAccessSignature)=(?<value>'[^'\r\n]*'|""[^""\r\n]*""|[^;""'`\s<>()]+)(?=[;""'`\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConnectionPart();

    // "postgres://user:pass@host" — only the password.
    [GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]{1,20}://[^:/?#@\s'""]{1,128}:(?<value>[^@/?#\s'""]{1,256})@", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UrlCredentials();

    // <add key="SmtpPassword" value="..." /> (web.config, app.config).
    [GeneratedRegex(@"<add\s+(?:key|name)\s*=\s*""(?<key>[^""]{1,128})""\s+(?:value|connectionString)\s*=\s*""(?<value>[^""\r\n]{1,512})""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex XmlAddKey();

    // <Password>...</Password>
    [GeneratedRegex(@"<(?<key>[A-Za-z_][\w.\-:]{0,63})>(?<value>[^<\r\n]{1,512})</\k<key>>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex XmlElement();

    // "Password": "x" (JSON), Password = "x" (C#, Python, .env), password: 'x' (YAML, JS), 'password' => 'x' (PHP),
    // password="x" (XML attributes).
    [GeneratedRegex(@"(?<kq>[""']?)(?<key>[A-Za-z_$@][\w.$@\-:/]{0,127})\k<kq>\s*(?:=>|:=|:|=)\s*(?<value>""(?:\\.|[^""\\\r\n]){1,512}""|'(?:\\.|[^'\\\r\n]){1,512}')",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex QuotedPair();

    // DB_PASSWORD=abc (.env, shell, Dockerfile ENV, docker-compose lists); also after the "12| " of read_file and the
    // "path:12: " of search_text, and after a diff's "+".
    [GeneratedRegex(@"^(?<lead>(?:[ \t]*\d+\|[ \t]?|\S+:\d+:[ \t])?[+\- ]?[ \t]*(?:-[ \t]+)?(?:export[ \t]+|set[ \t]+|ENV[ \t]+|ARG[ \t]+)?)(?<key>[A-Z][A-Z0-9_]{1,127})[ \t]*=[ \t]*(?<value>[^\s""'#`][^\s]*)(?:[ \t]+#[^\r\n]*)?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex EnvLine();

    // password: abc / spring.datasource.password=abc in config files.
    [GeneratedRegex(@"^(?<lead>[ \t]*(?:-[ \t]+)?)(?<kq>[""']?)(?<key>[A-Za-z_][\w.\-]{0,127})\k<kq>[ \t]*[:=][ \t]*(?<value>[^\s""'#`\[{&*|>!%@][^\s]*)(?:[ \t]+[#;][^\r\n]*)?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConfigLine();

    [GeneratedRegex(@"^(?:-[ \t]+)?[""']?(?<key>[A-Za-z_][\w.\-]{0,127})[""']?[ \t]*:[ \t]*(?:#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex YamlMapping();

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"[^A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();

    [GeneratedRegex(@"^[A-Za-z_]+(?:[.\-/\\:][A-Za-z_]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeName();
}
