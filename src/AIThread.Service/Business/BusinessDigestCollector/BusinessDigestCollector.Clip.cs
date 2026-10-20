namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    private static string Clip(string value, int max) => value.Length > max ? value[..max] + " …" : value;
}
