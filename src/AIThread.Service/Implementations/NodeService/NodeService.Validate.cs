using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    private static void Validate(SaveNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new AppValidationException(Lang.T("Nama node wajib diisi.", "The node name is required."));
        if (request.Name.Length > 100) throw new AppValidationException(Lang.T("Nama node maksimal 100 karakter.", "The node name is at most 100 characters."));
        if (!Enum.IsDefined(request.Role)) throw new AppValidationException(Lang.T("Role node tidak valid.", "Invalid node role."));
        if (string.IsNullOrWhiteSpace(request.Model)) throw new AppValidationException(Lang.T("Model wajib dipilih.", "A model must be selected."));
        if (request.MaxTokens is < 256 or > 200_000) throw new AppValidationException(Lang.T("Max tokens harus di antara 256 dan 200000.", "Max tokens must be between 256 and 200000."));
        if (request.MaxConcurrency is < 1 or > 64) throw new AppValidationException(Lang.T("Max concurrency harus di antara 1 dan 64.", "Max concurrency must be between 1 and 64."));
        if (request.Temperature is < 0 or > 2) throw new AppValidationException(Lang.T("Temperature harus di antara 0 dan 2.", "Temperature must be between 0 and 2."));
    }
}
