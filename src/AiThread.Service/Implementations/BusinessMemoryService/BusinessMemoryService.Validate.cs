using AiThread.Common.DTOs;
using AiThread.Service.Business;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static void Validate(SaveBusinessPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Module)) throw new AppValidationException("Modul wajib diisi (mis. TO).");
        if (request.Module.Trim().Length > 100) throw new AppValidationException("Modul maksimal 100 karakter.");
        if (string.IsNullOrWhiteSpace(request.Title)) throw new AppValidationException("Judul wajib diisi.");
        if (request.Title.Trim().Length > 200) throw new AppValidationException("Judul maksimal 200 karakter.");
        if (request.Summary?.Length > 1000) throw new AppValidationException("Ringkasan maksimal 1.000 karakter.");
        if (request.Labels?.Length > 500) throw new AppValidationException("Label maksimal 500 karakter.");
        if (string.IsNullOrWhiteSpace(request.Content)) throw new AppValidationException("Isi halaman wajib diisi.");
        if (request.Content.Length > BusinessPageStore.MaxContentChars)
            throw new AppValidationException($"Isi halaman maksimal {BusinessPageStore.MaxContentChars:N0} karakter.");
    }
}
