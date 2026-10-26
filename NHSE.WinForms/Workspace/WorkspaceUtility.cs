using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record UtilityFile(string Name, byte[] Data);
    internal sealed record UtilityHashIssue(string File, string Region);
    internal sealed record UtilityHashes(bool Dirty, UtilityHashIssue[] Issues);
    private Dictionary<string, EncryptedFilePair> UtilityPairs()
    {
        var result = new Dictionary<string, EncryptedFilePair>(StringComparer.Ordinal) { [SaveFile.Main.NameData] = SaveFile.Main };
        foreach (var player in SaveFile.Players)
            foreach (var pair in player) result.Add(player.DirectoryName + "/" + pair.NameData, pair);
        return result;
    }
    public UtilityHashes VerifyHashes() => new(IsDirty, UtilityPairs().SelectMany(entry => entry.Value.InvalidHashes()
        .Select(region => new UtilityHashIssue(entry.Key, region.ToString()))).ToArray());
    public Dictionary<string, int> DecryptedFileSizes() => UtilityPairs().ToDictionary(p => p.Key, p => p.Value.Data.Length);
    public void ImportDecrypted(Dictionary<string, byte[]> files)
    {
        var pairs = UtilityPairs();
        if (!files.ContainsKey(SaveFile.Main.NameData) || files.Any(f => !pairs.TryGetValue(f.Key, out var pair) || f.Value is null || f.Value.Length != pair.Data.Length))
            throw new ArgumentException("utilityFileSize");
        var patches = files.Where(f => !pairs[f.Key].Data.SequenceEqual(f.Value))
            .Select(f => new IdentityPatch(pairs[f.Key], 0, pairs[f.Key].Data.ToArray(), f.Value.ToArray())).ToArray();
        if (patches.Length == 0) return;
        void Apply(bool undo) { foreach (var patch in patches) (undo ? patch.Before : patch.After).CopyTo(patch.File.Data); }
        Commit(() => Apply(true), () => Apply(false));
    }
    public UtilityFile[] ExportAllHousing(string kind)
    {
        var count = kind switch { "playerHouses" => MainSaveOffsets.PlayerCount, "residentHouses" => MainSaveOffsets.VillagerCount,
            _ => throw new ArgumentException("invalidRequest") };
        return Enumerable.Range(0, count).Select(index => new UtilityFile($"{kind}-{index + 1:00}.{HousingFileExtension(kind, index, 0, false)}",
            ExportHousing(kind, index, 0, false))).ToArray();
    }
    public byte[] ExportHouseFlags(int index)
    {
        var house = (IPlayerHouse)ReadHouse("playerHouses", index);
        var bytes = new byte[house.EventFlags.Length * 2];
        for (var i = 0; i < house.EventFlags.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), house.EventFlags[i]);
        return bytes;
    }
    public void ImportHouseFlags(int index, byte[] bytes)
    {
        var expected = ExportHouseFlags(index);
        if (bytes.Length != expected.Length) throw new ArgumentException("utilityFileSize");
        var before = HouseBytes(ReadHouse("playerHouses", index));
        var house = (IPlayerHouse)CloneHouse("playerHouses", before);
        for (var i = 0; i < house.EventFlags.Length; i++) house.EventFlags[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2));
        CommitHouse("playerHouses", index, before, house.Write());
    }
}
