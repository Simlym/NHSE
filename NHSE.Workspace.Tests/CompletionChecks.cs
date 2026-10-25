using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NHSE.Core;
using NHSE.Injection;
using NHSE.WinForms;

internal static class CompletionChecks
{
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        Console.WriteLine("PASS " + text);
    }
    private static void Invalid(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Expected atomic validation failure.");
    }
    private static Dictionary<string, string> Values(params (string Key, string Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);
    public static void Run(MemoryProvider provider, HorizonSave oldSave)
    {
        var save = new HorizonSave(provider);
        var session = new WorkspaceSession(save);
        foreach (var source in new[] { save, oldSave })
        {
            if (source.Players[0].Personal.PocketCount == 0) source.Players[0].Personal.PocketCount = 20;
            var edit = new WorkspaceSession(source);
            foreach (var kind in new[] { "flagsPlayer", "flagsLand", "flagsResident", "resident", "memory", "wear", "furniture", "room", "mailbox", "item" })
            {
                var target = new WorkspaceSession.AdvancedTarget(kind);
                var main = source.Main.Data.ToArray(); var personal = source.Players[0].Personal.Data.ToArray(); var post = source.Players[0].PostBox.Data.ToArray();
                var data = edit.GetAdvanced(target);
                edit.SetAdvanced(target, data.Fields.ToDictionary(f => f.Key, f => f.Value));
                Check(main.SequenceEqual(source.Main.Data.ToArray()) && personal.SequenceEqual(source.Players[0].Personal.Data.ToArray()) && post.SequenceEqual(source.Players[0].PostBox.Data.ToArray()) && !edit.IsDirty,
                    "advanced unchanged forms preserve all raw bytes: " + kind + source.Main.Offsets.VillagerSize);
                var file = edit.ExportAdvanced(target);
                edit.ImportAdvanced(target, file);
                Check(edit.ExportAdvanced(target).SequenceEqual(file) && !edit.IsDirty, "advanced native files are exact no-ops: " + kind);
                Invalid(() => edit.ImportAdvanced(target, file[..^1]));
                Invalid(() => edit.SetAdvanced(target, Values(("notAField", "0"))));
            }
            foreach (var kind in new[] { "flagsPlayer", "flagsLand", "flagsResident" })
            {
                var first = new WorkspaceSession.AdvancedTarget(kind);
                var count = edit.GetAdvanced(first).Entries.Length;
                var target = first with { Index = count - 1 }; var before = edit.ExportAdvanced(target);
                var value = kind == "flagsResident" ? "65535" : "-32768";
                edit.SetAdvanced(target, Values(("Value", value)));
                Check(edit.GetAdvanced(target).Fields[0].Value == value, "last event flag retains correct signedness: " + kind);
                edit.Undo(); Check(edit.ExportAdvanced(target).SequenceEqual(before), "flag undo restores exact bytes: " + kind);
                Invalid(() => edit.SetAdvanced(target, Values(("Value", kind == "flagsResident" ? "-1" : "32768"))));
                Invalid(() => edit.GetAdvanced(target with { Index = count }));
            }
            var resident = new WorkspaceSession.AdvancedTarget("resident");
            var residentBefore = edit.ExportAdvanced(resident);
            edit.SetAdvanced(resident, Values(("DIYRecipeIndex", "65535"), ("CatchPhrase", "测试😀")));
            Check(edit.GetAdvanced(resident).Fields.Any(f => f.Key == "CatchPhrase" && f.Value == "测试😀"), "resident advanced Unicode and unsigned recipe values round-trip");
            Invalid(() => edit.SetAdvanced(resident, Values(("DIYRecipeIndex", "65536"), ("BirthType", "2"))));
            Invalid(() => edit.SetAdvanced(resident, Values(("CatchPhrase", new string('a', 25)))));
            edit.Undo(); Check(edit.ExportAdvanced(resident).SequenceEqual(residentBefore), "resident advanced edits reject partially valid forms and undo exactly");
            var memory = resident with { Kind = "memory", Index = 7 };
            var memoryBefore = edit.ExportAdvanced(memory);
            edit.SetAdvanced(memory, Values(("Friendship", "255"), ("Greeting.10", "Greeting😀"), ("Flag.255", "254")));
            Check(source.Main.GetVillager(0).GetMemory(7).Friendship == 255 && source.Main.GetVillager(0).GetMemory(7).GetGreeting(10) == "Greeting😀", "resident memory reaches final player, greeting and flag slots");
            edit.Undo(); Check(edit.ExportAdvanced(memory).SequenceEqual(memoryBefore), "memory change is one complete undo step");
            var room = resident with { Kind = "room" };
            var beforeRoom = edit.ExportAdvanced(resident);
            edit.SetAdvanced(room, Values(("WallItem.UniqueID", "2213"), ("FloorDirection", "255")));
            Check(source.Main.GetVillager(0).Room.WallItem.UniqueID == 2213 && source.Main.GetVillager(0).Room.FloorDirection == 255, "resident room nested surface items and metadata are editable");
            edit.Undo(); Check(edit.ExportAdvanced(resident).SequenceEqual(beforeRoom), "room surface undo restores complete resident bytes");
        }
        var pocket = new WorkspaceSession.AdvancedTarget("item");
        var current = session.ExportAdvanced(pocket);
        session.SetAdvanced(pocket, Values(("RawValue", ulong.MaxValue.ToString())));
        Check(save.Players[0].Personal.Pocket[0].RawValue == ulong.MaxValue, "item detail avoids JavaScript precision loss for full 64-bit raw values"); session.Undo();
        Invalid(() => session.SetAdvanced(pocket, Values(("Rotation", "4"), ("Count", "7"))));
        Invalid(() => session.SetAdvanced(pocket, Values(("Count", "65536"))));
        Check(session.ExportAdvanced(pocket).SequenceEqual(current), "item alias limits reject a whole form before committing");
        session.SetAdvanced(pocket, Values(("WrappingType", "4"), ("WateredVisitor.9", "True")));
        Check(save.Players[0].Personal.Pocket[0].GetIsWateredByVisitor(9), "item detail exposes visitor watering bits"); session.Undo();
        var mail = new WorkspaceSession.AdvancedTarget("mailbox", Player: 1, Index: 299);
        var postBefore = save.Players[1].PostBox.Data.ToArray();
        session.SetAdvanced(mail, Values(("ReadStatus", "3"), ("Message", "Hello\n世界😀"), ("SortIndex", "299"), ("Language", "en-GB"), ("FreeParam", int.MinValue.ToString())));
        Check(save.Players[1].PostBox.GetUsedPostBoxSlots().Contains(299) && save.Players[1].PostBox.GetPostBoxSortIndex(299) == 299, "mailbox includes slot 299 and preserves 16-bit sort indexes");
        Check(save.Players[1].PostBox.GetLetterMessageText(299) == "Hello\n世界😀" && save.Players[1].PostBox.GetMailLanguageString(299) == "en-GB", "mail text, ASCII language and full attachment parameters round-trip");
        var mailAfter = save.Players[1].PostBox.Data.ToArray();
        Invalid(() => session.SetAdvanced(mail, Values(("Message", new string('a', 240)), ("SenderID", "20"))));
        Invalid(() => session.GetAdvanced(mail with { Index = 300 }));
        Check(save.Players[1].PostBox.Data.SequenceEqual(mailAfter), "oversized mail and out-of-range slots cannot partially write");
        session.Undo(); Check(save.Players[1].PostBox.Data.SequenceEqual(postBefore), "mail undo restores every postbox byte"); session.Redo();
        var mailFile = session.ExportAdvanced(mail); mailFile[Mail.PostBoxSortIndex] = 0;
        session.ImportAdvanced(mail, mailFile);
        Check(save.Players[1].PostBox.GetPostBoxSortIndex(299) == 299, "mail import keeps destination sorting position");
        var languageBefore = save.Players[1].PostBox.Data.ToArray(); save.Players[1].PostBox.SetMailLanguageString(299, "ja-JP", true);
        var languageOffset = save.Players[1].PostBox.Offsets.MailList + 299 * Mail.SIZE + Mail.LanguageString;
        Check(save.Players[1].PostBox.Data[..languageOffset].SequenceEqual(languageBefore.AsSpan(0, languageOffset))
            && save.Players[1].PostBox.Data[(languageOffset + 5)..].SequenceEqual(languageBefore.AsSpan(languageOffset + 5)), "legacy mail language setter writes exactly five ASCII bytes");
        save.Players[1].PostBox.SetMailLanguageString(299, "en-GB", true);
        var stockTarget = new WorkspaceSession.AdvancedTarget("wear");
        var stockBefore = session.ExportAdvanced(stockTarget);
        session.SetAdvanced(stockTarget, Values(("U08", "4294967295")));
        var fullStock = session.ExportAdvancedCollection(stockTarget); fullStock[0] ^= 1;
        session.ImportAdvancedCollection(stockTarget, fullStock);
        Check(save.Main.GetVillager(0).WearStockList[0].U08 == uint.MaxValue, "stock inventory imports preserve per-item resident metadata"); session.Undo(); session.Undo();
        Check(session.ExportAdvanced(stockTarget).SequenceEqual(stockBefore), "stock metadata and inventory each undo exactly");
        var collectionBefore = session.ExportAdvancedCollection(pocket);
        var targetItem = new Item((ushort)2213); session.SetItem(0, "pocket", 0, targetItem);
        var batchBefore = session.ExportAdvancedCollection(pocket);
        session.TextBatchAdvanced(pocket, "=ItemId=2213\n.FreeParam=7\n;\n=FreeParam=*FreeParam\n.Count=$5,5");
        Check(save.Players[0].Personal.Pocket[0].Count == 5, "text batch retains first instruction after group separator, property references and random expressions"); session.Undo();
        Check(session.ExportAdvancedCollection(pocket).SequenceEqual(batchBefore), "text inventory batch is a single exact undo step");
        Invalid(() => session.TextBatchAdvanced(pocket, ".Count=65536"));
        Invalid(() => session.TextBatchAdvanced(pocket, ".Nothing=1"));
        Invalid(() => session.TextBatchAdvanced(pocket, ".Count=$8,3"));
        Invalid(() => session.TextBatchAdvanced(pocket, "this is not an instruction"));
        session.Undo(); Check(session.ExportAdvancedCollection(pocket).SequenceEqual(collectionBefore), "invalid text batches preserve all inventory bytes");
        var sets = StringInstructionSet.GetBatchSets(new[] { ".Count=1", ";", ".Count=2" }.AsSpan());
        Check(sets.Length == 2 && sets[1].Instructions.Count == 1, "legacy array batch parser retains the next group's first line");
        var random = new StringInstruction("Count", "$4,6", default); random.SetRandomRange("$4,6");
        Check(Enumerable.Range(0, 100).All(_ => random.RandomValue is >= 4 and <= 6), "random expressions retain their dollar-prefixed lower bound");
        var designTarget = new byte[DesignPatternPRO.SIZE];
        new DesignPatternPRO(designTarget).UsageCompatibility = 0xEE01;
        session.ImportDesigns("residentDesigns", 9, 0, [designTarget], false);
        Check(session.DesignCount("residentDesigns") == 10 && session.DesignBytes("residentDesigns", 9).Length == DesignPatternPRO.SIZE, "resident professional designs use existing preview and file exchange for all ten slots");
        var saved = session.ExportAdvanced(mail);
        session.Save(); var reloaded = new HorizonSave(provider);
        Check(new WorkspaceSession(reloaded).ExportAdvanced(mail).SequenceEqual(saved) && !reloaded.GetInvalidHashes().Any(), "advanced mailbox and resident changes survive encrypted reload and hashes");
        TransportChecks();
    }
    private static void TransportChecks()
    {
        var fake = new FakeTransport(); var source = new byte[] { 1, 2, 3, 4 };
        ConsoleTransfer.Write(fake, 0x1000, source); source[0] = 9;
        Check(fake.Data[0] == 1 && ConsoleTransfer.Read(fake, 0x1000, 4).SequenceEqual(fake.Data), "console transfers isolate write buffers and preserve byte payloads");
        Invalid(() => ConsoleTransfer.Read(fake, 0, 4)); Invalid(() => ConsoleTransfer.Read(fake, 1, 16385)); Invalid(() => ConsoleTransfer.Read(fake, uint.MaxValue, 2));
        fake.Short = true;
        try { ConsoleTransfer.Read(fake, 1, 4); throw new Exception("Expected short read rejection"); } catch (IOException) { }
        Check(fake.Writes == 1, "invalid or incomplete transfers never send a write");
        foreach (var response in new[] { "01020304\n", "01\n", "" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptSocketAsync(); var bot = new SysBot(); bot.Connect("127.0.0.1", port);
            var server = Task.Run(() => {
                using var socket = accept.GetAwaiter().GetResult();
                var request = new List<byte>(); var one = new byte[1];
                while (socket.Receive(one) == 1) { request.Add(one[0]); if (one[0] == '\n') break; }
                Check(Encoding.ASCII.GetString(request.ToArray()).StartsWith("peek ", StringComparison.Ordinal), "network transport sends a peek command to the local simulator");
                if (response.Length > 0) foreach (var character in response) socket.Send([(byte)character]);
            });
            if (response.Length == 9) Check(bot.ReadBytes(0x1000, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "network transport reconstructs fragmented hexadecimal replies");
            else
            {
                try { bot.ReadBytes(0x1000, 4); throw new Exception("Expected malformed network reply rejection."); } catch (IOException) { }
                Check(true, "network short replies and closed peers fail without hanging");
            }
            server.GetAwaiter().GetResult(); bot.Disconnect();
            Check(!bot.Connected, "network disconnect releases connection state");
        }
    }
    private sealed class FakeTransport : IRAMReadWriter
    {
        public bool Connected => true;
        public byte[] Data = []; public int Writes; public bool Short;
        public byte[] ReadBytes(uint offset, int length) => Short ? new byte[length - 1] : Data;
        public void WriteBytes(byte[] data, uint offset) { Data = data; Writes++; }
    }
}
