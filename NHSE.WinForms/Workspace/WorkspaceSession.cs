using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

/// <summary>Edits the existing C# save model. Nothing is written to disk until Save.</summary>
internal sealed partial class WorkspaceSession(HorizonSave save)
{
    public HorizonSave SaveFile { get; } = save;
    internal sealed record ActionInfo(string Command, string Scope);
    private sealed record Change(Action Undo, Action Redo, long Before, long After, ActionInfo? Description);
    private readonly string SessionId = Guid.NewGuid().ToString("N");
    private ActionInfo? CurrentAction;
    public void SetActionContext(ActionInfo? action) => CurrentAction = action;
    private sealed record IdentityPatch(EncryptedFilePair File, int Offset, byte[] Before, byte[] After);
    private readonly Stack<Change> UndoStack = new();
    private readonly Stack<Change> RedoStack = new();
    private long Revision;
    private long NextRevision;
    private long SavedRevision;
    private bool LegacyChanges;
    public bool IsDirty => LegacyChanges || Revision != SavedRevision;

    public object Snapshot(Func<int, string?>? getAvatar = null, Func<string, string?>? getVillagerImage = null) => new
    {
        id = SessionId,
        title = SaveFile.GetSaveTitle("NHSE"),
        island = SaveFile.Players[0].Personal.TownName,
        clipboard = GridBuffer?.Info, dirty = IsDirty, canUndo = UndoStack.Count != 0, canRedo = RedoStack.Count != 0,
        undoAction = UndoStack.TryPeek(out var undo) ? undo.Description : null,
        redoAction = RedoStack.TryPeek(out var redo) ? redo.Description : null,
        islandSettings = GetIslandSettings(),
        turnips = GetTurnips(),
        residents = GetResidents(getVillagerImage),
        players = SaveFile.Players.Select((p, i) => new
        {
            misc = GetMiscProfile(p.Personal),
            index = i, name = p.Personal.PlayerName, island = p.Personal.TownName, avatar = getAvatar?.Invoke(i),
            wallet = (uint)p.Personal.Wallet, bank = (uint)p.Personal.Bank,
            miles = (uint)p.Personal.NookMiles, earnedMiles = (uint)p.Personal.TotalNookMiles,
            pocketCapacity = p.Personal.PocketCount + p.Personal.BagCount,
            storageCapacity = p.Personal.ItemChestCount,
            maxPocketCapacity = p.Personal.Offsets.Pockets1Count + p.Personal.Offsets.Pockets2Count,
            maxStorageCapacity = p.Personal.Offsets.ItemChestCount,
            hotelTickets = p.Personal.Data30 is { IsInitialized30: true } addition ? (uint?)addition.HotelTickets : null,
            poki = p.WhereAreN is { } where ? (uint?)where.Poki : null,
        }).ToArray(),
    };

    private Personal Player(int player)
    {
        if ((uint)player >= SaveFile.Players.Count)
            throw new ArgumentException("invalidRequest");
        return SaveFile.Players[player].Personal;
    }

    public Item[] GetItems(int player, string container)
    {
        var p = Player(player);
        return container switch
        {
            "pocket" => p.Pocket.Concat(p.Bag).ToArray(),
            "storage" => p.ItemChest.ToArray(),
            "recycle" => SaveFile.Main.RecycleBin.ToArray(),
            _ => throw new ArgumentException("invalidRequest"),
        };
    }

    public bool IsSlotAvailable(int player, string container, int index)
    {
        var p = Player(player);
        if (container == "recycle") return (uint)index < MainSaveOffsets.RecycleBinCount;
        if (container == "storage")
            return (uint)index < Math.Min(p.ItemChestCount, (uint)p.Offsets.ItemChestCount);
        return index >= 0 && (index < p.Offsets.Pockets2Count
            ? index < Math.Min(p.PocketCount, (uint)p.Offsets.Pockets2Count)
            : index - p.Offsets.Pockets2Count < Math.Min(p.BagCount, (uint)p.Offsets.Pockets1Count));
    }

    private void PutItems(int player, string container, Item[] items)
    {
        var p = Player(player);
        if (container == "recycle")
        {
            SaveFile.Main.RecycleBin = items;
            return;
        }
        if (container == "storage")
        {
            p.ItemChest = items;
            return;
        }
        var split = p.Offsets.Pockets2Count;
        p.Pocket = items.AsSpan(0, split).ToArray();
        p.Bag = items.AsSpan(split).ToArray();
    }

    public static int MaxQuantity(Item item) => ItemInfo.TryGetMaxStackCount(item, out var max) ? Math.Max(1, (int)max) : 1;
    public static int Quantity(Item item) => item.IsNone ? 0 : MaxQuantity(item) > 1 ? item.Count + 1 : 1;

    public void SetItem(int player, string container, int index, Item replacement)
    {
        var items = GetItems(player, container);
        if ((uint)index >= items.Length || !IsSlotAvailable(player, container, index))
            throw new ArgumentException("invalidRequest");
        var previous = items[index].RawValue;
        var next = replacement.RawValue;
        if (previous == next)
            return;
        void Apply(ulong raw)
        {
            var current = GetItems(player, container);
            current[index] = new Item(raw);
            PutItems(player, container, current);
        }
        Commit(() => Apply(previous), () => Apply(next));
    }

