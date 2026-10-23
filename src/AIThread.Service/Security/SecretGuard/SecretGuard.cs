using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Security;

// What restoring placeholders did: the text with the values back, how many came back, and the placeholders AIT does
// not know (a secret from before a restart whose file was not read again); a write that still holds one is refused.
public sealed record SecretRestore(string Text, int Restored, IReadOnlyList<string> Unknown)
{
    public bool Complete => Unknown.Count == 0;
}

public sealed record SecurityCounters(long Masked, int KnownSecrets, long Restored, long BlockedReads);

// Every secret found in text on its way to a model (SecretRules) becomes {{AIT_SECRET:<label>:<id>}}. The id is an HMAC
// of the value under a key kept on this machine (Data Protection), so the same secret always gets the same placeholder,
// across restarts too, and nothing about it can be read from the placeholder. The values live in memory only (the
// vault); they are written back where AIT itself writes: executor edits, tester tool calls and Claude Code's edit
// tools (PreToolUse hook). A value that is long and mixed enough is also masked wherever it appears later (a password
// read from appsettings.json and then pasted into a prompt).
public sealed partial class SecretGuard(IDataProtectionProvider dataProtection, IOptions<AIThreadOptions> options, ILogger<SecretGuard> logger) : ISecretGuard
{
    private const int MaxVaultEntries = 50_000;
    private const int MaxDistinctive = 5_000;

    // Re-serialized tool call arguments read like the model wrote them (no < for "<").
    private static readonly JsonSerializerOptions ArgumentsJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _distinctive = new(StringComparer.Ordinal);
    private readonly AsyncLocal<Tally?> _tally = new();
    private readonly Lock _keyLock = new();
    private byte[]? _key;
    private int _distinctiveVersion;
    private (int Version, Regex? Pattern) _distinctivePattern;
    private volatile bool _enabled = true;
    private volatile bool _maskPassthrough = true;
    private long _masked;
    private long _restored;

    public bool Enabled => _enabled;

    public bool MaskPassthrough => _enabled && _maskPassthrough;

    public int TallyCount => _tally.Value?.Count ?? 0;

    // The distinct secrets masked while a thread (or one passthrough request) ran.
    private sealed class Tally : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _ids = new(StringComparer.Ordinal);
        private readonly AsyncLocal<Tally?> _slot;
        private readonly Tally? _previous;

        public Tally(AsyncLocal<Tally?> slot)
        {
            _slot = slot;
            _previous = slot.Value;
            slot.Value = this;
        }

        public int Count => _ids.Count;

        public void Add(string id) => _ids.TryAdd(id, 0);

        public void Dispose() => _slot.Value = _previous;
    }
}
