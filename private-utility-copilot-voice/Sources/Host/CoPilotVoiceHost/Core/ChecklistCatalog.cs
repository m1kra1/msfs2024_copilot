using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>Profile assignment helpers for checklists (pure, no I/O).</summary>
public static class ChecklistCatalog
{
    /// <summary>
    /// Empty/null <see cref="ChecklistDefinition.AssignedProfiles"/> means available for every profile.
    /// </summary>
    public static bool IsAssignedToProfile(ChecklistDefinition? checklist, string? profileId)
    {
        if (checklist is null) return false;
        var assigned = checklist.AssignedProfiles;
        if (assigned is null || assigned.Count == 0)
            return true;

        var profile = HostConstants.NormalizeProfileId(profileId);
        return assigned.Any(p =>
            !string.IsNullOrWhiteSpace(p)
            && p.Trim().Equals(profile, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<ChecklistDefinition> ForProfile(
        IEnumerable<ChecklistDefinition>? all,
        string? profileId)
    {
        return (all ?? Enumerable.Empty<ChecklistDefinition>())
            .Where(c => c is not null && IsAssignedToProfile(c, profileId))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
