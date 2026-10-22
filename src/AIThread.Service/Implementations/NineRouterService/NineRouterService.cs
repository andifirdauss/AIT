using System.Collections.Concurrent;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

// Client for 9router (https://github.com/decolua/9router):
//   GET  /api/health             -> {"ok":true}, no auth
//   GET  /v1/models              -> OpenAI list; combos have owned_by = "combo", no auth
//   POST /v1/chat/completions    -> OpenAI chat, Bearer key when "require API key" is on
//   POST /v1/messages            -> Anthropic Messages API (Claude Code passthrough)
//   POST /v1/responses           -> OpenAI Responses API (Codex passthrough)
//   POST /v1beta/models/{m}:generateContent | :streamGenerateContent -> Gemini native (Gemini CLI passthrough)
// and, for the quota footer, the dashboard API (no API key; 401 while 9router's "require login" is on, its default,
// unless the request carries the CLI token a 9router on this machine accepts, see RouterCliToken):
//   GET  /api/combos, /api/providers/client, /api/usage/{connectionId}
public sealed partial class NineRouterService(
    IHttpClientFactory httpClientFactory,
    ISettingService settingService,
    ISecretGuard secrets,
    ILogger<NineRouterService> logger) : INineRouterService
{
    public const string HttpClientName = "NineRouter";
    public const string KeyMissingMessage = "9router mewajibkan API key, tetapi belum ada API key tersimpan. Isi API key di menu Setting lalu klik Simpan.";
    public const string KeyRejectedMessage = "API key ditolak oleh 9router. Periksa atau ganti API key di menu Setting.";

    private static readonly TimeSpan ModelCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KeyCheckCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    // 9router asks the provider on every usage request (Claude's is cached there for 5 minutes).
    private static readonly TimeSpan QuotaCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuotaTimeout = TimeSpan.FromSeconds(10);
    private const int MaxConnectionPages = 10;

    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private readonly ConcurrentDictionary<string, UsageCache> _usageCache = new();
    // Models whose provider rejected prompt_cache_key (strict OpenAI-compatible APIs); they are called without it.
    private readonly ConcurrentDictionary<string, bool> _cacheKeyRejected = new(StringComparer.OrdinalIgnoreCase);
    private ModelCache? _modelCache;
    private KeyCheck? _keyCheck;
    private bool _dashboardLoginLogged;

    public RouterStatusDto? LastStatus { get; private set; }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();

        public ToolCall Build(int index) => new(
            string.IsNullOrEmpty(Id) ? $"call_{index}_{Guid.NewGuid():N}"[..24] : Id,
            Name.ToString(),
            Arguments.Length == 0 ? "{}" : Arguments.ToString());
    }

    private sealed record ModelCache(string BaseUrl, IReadOnlyList<RouterModelDto> Models, DateTime LoadedAt);

    private sealed record KeyCheck(string BaseUrl, string? Key, bool Valid, DateTime CheckedAt);

    private sealed record UsageCache(string Json, DateTime LoadedAt);
}
