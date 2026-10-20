namespace AiThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;
}
