using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record IslandRecordField(string Key, string Type, string Value, string Group,
        string Min = "", string Max = "", int Length = 0, string[]? Choices = null);
    internal sealed record IslandRecordEntry(int Index, string Body, string Date);
    internal sealed record IslandRecordsData(string Section, int Index, IslandRecordEntry[] Entries, IslandRecordField[] Fields);
    private sealed record IslandRecordBinding(IslandRecordField Field, Action<string> Set);
    private sealed record IslandRecordDocument(int Offset, byte[] Before, byte[] After,
        IslandRecordEntry[] Entries, List<IslandRecordBinding> Bindings);
    // Derive the existing marshaled layout without serializing a whole struct back over padding/unknown bytes.
    internal static int RecordOffset<T>(string property) => checked((int)Marshal.OffsetOf<T>($"<{property}>k__BackingField"));
    private static string RecordString(byte[] bytes, int offset, int chars) => Encoding.Unicode.GetString(bytes, offset, chars * 2).Split('\0')[0];
    private static string RecordDate(byte[] bytes, int offset)
    {
        var year = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        var month = bytes[offset + 2]; var day = bytes[offset + 3];
        return year == 0 && month == 0 && day == 0 ? "" : $"{year:0000}-{month:00}-{day:00}";
    }
    private IslandRecordDocument IslandRecordDocumentFor(string section, int index)
    {
        if (section is not ("bulletin" or "visitors") || index < 0 || index >= (section == "bulletin" ? BulletinBoardStock.MaxCount : 1))
            throw new ArgumentException("invalidRequest");
        var main = SaveFile.Main;
        var offset = section == "bulletin" ? main.Offsets.BulletinBoard : main.Offsets.Visitor;
        var before = main.Data.Slice(offset, section == "bulletin" ? GSaveBulletinBoard.SIZE : GSaveVisitorNpc.SIZE).ToArray();
        var after = before.ToArray();
        var bindings = new List<IslandRecordBinding>();
        void Number(string key, int at, Type type, string group = "advanced", string[]? choices = null, bool wideBoolean = false)
        {
            var size = type == typeof(byte) || type == typeof(bool) && !wideBoolean ? 1 : type == typeof(ushort) ? 2 : type == typeof(ulong) ? 8 : 4;
            var span = before.AsSpan(at, size);
            object value = type == typeof(bool) ? (wideBoolean ? BinaryPrimitives.ReadUInt32LittleEndian(span) != 0 : span[0] != 0) : type == typeof(byte) ? span[0]
                : type == typeof(ushort) ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : type == typeof(ulong) ? BinaryPrimitives.ReadUInt64LittleEndian(span)
                : type == typeof(int) ? (object)BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
            var binding = Bind(key, type, value, next =>
            {
                var dest = after.AsSpan(at, size);
                if (type == typeof(bool))
                {
                    if (wideBoolean) BinaryPrimitives.WriteUInt32LittleEndian(dest, (bool)next ? 1u : 0u);
                    else dest[0] = (bool)next ? (byte)1 : (byte)0;
                }
                else if (size == 1) dest[0] = (byte)next;
                else if (size == 2) BinaryPrimitives.WriteUInt16LittleEndian(dest, (ushort)next);
                else if (size == 8) BinaryPrimitives.WriteUInt64LittleEndian(dest, (ulong)next);
                else if (type == typeof(int)) BinaryPrimitives.WriteInt32LittleEndian(dest, (int)next);
                else BinaryPrimitives.WriteUInt32LittleEndian(dest, (uint)next);
            });
            var f = binding.Field;
            bindings.Add(new(new(f.Key, f.Type, f.Value, group, f.Min, f.Max, f.Length, choices), binding.Set));
        }
        void Text(string key, int at, int chars, string group)
        {
            bindings.Add(new(new(key, "text", RecordString(before, at, chars), group, Length: chars - 1), value =>
            {
                ValidateText(value, chars - 1);
                after.AsSpan(at, chars * 2).Clear(); Encoding.Unicode.GetBytes(value).CopyTo(after.AsSpan(at));
            }));
        }
        void Date(string key, int at, string group)
        {
            bindings.Add(new(new(key, "date", RecordDate(before, at), group), value =>
            {
                if (value == "") { after.AsSpan(at, 4).Clear(); return; }
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new ArgumentException("recordDateInvalid");
                BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan(at), (ushort)date.Year);
                after[at + 2] = (byte)date.Month; after[at + 3] = (byte)date.Day;
            }));
        }
        void Hex(string key, int at, int size)
        {
            bindings.Add(new(new(key, "hex", Convert.ToHexString(before.AsSpan(at, size)), "handwriting", Length: size * 2), value =>
            {
                if (value.Length != size * 2) throw new ArgumentException("recordHexInvalid");
                byte[] bytes; try { bytes = Convert.FromHexString(value); } catch (FormatException) { throw new ArgumentException("recordHexInvalid"); }
                bytes.CopyTo(after.AsSpan(at));
            }));
        }
        if (section == "visitors")
        {
            var choices = new[] { "", "gul", "hgc", "cml", "gstA", "hgh", "bey", "chy", "skk", "slo", "fox", "gulB" }
                .Select((id, i) => i == 0 ? "None" : GameInfo.Strings.GetVillager(id)).ToArray();
            for (var day = 0; day < 7; day++)
            {
                var group = "day" + day;
                Number("VisitorNPC." + day, RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.VisitorNPC)) + day * 4, typeof(int), group, choices);
                Number("InitSchedule." + day, RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.InitSchedule)) + day * 4, typeof(int), group, choices);
                Number("IsNormalLive." + day, RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.IsNormalLive)) + day, typeof(bool), group);
                Number("IsBirthdayLive." + day, RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.IsBirthdayLive)) + day, typeof(bool), group);
            }
            Number("DayWisp", RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.DayWisp)), typeof(int), "special");
            Number("DayCeleste", RecordOffset<GSaveVisitorNpc>(nameof(GSaveVisitorNpc.DayCeleste)), typeof(int), "special");
            foreach (var vector in new[] { nameof(GSaveVisitorNpc.CreatePos), "_61e631dc" })
                foreach (var axis in new[] { nameof(V3f.X), nameof(V3f.Y), nameof(V3f.Z) })
                {
                    var at = RecordOffset<GSaveVisitorNpc>(vector) + RecordOffset<V3f>(axis);
                    bindings.Add(new(new(vector + "." + axis, "float", BinaryPrimitives.ReadSingleLittleEndian(before.AsSpan(at)).ToString("R", CultureInfo.InvariantCulture), "advanced"), value =>
                    {
                        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
                            throw new ArgumentException("recordFloatInvalid");
                        BinaryPrimitives.WriteSingleLittleEndian(after.AsSpan(at), number);
                    }));
                }
            for (var i = 0; i < 14; i++) Number("_437619d8." + i, RecordOffset<GSaveVisitorNpc>("_437619d8") + i, typeof(bool));
            return new(offset, before, after, [], bindings);
        }
        var stock = RecordOffset<GSaveBulletinBoard>(nameof(GSaveBulletinBoard.Stock));
        var entry = stock + index * GSaveBBS.SIZE;
        Date("Date", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.Date)), "content");
        Text("Body", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.Body)), 240, "content");
        Text("Footer", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.Footer)), 48, "content");
        var identity = entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.PlayerId));
        var land = identity + RecordOffset<GSavePlayerId>(nameof(GSavePlayerId.LandId));
        var player = identity + RecordOffset<GSavePlayerId>(nameof(GSavePlayerId.BaseId));
        Text("PlayerName", player + RecordOffset<GSavePlayerBaseId>(nameof(GSavePlayerBaseId.Name)), 10, "author");
        Text("IslandName", land + RecordOffset<GSaveLandId>(nameof(GSaveLandId.Name)), 10, "author");
        Number("PlayerID", player + RecordOffset<GSavePlayerBaseId>(nameof(GSavePlayerBaseId.Id)), typeof(uint));
        Number("IslandID", land + RecordOffset<GSaveLandId>(nameof(GSaveLandId.Id)), typeof(uint));
        Number("Gender", player + RecordOffset<GSavePlayerBaseId>(nameof(GSavePlayerBaseId.Gender)), typeof(int));
        Number("IslandRubyType", land + RecordOffset<GSaveLandId>(nameof(GSaveLandId.IslandRubyType)), typeof(byte));
        Number("_5d1fcb04", entry + RecordOffset<GSaveBBS>("_5d1fcb04"), typeof(ushort));
        Number("DesignId", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.DesignId)), typeof(ushort));
        Number("PopId", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.PopId)), typeof(ulong));
        Number("NsaId", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.NsaId)), typeof(ulong));
        Number("UniqueId", entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.UniqueId)), typeof(uint));
        var handwriting = entry + RecordOffset<GSaveBBS>(nameof(GSaveBBS.HandWrite));
        Hex("HandWrite.Image", handwriting + RecordOffset<Handwriting>(nameof(Handwriting.Image)), Handwriting.InkCount);
        Hex("HandWrite.Palette", handwriting + RecordOffset<Handwriting>(nameof(Handwriting.Palette)), Handwriting.PaletteCount);
        Number("HandWrite.VerticesNum", handwriting + RecordOffset<Handwriting>(nameof(Handwriting.VerticesNum)), typeof(uint), "handwriting");
        Date("BuiltDate", RecordOffset<GSaveBulletinBoard>(nameof(GSaveBulletinBoard.BuiltDate)), "board");
        Number("_3347e149", RecordOffset<GSaveBulletinBoard>("_3347e149"), typeof(bool), "board", wideBoolean: true);
        Number("LatestUniqueId", RecordOffset<GSaveBulletinBoard>(nameof(GSaveBulletinBoard.LatestUniqueId)), typeof(uint), "board");
        for (var i = 0; i < BulletinBoardStock.MaxCount; i++)
            Number("IndexTable." + i, stock + RecordOffset<BulletinBoardStock>(nameof(BulletinBoardStock.IndexTable)) + i * 4, typeof(int), "board");
        var entries = Enumerable.Range(0, BulletinBoardStock.MaxCount).Select(i => new IslandRecordEntry(i,
            RecordString(before, stock + i * GSaveBBS.SIZE + RecordOffset<GSaveBBS>(nameof(GSaveBBS.Body)), 240),
            RecordDate(before, stock + i * GSaveBBS.SIZE))).ToArray();
        return new(offset, before, after, entries, bindings);
    }
    public IslandRecordsData GetIslandRecords(string section, int index)
    {
        var doc = IslandRecordDocumentFor(section, index);
        return new(section, index, doc.Entries, doc.Bindings.Select(b => b.Field).ToArray());
    }
    public void SetIslandRecords(string section, int index, Dictionary<string, string> values)
    {
        var doc = IslandRecordDocumentFor(section, index);
        var fields = doc.Bindings.ToDictionary(b => b.Field.Key);
        foreach (var (key, value) in values)
        {
            if (!fields.TryGetValue(key, out var binding)) throw new ArgumentException("invalidRequest");
            if (binding.Field.Value != value) binding.Set(value);
        }
        var patches = new List<IdentityPatch>();
        for (var i = 0; i < doc.Before.Length;)
        {
            if (doc.Before[i] == doc.After[i]) { i++; continue; }
            var start = i++;
            while (i < doc.Before.Length && doc.Before[i] != doc.After[i]) i++;
            patches.Add(new(SaveFile.Main, doc.Offset + start, doc.Before[start..i], doc.After[start..i]));
        }
        if (patches.Count == 0) return;
        void Apply(bool undo) { foreach (var patch in patches) (undo ? patch.Before : patch.After).CopyTo(patch.File.Data[patch.Offset..]); }
        Commit(() => Apply(true), () => Apply(false));
    }
}
