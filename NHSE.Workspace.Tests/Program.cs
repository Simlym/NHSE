using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NHSE.Core;
using NHSE.WinForms;

// All files below are synthetic and remain in memory. No user save is touched.
var provider = new MemoryProvider();
var revision = new FileHeaderInfo { Major = 0x80009, Minor = 0x80085, Unk1 = 2, Unk2 = 2, SaveRevision = 30 };
var sizes = RevisionChecker.SizeInfo[30];
provider.Add("main", sizes.Main, revision);
provider.Add("Villager0/personal", sizes.Personal, revision);
provider.Add("Villager0/photo_studio_island", sizes.PhotoStudioIsland, revision);
provider.Add("Villager0/postbox", sizes.PostBox, revision);
provider.Add("Villager0/profile", sizes.Profile, revision);
var save = new HorizonSave(provider);
var personal = save.Players[0].Personal;
personal.PlayerName = "Test";
personal.TownName = "TestTown";
personal.Wallet = new EncryptedInt32(0) { Value = 100 };
personal.Bank = new EncryptedInt32(0) { Value = 200 };
personal.NookMiles = new EncryptedInt32(0) { Value = 300 };
personal.TotalNookMiles = new EncryptedInt32(0) { Value = 400 };
personal.PocketCount = 20;
personal.BagCount = 10;
personal.ItemChestCount = 100;
personal.Pocket = Enumerable.Range(0, 20).Select(_ => new Item(Item.NONE)).ToArray();
personal.Bag = Enumerable.Range(0, 20).Select(_ => new Item(Item.NONE)).ToArray();
personal.ItemChest = Enumerable.Range(0, personal.Offsets.ItemChestCount).Select(_ => new Item(Item.NONE)).ToArray();
var session = new WorkspaceSession(save);
var stackId = GameInfo.Strings.ItemDataSource.First(x => x.Value is > 0 and < 60000 && ItemInfo.TryGetMaxStackCount((ushort)x.Value, out var max) && max > 1).Value;
var item = new Item((ushort)stackId) { Count = 4, SystemParam = 128, AdditionalParam = 9 };

Check(!session.IsDirty, "new session is clean");
session.SetItem(0, "pocket", 20, item);
Check(personal.Bag[0].RawValue == item.RawValue && personal.Pocket[0].IsNone, "pocket/bag split preserves order and all raw flags");
Check(session.IsDirty && WorkspaceSession.Quantity(item) == 5, "display quantity is one-based");
session.Undo(); Check(personal.Bag[0].IsNone && !session.IsDirty, "undo restores original bytes and clean state");
session.Redo(); Check(personal.Bag[0].RawValue == item.RawValue, "redo restores complete item");
ExpectInvalid(() => session.SetItem(0, "pocket", 30, item));
ExpectInvalid(() => session.SetItem(0, "storage", 100, item));
ExpectInvalid(() => session.GetItems(99, "pocket"));
ExpectInvalid(() => session.GetItems(0, "unknown"));
Check(personal.Bag[0].RawValue == item.RawValue, "rejected edits leave items untouched");
session.SetPlayer(0, 1234, 5678, 42, 1000);
Check((uint)personal.Wallet == 1234 && (uint)personal.TotalNookMiles == 1000, "balances use existing encrypted integer writers");
session.Undo(); Check((uint)personal.Wallet == 100, "player edits can be undone");
session.SetItem(0, "storage", 99, item);
session.Redo(); Check((uint)personal.Wallet == 100, "new edits discard obsolete redo branch");
session.Save();
Check(!session.IsDirty && provider.FlushCount == 1, "explicit save flushes provider and resets dirty state");
var reloaded = new HorizonSave(provider);
Check(reloaded.Players[0].Personal.Bag[0].RawValue == item.RawValue, "saved item survives encryption and reload");
Check(reloaded.Players[0].Personal.ItemChest[99].RawValue == item.RawValue, "storage edit survives reload");
Check(!reloaded.GetInvalidHashes().Any(), "saved synthetic files have valid hashes");
session.Undo(); Check(session.IsDirty, "undo after saving is marked unsaved");
session.Redo(); Check(!session.IsDirty, "redo back to saved revision is clean");
session.InvalidateHistory(); session.Undo();
Check(session.IsDirty, "legacy boundary clears undo history and conservatively marks changes");
Console.WriteLine("All workspace integration checks passed.");
if (args.Length == 2 && args[0] == "--fixture") provider.Export(args[1]);

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}
static void ExpectInvalid(Action action)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new Exception("Expected validation failure.");
}

sealed class MemoryProvider : ISaveFileProvider
{
    private readonly Dictionary<string, byte[]> Files;
    private readonly string Prefix;
    public int FlushCount { get; private set; }
    public MemoryProvider() : this([], "") { }
    private MemoryProvider(Dictionary<string, byte[]> files, string prefix) { Files = files; Prefix = prefix; }
    public void Add(string name, uint size, FileHeaderInfo revision)
    {
        var header = new byte[0x100];
        revision.ToBytesClass().CopyTo(header, 0);
        var encrypted = Encryption.Encrypt(new byte[size], 123, header);
        WriteFile(name + ".dat", encrypted.Data.Span);
        WriteFile(name + "Header.dat", encrypted.Header.Span);
    }
    public byte[] ReadFile(string relativePath) => Files[Prefix + relativePath.Replace('\\', '/')].ToArray();
    public void WriteFile(string relativePath, ReadOnlySpan<byte> data) => Files[Prefix + relativePath.Replace('\\', '/')] = data.ToArray();
    public bool FileExists(string relativePath) => Files.ContainsKey(Prefix + relativePath);
    public string[] GetDirectories(string searchPattern) => ["Villager0"];
    public ISaveFileProvider GetSubdirectoryProvider(string subdirectory) => new MemoryProvider(Files, Prefix + subdirectory + "/");
    public void Flush() => FlushCount++;
    public void Export(string directory)
    {
        foreach (var (name, data) in Files)
        {
            var path = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
    }
}
