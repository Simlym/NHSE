using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

/// <summary>Edits the existing C# save model. Nothing is written to disk until Save.</summary>
internal sealed class WorkspaceSession(HorizonSave save)
{
    public HorizonSave SaveFile { get; } = save;
    private sealed record Change(Action Undo, Action Redo, long Before, long After);
    private readonly Stack<Change> UndoStack = new();
    private readonly Stack<Change> RedoStack = new();
    private long Revision;
    private long NextRevision;
    private long SavedRevision;
    private bool LegacyChanges;
    public bool IsDirty => LegacyChanges || Revision != SavedRevision;

    public object Snapshot() => new
    {
        title = SaveFile.GetSaveTitle("NHSE"),
        island = SaveFile.Players[0].Personal.TownName,
        dirty = IsDirty, canUndo = UndoStack.Count != 0, canRedo = RedoStack.Count != 0,
        players = SaveFile.Players.Select((p, i) => new
        {
            index = i, name = p.Personal.PlayerName, island = p.Personal.TownName,
            wallet = (uint)p.Personal.Wallet, bank = (uint)p.Personal.Bank,
            miles = (uint)p.Personal.NookMiles, earnedMiles = (uint)p.Personal.TotalNookMiles,
            pocketCapacity = p.Personal.PocketCount + p.Personal.BagCount,
            storageCapacity = p.Personal.ItemChestCount,
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
            _ => throw new ArgumentException("invalidRequest"),
        };
    }

    public bool IsSlotAvailable(int player, string container, int index)
    {
        var p = Player(player);
        if (container == "storage")
            return (uint)index < Math.Min(p.ItemChestCount, (uint)p.Offsets.ItemChestCount);
        return index >= 0 && (index < p.Offsets.Pockets2Count
            ? index < Math.Min(p.PocketCount, (uint)p.Offsets.Pockets2Count)
            : index - p.Offsets.Pockets2Count < Math.Min(p.BagCount, (uint)p.Offsets.Pockets1Count));
    }

    private void PutItems(int player, string container, Item[] items)
    {
        var p = Player(player);
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

    public void SetPlayer(int player, uint wallet, uint bank, uint miles, uint earnedMiles)
    {
        if (wallet > 999_999_999 || bank > 999_999_999 || miles > 999_999_999 || earnedMiles > 999_999_999)
            throw new ArgumentException("invalidRequest");
        var p = Player(player);
        var before = new[] { (uint)p.Wallet, (uint)p.Bank, (uint)p.NookMiles, (uint)p.TotalNookMiles };
        var after = new[] { wallet, bank, miles, earnedMiles };
        if (before.SequenceEqual(after))
            return;
        void Apply(uint[] values)
        {
            p.Wallet = p.Wallet with { Value = values[0] };
            p.Bank = p.Bank with { Value = values[1] };
            p.NookMiles = p.NookMiles with { Value = values[2] };
            p.TotalNookMiles = p.TotalNookMiles with { Value = values[3] };
        }
        Commit(() => Apply(before), () => Apply(after));
    }

    private void Commit(Action undo, Action redo)
    {
        redo();
        UndoStack.Push(new Change(undo, redo, Revision, ++NextRevision));
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
