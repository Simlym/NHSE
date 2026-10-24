using System;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record IslandSettings(int AirportColor, int Hemisphere, int TourHemisphere, uint WeatherSeed, uint TourWeatherSeed);
    internal sealed record Resident(int Index, string Id, string Name, int Species, int Personality, string Catchphrase,
        string DefaultCatchphrase, bool MovingOut, bool Empty, bool CanReplace, string? Image);

    public IslandSettings GetIslandSettings() => new((int)SaveFile.Main.AirportThemeColor, (int)SaveFile.Main.Hemisphere,
        (int)SaveFile.Main.TourHemisphere, SaveFile.Main.WeatherSeed, SaveFile.Main.TourWeatherSeed);

    public Resident[] GetResidents(Func<string, string?>? image = null) => SaveFile.Main.GetVillagers().Select((v, index) =>
    {
        var empty = v.Species >= (byte)VillagerSpecies.non;
        return new Resident(index, v.InternalName, empty ? "" : GameInfo.Strings.GetVillager(v.InternalName), v.Species,
            (int)v.Personality, v.CatchPhrase, empty ? "" : GameInfo.Strings.GetVillagerDefaultPhrase(v.InternalName),
            v.MovingOut, empty, v is Villager2 && !empty, empty ? null : image?.Invoke(v.InternalName));
    }).ToArray();

    public void SetIsland(IslandSettings next)
    {
        if (next.AirportColor is < 0 or > 3 || next.Hemisphere is < 0 or > 1 || next.TourHemisphere is < 0 or > 1)
            throw new ArgumentException("invalidRequest");
        var before = GetIslandSettings();
        if (before == next) return;
        void Apply(IslandSettings value)
        {
            var m = SaveFile.Main;
            m.AirportThemeColor = (AirportColor)value.AirportColor;
            m.Hemisphere = (Hemisphere)value.Hemisphere;
            m.TourHemisphere = (Hemisphere)value.TourHemisphere;
            m.WeatherSeed = value.WeatherSeed;
            m.TourWeatherSeed = value.TourWeatherSeed;
        }
        Commit(() => Apply(before), () => Apply(next));
    }

    private IVillager ResidentAt(int index)
    {
        if ((uint)index >= MainSaveOffsets.VillagerCount) throw new ArgumentException("invalidRequest");
        var resident = SaveFile.Main.GetVillager(index);
        if (resident.Species >= (byte)VillagerSpecies.non) throw new ArgumentException("invalidRequest");
        return resident;
    }

    public void SetResident(int index, int personality, string catchphrase, bool movingOut)
    {
        if (personality is < 0 or > 8 || catchphrase.Length > 24 || catchphrase.Any(char.IsControl))
            throw new ArgumentException("invalidRequest");
        // Validate UTF-16 before invoking the existing writer, which otherwise truncates silently.
        for (var i = 0; i < catchphrase.Length; i++)
            if (char.IsSurrogate(catchphrase[i]) && (!char.IsHighSurrogate(catchphrase[i]) || ++i >= catchphrase.Length || !char.IsLowSurrogate(catchphrase[i])))
                throw new ArgumentException("invalidRequest");
        var v = ResidentAt(index);
        var before = v.Write();
        v.Personality = (VillagerPersonality)personality;
        v.CatchPhrase = catchphrase;
        v.MovingOut = movingOut;
        var after = v.Write();
        if (before.SequenceEqual(after)) return;
        Commit(() => PutResident(before, index), () => PutResident(after, index));
    }

    private void PutResident(byte[] bytes, int index) => SaveFile.Main.SetVillager(SaveFile.Main.Offsets.ReadVillager(bytes.ToArray()), index);

    public void ReplaceResident(int index, string id)
    {
        var current = ResidentAt(index);
        if (!WorkspaceVillagers.Contains(id) || SaveFile.Main.GetVillagers().Where((_, i) => i != index).Any(v => v.Species < 35 && v.InternalName == id))
            throw new ArgumentException("invalidRequest");
        if (current.InternalName == id) return;
        var houses = SaveFile.Main.GetVillagerHouses();
        var houseIndex = Array.FindIndex(houses, h => h.NPC1 == index);
        if (houseIndex < 0) throw new ArgumentException("villagerHouseMissing");
        var house = houses[houseIndex];
        var replacement = WorkspaceVillagers.CreateReplacement(current, house, id, SaveFile.Main.Offsets.VillagerHouseSize);
        var beforeVillager = current.Write(); var beforeHouse = house.Write();
        var afterVillager = replacement.Villager.Write(); var afterHouse = replacement.House.Write();
        void Apply(byte[] villager, byte[] home)
        {
            PutResident(villager, index);
            SaveFile.Main.SetVillagerHouse(SaveFile.Main.Offsets.ReadVillagerHouse(home.ToArray()), houseIndex);
        }
        Commit(() => Apply(beforeVillager, beforeHouse), () => Apply(afterVillager, afterHouse));
    }

    // Specialized dialogs only edit the shared model. Keep exact changed byte ranges
    // so their edits join the same undo history without retaining whole save copies.
    public void EditNative(Action edit)
    {
        var originals = new[] { SaveFile.Main }.Cast<EncryptedFilePair>().Concat(SaveFile.Players.SelectMany(p => p))
            .Select(file => (file, bytes: file.Data.ToArray())).ToArray();
        try { edit(); }
        catch
        {
            foreach (var (file, bytes) in originals) bytes.CopyTo(file.Data);
            throw;
        }
        var patches = new System.Collections.Generic.List<IdentityPatch>();
        foreach (var (file, bytes) in originals)
        {
            var current = file.Data;
            for (var i = 0; i < bytes.Length;)
            {
                if (bytes[i] == current[i]) { i++; continue; }
                var start = i++;
                while (i < bytes.Length && bytes[i] != current[i]) i++;
                patches.Add(new IdentityPatch(file, start, bytes[start..i], current[start..i].ToArray()));
            }
        }
        if (patches.Count == 0) return;
        void Apply(bool undo) { foreach (var patch in patches) (undo ? patch.Before : patch.After).CopyTo(patch.File.Data[patch.Offset..]); }
        Commit(() => Apply(true), () => Apply(false));
    }
}
