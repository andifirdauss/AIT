using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Interfaces;

// Finds out whether a project's app has "login as", how to use it and which roles may: a scan of the code, then a cheap
// model reading the hits. The result is saved in the project's testing settings.
public interface IImpersonationDetector
{
    // Null when the project has no key. by: the developer the detection is recorded for.
    Task<ImpersonationInfo?> DetectAsync(ProjectContext project, string by, CancellationToken cancellationToken = default);

    // What the tester saw while testing (a "Login as" button the detection missed): saved unless a developer already
    // recorded or confirmed something. Returns whether it was saved.
    Task<bool> ObserveAsync(string projectKey, ImpersonationInfo seen, string by, CancellationToken cancellationToken = default);
}
