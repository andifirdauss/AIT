using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace AiThread.Data.Cache;

// Layout:
//   aithread:stm:item:{id}   -> JSON of ShortMemoryRecord
//   aithread:stm:timeline    -> sorted set of ids scored by creation time (unix seconds)
// Items carry no TTL: the consolidation worker moves them to the database, so nothing is lost
// if the app is offline when an item turns one day old.
public sealed class RedisShortMemoryStore(RedisConnectionProvider connection) : IShortMemoryStore
{
    private const string ItemPrefix = "aithread:stm:item:";
    private const string TimelineKey = "aithread:stm:timeline";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public bool IsConnected => connection.IsConnected;

    public async Task SaveAsync(ShortMemoryRecord record)
    {
        var db = connection.GetDatabase();
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await db.StringSetAsync(ItemKey(record.Id), json);
        await db.SortedSetAddAsync(TimelineKey, record.Id.ToString("N"), ToScore(record.CreatedAt));
    }

    public async Task<ShortMemoryRecord?> GetAsync(Guid id)
    {
        var value = await connection.GetDatabase().StringGetAsync(ItemKey(id));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ShortMemoryRecord>(value.ToString(), JsonOptions);
    }

    public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() =>
        LoadRangeAsync(double.NegativeInfinity, double.PositiveInfinity);

    public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) =>
        LoadRangeAsync(double.NegativeInfinity, ToScore(cutoffUtc));

    public async Task<bool> DeleteAsync(Guid id)
    {
        var db = connection.GetDatabase();
        var removed = await db.KeyDeleteAsync(ItemKey(id));
        await db.SortedSetRemoveAsync(TimelineKey, id.ToString("N"));
        return removed;
    }

    private async Task<IReadOnlyList<ShortMemoryRecord>> LoadRangeAsync(double from, double to)
    {
        var db = connection.GetDatabase();
        var ids = await db.SortedSetRangeByScoreAsync(TimelineKey, from, to, order: Order.Descending);
        if (ids.Length == 0) return [];

        var keys = ids.Select(id => (RedisKey)(ItemPrefix + id)).ToArray();
        var values = await db.StringGetAsync(keys);

        var records = new List<ShortMemoryRecord>(values.Length);
        var dangling = new List<RedisValue>();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                dangling.Add(ids[i]);
                continue;
            }

            var record = JsonSerializer.Deserialize<ShortMemoryRecord>(values[i].ToString(), JsonOptions);
            if (record is not null) records.Add(record);
        }

        // Timeline entries whose item was removed out-of-band.
        if (dangling.Count > 0) await db.SortedSetRemoveAsync(TimelineKey, dangling.ToArray());

        return records;
    }

    private static RedisKey ItemKey(Guid id) => ItemPrefix + id.ToString("N");

    private static double ToScore(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
