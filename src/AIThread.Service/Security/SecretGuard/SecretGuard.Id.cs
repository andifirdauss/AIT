using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // 40 bits of HMAC-SHA256: unique enough per machine, and says nothing about the value without the key.
    private string Id(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes(value)), 0, 5);
}
