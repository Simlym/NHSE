using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    private static StringInstructionSet[] TextBatchSets(string text)
    {
        if (text.Length is < 1 or > 32000) throw new ArgumentException("advancedBatchInvalid");
        var groups = new List<StringInstructionSet>(); var lines = new List<string>();
        foreach (var raw in text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Append(";"))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line == ";") { if (lines.Count != 0) { groups.Add(new StringInstructionSet(lines.ToArray().AsSpan())); lines.Clear(); } continue; }
            if (!StringInstruction.TryParseFilter(line, out _) && !StringInstruction.TryParseInstruction(line, out _)) throw new ArgumentException("advancedBatchInvalid");
            lines.Add(line);
        }
        if (groups.Count is < 1 or > 32) throw new ArgumentException("advancedBatchInvalid");
        foreach (var set in groups)
        {
            if (set.Instructions.Count == 0 || set.Filters.Count > 32 || set.Instructions.Count > 32) throw new ArgumentException("advancedBatchInvalid");
            foreach (var (instruction, write) in set.Filters.Select(i => (i, false)).Concat(set.Instructions.Select(i => (i, true))))
            {
                var prop = MapProperties.FirstOrDefault(p => p.Name == instruction.PropertyName) ?? throw new ArgumentException("advancedBatchInvalid");
                if (write && !MapParameterWritable(prop)) throw new ArgumentException("advancedBatchInvalid");
                var value = instruction.PropertyValue;
                if (!write && value.StartsWith('*'))
                {
                    var other = MapProperties.FirstOrDefault(p => p.Name == value[1..]);
                    if (other is null || other.PropertyType != prop.PropertyType) throw new ArgumentException("advancedBatchInvalid");
                    continue;
                }
                if (value.StartsWith('$'))
                {
                    var range = value[1..].Split(',');
                    if (!write || range.Length != 2 || !int.TryParse(range[0], out var min) || !int.TryParse(range[1], out var max) || min > max) throw new ArgumentException("advancedBatchInvalid");
                    CheckMapRule(new(prop.Name, "set", min.ToString(CultureInfo.InvariantCulture)), true);
                    CheckMapRule(new(prop.Name, "set", max.ToString(CultureInfo.InvariantCulture)), true);
                    instruction.SetRandomRange(value.AsSpan(1));
                }
                else
                {
                    ItemProcessor.ScreenStrings([instruction]);
                    CheckMapRule(new(prop.Name, write ? "set" : "eq", instruction.PropertyValue), write);
                }
            }
        }
        return groups.ToArray();
    }
    private static void RunTextBatch(Item[] items, StringInstructionSet[] sets)
    {
        var mutator = new ItemMutator();
        foreach (var set in sets) foreach (var item in items)
            if (mutator.Modify(item, set.Filters, set.Instructions) is ModifyResult.Error or ModifyResult.Invalid) throw new ArgumentException("advancedBatchInvalid");
    }
    public MapEditCount TextBatchMap(MapTarget target, bool whole, string text)
    {
        var sets = TextBatchSets(text); var layer = MapItemLayer(target);
        var roots = MapRoots(target, whole, layer);
        var items = roots.Select(r => new Item(r.Item.RawValue)).ToArray(); RunTextBatch(items, sets);
        return ApplyMapRootEdits(target, layer, roots.Select((r, i) => (r.X, r.Y, new Item(r.Item.RawValue), items[i])).ToList(), roots.Count);
    }
    public void TextBatchAdvanced(AdvancedTarget target, string text)
    {
        if (target.Kind is not ("item" or "wear" or "furniture")) throw new ArgumentException("invalidRequest");
        var sets = TextBatchSets(text);
        var bytes = ExportAdvancedCollection(target); var items = Item.GetArray(bytes);
        RunTextBatch(target.Kind == "item" ? items.Where((_, i) => IsSlotAvailable(target.Player, target.Container, i)).ToArray() : items, sets);
        ImportAdvancedCollection(target, Item.SetArray(items));
    }
    public byte[] ExportAdvancedCollection(AdvancedTarget target)
    {
        AdvancedDocumentFor(target);
        var items = target.Kind == "item" ? GetItems(target.Player, target.Container)
            : target.Kind == "wear" ? AdvancedResident(target).WearStockList.Cast<Item>().ToArray()
            : target.Kind == "furniture" ? AdvancedResident(target).FtrStockList.Cast<Item>().ToArray() : throw new ArgumentException("invalidRequest");
        return Item.SetArray(items);
    }
    public void ImportAdvancedCollection(AdvancedTarget target, byte[] bytes)
    {
        var before = ExportAdvancedCollection(target);
        if (bytes.Length != before.Length) throw new ArgumentException("advancedFileSize");
        var after = bytes.ToArray(); if (before.SequenceEqual(after)) return;
        var items = Item.GetArray(after);
        if (target.Kind == "item")
        {
            for (var i = 0; i < items.Length; i++)
                if (!IsSlotAvailable(target.Player, target.Container, i) && !before.AsSpan(i * Item.SIZE, Item.SIZE).SequenceEqual(after.AsSpan(i * Item.SIZE, Item.SIZE))) throw new ArgumentException("advancedInvalid");
            void Write(byte[] data) => PutItems(target.Player, target.Container, Item.GetArray(data));
            Commit(() => Write(before), () => Write(after));
        }
        else
        {
            var resident = AdvancedResident(target); var residentBefore = resident.Write();
            var stock = (target.Kind == "wear" ? resident.WearStockList : resident.FtrStockList).ToArray();
            for (var i = 0; i < stock.Length; i++) stock[i].CopyFrom(items[i]);
            if (target.Kind == "wear") resident.WearStockList = stock; else resident.FtrStockList = stock;
            var residentAfter = resident.Write();
            Commit(() => PutResident(residentBefore, target.Resident), () => PutResident(residentAfter, target.Resident));
        }
    }
    public void ResidentBatch(string operation)
    {
        if (operation is not ("friendship" or "moveOut" or "resetPhrase")) throw new ArgumentException("invalidRequest");
        var changes = new List<(int Index, byte[] Before, byte[] After)>();
        for (var i = 0; i < MainSaveOffsets.VillagerCount; i++)
        {
            var resident = AdvancedResident(new("resident", Resident: i));
            if (resident.Species >= (byte)VillagerSpecies.non) continue;
            var before = resident.Write();
            if (operation == "friendship") resident.SetFriendshipAll();
            else if (operation == "moveOut") resident.MovingOut = true;
            else resident.CatchPhrase = GameInfo.Strings.GetVillagerDefaultPhrase(resident.InternalName);
            var after = resident.Write(); if (!before.SequenceEqual(after)) changes.Add((i, before, after));
        }
        if (changes.Count == 0) return;
        Commit(() => { foreach (var c in changes) PutResident(c.Before, c.Index); }, () => { foreach (var c in changes) PutResident(c.After, c.Index); });
    }
}
