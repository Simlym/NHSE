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
for (var i = 0; i < MainSaveOffsets.VillagerCount; i++)
{
    var blank = save.Main.GetVillager(i); blank.Species = (byte)VillagerSpecies.non; save.Main.SetVillager(blank, i);
    var blankHouse = save.Main.GetVillagerHouse(i); blankHouse.NPC1 = -1; save.Main.SetVillagerHouse(blankHouse, i);
}
foreach (var (id, index) in new[] { ("ant00", 0), ("cat00", 1) })
{
    var template = VillagerResources.GetVillager(id);
    save.Main.SetVillager(new Villager2(template.Villager), index);
    var home = new VillagerHouse2(template.House) { NPC1 = (sbyte)index, NPC2 = -1, BuildPlayer = 0 };
    save.Main.SetVillagerHouse(home, index);
}
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
var beforeWorld = save.Main.Data.ToArray();
var changedSettings = new WorkspaceSession.IslandSettings(3, 1, 1, uint.MaxValue, 12345);
session.SetIsland(changedSettings);
Check(session.GetIslandSettings() == changedSettings, "island settings preserve unsigned weather seeds and all five fields");
session.Undo();
Check(save.Main.Data.SequenceEqual(beforeWorld), "island undo restores all main bytes");
session.Redo(); Check(session.GetIslandSettings() == changedSettings, "island redo reapplies settings");
session.Undo();
ExpectInvalid(() => session.SetIsland(changedSettings with { AirportColor = 4 }));
ExpectInvalid(() => session.SetIsland(changedSettings with { Hemisphere = -1 }));
ExpectInvalid(() => session.SetIsland(changedSettings with { TourHemisphere = 2 }));
var sourceResident = save.Main.GetVillager(0).Write();
session.SetResident(0, 3, "hello", true);
Check(save.Main.GetVillager(0) is { Personality: VillagerPersonality.Smug, CatchPhrase: "hello", MovingOut: true }, "resident fields apply through the C# model");
session.Undo(); Check(save.Main.GetVillager(0).Write().SequenceEqual(sourceResident), "resident undo restores all resident bytes");
session.Redo(); Check(save.Main.GetVillager(0).MovingOut, "resident redo restores moving-out status");
session.Undo();
ExpectInvalid(() => session.SetResident(0, 9, "invalid", false));
ExpectInvalid(() => session.SetResident(0, 0, new string('a', 25), false));
ExpectInvalid(() => session.SetResident(0, 0, "\uD800", false));
ExpectInvalid(() => session.SetResident(9, 0, "", false));
ExpectInvalid(() => session.ReplaceResident(0, "cat00"));
ExpectInvalid(() => session.ReplaceResident(0, "bad00"));
Check(save.Main.Data.SequenceEqual(beforeWorld), "invalid resident edits and duplicate replacements leave save bytes intact");
session.ReplaceResident(0, "cat05");
Check(save.Main.GetVillager(0).InternalName == "cat05" && save.Main.GetVillagerHouse(0).NPC1 == 0, "workspace replacement updates resident and matching house together");
session.Undo(); Check(save.Main.Data.SequenceEqual(beforeWorld), "replacement undo restores the entire original resident and house");
session.Redo(); Check(save.Main.GetVillager(0).InternalName == "cat05", "replacement redo restores both records");
session.SetIsland(changedSettings);
session.Save();
var worldReload = new HorizonSave(provider);
Check(worldReload.Main.GetVillager(0).InternalName == "cat05" && worldReload.Main.WeatherSeed == uint.MaxValue, "world edits survive encryption and reload");
Check(!worldReload.GetInvalidHashes().Any(), "saved world edits keep valid hashes");
var nativeBeforeMain = save.Main.Data.ToArray();
var nativeBeforePersonal = personal.Data.ToArray();
session.EditNative(() => { save.Main.WeatherSeed = 54321; personal.PlayerName = "ToolName"; });
Check(save.Main.WeatherSeed == 54321 && personal.PlayerName == "ToolName", "specialized tools record changes across main and player files");
session.Undo();
Check(save.Main.Data.SequenceEqual(nativeBeforeMain) && personal.Data.SequenceEqual(nativeBeforePersonal), "specialized tool undo restores every changed byte in all affected files");
session.Redo(); Check(personal.PlayerName == "ToolName", "specialized tool redo reapplies changed byte ranges");
session.Undo();
ExpectInvalid(() => session.EditNative(() => { save.Main.WeatherSeed = 0; personal.PlayerName = "Failed"; throw new ArgumentException("invalidRequest"); }));
Check(save.Main.Data.SequenceEqual(nativeBeforeMain) && personal.Data.SequenceEqual(nativeBeforePersonal), "failed specialized tools roll back partial writes and preserve history");
session.EditNative(() => { });
Check(!session.IsDirty, "cancelling an unchanged specialized tool keeps the saved revision clean");
GameInfo.SetLanguage2Char(GameLanguage.GetLanguageIndex("zhs"));
Check(WorkspaceCatalog.Search("", "all", 0).Total == expectedIds.Count, "catalog retains complete coverage after language switching");
Check(WorkspaceVillagers.Search(targetVillager.Id, -1, -1, 0).Items.Single().Name == GameInfo.Strings.GetVillager(targetVillager.Id), "villager names follow the selected UI language");
// Batch 1: native-free turnip and shared recycle edits must preserve exact bytes.
var batchBefore = save.Main.Data.ToArray();
var batch = new WorkspaceSession(save);
var prices = Enumerable.Range(0, 14).Select(i => (uint)(100 + i)).ToArray();
var turnipEdit = new WorkspaceSession.TurnipSettings(uint.MaxValue, prices, 3, uint.MaxValue);
batch.SetTurnips(turnipEdit);
var turnipBytes = save.Main.Data.ToArray();
prices[0] = 999; // External drafts must not change captured history.
batch.Undo();
Check(save.Main.Data.SequenceEqual(batchBefore) && !batch.IsDirty, "turnip undo restores every original byte and the clean state");
batch.Redo();
Check(save.Main.Data.SequenceEqual(turnipBytes) && batch.GetTurnips().SellPrices[0] == 100, "turnip redo owns its arrays and preserves every field");
Check(batch.GetTurnips().SellPrices.SequenceEqual(Enumerable.Range(100, 14).Select(x => (uint)x)), "turnip table retains the day and AM/PM order");
ExpectInvalid(() => batch.SetTurnips(turnipEdit with { SellPrices = new uint[13] }));
ExpectInvalid(() => batch.SetTurnips(turnipEdit with { Pattern = 4 }));
Check(save.Main.Data.SequenceEqual(turnipBytes), "invalid turnip edits leave all bytes intact");
batch.Save();
batch.SetTurnips(batch.GetTurnips());
Check(!batch.IsDirty, "unchanged turnip form adds no history");
var recycleBefore = save.Main.Data.ToArray();
var pocketBeforeRecycle = personal.Data.ToArray();
batch.SetItem(0, "recycle", 39, item);
Check(save.Main.RecycleBin[39].RawValue == item.RawValue && batch.GetItems(1, "recycle")[39].RawValue == item.RawValue, "recycle slot 40 is shared between players and preserves item parameters");
Check(personal.Data.SequenceEqual(pocketBeforeRecycle), "recycle edits never change player inventories");
ExpectInvalid(() => batch.SetItem(0, "recycle", 40, item));
ExpectInvalid(() => batch.SetItem(0, "recycle", -1, item));
batch.Undo();
Check(save.Main.Data.SequenceEqual(recycleBefore), "recycle undo restores all original bytes");
batch.Redo();
batch.SetItem(1, "recycle", 39, new Item(Item.NONE));
Check(save.Main.RecycleBin[39].IsNone, "recycle removal uses the same shared inventory");
batch.Undo();
batch.Save();
var batchReload = new HorizonSave(provider);
Check(batchReload.Main.RecycleBin[39].RawValue == item.RawValue && batchReload.Main.Turnips.BuyPrice == uint.MaxValue
    && batchReload.Main.Turnips.FeverStart == uint.MaxValue && batchReload.Main.Turnips.SellSaturdayPM == 113
    && batchReload.Main.Turnips.Pattern == TurnipPattern.SmallSpike, "turnip and recycle edits survive encrypted save and reload");
Check(!batchReload.GetInvalidHashes().Any(), "batch 1 save has valid hashes");
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
