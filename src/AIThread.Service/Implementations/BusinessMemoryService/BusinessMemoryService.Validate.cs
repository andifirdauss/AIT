using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Business;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static void Validate(SaveBusinessPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Module)) throw new AppValidationException(Lang.T("Modul wajib diisi (mis. TO).", "The module is required (e.g. TO)."));
        if (request.Module.Trim().Length > 100) throw new AppValidationException(Lang.T("Modul maksimal 100 karakter.", "The module is at most 100 characters."));
        if (string.IsNullOrWhiteSpace(request.Title)) throw new AppValidationException(Lang.T("Judul wajib diisi.", "The title is required."));
        if (request.Title.Trim().Length > 200) throw new AppValidationException(Lang.T("Judul maksimal 200 karakter.", "The title is at most 200 characters."));
        if (request.Summary?.Length > 1000) throw new AppValidationException(Lang.T("Ringkasan maksimal 1.000 karakter.", "The summary is at most 1,000 characters."));
        if (request.Labels?.Length > 500) throw new AppValidationException(Lang.T("Label maksimal 500 karakter.", "The label is at most 500 characters."));
        if (string.IsNullOrWhiteSpace(request.Content)) throw new AppValidationException(Lang.T("Isi halaman wajib diisi.", "The page content is required."));
        if (request.Content.Length > BusinessPageStore.MaxContentChars)
            throw new AppValidationException(Lang.T($"Isi halaman maksimal {BusinessPageStore.MaxContentChars:N0} karakter.", $"The page content is at most {BusinessPageStore.MaxContentChars:N0} characters."));
    }
}
