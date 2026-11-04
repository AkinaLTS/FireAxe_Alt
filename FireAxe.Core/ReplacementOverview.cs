using Serilog;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FireAxe;

/// <summary>
/// One addon that replaces a slot.
/// </summary>
public sealed class ReplacementAddonEntry
{
    internal ReplacementAddonEntry(VpkAddon addon, bool isEnabledInHierarchy, int priority, int matchedFileCount)
    {
        ArgumentNullException.ThrowIfNull(addon);

        Addon = addon;
        IsEnabledInHierarchy = isEnabledInHierarchy;
        Priority = priority;
        MatchedFileCount = matchedFileCount;
    }

    public VpkAddon Addon { get; }

    /// <summary>
    /// Whether the addon (or a reference addon pointing at it) is enabled in the hierarchy.
    /// </summary>
    public bool IsEnabledInHierarchy { get; }

    public int Priority { get; }

    /// <summary>
    /// How many files inside the addon were identified as belonging to the slot.
    /// </summary>
    public int MatchedFileCount { get; }
}

public enum ReplacementSlotState
{
    /// <summary>No installed addon replaces this slot.</summary>
    Vanilla,

    /// <summary>At least one addon replaces this slot, but none of them is enabled.</summary>
    InstalledButDisabled,

    /// <summary>Exactly one enabled addon replaces this slot.</summary>
    Replaced,

    /// <summary>Several enabled addons replace this slot; the highest priority one wins.</summary>
    Overridden,

    /// <summary>Several enabled addons replace this slot with the same priority.</summary>
    Conflict
}

public sealed class ReplacementSlotStatus
{
    internal ReplacementSlotStatus(ReplacementSlot slot, IReadOnlyList<ReplacementAddonEntry> addons)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(addons);

        Slot = slot;
        Addons = addons;

        var activeAddons = addons.Where(addon => addon.IsEnabledInHierarchy)
            .OrderByDescending(addon => addon.Priority)
            .ToArray();
        ActiveAddons = activeAddons;

        if (addons.Count == 0)
        {
            State = ReplacementSlotState.Vanilla;
        }
        else if (activeAddons.Length == 0)
        {
            State = ReplacementSlotState.InstalledButDisabled;
        }
        else if (activeAddons.Length == 1)
        {
            State = ReplacementSlotState.Replaced;
        }
        else if (activeAddons[0].Priority > activeAddons[1].Priority)
        {
            State = ReplacementSlotState.Overridden;
        }
        else
        {
            State = ReplacementSlotState.Conflict;
        }
    }

    public ReplacementSlot Slot { get; }

    /// <summary>
    /// Every installed addon that replaces the slot, enabled or not, ordered by enabled state
    /// descending and then priority descending.
    /// </summary>
    public IReadOnlyList<ReplacementAddonEntry> Addons { get; }

    public IReadOnlyList<ReplacementAddonEntry> ActiveAddons { get; }

    public ReplacementSlotState State { get; }

    /// <summary>
    /// The addon that actually wins the slot (only for <see cref="ReplacementSlotState.Replaced"/>
    /// and <see cref="ReplacementSlotState.Overridden"/>).
    /// </summary>
    public ReplacementAddonEntry? EffectiveAddon =>
        State is ReplacementSlotState.Replaced or ReplacementSlotState.Overridden ? ActiveAddons[0] : null;

    /// <summary>
    /// Whether the given addon is one of the enabled addons that take effect for this slot.
    /// </summary>
    public bool IsEffective(ReplacementAddonEntry addon)
    {
        ArgumentNullException.ThrowIfNull(addon);

        return State switch
        {
            ReplacementSlotState.Replaced or ReplacementSlotState.Overridden => ReferenceEquals(addon, EffectiveAddon),
            ReplacementSlotState.Conflict => addon.IsEnabledInHierarchy && addon.Priority == ActiveAddons[0].Priority,
            _ => false
        };
    }
}

