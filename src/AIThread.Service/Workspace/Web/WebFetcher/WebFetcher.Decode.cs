using System.Text;

namespace AIThread.Service.Workspace.Web;

public sealed partial class WebFetcher
{
    private static string Decode(byte[] body, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"', ' '));
            }
            catch (ArgumentException)
            {
                // An unknown charset: UTF-8 reads most pages well enough.
            }
        }
        return encoding.GetString(body);
    }
}
