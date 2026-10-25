using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record AdvancedTarget(string Kind, int Player = 0, int Resident = 0, int Index = 0, string Container = "pocket");
    internal sealed record AdvancedField(string Key, string Type, string Value, string Min = "", string Max = "", int Length = 0);
    internal sealed record AdvancedEntry(int Index, string Name, bool Available = true);
    internal sealed record AdvancedData(AdvancedTarget Target, AdvancedEntry[] Entries, AdvancedField[] Fields, string Extension, int Size);
    private sealed record AdvancedBinding(AdvancedField Field, Action<string> Set);
    private sealed record AdvancedDocument(byte[] Before, Func<byte[]> Finish, Action<byte[]> Write, AdvancedEntry[] Entries, List<AdvancedBinding> Bindings, string Extension);
    private static string TextValue(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static bool Scalar(Type t) => t == typeof(string) || t == typeof(bool) || t.IsEnum || t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong);
    private static (decimal Min, decimal Max) AdvancedLimits(Type t) => t == typeof(ulong) ? (0, ulong.MaxValue) : t == typeof(long) ? (long.MinValue, long.MaxValue) : Limits(t);
    private static void ValidateText(string value, int length)
    {
        if (value.Length > length || value.Contains('\0')) throw new ArgumentException("advancedInvalid");
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i]))) throw new ArgumentException("advancedInvalid");
    }
    private static object ParseAdvanced(Type type, string value, int length)
    {
        if (type == typeof(string)) { ValidateText(value, length); return value; }
        if (type == typeof(bool)) return bool.TryParse(value, out var flag) ? flag : throw new ArgumentException("advancedInvalid");
        var numeric = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
        var (min, max) = AdvancedLimits(numeric);
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n < min || n > max || decimal.Truncate(n) != n) throw new ArgumentException("advancedInvalid");
        var converted = Convert.ChangeType(n, numeric, CultureInfo.InvariantCulture);
        return type.IsEnum ? Enum.ToObject(type, converted) : converted;
    }
    private static AdvancedBinding Bind(string key, Type type, object value, Action<object> set, int length = 0)
    {
        var (min, max) = Scalar(type) && type != typeof(string) && type != typeof(bool) ? AdvancedLimits(type.IsEnum ? Enum.GetUnderlyingType(type) : type) : (0m, 0m);
        var text = type.IsEnum ? TextValue(Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)) : TextValue(value);
        return new(new(key, type == typeof(bool) ? "boolean" : type == typeof(string) ? "text" : "number", text, TextValue(min), TextValue(max), length), v => set(ParseAdvanced(type, v, length)));
    }
    private static readonly string[] ResidentProperties = ["Species", "Variant", "Personality", "CatchPhrase", "BirthType", "InducementType", "MoveType", "MovingOut", "DIYEndHour", "DIYEndMinute", "DIYEndSecond", "DIYRecipeIndex"];
    private static List<AdvancedBinding> ObjectBindings(object root, string[]? allowed = null)
    {
        var bindings = new List<AdvancedBinding>();
        void Walk(object current, string prefix, Action<object> writeBack, int depth)
        {
            foreach (var prop in current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (depth == 0 && allowed is not null && !allowed.Contains(prop.Name)) continue;
                if (prop.Name is "PlayerId" || prop.Name.StartsWith("Extension", StringComparison.Ordinal)) continue;
                var key = prefix + prop.Name; var value = prop.GetValue(current)!;
                if (Scalar(prop.PropertyType))
                {
                    var length = prop.Name == "CatchPhrase" ? 24 : 10;
                    bindings.Add(Bind(key, prop.PropertyType, value, next => { prop.SetValue(current, next); writeBack(current); }, length));
                }
                else if (depth < 3 && prop.PropertyType.IsValueType && !prop.PropertyType.IsPrimitive)
                    Walk(value, key + ".", next => { prop.SetValue(current, next); writeBack(current); }, depth + 1);
            }
            foreach (var field in current.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly && Scalar(f.FieldType)))
                bindings.Add(Bind(prefix + field.Name, field.FieldType, field.GetValue(current)!, next => { field.SetValue(current, next); writeBack(current); }));
        }
        Walk(root, "", _ => { }, 0);
        if (root is Item item)
        {
            var parameters = MapParameters().ToDictionary(p => p.Key);
            for (var i = 0; i < bindings.Count; i++)
                if (parameters.TryGetValue(bindings[i].Field.Key, out var parameter))
                {
                    var binding = bindings[i];
                    bindings[i] = new(binding.Field with { Min = parameter.Min, Max = parameter.Max }, value => {
                        if (parameter.Type != "boolean" && (!decimal.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                            || n < decimal.Parse(parameter.Min, CultureInfo.InvariantCulture) || n > decimal.Parse(parameter.Max, CultureInfo.InvariantCulture))) throw new ArgumentException("advancedInvalid");
                        binding.Set(value);
                    });
                }
            for (var visitor = 0; visitor < 10; visitor++)
            {
                var index = visitor;
                bindings.Add(Bind("WateredVisitor." + index, typeof(bool), item.GetIsWateredByVisitor(index), value => item.SetIsWateredByVisitor(index, (bool)value)));
            }
        }
        return bindings;
    }
    private IVillager AdvancedResident(AdvancedTarget target)
    {
        if ((uint)target.Resident >= MainSaveOffsets.VillagerCount) throw new ArgumentException("invalidRequest");
        return SaveFile.Main.Offsets.ReadVillager(SaveFile.Main.GetVillager(target.Resident).Write().ToArray());
    }
    private static AdvancedEntry[] Numbered(int count, Func<int, string> name) => Enumerable.Range(0, count).Select(i => new AdvancedEntry(i, name(i))).ToArray();
    private AdvancedDocument AdvancedDocumentFor(AdvancedTarget t)
    {
        Player(t.Player);
        if (t.Kind is "flagsPlayer" or "flagsLand" or "flagsResident")
        {
            var villager = t.Kind == "flagsResident" ? AdvancedResident(t) : null;
            var values = t.Kind == "flagsPlayer" ? Player(t.Player).GetEventFlagsPlayer().Select(v => (int)v).ToArray()
                : t.Kind == "flagsLand" ? SaveFile.Main.EventFlagLand.ToArray().Select(v => (int)v).ToArray() : villager!.GetEventFlagsSave().Select(v => (int)v).ToArray();
            if ((uint)t.Index >= values.Length) throw new ArgumentException("invalidRequest");
            var bytes = new byte[values.Length * 2]; for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
            var after = bytes.ToArray();
            var unsigned = t.Kind == "flagsResident";
            var entryNames = Numbered(values.Length, i => t.Kind == "flagsPlayer" ? EventFlagPlayer.GetName((ushort)i, (short)values[i], GameInfo.Strings.InternalNameTranslation)
                : t.Kind == "flagsLand" ? EventFlagLand.GetName((ushort)i, (short)values[i], GameInfo.Strings.InternalNameTranslation)
                : EventFlagVillager.GetName((ushort)i, (ushort)values[i], GameInfo.Strings.InternalNameTranslation));
            void Write(byte[] data)
            {
                var numbers = Enumerable.Range(0, values.Length).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2))).ToArray();
                if (t.Kind == "flagsPlayer") Player(t.Player).SetEventFlagsPlayer(numbers.Select(v => unchecked((short)v)).ToArray());
                else if (t.Kind == "flagsLand") numbers.Select(v => unchecked((short)v)).ToArray().CopyTo(SaveFile.Main.EventFlagLand);
                else { var current = AdvancedResident(t); current.SetEventFlagsSave(numbers); PutResident(current.Write(), t.Resident); }
            }
            return new(bytes, () => after.ToArray(), Write, entryNames,
                [Bind("Value", unsigned ? typeof(ushort) : typeof(short), unsigned ? (object)(ushort)values[t.Index] : (short)values[t.Index], value => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan(t.Index * 2), unchecked((ushort)Convert.ToInt32(value, CultureInfo.InvariantCulture))))], "nhfl");
        }
        if (t.Kind == "mailbox") return MailDocument(t);
        if (t.Kind == "item")
        {
            var items = GetItems(t.Player, t.Container);
            if ((uint)t.Index >= items.Length || !IsSlotAvailable(t.Player, t.Container, t.Index)) throw new ArgumentException("invalidRequest");
            var item = new Item(items[t.Index].RawValue); var before = Item.SetArray([item]);
            void Write(byte[] bytes) { var current = GetItems(t.Player, t.Container); current[t.Index] = Item.GetArray(bytes)[0]; PutItems(t.Player, t.Container, current); }
            return new(before, () => Item.SetArray([item]), Write, items.Select((i, index) => new AdvancedEntry(index, i.IsNone ? "" : GameInfo.Strings.GetItemName(i), IsSlotAvailable(t.Player, t.Container, index))).ToArray(), ObjectBindings(item), "nhi");
        }
        var resident = AdvancedResident(t); var residentBefore = resident.Write();
        var count = 1; object root = resident; Action save = () => { }; string extension = resident.Extension;
        if (t.Kind == "resident") root = resident;
        else if (t.Kind == "room")
        {
            root = resident.Room;
            save = () => {
                resident.Room = (GSaveRoomFloorWall)root;
                var data = resident.Write(); var offset = resident is Villager1 ? 0x12100 : 0x12880;
                residentBefore.AsSpan(offset + 0x21, 3).CopyTo(data.AsSpan(offset + 0x21));
                resident = SaveFile.Main.Offsets.ReadVillager(data);
            };
            extension = "nhvroom";
        }
        else if (t.Kind == "memory")
        {
            var memories = resident.GetMemories(); count = memories.Length;
            if ((uint)t.Index >= count) throw new ArgumentException("invalidRequest");
            root = memories[t.Index]; save = () => resident.SetMemory((GSaveMemory)root, t.Index); extension = "nhvpm";
        }
        else if (t.Kind is "wear" or "furniture")
        {
            var list = (t.Kind == "wear" ? resident.WearStockList : resident.FtrStockList).ToArray(); count = list.Length;
            if ((uint)t.Index >= count) throw new ArgumentException("invalidRequest");
            root = list[t.Index]; save = () => { if (t.Kind == "wear") resident.WearStockList = list; else resident.FtrStockList = list; }; extension = "nhi";
        }
        else throw new ArgumentException("invalidRequest");
        var fields = ObjectBindings(root, t.Kind == "resident" ? ResidentProperties : null);
        if (root is GSaveMemory memory)
        {
            for (var i = 0; i < 11; i++) { var index = i; fields.Add(Bind("Greeting." + index, typeof(string), memory.GetGreeting(index), value => memory.SetGreeting((string)value, index), 22)); }
            var flags = memory.GetEventFlags();
            for (var i = 0; i < flags.Length; i++) { var index = i; fields.Add(Bind("Flag." + index, typeof(byte), flags[index], value => { var current = memory.GetEventFlags(); current[index] = (byte)value; memory.SetEventFlags(current); })); }
        }
        return new(residentBefore, () => { save(); return resident.Write(); }, bytes => PutResident(bytes, t.Resident), Numbered(count, i => "#" + i), fields, extension);
    }
    public AdvancedData GetAdvanced(AdvancedTarget target)
    {
        var doc = AdvancedDocumentFor(target);
        return new(target, doc.Entries, doc.Bindings.Select(b => b.Field).ToArray(), doc.Extension, ExportAdvanced(target).Length);
    }
    public void SetAdvanced(AdvancedTarget target, Dictionary<string, string> values)
    {
        var doc = AdvancedDocumentFor(target);
        if (values.Keys.Except(doc.Bindings.Select(b => b.Field.Key)).Any()) throw new ArgumentException("advancedInvalid");
        if (!doc.Bindings.Any(b => values.TryGetValue(b.Field.Key, out var v) && v != b.Field.Value)) return;
        foreach (var binding in doc.Bindings)
            if (values.TryGetValue(binding.Field.Key, out var value) && value != binding.Field.Value) binding.Set(value);
        var after = doc.Finish();
        if (!doc.Before.SequenceEqual(after)) Commit(() => doc.Write(doc.Before.ToArray()), () => doc.Write(after.ToArray()));
    }
    private sealed record MailSpec(string Key, int Offset, int Size, string Type = "number");
    private static readonly MailSpec[] MailSpecs = [
        new("ReadStatus",0,1), new("Favourite",1,1), new("SenderIslandID",4,4),new("SenderIslandName",8,20,"text"),
        new("SenderSpecies",4,1), new("SenderVariant",5,1),new("NPCIslandID",8,4),new("NPCIslandName",12,20,"text"),
        new("SenderID",0x20,4),new("SenderName",0x24,20,"text"),new("SenderType",0x5C,1),
        new("ReceiverIslandID",0x60,4),new("ReceiverIslandName",0x64,20,"text"),new("ReceiverPlayerID",0x7C,4),new("ReceiverPlayerName",0x80,20,"text"),
        new("Year",0xBC,2),new("Month",0xBE,1),new("Day",0xBF,1),
        new("ItemId",0xC0,2),new("SystemParam",0xC2,1),new("AdditionalParam",0xC3,1),new("FreeParam",0xC4,4,"signed"),
        new("MailType01",0xC8,1),new("MailType02",0xCC,1),new("To",0xCE,96,"text"),new("Message",0x12E,480,"text"),new("From",0x30E,96,"text"),
        new("Stationery",0x36E,2),new("SortIndex",0x374,2),new("Language",0x390,5,"ascii")];
    private AdvancedDocument MailDocument(AdvancedTarget t)
    {
        if ((uint)t.Index >= 300) throw new ArgumentException("invalidRequest");
        var post = SaveFile.Players[t.Player].PostBox;
        var before = post.GetIndividualLetter(t.Index).ToArray(); var bytes = before.ToArray();
        var fields = MailSpecs.Select(spec => {
            var slice = bytes.AsSpan(spec.Offset, spec.Size);
            if (spec.Type == "text") return Bind(spec.Key, typeof(string), StringUtil.GetString(bytes, spec.Offset, spec.Size / 2 - 1), value => StringUtil.GetBytes((string)value, spec.Size / 2 - 1).CopyTo(bytes.AsSpan(spec.Offset)), spec.Size / 2 - 1);
            if (spec.Type == "ascii") return Bind(spec.Key, typeof(string), Encoding.ASCII.GetString(slice).TrimEnd('\0'), value => {
                var text = (string)value; if (text.Length == 0) { bytes.AsSpan(spec.Offset, spec.Size).Clear(); return; }
                if (text.Length != 5 || text.Any(c => c > 127)) throw new ArgumentException("advancedInvalid");
                Encoding.ASCII.GetBytes(text).CopyTo(bytes.AsSpan(spec.Offset)); }, 5);
            var type = spec.Size == 1 ? typeof(byte) : spec.Size == 2 ? typeof(ushort) : spec.Type == "signed" ? typeof(int) : typeof(uint);
            object current = spec.Size == 1 ? slice[0] : spec.Size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(slice) : spec.Type == "signed" ? (object)BinaryPrimitives.ReadInt32LittleEndian(slice) : BinaryPrimitives.ReadUInt32LittleEndian(slice);
            return Bind(spec.Key, type, current, value => {
                var dest = bytes.AsSpan(spec.Offset, spec.Size);
                if (spec.Size == 1) dest[0] = Convert.ToByte(value); else if (spec.Size == 2) BinaryPrimitives.WriteUInt16LittleEndian(dest, Convert.ToUInt16(value));
                else if (spec.Type == "signed") BinaryPrimitives.WriteInt32LittleEndian(dest, (int)value); else BinaryPrimitives.WriteUInt32LittleEndian(dest, Convert.ToUInt32(value));
            });
        }).ToList();
        return new(before, () => bytes.ToArray(), data => post.SetIndividualLetter(t.Index, data),
            Numbered(300, i => $"{post.GetIndividualLetter(i)[0]} · {post.GetLetterToLineText(i)} · {post.GetLetterMessageText(i)}"), fields, "nhpb");
    }
    public byte[] ExportAdvanced(AdvancedTarget t)
    {
        var doc = AdvancedDocumentFor(t);
        if (t.Kind is "resident" or "flagsPlayer" or "flagsLand" or "flagsResident" or "mailbox" or "item") return doc.Before.ToArray();
        var v = AdvancedResident(t);
        return t.Kind switch { "memory" => v.GetMemory(t.Index).Data.ToArray(), "room" => v.Write().AsSpan(v is Villager1 ? 0x12100 : 0x12880, GSaveRoomFloorWall.SIZE).ToArray(),
            "wear" => Item.SetArray([v.WearStockList[t.Index]]), "furniture" => Item.SetArray([v.FtrStockList[t.Index]]), _ => throw new ArgumentException("invalidRequest") };
    }
    public void ImportAdvanced(AdvancedTarget t, byte[] bytes)
    {
        var expected = ExportAdvanced(t);
        if (t.Kind == "resident")
        {
            if (!VillagerConverter.IsCompatible(bytes.Length, expected.Length)) throw new ArgumentException("advancedFileSize");
            bytes = VillagerConverter.GetCompatible(bytes.ToArray(), expected.Length).ToArray();
        }
        if (bytes.Length != expected.Length) throw new ArgumentException("advancedFileSize");
        if (t.Kind == "mailbox") { bytes = bytes.ToArray(); expected.AsSpan(Mail.PostBoxSortIndex, 2).CopyTo(bytes.AsSpan(Mail.PostBoxSortIndex)); }
        if (expected.SequenceEqual(bytes)) return;
        var doc = AdvancedDocumentFor(t); byte[] after;
        if (t.Kind is "resident" or "flagsPlayer" or "flagsLand" or "flagsResident" or "mailbox" or "item") after = bytes.ToArray();
        else
        {
            var v = AdvancedResident(t);
            if (t.Kind == "memory") v.SetMemory(new GSaveMemory(bytes.ToArray()), t.Index);
            else if (t.Kind == "room") { var data = v.Write(); bytes.CopyTo(data.AsSpan(v is Villager1 ? 0x12100 : 0x12880)); v = SaveFile.Main.Offsets.ReadVillager(data); }
            else { var list = (t.Kind == "wear" ? v.WearStockList : v.FtrStockList).ToArray(); list[t.Index].CopyFrom(Item.GetArray(bytes)[0]); if (t.Kind == "wear") v.WearStockList = list; else v.FtrStockList = list; }
            after = v.Write();
        }
        if (!doc.Before.SequenceEqual(after)) Commit(() => doc.Write(doc.Before.ToArray()), () => doc.Write(after.ToArray()));
    }
}
