using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NHSE.Core;
using NHSE.WinForms;

internal static class IslandRecordsChecks
{
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        Console.WriteLine("PASS " + text);
    }
    private static void Invalid(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Expected island-record validation failure.");
    }
    private static Dictionary<string, string> Values(params (string Key, string Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);
    private static string Field(WorkspaceSession.IslandRecordsData data, string key) => data.Fields.Single(f => f.Key == key).Value;
    private static int At<T>(string key) => WorkspaceSession.RecordOffset<T>(key);
    public static void Run(MemoryProvider provider, MemoryProvider originalProvider)
    {
        Check(Marshal.SizeOf<GSaveBBS>() == GSaveBBS.SIZE && Marshal.SizeOf<GSaveBulletinBoard>() == GSaveBulletinBoard.SIZE
            && Marshal.SizeOf<GSaveVisitorNpc>() == GSaveVisitorNpc.SIZE, "island record adapter follows the existing marshaled sizes");
        foreach (var sourceProvider in new[] { provider, originalProvider })
        {
            var source = new HorizonSave(sourceProvider);
            var board = source.Main.Data.Slice(source.Main.Offsets.BulletinBoard, GSaveBulletinBoard.SIZE);
            for (var i = 0; i < board.Length; i++) board[i] = (byte)(i * 17 + 23);
            // Keep a deliberately odd date and trailing string bytes. No-op edits must not normalize them.
            var stock = At<GSaveBulletinBoard>("Stock");
            BinaryPrimitives.WriteUInt16LittleEndian(board[stock..], ushort.MaxValue); board[stock + 2] = 255; board[stock + 3] = 255;
            board.Slice(stock + At<GSaveBBS>("Body"), 480).Clear(); Encoding.Unicode.GetBytes("原始公告😀").CopyTo(board[(stock + At<GSaveBBS>("Body"))..]);
            board[stock + At<GSaveBBS>("Body") + 100] = 0x77;
            var visitor = source.Main.Data.Slice(source.Main.Offsets.Visitor, GSaveVisitorNpc.SIZE);
            for (var i = 0; i < visitor.Length; i++) visitor[i] = (byte)(i * 11 + 3);
            BinaryPrimitives.WriteInt32LittleEndian(visitor, 999);
            visitor[At<GSaveVisitorNpc>("IsNormalLive")] = 2;
            var pos = At<GSaveVisitorNpc>("CreatePos");
            BinaryPrimitives.WriteUInt32LittleEndian(visitor[pos..], 0x7FC00001); // NaN payload
            BinaryPrimitives.WriteUInt32LittleEndian(visitor[(pos + 4)..], 0x80000000); // -0
            var session = new WorkspaceSession(source);
            var before = source.Main.Data.ToArray();
            var personal = source.Players.Select(p => p.Personal.Data.ToArray()).ToArray();
            var bulletin = session.GetIslandRecords("bulletin", 0);
            var visits = session.GetIslandRecords("visitors", 0);
            Check(bulletin.Entries.Length == 30 && bulletin.Entries[^1].Index == 29 && bulletin.Fields.Any(f => f.Key == "IndexTable.29")
                && Field(bulletin, "Body") == "原始公告😀" && Field(bulletin, "Date") == "65535-255-255", "bulletin query covers thirty slots, raw dates and the complete index table");
            Check(visits.Fields.Count(f => f.Group.StartsWith("day", StringComparison.Ordinal)) == 28
                && Field(visits, "VisitorNPC.0") == "999" && Field(visits, "CreatePos.X") == "NaN" && Field(visits, "IsNormalLive.0") == "True",
                "visitor query includes seven schedules, unknown IDs and original nonfinite coordinates");
            var options = visits.Fields.Single(f => f.Key == "VisitorNPC.0").Choices!;
            Check(options.Length == 12 && options[1] == GameInfo.Strings.GetVillager("gul") && options[6] == GameInfo.Strings.GetVillager("bey")
                && options[11] == GameInfo.Strings.GetVillager("gulB"), "visitor choices reuse localized game names without changing numeric IDs");
            session.SetIslandRecords("bulletin", 0, bulletin.Fields.ToDictionary(f => f.Key, f => f.Value));
            session.SetIslandRecords("visitors", 0, visits.Fields.ToDictionary(f => f.Key, f => f.Value));
            Check(source.Main.Data.SequenceEqual(before) && !session.IsDirty, "untouched island records preserve every byte, padding, NaN payload and raw boolean without history");
            Invalid(() => session.GetIslandRecords("unknown", 0)); Invalid(() => session.GetIslandRecords("bulletin", 30));
            Invalid(() => session.GetIslandRecords("visitors", 1)); Invalid(() => session.GetIslandRecords("bulletin", -1));
            session.SetIslandRecords("bulletin", 29, Values(("Body", "Hello\n您好😀"), ("Footer", "公告落款"), ("Date", "2024-02-29"),
                ("PlayerName", "作者😀"), ("IslandName", "岛屿"), ("Gender", "-2147483648"), ("PopId", "18446744073709551615"), ("NsaId", "9223372036854775808")));
            var edited = session.GetIslandRecords("bulletin", 29);
            Check(Field(edited, "Body") == "Hello\n您好😀" && Field(edited, "PopId") == "18446744073709551615"
                && Field(edited, "NsaId") == "9223372036854775808" && Field(edited, "Gender") == "-2147483648",
                "final bulletin slot supports Unicode, full signed enum values and exact unsigned 64-bit IDs");
            var nativeEntry = source.Main.Bulletin.Stock.Buffer[29];
            Check(nativeEntry.Body == "Hello\n您好😀" && nativeEntry.PlayerId.BaseId.Name == "作者😀" && (int)nativeEntry.PlayerId.BaseId.Gender == int.MinValue,
                "bulletin field edits agree with the existing native model");
            var untouched = before.AsSpan(source.Main.Offsets.BulletinBoard + stock + 29 * GSaveBBS.SIZE + At<GSaveBBS>("HandWrite"), Handwriting.SIZE);
            Check(source.Main.Data.Slice(source.Main.Offsets.BulletinBoard + stock + 29 * GSaveBBS.SIZE + At<GSaveBBS>("HandWrite"), Handwriting.SIZE).SequenceEqual(untouched),
                "editing bulletin text, author and IDs leaves every handwriting byte unchanged");
            var after = source.Main.Data.ToArray();
            session.Undo(); Check(source.Main.Data.SequenceEqual(before), "all bulletin fields undo in one byte-exact step");
            session.Redo(); Check(source.Main.Data.SequenceEqual(after), "bulletin redo restores the entire edit"); session.Undo();
            var image = Field(bulletin, "HandWrite.Image");
            session.SetIslandRecords("bulletin", 0, Values(("HandWrite.Image", "A5" + image[2..]), ("HandWrite.Palette", "01020304"), ("HandWrite.VerticesNum", "4294967295"), ("IndexTable.29", "-2147483648"), ("_3347e149", "False")));
            Check(Field(session.GetIslandRecords("bulletin", 0), "HandWrite.Image").StartsWith("A5", StringComparison.Ordinal)
                && source.Main.Bulletin.Stock.IndexTable[29] == int.MinValue && !source.Main.Bulletin._3347e149, "handwriting bytes, palette, vertex count and final board index remain editable");
            session.Undo(); Check(source.Main.Data.SequenceEqual(before), "handwriting and board metadata undo together without losing padding");
            foreach (var change in new[] { Values(("Body", "valid"), ("Date", "2023-02-29")), Values(("Body", "valid"), ("PopId", "18446744073709551616")),
                Values(("Body", new string('a', 240))), Values(("Body", "\ud800")), Values(("Footer", "bad\0text")), Values(("HandWrite.Palette", "GG000000")),
                Values(("HandWrite.Image", "00")), Values(("Body", "valid"), ("notAField", "1")), Values(("Gender", "2147483648")) })
                Invalid(() => session.SetIslandRecords("bulletin", 0, change));
            Check(source.Main.Data.SequenceEqual(before) && !session.IsDirty, "invalid bulletin dates, UTF-16, lengths, hex, IDs and unknown fields reject atomically");
            session.SetIslandRecords("bulletin", 0, Values(("Body", "修改正文")));
            Check(Field(session.GetIslandRecords("bulletin", 0), "Date") == "65535-255-255", "changing only text retains an unusual stored bulletin date"); session.Undo();
            session.SetIslandRecords("bulletin", 0, Values(("Date", "0001-01-01"), ("BuiltDate", "")));
            Check(Field(session.GetIslandRecords("bulletin", 0), "Date") == "0001-01-01" && Field(session.GetIslandRecords("bulletin", 0), "BuiltDate") == "",
                "bulletin dates support the minimum calendar date and explicit clearing"); session.Undo();
            session.SetIslandRecords("visitors", 0, Values(("VisitorNPC.6", "11"), ("InitSchedule.6", "-2147483648"), ("IsNormalLive.6", "False"),
                ("IsBirthdayLive.6", "True"), ("_437619d8.13", "False"), ("CreatePos.Z", "123.25"), ("DayWisp", "-1"), ("DayCeleste", "2147483647")));
            Check(source.Main.Visitor.VisitorNPC[6] == VisitorNPC.Gullivarrr && (int)source.Main.Visitor.InitSchedule[6] == int.MinValue
                && source.Main.Visitor.IsBirthdayLive[6] && source.Main.Visitor.CreatePos.Z == 123.25f && source.Main.Visitor.DayWisp == -1,
                "visitor final slots, raw schedules, flags, finite coordinates and special days use the native layout");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(source.Main.Data[(source.Main.Offsets.Visitor + pos)..]) == 0x7FC00001
                && source.Main.Data[source.Main.Offsets.Visitor + At<GSaveVisitorNpc>("IsNormalLive")] == 2,
                "editing other visitor fields preserves untouched NaN payloads and nonstandard boolean bytes");
            var visitAfter = source.Main.Data.ToArray();
            session.Undo(); Check(source.Main.Data.SequenceEqual(before), "visitor schedule changes undo as one byte-exact operation");
            session.Redo(); Check(source.Main.Data.SequenceEqual(visitAfter), "visitor redo retains coordinates, flags and padding"); session.Undo();
            foreach (var value in new[] { "NaN", "Infinity", "1e100", "not a number" }) Invalid(() => session.SetIslandRecords("visitors", 0, Values(("DayWisp", "1"), ("CreatePos.Z", value))));
            Invalid(() => session.SetIslandRecords("visitors", 0, Values(("IsNormalLive.0", "yes"))));
            Invalid(() => session.SetIslandRecords("visitors", 0, Values(("VisitorNPC.6", "2147483648"))));
            Check(source.Main.Data.SequenceEqual(before) && !session.IsDirty, "invalid visitor numbers, flags and nonfinite replacements never partially apply");
            // History owns its bytes, and separate records do not overwrite each other on undo.
            var caller = Values(("Body", "保存公告")); session.SetIslandRecords("bulletin", 29, caller); caller["Body"] = "mutated";
            session.SetIslandRecords("visitors", 0, Values(("VisitorNPC.6", "10"))); session.Undo();
            Check(Field(session.GetIslandRecords("bulletin", 29), "Body") == "保存公告", "visitor undo preserves an independent bulletin edit and ignores caller dictionary mutations");
            session.Redo();
            Check(source.Players.Select((p, i) => p.Personal.Data.SequenceEqual(personal[i])).All(v => v), "island record edits never modify any personal save");
            session.Save();
            // The save file uses its own provider (new and original formats are tested independently).
            var reloaded = new HorizonSave(sourceProvider);
            Check(reloaded.Main.Bulletin.Stock.Buffer[29].Body == "保存公告" && reloaded.Main.Visitor.VisitorNPC[6] == VisitorNPC.Redd && !reloaded.GetInvalidHashes().Any(),
                "bulletin and visitor records survive encrypted save/reload with valid hashes");
        }
    }
}
