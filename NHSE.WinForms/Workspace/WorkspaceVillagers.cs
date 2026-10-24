using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;
using NHSE.Villagers;

namespace NHSE.WinForms;

internal static class WorkspaceVillagers
{
    internal sealed record Entry(string Id, string Name, int Species, int Personality);
    internal sealed record Count(int Id, int Total);
    internal sealed record Result(int Page, int Pages, int Total, Count[] Species, Count[] Personalities, Entry[] Items);
    private sealed record Metadata(string Id, int Species, int Personality);
    private static readonly Lazy<Metadata[]> MetadataEntries = new(() => GameInfo.GetStrings("en").VillagerMap.Keys
        .Where(id => id.Length == 5 && VillagerResources.IsVillagerDataKnown(id))
        .Select(id => (id, data: new Villager2(VillagerResources.GetVillager(id).Villager)))
        .Where(x => x.data.Species < (byte)VillagerSpecies.non)
        .Select(x => new Metadata(x.id, x.data.Species, (int)x.data.Personality)).OrderBy(x => x.Id).ToArray());

    internal static bool Contains(string id) => MetadataEntries.Value.Any(x => x.Id == id);

    internal static Result Search(string query, int species, int personality, int page)
    {
        if (species is < -1 or >= 35 || personality is < -1 or > 8) throw new ArgumentException("invalidRequest");
        var entries = MetadataEntries.Value.Select(x => new Entry(x.Id, GameInfo.Strings.GetVillager(x.Id), x.Species, x.Personality))
            .Where(x => x.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) || x.Id.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var speciesCounts = Enumerable.Range(0, 35).Select(id => new Count(id, entries.Count(x => x.Species == id && (personality < 0 || x.Personality == personality)))).ToArray();
        var personalityCounts = Enumerable.Range(0, 9).Select(id => new Count(id, entries.Count(x => x.Personality == id && (species < 0 || x.Species == species)))).ToArray();
        var filtered = entries.Where(x => (species < 0 || x.Species == species) && (personality < 0 || x.Personality == personality))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id).ToArray();
        const int pageSize = 60;
        var pages = Math.Max(1, (filtered.Length + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 0, pages - 1);
        return new Result(page, pages, filtered.Length, speciesCounts, personalityCounts, filtered.Skip(page * pageSize).Take(pageSize).ToArray());
    }

    internal static VillagerInfo CreateReplacement(IVillager current, IVillagerHouse house, string id, int houseSize)
    {
        if (current is not Villager2 || !VillagerResources.IsVillagerDataKnown(id) || houseSize is not (VillagerHouse1.SIZE or VillagerHouse2.SIZE))
            throw new ArgumentException("invalidRequest");
        // Work on copies: cancelling or failing a conversion must not mutate the existing villager.
        var clone = new Villager2(current.Write());
        IVillagerHouse oldHouse = house is VillagerHouse2 ? new VillagerHouse2(house.Write()) : new VillagerHouse1(house.Write());
        var replacement = VillagerSwap.GetReplacementVillager(new VillagerInfo(clone, oldHouse), id);
        var houseBytes = replacement.House.Span[..houseSize].ToArray();
        IVillagerHouse newHouse = houseSize == VillagerHouse2.SIZE ? new VillagerHouse2(houseBytes) : new VillagerHouse1(houseBytes);
        return new VillagerInfo(new Villager2(replacement.Villager), newHouse);
    }
}