public sealed class ReplacementOverview
{
    internal ReplacementOverview(IReadOnlyList<ReplacementSlotStatus> slots)
    {
        Slots = slots;
        ConflictCount = slots.Count(status => status.State == ReplacementSlotState.Conflict);
        ReplacedCount = slots.Count(status => status is { State: ReplacementSlotState.Replaced or ReplacementSlotState.Overridden });
        VanillaCount = slots.Count(status => status.State == ReplacementSlotState.Vanilla);
        InstalledButDisabledCount = slots.Count(status => status.State == ReplacementSlotState.InstalledButDisabled);
    }

    public IReadOnlyList<ReplacementSlotStatus> Slots { get; }

    public ReplacementSlotStatus? FindStatus(string slotId)
    {
        ArgumentNullException.ThrowIfNull(slotId);

        foreach (var status in Slots)
        {
            if (status.Slot.Id == slotId)
            {
                return status;
            }
        }
        return null;
    }

    public int ConflictCount { get; }

    public int ReplacedCount { get; }

    public int VanillaCount { get; }

    public int InstalledButDisabledCount { get; }
}

/// <summary>
/// Scans every VPK addon under an <see cref="AddonRoot"/> and reports, for each catalogued slot,
/// which addons replace it and which one wins.
/// </summary>
public static class ReplacementOverviewAnalyzer
{
    public static Task<ReplacementOverview> AnalyzeAsync(AddonRoot addonRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addonRoot);

        addonRoot.ThrowIfInvalid();

