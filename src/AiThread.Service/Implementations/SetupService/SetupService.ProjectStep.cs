using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    // Optional: the first prompt of a CLI session inside a project folder registers it anyway.
    private static SetupStepDto ProjectStep(int projectCount) =>
        new("project", "Daftarkan project", false, projectCount > 0, projectCount > 0
            ? $"{projectCount} project terdaftar."
            : "Opsional: project juga terdaftar otomatis pada prompt pertama dari CLI di folder project. Daftarkan sekarang bila ingin menyalakan CLI per project.");
}
