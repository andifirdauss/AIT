using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Optional: the first prompt of a CLI session inside a project folder registers it anyway.
    private static SetupStepDto ProjectStep(int projectCount) =>
        new("project", Lang.T("Daftarkan project", "Register a project"), false, projectCount > 0, projectCount > 0
            ? Lang.T($"{projectCount} project terdaftar.", $"{projectCount} projects registered.")
            : Lang.T("Opsional: project juga terdaftar otomatis pada prompt pertama dari CLI di folder project. Daftarkan sekarang bila ingin menyalakan CLI per project.", "Optional: a project is also registered automatically on the first prompt from a CLI in the project folder. Register it now to turn on a CLI per project."));
}