        // Walk the node tree on the calling thread (it is bound to the UI) and only touch
        // the VPK files from the background thread.
        var candidates = CollectCandidates(addonRoot);
        return Task.Run(() => Analyze(candidates, cancellationToken), cancellationToken);
    }

    private sealed class AddonCandidate
    {
        public required VpkAddon Addon { get; init; }
        public required string Name { get; init; }
        public bool IsEnabled { get; set; }
        public int MaxPriority { get; set; }
        public int MaxEnabledPriority { get; set; }
        public string? VpkFilePath { get; init; }
        public string? WorkshopDirectoryPath { get; init; }
    }

    private static List<AddonCandidate> CollectCandidates(AddonRoot addonRoot)
    {
        var candidates = new Dictionary<VpkAddon, AddonCandidate>();
        foreach (var node in addonRoot.GetDescendants())
        {
            AddonNode? actualAddon = node;
            if (node is RefAddonNode refAddon)
            {
                actualAddon = refAddon.ActualSourceAddon;
            }
            if (actualAddon is not VpkAddon vpkAddon)
            {
                continue;
            }

            if (!candidates.TryGetValue(vpkAddon, out var candidate))
            {
                candidate = new AddonCandidate
                {
                    Addon = vpkAddon,
                    Name = vpkAddon.Name,
                    VpkFilePath = TryGetDirectVpkFilePath(vpkAddon),
                    WorkshopDirectoryPath = vpkAddon is WorkshopVpkAddon ? TryGetFullFilePath(vpkAddon) : null
                };
                candidates[vpkAddon] = candidate;
            }

            bool enabled = node.IsEnabledInHierarchy;
            int priority = node.PriorityInHierarchy;
            candidate.IsEnabled |= enabled;
            if (priority > candidate.MaxPriority)
            {
                candidate.MaxPriority = priority;
            }
            if (enabled && priority > candidate.MaxEnabledPriority)
            {
                candidate.MaxEnabledPriority = priority;
            }
        }

        return [.. candidates.Values];
    }

    private static ReplacementOverview Analyze(List<AddonCandidate> candidates, CancellationToken cancellationToken)
    {
        var matches = new Dictionary<AddonCandidate, Dictionary<string, int>>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? vpkFilePath = ResolveVpkFilePath(candidate);
            if (vpkFilePath is null)
            {
                continue;
            }

            var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var pak = new Package();
            try
            {
                pak.Read(vpkFilePath);
                if (pak.Entries is not null)
                {
                    foreach (var entries in pak.Entries.Values)
                    {
                        foreach (var entry in entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var slot = ReplacementSlotCatalog.TryMatch(entry.GetFullPath());
                            if (slot is null)
                            {
                                continue;
                            }
                            slotCounts.TryGetValue(slot.Id, out int count);
                            slotCounts[slot.Id] = count + 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception occurred during reading the VPK file for the replacement overview: {VpkPath}", vpkFilePath);
            }
            finally
            {
                pak.Dispose();
            }

            if (slotCounts.Count > 0)
            {
                matches[candidate] = slotCounts;
            }
        }

        var statuses = new List<ReplacementSlotStatus>(ReplacementSlotCatalog.Slots.Count);
        foreach (var slot in ReplacementSlotCatalog.Slots)
        {
            var entries = new List<ReplacementAddonEntry>();
            foreach (var candidate in candidates)
            {
                if (!matches.TryGetValue(candidate, out var slotCounts)
                    || !slotCounts.TryGetValue(slot.Id, out int matchedFileCount))
                {
                    continue;
                }

                int priority = candidate.IsEnabled ? candidate.MaxEnabledPriority : candidate.MaxPriority;
                entries.Add(new ReplacementAddonEntry(candidate.Addon, candidate.IsEnabled, priority, matchedFileCount));
            }

            entries.Sort((a, b) =>
            {
                if (a.IsEnabledInHierarchy != b.IsEnabledInHierarchy)
                {
                    return a.IsEnabledInHierarchy ? -1 : 1;
                }
                int byPriority = b.Priority.CompareTo(a.Priority);
                if (byPriority != 0)
                {
                    return byPriority;
                }
                return string.Compare(a.Addon.Name, b.Addon.Name, StringComparison.OrdinalIgnoreCase);
            });

            statuses.Add(new ReplacementSlotStatus(slot, entries));
        }

        return new ReplacementOverview(statuses);
    }

    /// <summary>
    /// Resolves the VPK file of an addon. A workshop addon's <see cref="VpkAddon.VpkFilePath"/>
    /// stays null until its download check has run, so fall back to the file inside its
    /// <c>.workshop</c> directory, preferring the file recorded in the meta info.
    /// </summary>
    private static string? ResolveVpkFilePath(AddonCandidate candidate)
    {
        string? path = candidate.VpkFilePath;
        if (path is not null && File.Exists(path))
        {
            return path;
        }

        string? directoryPath = candidate.WorkshopDirectoryPath;
        if (directoryPath is null)
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            string metaInfoPath = Path.Join(directoryPath, WorkshopVpkAddon.MetaInfoFileName);
            if (File.Exists(metaInfoPath))
            {
                try
                {
                    var metaInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkshopVpkMetaInfo>(
                        File.ReadAllText(metaInfoPath));
                    if (metaInfo is not null)
                    {
                        string currentFilePath = Path.Join(directoryPath, metaInfo.CurrentFile);
                        if (File.Exists(currentFilePath))
                        {
                            return currentFilePath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Exception occurred during reading the workshop meta info of the addon: {AddonName}", candidate.Name);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directoryPath, "*.vpk"))
            {
                return file;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception occurred during searching the VPK file of the workshop addon: {AddonName}", candidate.Name);
        }

        return null;
    }

    private static string? TryGetDirectVpkFilePath(VpkAddon addon)
    {
        try
        {
            return addon.VpkFilePath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception occurred during getting the VPK path of the addon: {AddonName}", addon.Name);
            return null;
        }
    }

    private static string? TryGetFullFilePath(VpkAddon addon)
    {
        try
        {
            return addon.FullFilePath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception occurred during getting the file path of the addon: {AddonName}", addon.Name);
            return null;
        }
    }
}
