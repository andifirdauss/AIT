using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

// Codex CLI: a "model_providers.aithread" entry plus a "profiles.aithread" profile at the end of
// $CODEX_HOME/config.toml (default ~/.codex), so "codex --profile aithread" goes through AiThread while plain codex
// keeps its own provider. Codex strips model_provider(s) and profiles from a project's .codex/config.toml, so there
// is no project scope. Codex only speaks the Responses API to a custom provider; the key is sent as
// Authorization: Bearer. An orchestrated thread can stay quiet for minutes, hence the long stream idle timeout.
public sealed partial class CodexIntegration(ISettingService settingService, string? configPath = null) : FileCliIntegration(settingService)
{
    public const string Id = "codex";
    public const string ProviderId = "aithread";
    private const string FileName = "config.toml";

    private readonly string _configPath = configPath ?? Path.Combine(CodexHome, FileName);

    private static string CodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(UserProfile, ".codex");

    public override CliInfoDto Info => new(
        Id,
        "Codex CLI — profile AIT",
        $"Menambah provider dan profile \"{ProviderId}\" di config.toml Codex. Jalankan codex --profile {ProviderId} (atau -p {ProviderId}); codex tanpa profile tetap memakai provider aslinya.",
        _configPath,
        null,
        $"Berlaku untuk sesi codex berikutnya yang dijalankan dengan --profile {ProviderId}. Project terdeteksi dari folder tempat codex dijalankan.");

    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";

    protected override string? CurrentBaseUrl(string content) =>
        ManagedTextBlock.Managed(content)?.Select(l => BaseUrlLine().Match(l)).FirstOrDefault(m => m.Success)?.Groups["url"].Value;

    protected override string Apply(string content, CliEntry entry)
    {
        if (Conflict(content) is { } conflict) throw new AppValidationException(conflict);
        return ManagedTextBlock.Append(content, Block(entry));
    }

    protected override string Remove(string content) => ManagedTextBlock.Remove(content);

    protected override string Preview(CliEntry entry) =>
        string.Join("\n", Block(entry).Prepend(ManagedTextBlock.Begin).Append(ManagedTextBlock.End));

    private static IReadOnlyList<string> Block(CliEntry entry) =>
    [
        $"[model_providers.{ProviderId}]",
        "name = \"AIT\"",
        $"base_url = {YamlProviderBlock.Quote(entry.BaseUrl)}",
        "wire_api = \"responses\"",
        $"experimental_bearer_token = {YamlProviderBlock.Quote(entry.ApiKey)}",
        "stream_idle_timeout_ms = 1800000",
        string.Empty,
        $"[profiles.{ProviderId}]",
        $"model_provider = \"{ProviderId}\""
    ];

    // A user-written entry with our id, or an inline table we cannot add a key to, makes appending unsafe.
    public static string? Conflict(string content)
    {
        string? table = null;
        foreach (var raw in ManagedTextBlock.Outside(content))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (TableHeader().Match(line) is { Success: true } header)
            {
                table = Normalize(header.Groups["name"].Value);
                if (IsOurs(table))
                    return $"{FileName} sudah punya [{table}] buatan Anda. Hapus atau ganti namanya dulu.";
                continue;
            }

            if (KeyLine().Match(line) is not { Success: true } key) continue;
            var name = Normalize(key.Groups["key"].Value);
            if (table is null && name is "model_providers" or "profiles")
                return $"{FileName} menulis {name} sebagai inline table ({name} = {{ ... }}). Ubah ke bentuk [{name}.<id>] dulu.";
            var fullName = table is null ? name : $"{table}.{name}";
            if (IsOurs(fullName))
                return $"{FileName} sudah punya {fullName} buatan Anda. Hapus atau ganti namanya dulu.";
        }
        return null;
    }

    // model_providers.aithread / profiles.aithread, or a key below either.
    private static bool IsOurs(string dotted) =>
        new[] { $"model_providers.{ProviderId}", $"profiles.{ProviderId}" }.Any(t => dotted == t || dotted.StartsWith(t + "."));

    private static string Normalize(string dotted) => Regex.Replace(dotted, @"[\s""']", string.Empty);

    [GeneratedRegex(@"^\[\[?(?<name>[^\]]+)\]\]?\s*(#.*)?$")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_\-.""' ]+?)\s*=")]
    private static partial Regex KeyLine();

    [GeneratedRegex(@"^\s*base_url\s*=\s*""(?<url>[^""]*)""")]
    private static partial Regex BaseUrlLine();
}