    public void SetPlayer(int player, uint wallet, uint bank, uint miles, uint earnedMiles,
        string? name = null, string? island = null, uint? pocketCapacity = null, uint? storageCapacity = null,
        uint? hotelTickets = null, uint? poki = null)
    {
        if (wallet > 999_999_999 || bank > 999_999_999 || miles > 999_999_999 || earnedMiles > 999_999_999)
            throw new ArgumentException("invalidRequest");
        var p = Player(player);
        if (name is not null) ValidateName(name);
        if (island is not null) ValidateName(island);
        if (pocketCapacity > p.Offsets.Pockets1Count + p.Offsets.Pockets2Count || storageCapacity > p.Offsets.ItemChestCount
            || hotelTickets > int.MaxValue || poki > int.MaxValue)
            throw new ArgumentException("invalidRequest");
        var addition = p.Data30 is { IsInitialized30: true } initialized ? initialized : null;
        var where = SaveFile.Players[player].WhereAreN;
        if ((hotelTickets is not null && addition is null) || (poki is not null && where is null))
            throw new ArgumentException("invalidRequest");
        var before = new[] { (uint)p.Wallet, (uint)p.Bank, (uint)p.NookMiles, (uint)p.TotalNookMiles };
        var after = new[] { wallet, bank, miles, earnedMiles };
        var oldPocket = p.PocketCount;
        var oldBag = p.BagCount;
        var oldStorage = p.ItemChestCount;
        var oldTickets = addition is null ? 0 : (uint)addition.HotelTickets;
        var oldPoki = where is null ? 0 : (uint)where.Poki;
        var patches = new List<IdentityPatch>();
        if (name is not null && name != p.PlayerName)
            CollectIdentityPatches(p.GetPlayerIdentity().ToArray(), name, patches);
        if (island is not null && island != p.TownName)
            CollectIdentityPatches(p.GetTownIdentity().ToArray(), island, patches);
        if (before.SequenceEqual(after) && patches.Count == 0 && (!pocketCapacity.HasValue || pocketCapacity == oldPocket + oldBag)
            && (!storageCapacity.HasValue || storageCapacity == oldStorage)
            && (!hotelTickets.HasValue || hotelTickets == oldTickets) && (!poki.HasValue || poki == oldPoki))
            return;
        void Apply(uint[] values, bool undo)
        {
            foreach (var patch in patches)
                (undo ? patch.Before : patch.After).CopyTo(patch.File.Data[patch.Offset..]);
            p.Wallet = p.Wallet with { Value = values[0] };
            p.Bank = p.Bank with { Value = values[1] };
            p.NookMiles = p.NookMiles with { Value = values[2] };
            p.TotalNookMiles = p.TotalNookMiles with { Value = values[3] };
            if (undo)
            {
                p.PocketCount = oldPocket;
                p.BagCount = oldBag;
                p.ItemChestCount = oldStorage;
            }
            else
            {
                if (pocketCapacity is { } capacity && capacity != oldPocket + oldBag)
                {
                    p.PocketCount = Math.Min(capacity, (uint)p.Offsets.Pockets2Count);
                    p.BagCount = capacity - p.PocketCount;
                }
                if (storageCapacity is { } storage) p.ItemChestCount = storage;
            }
            if (hotelTickets.HasValue && addition is not null)
                addition.HotelTickets = addition.HotelTickets with { Value = undo ? oldTickets : hotelTickets.Value };
            if (poki.HasValue && where is not null)
                where.Poki = where.Poki with { Value = undo ? oldPoki : poki.Value };
        }
        Commit(() => Apply(before, true), () => Apply(after, false));
    }

    private static void ValidateName(string name)
    {
        // The game's names contain at most 10 UTF-16 code units. Never silently truncate.
        if (string.IsNullOrWhiteSpace(name) || name.Length > 10 || name.Any(char.IsControl))
            throw new ArgumentException("invalidRequest");
        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsSurrogate(name[i])) continue;
            if (!char.IsHighSurrogate(name[i]) || i + 1 >= name.Length || !char.IsLowSurrogate(name[++i]))
                throw new ArgumentException("invalidRequest");
        }
    }

    private void CollectIdentityPatches(byte[] original, string name, List<IdentityPatch> patches)
    {
        var updated = original.ToArray();
        StringUtil.GetBytes(name, 10).CopyTo(updated, 4);
        // Match the existing ChangeIdentity scope, but record exact offsets for undo.
        // Reversing a global replacement could also rename pre-existing matching identities.
        foreach (var file in new[] { SaveFile.Main }.Cast<EncryptedFilePair>().Concat(SaveFile.Players.SelectMany(x => x)))
        {
            var offset = 0;
            while (offset <= file.Data.Length - original.Length)
            {
                var found = file.Data[offset..].IndexOf(original);
                if (found < 0) break;
                offset += found;
                patches.Add(new IdentityPatch(file, offset, original, updated));
                offset += original.Length;
            }
        }
    }

    private void Commit(Action undo, Action redo)
    {
        redo();
        UndoStack.Push(new Change(undo, redo, Revision, ++NextRevision, CurrentAction));
        Revision = NextRevision;
        RedoStack.Clear();
    }

    public void Undo()
    {
        if (!UndoStack.TryPop(out var change))
            return;
        change.Undo();
        Revision = change.Before;
        RedoStack.Push(change);
    }

    public void Redo()
    {
        if (!RedoStack.TryPop(out var change))
            return;
        change.Redo();
        Revision = change.After;
        UndoStack.Push(change);
    }

    public void Save()
    {
        SaveFile.Save((uint)DateTime.Now.Ticks);
        SavedRevision = Revision;
        LegacyChanges = false;
    }

    public void InvalidateHistory()
    {
        UndoStack.Clear();
        RedoStack.Clear();
        LegacyChanges = true;
    }
}
