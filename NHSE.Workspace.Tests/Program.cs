using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NHSE.Core;
using NHSE.WinForms;
using NHSE.Villagers;

// All files below are synthetic and remain in memory. No user save is touched.
var provider = new MemoryProvider();
var revision = new FileHeaderInfo { Major = 0x80009, Minor = 0x80085, Unk1 = 2, Unk2 = 2, SaveRevision = 30 };
var sizes = RevisionChecker.SizeInfo[30];
provider.Add("main", sizes.Main, revision);
provider.Add("Villager0/personal", sizes.Personal, revision);
provider.Add("Villager0/photo_studio_island", sizes.PhotoStudioIsland, revision);
provider.Add("Villager0/postbox", sizes.PostBox, revision);
provider.Add("Villager0/profile", sizes.Profile, revision);
provider.Add("Villager1/personal", sizes.Personal, revision);
provider.Add("Villager1/photo_studio_island", sizes.PhotoStudioIsland, revision);
provider.Add("Villager1/postbox", sizes.PostBox, revision);
provider.Add("Villager1/profile", sizes.Profile, revision);
var save = new HorizonSave(provider);
var personal = save.Players[0].Personal;
personal.PlayerName = "Test";
personal.TownName = "TestTown";
personal.PlayerID = 1001;
personal.TownID = 2001;
var other = save.Players[1].Personal;
other.PlayerName = "Other";
other.PlayerID = 1002;
other.TownID = personal.TownID;
other.TownName = personal.TownName;
other.Wallet = new EncryptedInt32(0) { Value = 10 };
other.Bank = new EncryptedInt32(0) { Value = 20 };
other.NookMiles = new EncryptedInt32(0) { Value = 30 };
other.TotalNookMiles = new EncryptedInt32(0) { Value = 40 };
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
var oldPlayerIdentity = personal.GetPlayerIdentity().ToArray();
var oldIslandIdentity = personal.GetTownIdentity().ToArray();
oldPlayerIdentity.CopyTo(save.Main.Data[0x1000..]);
oldIslandIdentity.CopyTo(save.Main.Data[0x1040..]);
var preexistingTarget = oldPlayerIdentity.ToArray();
StringUtil.GetBytes("Renamed", 10).CopyTo(preexistingTarget, 4);
preexistingTarget.CopyTo(save.Main.Data[0x1080..]);
var mainBeforeRename = save.Main.Data.ToArray();
session.SetPlayer(0, 100, 200, 300, 400, "Renamed", "NewIsland", 40, 200);
Check(personal.PlayerName == "Renamed" && other.PlayerName == "Other", "rename changes only the selected player's name");
Check(personal.TownName == "NewIsland" && other.TownName == "NewIsland", "island rename updates every resident sharing that island identity");
Check(save.Main.Data.Slice(0x1000, 24).SequenceEqual(personal.GetPlayerIdentity()) && save.Main.Data.Slice(0x1040, 24).SequenceEqual(personal.GetTownIdentity()), "rename synchronizes related identities in main data");
Check(personal.PocketCount == 20 && personal.BagCount == 20 && personal.ItemChestCount == 200, "new UI capacity edits use the game's pocket and bag layout");
session.Undo();
Check(save.Main.Data.SequenceEqual(mainBeforeRename) && personal.PlayerName == "Test" && other.TownName == "TestTown", "rename undo restores exact references without renaming a pre-existing target identity");
Check(personal.BagCount == 10 && personal.ItemChestCount == 100, "capacity changes are undone with the profile");
ExpectInvalid(() => session.SetPlayer(0, 100, 200, 300, 400, "12345678901"));
ExpectInvalid(() => session.SetPlayer(0, 100, 200, 300, 400, "Bad\0Name"));
ExpectInvalid(() => session.SetPlayer(0, 100, 200, 300, 400, "\uD800"));
ExpectInvalid(() => session.SetPlayer(0, 100, 200, 300, 400, pocketCapacity: 41));
Check(save.Main.Data.SequenceEqual(mainBeforeRename), "invalid names and capacities cannot partially update the save");
session.Redo();
session.Save();
Check(!session.IsDirty && provider.FlushCount == 1, "explicit save flushes provider and resets dirty state");
var reloaded = new HorizonSave(provider);
Check(reloaded.Players[0].Personal.Bag[0].RawValue == item.RawValue, "saved item survives encryption and reload");
Check(reloaded.Players[0].Personal.ItemChest[99].RawValue == item.RawValue, "storage edit survives reload");
Check(!reloaded.GetInvalidHashes().Any(), "saved synthetic files have valid hashes");
Check(reloaded.Players[0].Personal.PlayerName == "Renamed" && reloaded.Players.All(x => x.Personal.TownName == "NewIsland"), "player and island names survive encrypted save/reload");
session.Undo(); Check(session.IsDirty, "undo after saving is marked unsaved");
session.Redo(); Check(!session.IsDirty, "redo back to saved revision is clean");
session.InvalidateHistory(); session.Undo();
Check(session.IsDirty, "legacy boundary clears undo history and conservatively marks changes");
var expectedIds = GameInfo.Strings.ItemDataSource.Where(x => x.Value is >= 0 and <= ushort.MaxValue && x.Value is not (Item.NONE or Item.EXTENSION)).Select(x => x.Value).ToHashSet();
var firstPage = WorkspaceCatalog.Search("", "all", 0);
Check(firstPage.Items.All(x => !x.Name.StartsWith("(Item #", StringComparison.Ordinal)), "named items appear before unnamed internal placeholders");
var actualIds = Enumerable.Range(0, firstPage.Pages).SelectMany(page => WorkspaceCatalog.Search("", "all", page).Items).Select(x => x.ItemId).ToArray();
Check(firstPage.Total > 96 && actualIds.Length == expectedIds.Count && actualIds.ToHashSet().SetEquals(expectedIds), "paged catalog exposes every original editor item exactly once");
Check(firstPage.Categories.Where(x => x.Id != "all").Sum(x => x.Count) == expectedIds.Count, "all catalog entries belong to a game-derived category");
foreach (var category in firstPage.Categories.Where(x => x.Id != "all" && x.Count > 0))
{
    var result = WorkspaceCatalog.Search("", category.Id, 0);
    Check(result.Total == category.Count && result.Items.All(x => x.Category == category.Id), "category filter: " + category.Id);
}
var lastEntry = WorkspaceCatalog.Search("", "all", int.MaxValue).Items.Last();
Check(WorkspaceCatalog.Search(lastEntry.Name, "all", 0).Items.Any(x => x.ItemId == lastEntry.ItemId), "catalog can search an item beyond the original first 60 results");
Check(WorkspaceCatalog.Search(lastEntry.ItemId.ToString(), "all", 0).Items.Any(x => x.ItemId == lastEntry.ItemId), "catalog can search item IDs");
ExpectInvalid(() => WorkspaceCatalog.Search("", "invalid", 0));
var clothing = WorkspaceCatalog.Search("", "clothing", 0);
Check(clothing.Subcategories.Where(x => x.Id != "all").Sum(x => x.Count) == clothing.Total, "clothing subcategories cover every clothing item without overlap");
foreach (var group in clothing.Subcategories.Where(x => x.Id != "all" && x.Count > 0))
{
    var subset = WorkspaceCatalog.Search("", "clothing", 0, group.Id);
    Check(subset.Total == group.Count && subset.Items.All(x => x.Subcategory == group.Id), "clothing subcategory: " + group.Id);
}
ExpectInvalid(() => WorkspaceCatalog.Search("", "furniture", 0, "tops"));
var villagerCatalog = WorkspaceVillagers.Search("", -1, -1, 0);
var allVillagers = Enumerable.Range(0, villagerCatalog.Pages).SelectMany(page => WorkspaceVillagers.Search("", -1, -1, page).Items).ToArray();
Check(allVillagers.Length > 350 && allVillagers.Select(x => x.Id).Distinct().Count() == villagerCatalog.Total, "villager portraits paginate every bundled replacement template exactly once");
foreach (var group in villagerCatalog.Species.Where(x => x.Total > 0))
{
    var subset = WorkspaceVillagers.Search("", group.Id, -1, 0);
    Check(subset.Total == group.Total && subset.Items.All(x => x.Species == group.Id), "villager species filter: " + group.Id);
}
var personalitySubset = WorkspaceVillagers.Search("", -1, 0, 0);
Check(personalitySubset.Items.All(x => x.Personality == 0), "villager personality filter uses the template's actual personality");
var targetVillager = allVillagers.First(x => x.Species == 4);
Check(WorkspaceVillagers.Search(targetVillager.Id, -1, -1, 0).Items.Single().Id == targetVillager.Id, "villager ID search");
var originalTemplate = VillagerResources.GetVillager(allVillagers[0].Id);
var originalVillager = new Villager2(originalTemplate.Villager);
var originalHouse = new VillagerHouse2(originalTemplate.House) { NPC1 = 3, NPC2 = -1, BuildPlayer = 2 };
var villagerBefore = originalVillager.Write(); var houseBefore = originalHouse.Write();
foreach (var size in new[] { VillagerHouse1.SIZE, VillagerHouse2.SIZE })
{
    var replacement = WorkspaceVillagers.CreateReplacement(originalVillager, originalHouse, targetVillager.Id, size);
    Check(replacement.Villager.InternalName == targetVillager.Id && replacement.House.Write().Length == size, "replacement converts house bytes for save format " + size);
    Check(replacement.House.NPC1 == 3 && replacement.House.NPC2 == -1 && replacement.House.BuildPlayer == 2, "replacement preserves house-to-resident assignments");
    Check(originalVillager.Write().SequenceEqual(villagerBefore) && originalHouse.Write().SequenceEqual(houseBefore), "preparing a replacement never mutates the current resident or house");
}
ExpectInvalid(() => WorkspaceVillagers.Search("", 50, -1, 0));
ExpectInvalid(() => WorkspaceVillagers.CreateReplacement(originalVillager, originalHouse, "bad00", VillagerHouse2.SIZE));
GameInfo.SetLanguage2Char(GameLanguage.GetLanguageIndex("zhs"));
Check(WorkspaceCatalog.Search("", "all", 0).Total == expectedIds.Count, "catalog retains complete coverage after language switching");
Check(WorkspaceVillagers.Search(targetVillager.Id, -1, -1, 0).Items.Single().Name == GameInfo.Strings.GetVillager(targetVillager.Id), "villager names follow the selected UI language");
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
    public string[] GetDirectories(string searchPattern) => Files.Keys.Where(x => x.StartsWith(Prefix, StringComparison.Ordinal))
        .Select(x => x[Prefix.Length..].Split('/')[0]).Where(x => x.StartsWith("Villager", StringComparison.Ordinal)).Distinct().Order().ToArray();
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
