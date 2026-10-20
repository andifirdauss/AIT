using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The file is the truth for its own nodes: fallback, tool delegations and skills become exactly the file's (as far
    // as they resolve here). Links that do not resolve are dropped with a warning instead of failing the import.
    private async Task PlanNodesAsync(IReadOnlyList<SetupProfileNode> nodes, SetupProfileNodeMode mode, ImportPlan plan, CancellationToken cancellationToken)
    {
        var existing = await nodeService.GetAllAsync(cancellationToken);
        var skills = await SkillIndexAsync(cancellationToken);

        var valid = new List<SetupProfileNode>();
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name) || !Enum.IsDefined(node.Role) || string.IsNullOrWhiteSpace(node.Model))
            {
                plan.Warn($"Node '{node.Name}' dilewati: nama, role, dan model wajib diisi.");
                continue;
            }
            if (valid.Any(v => IsNode(v.Role, v.Name, node.Role, node.Name)))
            {
                plan.Warn($"Node {RoleLabels[node.Role]} '{node.Name}' ada lebih dari sekali di profil; yang pertama dipakai.");
                continue;
            }
            valid.Add(node with { Name = node.Name.Trim(), Model = node.Model.Trim() });
        }

        bool Known(NodeRole role, string name) =>
            valid.Any(v => IsNode(v.Role, v.Name, role, name)) || existing.Any(e => IsNode(e.Role, e.Name, role, name));

        var resolved = new List<ResolvedNode>();
        foreach (var node in valid)
        {
            var match = existing.FirstOrDefault(e => IsNode(e.Role, e.Name, node.Role, node.Name));

            var fallback = Clean(node.Fallback);
            if (fallback is not null && !Known(node.Role, fallback))
            {
                plan.Warn($"Node cadangan '{fallback}' untuk '{node.Name}' tidak ada di profil maupun di komputer ini; cadangan dikosongkan.");
                fallback = null;
            }

            var delegations = new List<SetupProfileDelegation>();
            if (node.Role == NodeRole.Planner)
            {
                foreach (var delegation in node.ToolDelegations ?? [])
                {
                    if (!PlannerTools.Delegable.Contains(delegation.Tool))
                        plan.Warn($"Tool '{delegation.Tool}' untuk planner '{node.Name}' tidak bisa didelegasikan; dilewati.");
                    else if (!Known(NodeRole.ToolsNode, delegation.ToolsNode))
                        plan.Warn($"Tools node '{delegation.ToolsNode}' untuk planner '{node.Name}' tidak ditemukan; {delegation.Tool} dijalankan planner sendiri.");
                    else if (delegations.All(d => d.Tool != delegation.Tool))
                        delegations.Add(delegation);
                }
            }

            var skillIds = new List<Guid>();
            foreach (var skill in node.Skills ?? [])
            {
                var found = skills.FirstOrDefault(s => string.Equals(s.Name, skill.Name, StringComparison.OrdinalIgnoreCase)
                                                       && (skill.SourceUrl is null || string.Equals(s.SourceUrl, skill.SourceUrl, StringComparison.OrdinalIgnoreCase)));
                if (found is null)
                    plan.Warn($"Skill '{skill.Name}' belum ada di gudang skill komputer ini. Setelah sumbernya selesai ditarik, import profil ini lagi untuk memasangnya.");
                else if (!skillIds.Contains(found.Id))
                    skillIds.Add(found.Id);
            }

            var draft = new ResolvedNode(node, match, fallback, delegations, skillIds, false);
            if (match is null)
            {
                plan.Change(SetupProfileSection.Nodes, $"Ditambah: {RoleLabels[node.Role]} '{node.Name}' ({node.Model}).");
                resolved.Add(draft with { Changed = true });
                continue;
            }

            var diffs = NodeDiffs(match, draft);
            if (diffs.Count > 0) plan.Change(SetupProfileSection.Nodes, $"Diperbarui: {RoleLabels[node.Role]} '{node.Name}' — {string.Join(", ", diffs)}.");
            resolved.Add(draft with { Changed = diffs.Count > 0 });
        }

        var others = mode == SetupProfileNodeMode.Replace
            ? existing.Where(e => e.IsActive && !valid.Any(v => IsNode(v.Role, v.Name, e.Role, e.Name))).ToList()
            : [];
        foreach (var other in others)
            plan.Change(SetupProfileSection.Nodes, $"Dinonaktifkan: {RoleLabels[other.Role]} '{other.Name}' ({other.Model}).");

        if (others.Count == 0 && !resolved.Any(r => r.Changed)) return;
        plan.Actions.Add((SetupProfileSection.Nodes, () => ApplyNodesAsync(resolved, others, cancellationToken)));
    }
}
