using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record DesignEntry(int Index, string Name, string Preview);
    internal sealed record DesignDetail(string Name, string Player, string Town, uint Compatibility, string[] Sheets, string[] Palette);
    internal sealed record DesignData(string Kind, int Index, DesignEntry[] Entries, DesignDetail Selected);

    public int DesignCount(string kind) => kind switch
    {
        "patterns" or "proDesigns" => SaveFile.Main.Offsets.PatternCount,
        "flag" => 1,
        "tailor" => MainSaveOffsets.PatternTailorCount,
        _ => throw new ArgumentException("invalidRequest"),
    };
    public static bool IsProDesign(string kind) => kind is "proDesigns" or "tailor";
    public byte[] DesignBytes(string kind, int index)
    {
        if ((uint)index >= DesignCount(kind)) throw new ArgumentException("invalidRequest");
        var m = SaveFile.Main;
        return kind switch
        {
            "patterns" => m.GetDesign(index).Data.ToArray(),
            "proDesigns" => m.GetDesignPRO(index).Data.ToArray(),
            "flag" => m.FlagMyDesign.Data.ToArray(),
            "tailor" => m.GetDesignsTailor()[index].Data.ToArray(),
            _ => throw new ArgumentException("invalidRequest"),
        };
    }
    private void WriteDesign(string kind, int index, byte[] bytes)
    {
        var copy = bytes.ToArray();
        var m = SaveFile.Main;
        switch (kind)
        {
            case "patterns": m.SetDesign(new DesignPattern(copy), index); break;
            case "proDesigns": m.SetDesignPRO(new DesignPatternPRO(copy), index); break;
            case "flag": m.FlagMyDesign = new DesignPattern(copy); break;
            case "tailor":
                // A fixed, validated tailor slot; leave all other slots untouched.
                copy.CopyTo(m.Data[(m.Offsets.PatternTailor + (index * DesignPatternPRO.SIZE))..]);
                break;
            default: throw new ArgumentException("invalidRequest");
        }
    }
    private static DesignDetail DesignPreview(byte[] bytes, bool pro)
    {
        if (pro)
        {
            var p = new DesignPatternPRO(bytes);
            return new(p.DesignName, p.PlayerName, p.TownName, p.UsageCompatibility,
                Enumerable.Range(0, 4).Select(i => Convert.ToBase64String(p.GetBitmap(i))).ToArray(), Colors(p.GetPaletteBitmap()));
        }
        var regular = new DesignPattern(bytes);
        return new(regular.DesignName, regular.PlayerName, regular.TownName, regular.UsageCompatibility,
            [Convert.ToBase64String(regular.GetBitmap())], Colors(regular.GetPaletteBitmap()));
    }
    private static string[] Colors(byte[] bgr) => Enumerable.Range(0, 15)
        .Select(i => $"#{bgr[(i * 3) + 2]:X2}{bgr[(i * 3) + 1]:X2}{bgr[i * 3]:X2}").ToArray();

    public DesignData GetDesignWorkspace(string kind, int index)
    {
        var selected = DesignPreview(DesignBytes(kind, index), IsProDesign(kind));
        var entries = Enumerable.Range(0, DesignCount(kind)).Select(i =>
        {
            var bytes = DesignBytes(kind, i);
            if (IsProDesign(kind))
            {
                var p = new DesignPatternPRO(bytes);
                return new DesignEntry(i, p.DesignName, Convert.ToBase64String(p.GetBitmap(0)));
            }
            var regular = new DesignPattern(bytes);
            return new DesignEntry(i, regular.DesignName, Convert.ToBase64String(regular.GetBitmap()));
        }).ToArray();
        return new(kind, index, entries, selected);
    }

    // Read and prepare every file before committing. A malformed batch cannot
    // partially replace earlier slots, and one undo restores the complete batch.
    public void ImportDesigns(string kind, int index, int player, IReadOnlyList<byte[]> files, bool useCurrentPlayer)
    {
        var owner = Player(player);
        var count = DesignCount(kind);
        if ((uint)index >= count || files.Count < 1 || files.Count > count - index) throw new ArgumentException("designCapacity");
        var pro = IsProDesign(kind);
        var expected = pro ? DesignPatternPRO.SIZE : DesignPattern.SIZE;
        var prepared = new List<(int Index, byte[] Before, byte[] After)>();
        foreach (var (file, i) in files.Select((file, i) => (file, i)))
        {
            if (file.Length != expected) throw new ArgumentException("designFileSize");
            var data = file.ToArray();
            if (pro)
            {
                var p = new DesignPatternPRO(data);
                if (useCurrentPlayer) CopyDesignOrigin(p, owner);
                if (p.UsageCompatibility is not (0xEE01 or 0xEE05)) p.UsageCompatibility = 0xEE01;
            }
            else
            {
                var p = new DesignPattern(data);
                if (useCurrentPlayer) CopyDesignOrigin(p, owner);
                if (p.UsageCompatibility is not (0xEE00 or 0xEE02))
                    p.UsageCompatibility = Enumerable.Range(0, 1024).Any(n => p[n] == 15) ? 0xEE02u : 0xEE00u;
            }
            var before = DesignBytes(kind, index + i);
            if (!before.SequenceEqual(data)) prepared.Add((index + i, before, data));
        }
        if (prepared.Count == 0) return;
        void Apply(bool undo) { foreach (var p in prepared) WriteDesign(kind, p.Index, undo ? p.Before : p.After); }
        Commit(() => Apply(true), () => Apply(false));
    }
    private static void CopyDesignOrigin(IVillagerOrigin design, IVillagerOrigin owner)
    {
        // Copy only the identity fields. Matching pixel bytes must never be renamed.
        owner.GetTownIdentity().CopyTo(design.GetTownIdentity());
        owner.GetPlayerIdentity().CopyTo(design.GetPlayerIdentity());
    }
}
