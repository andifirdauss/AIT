using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    private static void Validate(SaveNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new AppValidationException("Nama node wajib diisi.");
        if (request.Name.Length > 100) throw new AppValidationException("Nama node maksimal 100 karakter.");
        if (!Enum.IsDefined(request.Role)) throw new AppValidationException("Role node tidak valid.");
        if (string.IsNullOrWhiteSpace(request.Model)) throw new AppValidationException("Model wajib dipilih.");
        if (request.MaxTokens is < 256 or > 200_000) throw new AppValidationException("Max tokens harus di antara 256 dan 200000.");
        if (request.MaxConcurrency is < 1 or > 64) throw new AppValidationException("Max concurrency harus di antara 1 dan 64.");
        if (request.Temperature is < 0 or > 2) throw new AppValidationException("Temperature harus di antara 0 dan 2.");
    }
}
