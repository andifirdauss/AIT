using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Saved through MemoryStorageService, which refuses a store it cannot reach. A stored password is only reused for
    // the very same server; a different server gets the typed password or none, never this machine's old one.
    private async Task PlanMemoryAsync(SetupProfileMemory memory, SetupProfileSecrets secrets, bool copyExisting, ImportPlan plan,
        CancellationToken cancellationToken)
    {
        var current = await memoryStorage.GetAsync(cancellationToken);
        var defaultStore = new SetupProfileStore(MemoryStoreProvider.Default, null, null, null, null, null);
        var levels = new[]
        {
            (Id: "shortPassword", Name: "Short memory", Current: current.ShortMemory, Target: memory.Short ?? defaultStore, Password: Clean(secrets.ShortPassword)),
            (Id: "longPassword", Name: "Long memory", Current: current.LongMemory, Target: memory.Long ?? defaultStore, Password: Clean(secrets.LongPassword)),
            (Id: "businessPassword", Name: "Business memory", Current: current.BusinessMemory, Target: memory.Business ?? defaultStore,
                Password: Clean(secrets.BusinessPassword))
        };

        var requests = new List<SaveMemoryStoreRequest>();
        var changed = false;
        foreach (var level in levels)
        {
            var target = level.Target;
            var same = SameStore(level.Current, target);
            var needsPassword = target.Provider is not (MemoryStoreProvider.Default or MemoryStoreProvider.Sqlite);
            if (needsPassword)
                plan.Secrets.Add(new SetupProfileSecretDto(level.Id, $"Password {level.Name} ({DescribeStore(target)})", same && level.Current.HasPassword));

            if (!same)
            {
                plan.Change(SetupProfileSection.Memory, $"{level.Name}: {DescribeStore(ToProfileStore(level.Current))} → {DescribeStore(target)}");
                changed = true;
            }
            else if (needsPassword && level.Password is not null)
            {
                plan.Change(SetupProfileSection.Memory, $"{level.Name}: password diganti dengan yang diisi.");
                changed = true;
            }

            requests.Add(new SaveMemoryStoreRequest(target.Provider, target.Host, target.Port, target.Database, target.Username, level.Password,
                ClearPassword: !same && level.Password is null, target.Options));
        }
        if (!changed) return;

        plan.Actions.Add((SetupProfileSection.Memory, async () =>
        {
            var result = await memoryStorage.UpdateAsync(new UpdateMemoryStorageRequest(requests[0], requests[1], copyExisting, requests[2]), cancellationToken);
            foreach (var warning in result.Warnings) plan.Warn(warning);
            if (result.CopiedShort + result.CopiedLong + result.CopiedBusiness > 0)
            {
                plan.Change(SetupProfileSection.Memory,
                    $"{result.CopiedShort} short, {result.CopiedLong} long memory & {result.CopiedBusiness} halaman bisnis disalin ke penyimpanan baru.");
            }
        }));
    }
}
