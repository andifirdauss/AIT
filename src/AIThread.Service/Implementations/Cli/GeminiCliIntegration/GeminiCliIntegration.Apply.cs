using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    protected override string Apply(string content, CliEntry entry)
    {
        var own = ManagedTextBlock.Outside(content).Select(l => Assignment().Match(l))
            .FirstOrDefault(m => m.Success && m.Groups["key"].Value is BaseUrlKey or ApiKeyKey);
        if (own is not null)
            throw new AppValidationException(Lang.T($".env sudah berisi {own.Groups["key"].Value} buatan Anda. Hapus atau jadikan komentar dulu, lalu nyalakan lagi.", $".env already contains {own.Groups["key"].Value} made by you. Delete it or turn it into a comment first, then turn it on again."));
        return ManagedTextBlock.Append(content, Block(entry));
    }
}
