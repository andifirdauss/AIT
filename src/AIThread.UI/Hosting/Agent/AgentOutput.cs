using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIThread.UI.Hosting.Agent;

// The commands an AI agent drives (doctor, setup auto, project add, thread, chat --detach) print one JSON document on
// stdout under --json, so its answer costs few tokens and never has to be parsed out of progress text.
public static class AgentOutput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    public static void Write(object value) => Console.Out.WriteLine(Serialize(value));
}
