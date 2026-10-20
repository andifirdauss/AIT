using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    protected override string Apply(string content, CliEntry entry)
    {
        var own = ManagedTextBlock.Outside(content).Select(l => Assignment().Match(l))
            .FirstOrDefault(m => m.Success && m.Groups["key"].Value is BaseUrlKey or ApiKeyKey);
        if (own is not null)
            throw new AppValidationException($".env sudah berisi {own.Groups["key"].Value} buatan Anda. Hapus atau jadikan komentar dulu, lalu nyalakan lagi.");
        return ManagedTextBlock.Append(content, Block(entry));
    }
}
