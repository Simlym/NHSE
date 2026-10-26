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
var inventoryTarget = new WorkspaceSession.GridTarget("inventory", Player: 0, Container: "pocket", Slots: Enumerable.Range(0, 40).ToArray());
var inventoryBefore = personal.Data.ToArray();
session.CopyGrid(inventoryTarget, [new(0, 2)]);
Check(personal.Data.SequenceEqual(inventoryBefore), "inventory copy is read-only");
session.PasteGrid(inventoryTarget, new(0, 0), [new(0, 0), new(1, 0)], true);
Check(personal.Pocket[0].RawValue == item.RawValue && personal.Pocket[1].RawValue == item.RawValue, "inventory paste fills selected slots preserving raw parameters");
session.Undo(); Check(personal.Data.SequenceEqual(inventoryBefore), "entire inventory paste is one exact undo step");
session.Redo(); Check(personal.Pocket[1].RawValue == item.RawValue, "inventory redo restores the batch"); session.Undo();
ExpectInvalid(() => session.PasteGrid(inventoryTarget, new(9, 2), [new(9, 2), new(0, 3)], true));
ExpectInvalid(() => session.EditGrid(inventoryTarget, [new(0, 2), new(0, 3)], "delete"));
ExpectInvalid(() => session.PasteGrid(inventoryTarget, new(10, 0), [new(0, 0)], true));
Check(personal.Data.SequenceEqual(inventoryBefore), "mixed locked selections and invalid anchors reject atomically");
session.EditGrid(inventoryTarget, [new(0, 2), new(1, 2)], "delete");
Check(personal.Bag[0].IsNone, "inventory batch delete clears selected slots"); session.Undo();
session.EditGrid(inventoryTarget, [new(0, 2)], "rotate");
Check(personal.Bag[0].Rotation == ((item.Rotation + 1) & 3), "inventory rotation follows native item bits"); session.Undo();
var filteredInventory = inventoryTarget with { Slots = [20, 0, 21, 1], Columns = 2 };
session.CopyGrid(filteredInventory, [new(0, 0), new(1, 0)]);
session.PasteGrid(filteredInventory, new(0, 1), [new(0, 1)], true);
Check(personal.Bag[1].RawValue == item.RawValue && personal.Pocket[1].IsNone, "filtered inventory layout maps visual cells to original slots and skips empty source cells"); session.Undo();
var storageInventory = inventoryTarget with { Container = "storage", Slots = [98, 99], Columns = 2 };
session.PasteGrid(storageInventory, new(0, 0), [new(0, 0), new(1, 0)], true);
Check(personal.ItemChest[98].RawValue == item.RawValue && personal.ItemChest[99].RawValue == item.RawValue, "inventory clipboard is shared across containers"); session.Undo();
Check(personal.Data.SequenceEqual(inventoryBefore), "inventory undo preserves unrelated player bytes");
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
// Batch 2: progress edits share byte-exact history without touching other players.
var progress = new WorkspaceSession(save);
var progressMainBefore = save.Main.Data.ToArray();
var progressPlayerBefore = personal.Data.ToArray();
var progressOtherBefore = other.Data.ToArray();
var recipeRows = System.Text.Json.JsonSerializer.SerializeToElement(progress.GetProgress(0, "recipes")).GetProperty("entries");
Check(recipeRows.GetArrayLength() == 2048 && recipeRows[2047].GetProperty("index").GetInt32() == 2047, "progress query exposes every valid recipe without an out-of-bounds extra entry");
var reactionRows = System.Text.Json.JsonSerializer.SerializeToElement(progress.GetProgress(0, "reactions")).GetProperty("entries");
Check(reactionRows.GetArrayLength() == personal.Reactions.ManpuBit.Length + 8 && reactionRows[reactionRows.GetArrayLength() - 1].GetProperty("wheel").GetBoolean(), "reaction query covers saved-format capacity and all eight wheel slots");
var achievementRows = System.Text.Json.JsonSerializer.SerializeToElement(progress.GetProgress(0, "achievements")).GetProperty("entries");
Check(achievementRows.GetArrayLength() == 512 && achievementRows[0].GetProperty("dates").GetArrayLength() == 6, "achievement query exposes all stored entries and six date fields");
ExpectInvalid(() => progress.GetProgress(0, "bad"));
ExpectInvalid(() => progress.GetProgress(-1, "recipes"));
Check(personal.Data.SequenceEqual(progressPlayerBefore), "progress queries and invalid requests do not mutate saves");
progress.SetRecipe(0, RecipeBook.RecipeCount - 1, true, true, true, true);
var recipeAfter = personal.Data.ToArray();
var finalRecipe = personal.GetRecipeBook();
Check(finalRecipe.GetIsKnown(2047) && finalRecipe.GetIsMade(2047) && finalRecipe.GetIsNew(2047) && finalRecipe.GetIsFavorite(2047), "last valid recipe preserves all four independent flags");
ExpectInvalid(() => progress.SetRecipe(0, RecipeBook.RecipeCount, true, true, true, true));
ExpectInvalid(() => progress.SetRecipe(0, -1, true, true, true, true));
progress.Undo();
Check(personal.Data.SequenceEqual(progressPlayerBefore) && !progress.IsDirty, "recipe undo restores every personal byte and clean revision");
progress.Redo(); Check(personal.Data.SequenceEqual(recipeAfter), "recipe redo restores all flags");
progress.Save();
progress.SetRecipe(0, 2047, true, true, true, true);
Check(!progress.IsDirty, "unchanged recipe adds no undo entry");
var beforeRecipeBulk = personal.Data.ToArray();
progress.ProgressBatch(0, "recipes", "learnAll");
var allKnown = personal.GetRecipeBook();
Check(RecipeList.Recipes.Keys.Where(i => i != RecipeList.BridgeConstructionKit && i != RecipeList.CampsiteConstructionKit).All(i => allKnown.GetIsKnown(i)), "learn-all uses the original recipe metadata");
Check(!allKnown.GetIsKnown(RecipeList.BridgeConstructionKit) && !allKnown.GetIsKnown(RecipeList.CampsiteConstructionKit), "learn-all preserves the original construction-kit exclusions");
progress.Undo(); Check(personal.Data.SequenceEqual(beforeRecipeBulk), "one undo restores the entire recipe batch");
progress.ProgressBatch(0, "recipes", "craftAll");
Check(personal.GetRecipeBook().GetIsMade(2047) && !personal.GetRecipeBook().GetIsNew(2047), "craft-all clears new flags on known recipes");
progress.Undo();
progress.ProgressBatch(0, "recipes", "clearAll");
Check(!personal.GetRecipeBook().GetIsKnown(2047) && !personal.GetRecipeBook().GetIsFavorite(2047), "clear-all removes all four recipe flags");
progress.Undo();
var reactionCount = personal.Reactions.ManpuBit.Length;
progress.SetReaction(0, reactionCount - 1, 255, true);
progress.SetReaction(0, reactionCount + 7, (int)Reaction.Joy, false);
Check(personal.Reactions.ManpuBit[^1] == (Reaction)255 && personal.Reactions.NewFlag[^1] && personal.Reactions.UIList[7] == Reaction.Joy, "reaction slots retain raw IDs, new flags and wheel assignments");
var reactionAfter = personal.Data.ToArray();
ExpectInvalid(() => progress.SetReaction(0, -1, 1, false));
ExpectInvalid(() => progress.SetReaction(0, reactionCount + 8, 1, false));
ExpectInvalid(() => progress.SetReaction(0, 0, 256, false));
Check(personal.Data.SequenceEqual(reactionAfter), "invalid reaction edits do not change bytes");
progress.Undo(); progress.Undo();
Check(personal.Data.SequenceEqual(beforeRecipeBulk), "reaction undo preserves previous recipe edits");
progress.Redo(); progress.Redo();
Check(personal.Data.SequenceEqual(reactionAfter), "reaction redo restores all personal bytes");
progress.ProgressBatch(0, "reactions", "learnAll");
Check(personal.Reactions.ManpuBit.Contains(Reaction.Joy) && !personal.Reactions.ManpuBit.Contains(Reaction.UNUSED_6), "reaction learn-all excludes unused game reactions");
progress.Undo(); Check(personal.Data.SequenceEqual(reactionAfter), "reaction batch is one undo step");
progress.ProgressBatch(0, "reactions", "clearAll");
Check(personal.Reactions.ManpuBit.All(v => v == Reaction.None) && personal.Reactions.UIList.All(v => v == Reaction.None) && personal.Reactions.NewFlag.All(v => !v), "reaction clear-all clears learned slots, flags and wheel");
progress.Undo();
var dates = new[] { "2024-02-29", "", "2026-10-24", "", "", "" };
var reads = new[] { true, false, true, false, false, false };
progress.SetAchievement(0, 0, uint.MaxValue, 255, dates, reads);
var achievementAfter = personal.Data.ToArray();
Check(personal.Achievements.Counts[0] == uint.MaxValue && personal.Achievements.Flags[0] == 255 && personal.Achievements.Date[0][0].ToString() == "2024-02-29" && personal.Achievements.Date[0][1].IsEmpty, "achievement edits preserve unsigned counts, raw flags and empty dates");
ExpectInvalid(() => progress.SetAchievement(0, 512, 0, 0, dates, reads));
ExpectInvalid(() => progress.SetAchievement(0, 0, 0, 256, dates, reads));
ExpectInvalid(() => progress.SetAchievement(0, 0, 0, 0, new[] { "2023-02-29", "", "", "", "", "" }, reads));
ExpectInvalid(() => progress.SetAchievement(0, 0, 0, 0, new string[5], reads));
Check(personal.Data.SequenceEqual(achievementAfter), "invalid dates and achievement ranges cannot partially modify the save");
dates[0] = "2001-01-01"; reads[0] = false;
progress.Undo(); Check(personal.Data.SequenceEqual(reactionAfter), "achievement undo restores all bytes without touching reactions");
progress.Redo(); Check(personal.Data.SequenceEqual(achievementAfter), "achievement redo is independent of caller-owned arrays");
progress.ProgressBatch(0, "achievements", "clear", 0);
Check(personal.Achievements.Counts[0] == 0 && personal.Achievements.Date[0][0].IsEmpty, "selected achievement clear resets its records");
progress.Undo(); Check(personal.Data.SequenceEqual(achievementAfter), "selected clear undo restores exact dates and flags");
progress.ProgressBatch(0, "achievements", "complete", 1);
Check(personal.Achievements.Counts[1] >= LifeSupportAchievement.List[1].MaxThreshold && personal.Achievements.Read[1][0], "selected completion uses the original achievement thresholds");
progress.Undo();
progress.ProgressBatch(0, "achievements", "complete");
progress.Undo(); Check(personal.Data.SequenceEqual(achievementAfter), "completing all achievements is one reversible edit");
ExpectInvalid(() => progress.ProgressBatch(0, "recipes", "bad"));
ExpectInvalid(() => progress.ProgressBatch(0, "achievements", "clear", -2));
Check(save.Main.Data.SequenceEqual(progressMainBefore) && other.Data.SequenceEqual(progressOtherBefore), "progress edits leave the main save and other players byte-identical");
progress.Save();
var progressReload = new HorizonSave(provider);
var progressReloadPlayer = progressReload.Players[0].Personal;
Check(progressReloadPlayer.GetRecipeBook().GetIsFavorite(2047) && progressReloadPlayer.Reactions.UIList[7] == Reaction.Joy
    && progressReloadPlayer.Achievements.Counts[0] == uint.MaxValue && progressReloadPlayer.Achievements.Date[0][0].ToString() == "2024-02-29", "recipes, reactions and achievements survive encrypted save and reload");
Check(!progressReload.GetInvalidHashes().Any(), "progress save retains valid hashes");
// Original saves have only 64 learned slots; never assume the later 256-slot layout.
var originalProvider = new MemoryProvider();
var originalRevision = new FileHeaderInfo { Major = 0x67, Minor = 0x6F, Unk1 = 2, Unk2 = 2, SaveRevision = 0 };
var originalSizes = RevisionChecker.SizeInfo[0];
originalProvider.Add("main", originalSizes.Main, originalRevision);
originalProvider.Add("Villager0/personal", originalSizes.Personal, originalRevision);
originalProvider.Add("Villager0/photo_studio_island", originalSizes.PhotoStudioIsland, originalRevision);
originalProvider.Add("Villager0/postbox", originalSizes.PostBox, originalRevision);
originalProvider.Add("Villager0/profile", originalSizes.Profile, originalRevision);
var originalSave = new HorizonSave(originalProvider);
var originalProgress = new WorkspaceSession(originalSave);
var originalPersonal = originalSave.Players[0].Personal;
var originalBytes = originalPersonal.Data.ToArray();
Check(originalPersonal.Reactions.ManpuBit.Length == 64, "original reaction save format uses 64 learned slots");
originalProgress.SetReaction(0, 63, 255, true);
originalProgress.SetReaction(0, 71, 3, false);
Check(originalPersonal.Reactions.ManpuBit[63] == (Reaction)255 && originalPersonal.Reactions.UIList[7] == Reaction.Joy, "original save reaction and wheel boundaries remain editable");
ExpectInvalid(() => originalProgress.SetReaction(0, 72, 1, false));
originalProgress.Undo(); originalProgress.Undo();
Check(originalPersonal.Data.SequenceEqual(originalBytes), "original save progress undo restores every byte");
originalProgress.SetRecipe(0, 2047, true, false, true, false);
originalProgress.SetAchievement(0, 0, 123, 1, new[] { "2024-02-29", "", "", "", "", "" }, new bool[6]);
originalProgress.Save();
var originalReload = new HorizonSave(originalProvider);
Check(originalReload.Players[0].Personal.GetRecipeBook().GetIsKnown(2047) && originalReload.Players[0].Personal.Achievements.Counts[0] == 123
    && !originalReload.GetInvalidHashes().Any(), "original-format recipe and achievement edits survive encrypted save and reload");
// Batch 3: house edits use the save's concrete house type, including hidden v2 offsets.
var housing = new WorkspaceSession(save);
var mainHousingBefore = save.Main.Data.ToArray();
var housingPlayerBefore = personal.Data.ToArray();
var housingOtherBefore = other.Data.ToArray();
var houseData = housing.GetHousing("playerHouses", 0, 0, 0, 0);
Check(houseData.Fields.Any(f => f.Key == "Loan") && houseData.Flags.Length == 128 && houseData.Room!.Tiles.Length == 400, "player housing exposes property fields, signed flags and a complete room grid");
var unchangedHouseValues = houseData.Fields.Concat(houseData.Flags).ToDictionary(f => f.Key, f => System.Text.Json.JsonSerializer.SerializeToElement(f.Value));
housing.SetHousing("playerHouses", 0, 0, unchangedHouseValues);
Check(!housing.IsDirty && save.Main.Data.SequenceEqual(mainHousingBefore), "unchanged house form never normalizes encrypted fields or unknown bytes");
housing.SetHousing("playerHouses", 0, 0, HousingValues(("HouseLevel", 7), ("NPC1", -1), ("Loan", uint.MaxValue), ("DoorDecoItemName.ItemId", 2213), ("DoorDecoItemName.FreeParam", int.MinValue), ("EventFlag.127", short.MinValue)));
var houseUpdated = (PlayerHouse2)save.Main.GetPlayerHouse(0);
Check(houseUpdated.HouseLevel == 7 && houseUpdated.NPC1 == -1 && (uint)houseUpdated.Loan == uint.MaxValue && houseUpdated.DoorDecoItemName.ItemId == 2213 && houseUpdated.DoorDecoItemName.FreeParam == int.MinValue && houseUpdated.EventFlags[127] == short.MinValue, "v2 house fields use concrete tail offsets and retain raw item parameters");
var houseAfter = save.Main.Data.ToArray();
ExpectInvalid(() => housing.SetHousing("playerHouses", 0, 0, HousingValues(("HouseLevel", 8), ("EventFlag.127", 32768))));
ExpectInvalid(() => housing.SetHousing("playerHouses", 0, 0, HousingValues(("Data", 0))));
ExpectInvalid(() => housing.GetHousing("playerHouses", 0, 8, 0, 0));
Check(save.Main.Data.SequenceEqual(houseAfter), "invalid house fields cannot partially write valid fields");
housing.Undo(); Check(save.Main.Data.SequenceEqual(mainHousingBefore), "house undo restores all main bytes");
housing.Redo(); Check(save.Main.Data.SequenceEqual(houseAfter), "house redo restores all edited bytes");
housing.Undo();
housing.SetHousing("residentHouses", 0, 0, HousingValues(("NPC1", -1)));
housing.SetHousing("residentHouses", 0, 5, HousingValues(("NPC1", 0), ("HouseStatus", uint.MaxValue)));
Check(housing.FindResidentHouse(0) == 5 && save.Main.GetVillagerHouse(5) is VillagerHouse2 { HouseStatus: uint.MaxValue }, "resident shortcut resolves the house assignment instead of assuming matching slot numbers");
housing.Undo(); housing.Undo();
// Prepare an empty room; all non-room bytes and the other seven layers are retained.
var batchRoomHouse = save.Main.GetPlayerHouse(0);
var blankRoom = batchRoomHouse.GetRoom(0);
var blankLayers = blankRoom.GetItemLayers();
foreach (var l in blankLayers) foreach (var tile in l.Tiles) tile.Delete();
blankRoom.SetItemLayers(blankLayers); batchRoomHouse.SetRoom(0, blankRoom); save.Main.SetPlayerHouse(batchRoomHouse, 0);
var beforeRoom = save.Main.Data.ToArray();
var furnitureId = GameInfo.Strings.ItemDataSource.Select(x => x.Value).Where(x => x is >= 0 and < 60000 && WorkspaceCatalog.Contains(x))
    .First(id => { var size = ItemInfo.GetItemSize(new Item((ushort)id)); return size.Width > 1 && size.Height > 1 && size.Width < 10 && size.Height < 10; });
var furniture = new WorkspaceSession.RoomItem(furnitureId, 1, 9, 123);
var nextFurniture = furniture.ToItem();
var footprint = ItemInfo.GetItemSize(nextFurniture);
housing.SetRoomItem(0, 0, 0, 2, 3, furniture, true, true, false);
var placedRoom = save.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[0];
Check(placedRoom.GetTile(2, 3).RawValue == nextFurniture.RawValue && placedRoom.GetTile(3, 3).IsExtension && placedRoom.GetTile(3, 3).ExtensionItemId == furnitureId, "room placement uses the original multi-tile extension rules and preserves all item bytes");
var placedMain = save.Main.Data.ToArray();
housing.SetRoomItem(0, 0, 0, 2, 3, furniture, true, true, false);
Check(save.Main.Data.SequenceEqual(placedMain), "reapplying the same room item preserves root and extension bytes");
ExpectInvalid(() => housing.SetRoomItem(0, 0, 0, 19, 19, furniture, true, true, false));
ExpectInvalid(() => housing.SetRoomItem(0, 0, 0, 1, 3, furniture, true, true, false));
ExpectInvalid(() => housing.SetRoomItem(0, 6, 0, 0, 0, furniture, true, true, false));
ExpectInvalid(() => housing.SetRoomItem(0, 0, 8, 0, 0, furniture, true, true, false));
Check(save.Main.Data.SequenceEqual(placedMain), "blocked room placements never leak detached changes into the save");
housing.SetRoomItem(0, 0, 0, 3, 3, furniture, true, true, true);
Check(save.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[0].GetTile(2, 3).IsNone && save.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[0].GetTile(3, 3).IsNone, "deleting an extension tile also deletes its root and footprint");
housing.Undo(); Check(save.Main.Data.SequenceEqual(placedMain), "room delete undo restores root and every extension byte");
housing.Undo(); Check(save.Main.Data.SequenceEqual(beforeRoom), "room placement undo restores every layer and unrelated bytes");
housing.Redo();
// Campsite preserves the real date instead of replacing it with today's date.
save.Main.CampsiteStatus = 1;
Check(housing.GetHousing("campsite", 0, 0, 0, 0).Locked, "locked campsite is read-only");
ExpectInvalid(() => housing.SetHousing("campsite", 0, 0, HousingValues(("Occupied", true))));
save.Main.CampsiteStatus = 2;
save.Main.CampTimestamp = new DateTime(2020, 3, 20);
var camper = WorkspaceVillagers.All()[0];
var camperCode = camper.Species * 256 + int.Parse(camper.Id.AsSpan(3));
housing.SetHousing("campsite", 0, 0, HousingValues(("Occupied", true), ("VisitorCode", camperCode)));
Check(save.Main.CampsiteStatus == 3 && save.Main.CampTimestamp.ToString() == "2020-03-20" && save.Main.CampsiteVillagerSpeciesID == camper.Species, "choosing a campsite visitor preserves the saved visit date");
var beforeBadCamp = save.Main.Data.ToArray();
ExpectInvalid(() => housing.SetHousing("campsite", 0, 0, HousingValues(("VisitDate", "2023-02-29"))));
ExpectInvalid(() => housing.SetHousing("campsite", 0, 0, HousingValues(("VisitorCode", 65535))));
Check(save.Main.Data.SequenceEqual(beforeBadCamp), "invalid campsite date and visitor cannot modify bytes");
housing.SetHousing("campsite", 0, 0, HousingValues(("Occupied", false)));
Check(save.Main.CampsiteStatus == 2 && save.Main.CampsiteVillagerSpeciesID == 35 && save.Main.CampsiteVillagerVariantID == 0 && save.Main.CampTimestamp.ToString() == "2020-03-20", "clearing campsite uses the game's empty visitor sentinel and preserves date");
housing.Undo(); Check(save.Main.Data.SequenceEqual(beforeBadCamp), "campsite undo restores exact status, visitor and date");
var beforeFruitMain = save.Main.Data.ToArray();
var beforeFruitPlayer = personal.Data.ToArray();
housing.SetHousing("fruitFlowers", 0, 0, HousingValues(("ProfileFruit", 2214), ("SpecialtyFruit", 2287), ("SisterFruit", 2285), ("SpecialtyFlower", 7), ("SisterFlower", 6)));
Check(personal.ProfileFruit == 2214 && save.Main.SpecialtyFruit == 2287 && save.Main.FruitFlags.SequenceEqual(new byte[] { 0, 0, 0, 0, 1 }) && save.Main.SisterFruit == 2285 && save.Main.SpecialtyFlower == IslandFlowers.Pansy, "fruit and flower edits synchronize native fruit flags without conflating player profile fruit");
var fruitAfterMain = save.Main.Data.ToArray(); var fruitAfterPersonal = personal.Data.ToArray();
ExpectInvalid(() => housing.SetHousing("fruitFlowers", 0, 0, HousingValues(("SpecialtyFruit", 1), ("SpecialtyFlower", 0))));
ExpectInvalid(() => housing.SetHousing("fruitFlowers", 0, 0, HousingValues(("SisterFlower", 8))));
Check(save.Main.Data.SequenceEqual(fruitAfterMain) && personal.Data.SequenceEqual(fruitAfterPersonal), "invalid fruit and flower edits are atomic across main and player files");
housing.Undo(); Check(save.Main.Data.SequenceEqual(beforeFruitMain) && personal.Data.SequenceEqual(beforeFruitPlayer), "one undo restores fruit settings across both files");
housing.Redo();
Check(other.Data.SequenceEqual(housingOtherBefore), "housing and island life never modify another player's personal file");
housing.Save();
var housingReload = new HorizonSave(provider);
Check(housingReload.Main.CampsiteStatus == 3 && housingReload.Main.SpecialtyFruit == 2287 && housingReload.Players[0].Personal.ProfileFruit == 2214
    && housingReload.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[0].GetTile(2, 3).RawValue == nextFurniture.RawValue && !housingReload.GetInvalidHashes().Any(), "housing, room furniture, campsite and fruit survive encrypted save and hash validation");
// Original-format house fields and rooms use their own offsets.
var oldHousing = new WorkspaceSession(originalSave);
var oldBefore = originalSave.Main.Data.ToArray();
oldHousing.SetHousing("playerHouses", 0, 0, HousingValues(("NPC1", -1), ("HouseLevel", 3), ("PostItemName.ItemId", 2213)));
var oldHouse = (PlayerHouse1)originalSave.Main.GetPlayerHouse(0);
Check(oldHouse.NPC1 == -1 && oldHouse.HouseLevel == 3 && oldHouse.PostItemName.ItemId == 2213, "original house format uses its own property offsets");
oldHousing.Undo(); Check(originalSave.Main.Data.SequenceEqual(oldBefore), "original house format undo is byte-exact");
var oldRoomHouse = originalSave.Main.GetPlayerHouse(0);
var oldRoom = oldRoomHouse.GetRoom(0); var oldLayers = oldRoom.GetItemLayers();
foreach (var l in oldLayers) foreach (var tile in l.Tiles) tile.Delete();
oldRoom.SetItemLayers(oldLayers); oldRoomHouse.SetRoom(0, oldRoom); originalSave.Main.SetPlayerHouse(oldRoomHouse, 0);
var oldRoomBefore = originalSave.Main.Data.ToArray();
oldHousing.SetRoomItem(0, 0, 0, 2, 3, furniture, true, true, false);
Check(originalSave.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[0].GetTile(3, 3).IsExtension, "original room format supports the same multi-tile placement rules");
oldHousing.Undo(); Check(originalSave.Main.Data.SequenceEqual(oldRoomBefore), "original room placement undo preserves every byte");
// House/room files: native byte preservation, both conversion directions and atomic undo.
var residentV2File = Enumerable.Range(0, VillagerHouse2.SIZE).Select(i => (byte)(i * 37)).ToArray();
Check(new VillagerHouse2(residentV2File).Downgrade().Write().SequenceEqual(residentV2File.AsSpan(0, VillagerHouse1.SIZE).ToArray()), "resident house downgrade copies only the supported legacy prefix");
foreach (var fileSave in new[] { save, originalSave })
{
    var files = new WorkspaceSession(fileSave);
    var filesBefore = fileSave.Main.Data.ToArray();
    foreach (var kind in new[] { "playerHouses", "residentHouses" })
    {
        var native = files.ExportHousing(kind, 0, 0, false);
        files.ImportHousing(kind, 0, 0, false, native);
        Check(!files.IsDirty && fileSave.Main.Data.SequenceEqual(filesBefore), "unchanged house file keeps bytes and clean history: " + kind + native.Length);
        var expected = native.ToArray(); expected[0] ^= 1; expected[^1] ^= 0x5A;
        object source = kind == "playerHouses" ? fileSave.Main.Offsets.ReadPlayerHouse(expected.ToArray()) : fileSave.Main.Offsets.ReadVillagerHouse(expected.ToArray());
        foreach (var key in new[] { "NPC1", "NPC2", "BuildPlayer" })
        {
            var prop = source.GetType().GetProperty(key);
            if (prop is { CanWrite: true }) prop.SetValue(source, (sbyte)-7);
        }
        var imported = source is IPlayerHouse ph ? ph.Write() : ((IVillagerHouse)source).Write();
        var inputBefore = imported.ToArray();
        files.ImportHousing(kind, 0, 0, false, imported);
        Check(files.ExportHousing(kind, 0, 0, false).SequenceEqual(expected) && imported.SequenceEqual(inputBefore), "house import preserves assignments, unknown bytes and caller buffer: " + kind + native.Length);
        files.Undo(); Check(fileSave.Main.Data.SequenceEqual(filesBefore), "house file undo restores complete main bytes: " + kind);
        files.Redo(); Check(files.ExportHousing(kind, 0, 0, false).SequenceEqual(expected), "house file redo restores complete import: " + kind); files.Undo();
        var otherSize = kind == "playerHouses" ? (native.Length == PlayerHouse1.SIZE ? PlayerHouse2.SIZE : PlayerHouse1.SIZE)
            : native.Length == VillagerHouse1.SIZE ? VillagerHouse2.SIZE : VillagerHouse1.SIZE;
        var crossFile = kind == "playerHouses" ? PlayerHouseConverter.GetCompatible(expected, otherSize) : VillagerHouseConverter.GetCompatible(expected, otherSize).ToArray();
        var converted = kind == "playerHouses" ? PlayerHouseConverter.GetCompatible(crossFile, native.Length) : VillagerHouseConverter.GetCompatible(crossFile, native.Length).ToArray();
        files.ImportHousing(kind, 0, 0, false, crossFile);
        Check(files.ExportHousing(kind, 0, 0, false).SequenceEqual(converted), "house file converts through existing core format converter: " + kind + native.Length); files.Undo();
        ExpectInvalid(() => files.ImportHousing(kind, 0, 0, false, [1, 2, 3]));
        ExpectInvalid(() => files.ImportHousing(kind, 999, 0, false, native));
        Check(fileSave.Main.Data.SequenceEqual(filesBefore), "invalid house files reject without partial writes: " + kind);
    }
    foreach (var room in new[] { 0, 5 })
    {
        var native = files.ExportHousing("playerHouses", 0, room, true);
        files.ImportHousing("playerHouses", 0, room, true, native);
        Check(!files.IsDirty, "unchanged room file adds no history");
        var edited = native.ToArray(); edited[0] ^= 1; edited[^1] ^= 0x5A;
        files.ImportHousing("playerHouses", 0, room, true, edited);
        Check(files.ExportHousing("playerHouses", 0, room, true).SequenceEqual(edited), "room import retains every layer and unknown byte: " + room + native.Length);
        files.Undo(); Check(fileSave.Main.Data.SequenceEqual(filesBefore), "room file undo restores house and unrelated rooms");
        var otherSize = native.Length == PlayerRoom1.SIZE ? PlayerRoom2.SIZE : PlayerRoom1.SIZE;
        var crossFile = PlayerRoomConverter.GetCompatible(edited, otherSize);
        files.ImportHousing("playerHouses", 0, room, true, crossFile);
        Check(files.ExportHousing("playerHouses", 0, room, true).SequenceEqual(PlayerRoomConverter.GetCompatible(crossFile, native.Length)), "room file converts in both revision directions"); files.Undo();
    }
    ExpectInvalid(() => files.ImportHousing("playerHouses", 0, 6, true, new byte[PlayerRoom1.SIZE]));
    ExpectInvalid(() => files.ImportHousing("residentHouses", 0, 0, true, new byte[PlayerRoom1.SIZE]));
    ExpectInvalid(() => files.ExportHousing("campsite", 0, 0, false));
    ExpectInvalid(() => files.ImportHousing("playerHouses", 0, 0, true, [0]));
    Check(!WorkspaceSession.IsHousingFileSizeValid("playerHouses", false, long.MaxValue) && fileSave.Main.Data.SequenceEqual(filesBefore), "oversized files and invalid room targets leave all data intact");
}
// Batch 4: preview and file exchange preserve complete design bytes and ownership.
var designs = new WorkspaceSession(save);
var designsBefore = save.Main.Data.ToArray();
var designsPersonalBefore = personal.Data.ToArray();
var designsOtherBefore = other.Data.ToArray();
Check(designs.DesignCount("patterns") == save.Main.Offsets.PatternCount && designs.DesignCount("flag") == 1 && designs.DesignCount("tailor") == 8, "design libraries use format-specific capacities and the fixed flag/tailor slots");
var ordinaryFile = DesignFixture(false, "Pattern A");
var proFile = DesignFixture(true, "PRO A");
ExpectInvalid(() => designs.ImportDesigns("patterns", 0, 0, [ordinaryFile, new byte[1]], false));
ExpectInvalid(() => designs.ImportDesigns("patterns", designs.DesignCount("patterns") - 1, 0, [ordinaryFile, ordinaryFile], false));
ExpectInvalid(() => designs.ImportDesigns("flag", 1, 0, [ordinaryFile], false));
ExpectInvalid(() => designs.ImportDesigns("tailor", 8, 0, [proFile], false));
ExpectInvalid(() => designs.ImportDesigns("proDesigns", 0, 0, [ordinaryFile], false));
ExpectInvalid(() => designs.ImportDesigns("patterns", 0, -1, [ordinaryFile], false));
ExpectInvalid(() => designs.GetDesignWorkspace("bad", 0));
Check(save.Main.Data.SequenceEqual(designsBefore) && !designs.IsDirty, "invalid design batches, kinds and player indexes leave every byte and history unchanged");
foreach (var kind in new[] { "patterns", "proDesigns", "flag", "tailor" })
{
    var file = WorkspaceSession.IsProDesign(kind) ? proFile : ordinaryFile;
    designs.ImportDesigns(kind, 0, 0, [file], false);
    Check(designs.DesignBytes(kind, 0).SequenceEqual(file), "valid design import/export roundtrip preserves all bytes: " + kind);
    designs.Undo(); Check(save.Main.Data.SequenceEqual(designsBefore), "design undo restores all main bytes: " + kind);
    designs.Redo(); Check(designs.DesignBytes(kind, 0).SequenceEqual(file), "design redo restores the imported file: " + kind);
    designs.Undo();
}
var firstFile = DesignFixture(false, "First"); var secondFile = DesignFixture(false, "Second");
new DesignPattern(firstFile).GetTownIdentity().CopyTo(firstFile.AsSpan(0xA5 + 64));
new DesignPattern(firstFile).GetPlayerIdentity().CopyTo(firstFile.AsSpan(0xA5 + 128));

designs.ImportDesigns("patterns", 0, 1, [firstFile, secondFile], true);
var importedBatch = save.Main.Data.ToArray();
Check(save.Main.GetDesign(0).IsOriginatedFrom(other) && save.Main.GetDesign(1).IsOriginatedFrom(other), "batch ownership uses the selected player rather than the slot number");
Check(save.Main.GetDesign(0).Data[0xA5..].SequenceEqual(firstFile.AsSpan(0xA5)) && save.Main.GetDesign(1).Data[0xA5..].SequenceEqual(secondFile.AsSpan(0xA5)), "ownership updates never replace matching identity-like bytes in the palette, pixels or padding");
firstFile.AsSpan().Fill(255); secondFile.AsSpan().Fill(255);
designs.Undo(); Check(save.Main.Data.SequenceEqual(designsBefore), "one undo restores an entire multi-file design batch");
designs.Redo(); Check(save.Main.Data.SequenceEqual(importedBatch), "design history owns copies independent of imported byte arrays");
designs.Save();
var savedDesignBatch = save.Main.Data.ToArray();
var noOpFiles = new[] { designs.DesignBytes("patterns", 0), designs.DesignBytes("patterns", 1) };
designs.ImportDesigns("patterns", 0, 1, noOpFiles, false);
Check(!designs.IsDirty, "importing identical valid files adds no revision");
var detachedExport = designs.DesignBytes("patterns", 0); detachedExport[0] ^= 255;
Check(save.Main.Data.SequenceEqual(savedDesignBatch), "exported design buffers cannot mutate the save");
var transparent = DesignFixture(false, "Transparent");
new DesignPattern(transparent).UsageCompatibility = 0xDEADBEEF;
new DesignPattern(transparent)[1023] = 15;
designs.ImportDesigns("flag", 0, 0, [transparent], false);
Check(save.Main.FlagMyDesign.UsageCompatibility == 0xEE02 && save.Main.FlagMyDesign[1023] == 15, "ordinary compatibility repair detects transparent pixels without changing the pixels");
var opaque = DesignFixture(false, "Opaque"); new DesignPattern(opaque).UsageCompatibility = 0;
designs.ImportDesigns("patterns", designs.DesignCount("patterns") - 1, 0, [opaque], false);
Check(save.Main.GetDesign(designs.DesignCount("patterns") - 1).UsageCompatibility == 0xEE00, "opaque compatibility repair uses the regular design flag");
var proInvalid = DesignFixture(true, "PRO repaired"); new DesignPatternPRO(proInvalid).UsageCompatibility = 0;
designs.ImportDesigns("proDesigns", 0, 0, [proInvalid], false);
Check(save.Main.GetDesignPRO(0).UsageCompatibility == 0xEE01, "PRO compatibility repair uses the PRO flag");
var proUnused = DesignFixture(true, "Unused PRO"); new DesignPatternPRO(proUnused).UsageCompatibility = 0xEE05;
designs.ImportDesigns("tailor", 7, 0, [proUnused], false);
Check(designs.DesignBytes("tailor", 7).SequenceEqual(proUnused), "known unused PRO compatibility is retained in the last tailor slot");
var preview = designs.GetDesignWorkspace("proDesigns", 0);
Check(preview.Entries.Length == designs.DesignCount("proDesigns") && preview.Selected.Sheets.Length == 4 && preview.Selected.Palette.Length == 15 && preview.Selected.Palette[0] == "#112233", "design previews expose every slot, four PRO sheets and the correct RGB palette");
for (var sheet = 0; sheet < 4; sheet++)
{
    var bitmap = Convert.FromBase64String(preview.Selected.Sheets[sheet]);
    Check(bitmap.Length == 4096 && bitmap[0] == 0x33 + sheet && bitmap[1] == 0x22 + sheet && bitmap[2] == 0x11 + sheet && bitmap[3] == 255
        && bitmap[4092] == 0x33 + sheet, "PRO preview keeps sheet offsets independent: " + sheet);
}
var transparentPreview = designs.GetDesignWorkspace("flag", 0);
Check(Convert.FromBase64String(transparentPreview.Selected.Sheets[0])[4095] == 0, "transparent design pixels keep zero alpha in previews");
Check(personal.Data.SequenceEqual(designsPersonalBefore) && other.Data.SequenceEqual(designsOtherBefore), "design operations never alter player files");
designs.Save();
var designsReload = new HorizonSave(provider);
Check(designsReload.Main.GetDesign(0).IsOriginatedFrom(designsReload.Players[1].Personal) && designsReload.Main.FlagMyDesign.UsageCompatibility == 0xEE02
    && designsReload.Main.GetDesignPRO(0).DesignName == "PRO repaired" && designsReload.Main.GetDesignsTailor()[7].Data.SequenceEqual(proUnused)
    && !designsReload.GetInvalidHashes().Any(), "all four design libraries survive encrypted save/reload with valid hashes");
var oldDesigns = new WorkspaceSession(originalSave);
var oldDesignBefore = originalSave.Main.Data.ToArray();
Check(oldDesigns.DesignCount("patterns") == 50, "original saves retain their 50-slot design capacity");
oldDesigns.ImportDesigns("patterns", 49, 0, [ordinaryFile], false);
Check(oldDesigns.DesignBytes("patterns", 49).SequenceEqual(ordinaryFile), "original save imports the last available design slot");
oldDesigns.Undo(); Check(originalSave.Main.Data.SequenceEqual(oldDesignBefore), "original save design undo restores every byte");
// Map edits operate on detached tiles and preserve all bytes outside their validated regions.
var mapSession = new WorkspaceSession(save);
var mapTarget = new WorkspaceSession.MapTarget("terrain", 10, 0, 2, 3, 0, 0);
var mapBefore = save.Main.Data.ToArray();
Check(mapSession.GetMapWorkspace(mapTarget).Cells.First(c => c.X == 2 && c.Y == 3) is { RelativeX: 2, RelativeY: 3, Valid: true }, "terrain acre offset uses coarse coordinates");
Check(mapSession.GetMapWorkspace(mapTarget with { Acre = 0 }).Fields.Length == 0, "exterior ocean cells are not writable terrain");
mapSession.EditMap(mapTarget, HousingValues(("Map.Elevation", 2), ("Map.UnitModel", 1)), "apply", true, true, true);
var mapTerrainIndex = 2 * 96 + 3;
Check(save.Main.GetTerrainTiles()[mapTerrainIndex].Elevation == 2, "terrain uses column-major indexing");
mapSession.EditMap(mapTarget, HousingValues(), "rotate", true, true, true);
Check(save.Main.GetTerrainTiles()[mapTerrainIndex].LandMakingAngle == 1, "terrain rotation reuses native rules");
mapSession.Undo(); mapSession.Undo();
Check(save.Main.Data.SequenceEqual(mapBefore), "terrain undo restores every main-save byte");
ExpectInvalid(() => mapSession.EditMap(mapTarget, HousingValues(("Map.Elevation", 2), ("Map.Bad", 1)), "apply", true, true, true));
Check(save.Main.Data.SequenceEqual(mapBefore), "invalid map fields reject atomically");
var mapBuilding = mapTarget with { Mode = "buildings", Building = 45 };
var mapBuildingOffset = save.Main.Offsets.MainFieldStructure + 45 * Building.SIZE;
save.Main.Data[mapBuildingOffset + 11] = 0xAB;
save.Main.Data[mapBuildingOffset + 14] = 0xCD;
save.Main.Data[mapBuildingOffset + 15] = 0xEF;
var mapBuildingBefore = save.Main.Data.ToArray();
mapSession.EditMap(mapBuilding, HousingValues(("Map.Bit", -128), ("Map.X", 123), ("Map.Unused", uint.MaxValue)), "apply", true, true, true);
Check(save.Main.Buildings[45] is { Bit: -128, X: 123, Unused: uint.MaxValue } && save.Main.Data[mapBuildingOffset + 11] == 0xAB && save.Main.Data[mapBuildingOffset + 14] == 0xCD && save.Main.Data[mapBuildingOffset + 15] == 0xEF, "building edits preserve padding and signed flags");
mapSession.Undo(); Check(save.Main.Data.SequenceEqual(mapBuildingBefore), "building undo is byte exact");
mapSession.EditMap(mapTarget with { Mode = "acres", Acre = 71 }, HousingValues(("Map.AcreTemplate", 123)), "apply", true, true, true);
Check(save.Main.GetAcre(71) == 123, "last exterior acre is editable");
mapSession.Undo();
var mapMarker = mapTarget with { Mode = "markers", Marker = 7 * 6 * 256 - 1 };
mapSession.EditMap(mapMarker, HousingValues(("Map.PlazaX", uint.MaxValue), ("Map.PlazaY", 456), ("Map.DesignValue", 0xF800)), "apply", true, true, true);
Check(save.Main.EventPlazaLeftUpX == uint.MaxValue && save.Main.EventPlazaLeftUpZ == 456, "plaza and final design slot share an undo command");
mapSession.Undo(); Check(save.Main.Data.SequenceEqual(mapBuildingBefore), "multi-region map undo restores all bytes");
var mapItemsTarget = mapTarget with { Mode = "items", X = 4, Y = 6, Layer = 1 };
var mapItems = Enumerable.Range(0, 7 * 6 * 32 * 32).Select(_ => new Item(Item.NONE)).ToArray();
save.Main.SetFieldItemLayer1(mapItems);
var mapItemBefore = save.Main.Data.ToArray();
mapSession.EditMap(mapItemsTarget, HousingValues(("Map.ItemId", 60000), ("Map.SystemParam", 0), ("Map.AdditionalParam", 0), ("Map.FreeParam", 0)), "apply", true, true, true);
Check(save.Main.GetFieldItemLayer1()[4 * 192 + 6].ItemId == 60000, "field object placement selects correct item layer and column-major tile");
var mapPlaced = save.Main.Data.ToArray();
var mapExtensionIndex = Array.FindIndex(save.Main.GetFieldItemLayer1(), t => t.IsExtension);
Check(mapExtensionIndex >= 0, "automatic map footprint creates extension tiles");
var mapExtensionTarget = mapItemsTarget with { X = mapExtensionIndex / 192, Y = mapExtensionIndex % 192 };
mapSession.EditMap(mapExtensionTarget, HousingValues(), "delete", true, true, true);
Check(save.Main.GetFieldItemLayer1()[4 * 192 + 6].IsNone && !save.Main.GetFieldItemLayer1().Any(t => t.IsExtension), "deleting an extension clears its root and complete footprint");
mapSession.Undo(); Check(save.Main.Data.SequenceEqual(mapPlaced), "extension deletion undo restores complete item layer");
ExpectInvalid(() => mapSession.EditMap(mapItemsTarget with { X = 3 }, HousingValues(("Map.ItemId", 60000)), "apply", true, true, false));
Check(save.Main.Data.SequenceEqual(mapPlaced), "overlapping map placement is rejected without partial writes");
ExpectInvalid(() => mapSession.EditMap(mapItemsTarget with { Acre = 16, X = 31 }, HousingValues(("Map.ItemId", 60000)), "apply", true, true, false));
Check(save.Main.Data.SequenceEqual(mapPlaced), "footprints beyond map bounds are rejected atomically");

ExpectInvalid(() => mapSession.EditMap(mapItemsTarget with { Acre = 0 }, HousingValues(), "delete", true, true, true));
Check(save.Main.Data.SequenceEqual(mapPlaced), "invalid exterior placement leaves main unchanged");
mapSession.EditMap(mapItemsTarget, HousingValues(), "delete", true, true, true);
Check(save.Main.GetFieldItemLayer1()[4 * 192 + 6].IsNone, "field object deletion clears selected root");
mapSession.Undo(); Check(save.Main.Data.SequenceEqual(mapPlaced), "item deletion undo preserves footprints and unrelated flags");
mapSession.Undo(); Check(save.Main.Data.SequenceEqual(mapItemBefore), "item placement undo restores every main byte");
var mapNewProvider = new MemoryProvider();
var mapNewRevision = new FileHeaderInfo { Major = 0xA0002, Minor = 0xA0028, Unk1 = 2, Unk2 = 2, SaveRevision = 31 };
mapNewProvider.Add("main", RevisionChecker.SizeInfo[31].Main, mapNewRevision);
mapNewProvider.Add("Villager0/personal", RevisionChecker.SizeInfo[31].Personal, mapNewRevision);
mapNewProvider.Add("Villager0/photo_studio_island", RevisionChecker.SizeInfo[31].PhotoStudioIsland, mapNewRevision);
mapNewProvider.Add("Villager0/postbox", RevisionChecker.SizeInfo[31].PostBox, mapNewRevision);
mapNewProvider.Add("Villager0/profile", RevisionChecker.SizeInfo[31].Profile, mapNewRevision);
var mapNewSave = new HorizonSave(mapNewProvider);
var mapNewSession = new WorkspaceSession(mapNewSave);
var mapNewData = mapNewSession.GetMapWorkspace(mapItemsTarget with { Acre = 9 });
Check(mapNewData.ItemWidth == 288 && mapNewData.MarkerCount == 9 * 6 * 256 && mapNewData.Cells[0].RelativeX == 0 && mapNewData.Cells[0].RelativeY == 0, "expanded map exposes nine item acres with correct origin and design capacity");
mapNewSession.EditMap(mapTarget with { Mode = "markers", Marker = 9 * 6 * 256 - 1 }, HousingValues(("Map.DesignValue", 0xF800)), "apply", true, true, true);
mapNewSession.Save();
Check(!new HorizonSave(mapNewProvider).GetInvalidHashes().Any(), "expanded map edit survives encrypted save/reload with valid hashes");
// Advanced map tools: flag layout, compatible raw files and atomic area edits.
var mapTools = new WorkspaceSession(save);
var mapToolsTarget = mapItemsTarget with { X = 3, Y = 5, Layer = 1 };
var mapToolsBefore = save.Main.Data.ToArray();
Check(save.Main.FieldItemFlag0Data.Length == 5376 && save.Main.FieldItemFlag1 + 5376 == save.Main.Offsets.LandMakingMap, "old flag layers occupy exactly the gap before terrain");
Check(mapNewSave.Main.FieldItemFlag0Data.Length == 6912 && mapNewSave.Main.FieldItemFlag1 + 6912 == mapNewSave.Main.Offsets.LandMakingMap, "expanded flag layers end exactly at terrain");
mapTools.SetMapActive(mapToolsTarget, true);
var mapFlagByte = save.Main.FieldItemFlag1 + (5 * 224 + 3) / 8;
var mapFlagExpected = mapToolsBefore.ToArray();
mapFlagExpected[mapFlagByte] |= 1 << 3;
Check(save.Main.Data.SequenceEqual(mapFlagExpected) && mapTools.MapActive(mapToolsTarget) == true, "activity flags use row-major bit addressing in the selected layer only");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapToolsBefore) && !mapTools.IsDirty, "activity undo restores all bytes and clean state");
ExpectInvalid(() => mapTools.SetMapActive(mapToolsTarget with { Acre = 0 }, true));
var mapManager = MapTileManagerUtil.FromSaveFile(save.Main);
mapManager.LayerItemFlag0.SetIsActive(223, 191, true);
Check(save.Main.Data.SequenceEqual(mapToolsBefore), "native activity edits stay detached until accepted");
mapManager.SetManager(save.Main);
Check((save.Main.Data[save.Main.FieldItemFlag0 + 5375] & 0x80) != 0 && save.Main.GetTerrainTiles()[mapTerrainIndex].Elevation == 0, "native manager persists last activity bit without touching terrain");
mapToolsBefore.CopyTo(save.Main.Data);
foreach (var mapResource in new[] { "items", "terrain", "buildings", "acres", "flags", "designs" })
{
    var mapRaw = mapTools.ExportMap(mapToolsTarget, mapResource, true);
    Check(mapRaw.Length == mapTools.MapFileSize(mapToolsTarget, mapResource, true), "raw map file has expected size: " + mapResource);
    mapTools.ImportMap(mapToolsTarget, mapResource, true, mapRaw);
    Check(save.Main.Data.SequenceEqual(mapToolsBefore) && !mapTools.IsDirty, "unchanged map import preserves bytes and history: " + mapResource);
    ExpectInvalid(() => mapTools.ImportMap(mapToolsTarget, mapResource, true, mapRaw[..^1]));
    Check(save.Main.Data.SequenceEqual(mapToolsBefore), "invalid map file rejects before modifying: " + mapResource);
}
var mapAcreTerrain = Enumerable.Range(0, 256).Select(i => new TerrainTile { Variation = (ushort)(100 + i), Elevation = (ushort)(i / 16) }).ToArray();
var mapAcreBytes = TerrainTile.SetArray(mapAcreTerrain);
mapTools.ImportMap(mapTarget, "terrain", false, mapAcreBytes);
Check(mapTools.ExportMap(mapTarget, "terrain", false).SequenceEqual(mapAcreBytes) && save.Main.GetTerrainTiles()[2 * 96 + 3].Variation == 150, "terrain acre files round-trip asymmetric row-major data with a 14-byte stride");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapToolsBefore), "terrain acre import undo restores every byte");
var mapAcreItemBytes = Item.SetArray(Enumerable.Range(0, 1024).Select(i => new Item(2500) { FreeParam = i }).ToArray());
mapTools.ImportMap(mapToolsTarget, "items", false, mapAcreItemBytes);
Check(mapTools.ExportMap(mapToolsTarget, "items", false).SequenceEqual(mapAcreItemBytes) && save.Main.GetFieldItemLayer1()[3 * 192 + 5].FreeParam == 163, "item acre files preserve existing row-major format and raw parameters");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapToolsBefore), "item acre import does not touch activity flags or the other layer");
ExpectInvalid(() => mapTools.ExportMap(mapToolsTarget with { Acre = 0 }, "terrain", false));
var mapOldBuildings = Enumerable.Range(0, 800).Select(i => (byte)(i % 251)).ToArray();
mapTools.ImportMap(mapToolsTarget, "buildings", true, mapOldBuildings);
Check(save.Main.Data.Slice(save.Main.Offsets.MainFieldStructure, 800).SequenceEqual(mapOldBuildings)
    && save.Main.Data.Slice(save.Main.Offsets.MainFieldStructure + 800, 120).SequenceEqual(mapToolsBefore.AsSpan(save.Main.Offsets.MainFieldStructure + 800, 120)), "40-building imports preserve all raw bytes and the final six buildings");
mapTools.Undo();
var mapExpandedTools = new WorkspaceSession(mapNewSave);
var mapOldItems = mapTools.ExportMap(mapToolsTarget, "items", true);
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(mapOldItems, 2500);
mapExpandedTools.ImportMap(mapToolsTarget, "items", true, mapOldItems);
var mapExpandedItems = mapExpandedTools.ExportMap(mapToolsTarget, "items", true);
Check(mapExpandedItems.AsSpan(6 * 1024 * 8, mapOldItems.Length).SequenceEqual(mapOldItems) && Item.GetArray(mapExpandedItems)[0].IsNone, "old item files expand with empty columns on both sides");
mapExpandedTools.Undo();
var mapOldFlags = new byte[5376];
FlagUtil.SetFlag(mapOldFlags, 191 * 224 + 223, true);
mapExpandedTools.ImportMap(mapToolsTarget, "flags", true, mapOldFlags);
var mapExpandedFlags = mapExpandedTools.ExportMap(mapToolsTarget, "flags", true);
Check(FlagUtil.GetFlag(mapExpandedFlags, 191 * 288 + 255) && !FlagUtil.GetFlag(mapExpandedFlags, 191 * 288 + 287), "flag conversion shifts each row by one acre rather than padding contiguous bytes");
mapTools.ImportMap(mapToolsTarget, "flags", true, mapExpandedFlags);
Check(mapTools.ExportMap(mapToolsTarget, "flags", true).SequenceEqual(mapOldFlags), "flag expansion and contraction round-trip center columns");
mapTools.Undo(); mapExpandedTools.Undo();
var mapOldDesignSlots = mapTools.ExportMap(mapToolsTarget, "designs", true);
mapOldDesignSlots[0] = 0x34; mapOldDesignSlots[1] = 0x12;
mapExpandedTools.ImportMap(mapToolsTarget, "designs", true, mapOldDesignSlots);
var mapExpandedDesignSlots = mapExpandedTools.ExportMap(mapToolsTarget, "designs", true);
Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(mapExpandedDesignSlots) == MainSave.MapDesignNone
    && mapExpandedDesignSlots.AsSpan(0xC00, 0x5400).SequenceEqual(mapOldDesignSlots), "old design tile files expand with the correct none sentinel");
mapTools.ImportMap(mapToolsTarget, "designs", true, mapExpandedDesignSlots);
Check(mapTools.ExportMap(mapToolsTarget, "designs", true).SequenceEqual(mapOldDesignSlots), "design tile contraction preserves the original center data");
mapTools.Undo(); mapExpandedTools.Undo();
mapTools.PaintMap(mapTarget with { X = 0, Y = 0 }, 2, false, HousingValues(("Map.Elevation", 3), ("Map.Variation", 9)));
Check(save.Main.GetTerrainTiles().Count(t => t.Elevation == 3 && t.Variation == 9) == 3, "circular brush clips at terrain boundary and applies complete draft fields");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapToolsBefore), "brush stroke is one byte-exact undo step");
mapTools.PaintMap(mapTarget, 1, true, HousingValues(("Map.UnitModel", TerrainUnitModel.Cliff5B), ("Map.Variation", 9)));
Check(save.Main.GetTerrainTiles()[mapTerrainIndex].Variation < 4, "brush randomization uses the supported cliff variation range");
var mapRandomAfter = save.Main.Data.ToArray();
mapTools.Undo(); mapTools.Redo(); Check(save.Main.Data.SequenceEqual(mapRandomAfter), "brush redo restores the same randomized result");
mapTools.Undo();
mapTools.PaintMap(mapTarget, 1, true, HousingValues(("Map.UnitModel", TerrainUnitModel.Base), ("Map.Variation", 9)));
Check(save.Main.GetTerrainTiles()[mapTerrainIndex].Variation == 9, "brush leaves unsupported terrain variants unchanged");
mapTools.Undo();
mapTools.BatchMap(mapTarget, "setRoad", false, false, HousingValues(("Map.UnitModelRoad", 1), ("Map.VariationRoad", 7), ("Map.Elevation", 4)));
Check(save.Main.GetTerrainTiles().Count(t => t.VariationRoad == 7) == 256 && save.Main.GetTerrainTiles().All(t => t.Elevation == 0), "path batch modifies only road fields in the selected acre");
mapTools.Undo();
mapTools.BatchMap(mapTarget, "setTerrain", true, true, HousingValues(("Map.Elevation", 2)));
Check(save.Main.GetTerrainTiles().Count(t => t.Elevation == 2) == 80 * 64, "interior terrain batch preserves the outer ring of acres");
mapTools.BatchMap(mapTarget, "zeroElevation", true, false, HousingValues());
Check(save.Main.GetTerrainTiles().All(t => t.Elevation == 0), "zero elevation applies across the full layer");
mapTools.Undo(); Check(save.Main.GetTerrainTiles().Count(t => t.Elevation == 2) == 80 * 64, "zero-elevation undo restores the previous terrain batch");
mapTools.Undo();
ExpectInvalid(() => mapTools.PaintMap(mapTarget, 9, false, HousingValues()));
ExpectInvalid(() => mapTools.BatchMap(mapTarget, "setTerrain", true, false, HousingValues(("Map.Elevation", 5), ("Map.Bad", 4))));
Check(save.Main.Data.SequenceEqual(mapToolsBefore), "invalid brush and terrain batches reject atomically");
// A tree rooted just across an acre boundary must not leave orphaned extensions.
var mapToolsItemLayer = new LayerFieldItem(save.Main.GetFieldItemLayer1(), 7, 6);
var mapCrossTree = new Item(60000);
mapToolsItemLayer.GetTile(31, 4).CopyFrom(mapCrossTree);
mapToolsItemLayer.SetExtensionTiles(mapCrossTree, 31, 4);
save.Main.SetFieldItemLayer1(mapToolsItemLayer.Tiles);
var mapCrossBefore = save.Main.Data.ToArray();
mapTools.BatchMap(mapToolsTarget with { Acre = 11 }, "removeTrees", false, false, HousingValues());
Check(save.Main.GetFieldItemLayer1()[31 * 192 + 4].IsNone && !save.Main.GetFieldItemLayer1().Any(t => t.IsExtension), "acre cleanup removes complete footprints rooted across the boundary");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapCrossBefore), "batch cleanup undo restores cross-acre roots and footprints");
ExpectInvalid(() => mapTools.BatchMap(mapToolsTarget, "invalid", true, false, HousingValues()));
mapTools.BatchMap(mapTarget with { Mode = "markers" }, "clearDesigns", true, false, HousingValues());
Check(save.Main.GetMapDesignTiles(7, 6).All(t => t == MainSave.MapDesignNone), "clear placed designs uses the correct empty value");
mapTools.Undo(); Check(save.Main.Data.SequenceEqual(mapCrossBefore), "clearing placed designs is one reversible command");
mapTools.SetMapActive(mapToolsTarget, true);
mapTools.Save();
var mapToolsReload = new HorizonSave(provider);
Check(FlagUtil.GetFlag(mapToolsReload.Main.FieldItemFlag1Data.Span, 5 * 224 + 3) && !mapToolsReload.GetInvalidHashes().Any(), "activity flags survive encrypted save/reload with valid hashes");
// Drag strokes and map object generation/parameter edits remain atomic and reversible.
var advancedSave = new HorizonSave(provider);
var advanced = new WorkspaceSession(advancedSave);
advancedSave.Main.SetFieldItemLayer0(Enumerable.Range(0, 7 * 6 * 1024).Select(_ => new Item(Item.NONE)).ToArray());
var advancedBefore = advancedSave.Main.Data.ToArray();
var advancedTerrainTarget = new WorkspaceSession.MapTarget("terrain", 10, 0, 0, 0, 0, 0);
advanced.PaintMapStroke(advancedTerrainTarget, 1, false, HousingValues(("Map.Variation", 77), ("Map.Elevation", 4)), [new(2, 3), new(3, 3), new(4, 3), new(4, 3)]);
Check(advancedSave.Main.GetTerrainTiles().Count(t => t.Variation == 77 && t.Elevation == 4) == 3, "drag stroke applies all unique centers in one command");
advanced.Undo(); Check(advancedSave.Main.Data.SequenceEqual(advancedBefore), "whole drag stroke is one byte-exact undo step");
ExpectInvalid(() => advanced.PaintMapStroke(advancedTerrainTarget, 1, false, HousingValues(("Map.Elevation", 4)), [new(2, 3), new(16, 0)]));
Check(advancedSave.Main.Data.SequenceEqual(advancedBefore), "invalid stroke endpoint cannot partially paint terrain");
var advancedItemTarget = advancedTerrainTarget with { Mode = "items" };
var advancedCurrent = new WorkspaceSession.RoomItem(2500, 0x20, 0, 17);
var advancedOptions = new WorkspaceSession.MapSpawnOptions("current", "horizontal", 3, 0, 65535);
var advancedCount = advanced.SpawnMapItems(advancedItemTarget, advancedOptions, advancedCurrent);
Check(advancedCount is { Requested: 3, Applied: 3 } && new[] { 0, 2, 4 }.All(x => advancedSave.Main.GetFieldItemLayer0()[x * 192].FreeParam == 17), "horizontal generation uses even spacing and preserves raw item fields");
advanced.Undo(); Check(advancedSave.Main.Data.SequenceEqual(advancedBefore), "generation uses one undo step and preserves activity flags");
var advancedLayer = new LayerFieldItem(advancedSave.Main.GetFieldItemLayer0(), 7, 6);
advancedLayer.GetTile(0, 0).CopyFrom(new Item(2500) { IsDropped = true, FreeParam = 99 });
advancedSave.Main.SetFieldItemLayer0(advancedLayer.Tiles);
var advancedCollisionBefore = advancedSave.Main.Data.ToArray();
advancedCount = advanced.SpawnMapItems(advancedItemTarget, advancedOptions with { Count = 2 }, advancedCurrent);
Check(advancedCount.Applied == 2 && advancedSave.Main.GetFieldItemLayer0()[0].FreeParam == 99 && advancedSave.Main.GetFieldItemLayer0()[2 * 192].FreeParam == 17, "generation skips occupied cells without overwriting them");
advanced.Undo(); Check(advancedSave.Main.Data.SequenceEqual(advancedCollisionBefore), "generation undo preserves the pre-existing collision object");
advancedCount = advanced.SpawnMapItems(advancedItemTarget with { Acre = 61, X = 30, Y = 30 }, advancedOptions with { Count = 5, Arrangement = "vertical" }, advancedCurrent);
Check(advancedCount is { Requested: 5, Applied: 1 }, "edge generation terminates and reports partial placement accurately");
advanced.Undo();
advancedCount = advanced.SpawnMapItems(advancedItemTarget with { X = 3, Y = 5 }, advancedOptions with { Count = 1 }, new(60000, 0, 0, 0));
Check(advancedCount.Applied == 1 && advancedSave.Main.GetFieldItemLayer0()[2 * 192 + 4].ItemId == 60000, "field generation snaps roots to whole tiles and builds footprints");
advanced.Undo();
var advancedNhi = Item.SetArray(new Item[] { new(Item.NONE), new(2500) { AdditionalParam = 0xAB, FreeParam = int.MinValue }, new(Item.EXTENSION) });
advancedCount = advanced.SpawnMapItems(advancedItemTarget with { X = 4, Y = 4 }, advancedOptions with { Source = "nhi", Count = 2 }, advancedCurrent, advancedNhi);
Check(advancedCount is { Requested: 2, Applied: 2 } && advancedSave.Main.GetFieldItemLayer0()[4 * 192 + 4] is { SystemParam: 0x20, AdditionalParam: 0xAB, FreeParam: int.MinValue }, "NHI generation ignores empty and extension records, drops roots and preserves their parameters");
advanced.Undo();
ExpectInvalid(() => advanced.SpawnMapItems(advancedItemTarget, advancedOptions with { Source = "nhi" }, advancedCurrent, new byte[9]));
ExpectInvalid(() => advanced.SpawnMapItems(advancedItemTarget, advancedOptions with { Source = "recipes", Count = 1000 }, advancedCurrent));
Check(advancedSave.Main.Data.SequenceEqual(advancedCollisionBefore), "malformed and oversized generation requests leave all bytes unchanged");
advancedCount = advanced.SpawnMapItems(advancedItemTarget with { X = 8, Y = 8 }, advancedOptions with { Source = "recipes", Count = 1, Arrangement = "square" }, advancedCurrent);
var advancedRecipes = advancedSave.Main.GetFieldItemLayer0().Where(i => i.ItemId == Item.DIYRecipe).ToArray();
Check(advancedCount.Applied == advancedCount.Requested && advancedRecipes.Length == advancedCount.Requested && advancedRecipes.All(i => i.FreeParam is not (RecipeList.BridgeConstructionKit or RecipeList.CampsiteConstructionKit)), "recipe generation places the selected sequence without construction kits");
advanced.Undo();
var advancedAlphabetical = RecipeList.Recipes.Keys.Where(k => k <= 30 && k is not (RecipeList.BridgeConstructionKit or RecipeList.CampsiteConstructionKit))
    .Select(k => new Item(Item.DIYRecipe) { FreeParam = k }).OrderBy(i => GameInfo.Strings.GetItemName(i), StringComparer.CurrentCulture).ThenBy(i => i.FreeParam).ToArray();
advancedCount = advanced.SpawnMapItems(advancedItemTarget with { X = 8, Y = 8 }, advancedOptions with { Source = "alphabetical", Count = 1, RecipeMax = 30 }, advancedCurrent);
Check(advancedCount.Applied == advancedAlphabetical.Length && advancedAlphabetical.Select((item, i) => advancedSave.Main.GetFieldItemLayer0()[(8 + 2 * i) * 192 + 8].FreeParam == item.FreeParam).All(v => v), "alphabetical recipes use localized names and stable ID tie-breaking");
advanced.Undo();
mapNewSave.Main.SetFieldItemLayer0(Enumerable.Range(0, 9 * 6 * 1024).Select(_ => new Item(Item.NONE)).ToArray());
var advancedExpanded = new WorkspaceSession(mapNewSave);
var advancedExpandedBefore = mapNewSave.Main.Data.ToArray();
advancedExpanded.SpawnMapItems(advancedItemTarget with { Acre = 9 }, advancedOptions with { Count = 1 }, advancedCurrent);
Check(mapNewSave.Main.GetFieldItemLayer0()[0].ItemId == 2500, "expanded map generation uses the editable outer item acre origin");
advancedExpanded.Undo(); Check(mapNewSave.Main.Data.SequenceEqual(advancedExpandedBefore), "expanded map generation undo is byte exact");
var advancedProperties = WorkspaceSession.MapParameters();
Check(advancedProperties.Single(p => p.Key == "RawValue").Max == "18446744073709551615" && !advancedProperties.Single(p => p.Key == "IsRoot").Writable && !advancedProperties.Single(p => p.Key == "ExtensionX").Writable, "batch schema preserves 64-bit bounds and guards extension metadata");
advancedLayer = new LayerFieldItem(advancedSave.Main.GetFieldItemLayer0(), 7, 6);
advancedLayer.GetTile(0, 0).CopyFrom(advancedCurrent.ToItem());
advancedLayer.GetTile(4, 0).CopyFrom(advancedCurrent.ToItem());
advancedLayer.GetTile(8, 0).CopyFrom(new Item(2500) { IsDropped = true, FreeParam = 18 });
advancedSave.Main.SetFieldItemLayer0(advancedLayer.Tiles);
var advancedRuleBefore = advancedSave.Main.Data.ToArray();
advancedCount = advanced.EditMapParameters(advancedItemTarget, false, [new("ItemId", "eq", "2500"), new("FreeParam", "gt", "17")], [new("FreeParam", "set", "-2147483648")]);
Check(advancedCount is { Requested: 1, Applied: 1 } && advancedSave.Main.GetFieldItemLayer0()[8 * 192].FreeParam == int.MinValue && advancedSave.Main.GetFieldItemLayer0()[0].FreeParam == 17, "structured conditions combine numeric filters and preserve signed values");
advanced.Undo(); Check(advancedSave.Main.Data.SequenceEqual(advancedRuleBefore), "parameter undo restores raw values, footprints and flags exactly");
var advancedRaw = 0xFEDCBA98002009C4UL;
advancedCount = advanced.EditMapParameters(advancedItemTarget, false, [new("FreeParam", "eq", "18")], [new("RawValue", "set", advancedRaw.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
Check(advancedSave.Main.GetFieldItemLayer0()[8 * 192].RawValue == advancedRaw, "64-bit parameter values remain exact above the JavaScript safe integer range");
advanced.Undo();
ExpectInvalid(() => advanced.EditMapParameters(advancedItemTarget, false, [], [new("FreeParam", "set", "5"), new("Bad", "set", "1")]));
ExpectInvalid(() => advanced.EditMapParameters(advancedItemTarget, false, [], [new("Rotation", "set", "4")]));
ExpectInvalid(() => advanced.EditMapParameters(advancedItemTarget, false, [], [new("IsRoot", "set", "true")]));
Check(advancedSave.Main.Data.SequenceEqual(advancedRuleBefore), "invalid batch properties and ranges reject before any modifications");
advancedCount = advanced.EditMapParameters(advancedItemTarget, false, [new("FreeParam", "eq", "17")], [new("FreeParam", "set", "17")]);
Check(advancedCount is { Requested: 2, Applied: 0 } && !advanced.IsDirty && advancedSave.Main.Data.SequenceEqual(advancedRuleBefore), "unchanged parameter batches report matches without creating history");
advancedLayer = new LayerFieldItem(advancedSave.Main.GetFieldItemLayer0(), 7, 6);
advancedLayer.GetTile(1, 0).SetAsExtension(new Item(60000), 1, 0);
advancedSave.Main.SetFieldItemLayer0(advancedLayer.Tiles);
var advancedBadExtensionBefore = advancedSave.Main.Data.ToArray();
ExpectInvalid(() => advanced.ReplaceMapMatches(advancedItemTarget with { X = 1 }, false, advancedCurrent, false));
Check(advancedSave.Main.Data.SequenceEqual(advancedBadExtensionBefore), "mismatched extension back-references cannot select an unrelated root");
advancedRuleBefore.CopyTo(advancedSave.Main.Data);
advancedCount = advanced.ReplaceMapMatches(advancedItemTarget, false, new(60000, 0, 0, 0), false);
Check(advancedCount is { Requested: 2, Applied: 2 } && advancedSave.Main.GetFieldItemLayer0()[8 * 192].FreeParam == 18 && advancedSave.Main.GetFieldItemLayer0()[192].IsExtension, "replacement matches all raw parameters and regenerates footprints");
var advancedReplaced = advancedSave.Main.Data.ToArray();
advancedCount = advanced.ReplaceMapMatches(advancedItemTarget with { X = 1 }, false, advancedCurrent, true);
Check(advancedCount.Applied == 2 && !advancedSave.Main.GetFieldItemLayer0().Any(i => i.IsExtension), "selected extension redirects to its root for matching removal");
advanced.Undo(); Check(advancedSave.Main.Data.SequenceEqual(advancedReplaced), "matching removal restores every footprint with one undo");
advanced.Undo();
advancedLayer = new LayerFieldItem(advancedSave.Main.GetFieldItemLayer0(), 7, 6);
advancedLayer.GetTile(1, 0).CopyFrom(new Item(2500) { IsDropped = true, FreeParam = 99 });
advancedSave.Main.SetFieldItemLayer0(advancedLayer.Tiles);
var advancedBlockedBefore = advancedSave.Main.Data.ToArray();
ExpectInvalid(() => advanced.ReplaceMapMatches(advancedItemTarget, false, new(60000, 0, 0, 0), false));
ExpectInvalid(() => advanced.EditMapParameters(advancedItemTarget, false, [new("ItemId", "eq", "2500")], [new("ItemId", "set", "60000"), new("SystemParam", "set", "0")]));
Check(advancedSave.Main.Data.SequenceEqual(advancedBlockedBefore), "replacement and parameter collisions reject the entire batch atomically");
advanced.SpawnMapItems(advancedItemTarget with { X = 16, Y = 16 }, advancedOptions, advancedCurrent);
advanced.Save();
var advancedReload = new HorizonSave(provider);
Check(advancedReload.Main.GetFieldItemLayer0()[16 * 192 + 16].ItemId == 2500 && !advancedReload.GetInvalidHashes().Any(), "advanced map edits survive encrypted save/reload with valid hashes");
// Shared editor clipboard and multi-cell edits use one reversible transaction.
var gridSave = new HorizonSave(provider);
var gridMapLayer = new LayerFieldItem(Enumerable.Range(0, 7 * 6 * 1024).Select(_ => new Item(Item.NONE)).ToArray(), 7, 6);
var gridRawItem = new Item(2500) { SystemParam = 0x20, AdditionalParam = 0xAB, FreeParam = int.MinValue + 97 };
gridMapLayer.GetTile(2, 2).CopyFrom(gridRawItem); gridMapLayer.SetExtensionTiles(gridRawItem, 2, 2);
var gridSecondItem = new Item(2500) { SystemParam = 0x20, FreeParam = 12345 };
gridMapLayer.GetTile(6, 2).CopyFrom(gridSecondItem); gridMapLayer.SetExtensionTiles(gridSecondItem, 6, 2);
gridSave.Main.SetFieldItemLayer0(gridMapLayer.Tiles);
var gridHouse = gridSave.Main.GetPlayerHouse(0); var gridRoom = gridHouse.GetRoom(0); var gridRoomLayers = gridRoom.GetItemLayers();
foreach (var layer in gridRoomLayers) foreach (var tile in layer.Tiles) tile.Delete();
gridRoom.SetItemLayers(gridRoomLayers); gridHouse.SetRoom(0, gridRoom); gridSave.Main.SetPlayerHouse(gridHouse, 0);
var grid = new WorkspaceSession(gridSave);
var gridMap = new WorkspaceSession.GridTarget("mapItems", advancedItemTarget);
var gridRoomTarget = new WorkspaceSession.GridTarget("roomItems", House: 0, Room: 0, Layer: 7);
var gridInitial = gridSave.Main.Data.ToArray();
grid.CopyGrid(gridMap, [new(2, 2), new(3, 2), new(6, 2)]);
Check(!grid.IsDirty && gridSave.Main.Data.SequenceEqual(gridInitial), "copying a multi-cell selection never changes save bytes or undo history");
grid.PasteGrid(gridMap, new(10, 10), [new(10, 10)], true);
Check(gridSave.Main.GetFieldItemLayer0()[10 * 192 + 10].RawValue == gridRawItem.RawValue && gridSave.Main.GetFieldItemLayer0()[14 * 192 + 10].RawValue == gridSecondItem.RawValue, "multi-object paste preserves exact raw parameters and relative layout");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridInitial), "multi-object paste undoes in one byte-exact step");
grid.SetRoomItem(0, 0, 7, 2, 0, new(2500, 0x20, 0, 777), true, true, false);
var gridGapBefore = gridSave.Main.Data.ToArray();
grid.PasteGrid(gridRoomTarget, new(0, 0), [new(0, 0)], true);
var gridPastedRoom = gridSave.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[7];
Check(gridPastedRoom.GetTile(0, 0).RawValue == gridRawItem.RawValue && gridPastedRoom.GetTile(4, 0).RawValue == gridSecondItem.RawValue && gridPastedRoom.GetTile(2, 0).FreeParam == 777, "map-to-room paste keeps whole objects and does not erase empty gaps");
grid.CopyGrid(gridRoomTarget, [new(0, 0), new(4, 0)]);
grid.PasteGrid(gridMap with { Map = advancedItemTarget with { Layer = 1 } }, new(16, 16), [new(16, 16)], true);
Check(gridSave.Main.GetFieldItemLayer1()[16 * 192 + 16].RawValue == gridRawItem.RawValue, "room clipboard can paste back to the other map item layer");
grid.Undo(); grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridGapBefore), "cross-grid paste undo restores unrelated layers and gap objects");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridInitial), "room clipboard survives undo without changing save data");
grid.CopyGrid(gridMap, [new(3, 2)]);
var gridFillSelection = Enumerable.Range(0, 16).Select(i => new WorkspaceSession.MapPoint(i % 4, i / 4)).ToArray();
grid.PasteGrid(gridRoomTarget, new(0, 0), gridFillSelection, true);
Check(gridSave.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[7].Tiles.Count(i => i.IsRoot) == 4, "single-object paste packs a multi-cell selection by complete footprints");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridInitial), "filled selection has a single undo step");
grid.EditGrid(gridMap, [new(2, 2), new(3, 2), new(6, 2)], "delete");
Check(!gridSave.Main.GetFieldItemLayer0().Any(i => !i.IsNone), "selection deletion deduplicates roots and removes all owned extensions");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridInitial), "selection deletion undo restores every raw byte");
grid.EditGrid(gridMap, [new(2, 2), new(3, 2)], "rotate");
Check(gridSave.Main.GetFieldItemLayer0()[2 * 192 + 2].Rotation == 1 && gridSave.Main.GetFieldItemLayer0()[2 * 192 + 2].FreeParam == gridRawItem.FreeParam, "root and extension selection rotates a multi-cell object once");
grid.Undo();
ExpectInvalid(() => grid.PasteGrid(gridRoomTarget, new(19, 19), [new(19, 19)], true));
ExpectInvalid(() => grid.PasteGrid(gridMap with { Map = advancedItemTarget with { Acre = 61 } }, new(31, 31), [new(31, 31)], true));
Check(gridSave.Main.Data.SequenceEqual(gridInitial), "paste boundary failures cannot partially write a room or map");
grid.SetRoomItem(0, 0, 7, 5, 4, new(2500, 0x20, 0, 77), true, true, false);
var gridBlocked = gridSave.Main.Data.ToArray();
ExpectInvalid(() => grid.PasteGrid(gridRoomTarget, new(4, 4), [new(4, 4)], true));
Check(gridSave.Main.Data.SequenceEqual(gridBlocked), "paste collision rejection preserves the entire destination room");
grid.PasteGrid(gridRoomTarget, new(4, 4), [new(4, 4)], false);
Check(gridSave.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[7].GetTile(6, 4).IsNone, "explicit overlap replacement clears the displaced object's entire footprint");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridBlocked), "overlap replacement remains fully reversible"); grid.Undo();
grid.EditMap(advancedItemTarget with { X = 20, Y = 2 }, HousingValues(("Map.ItemId", 60000), ("Map.SystemParam", 0), ("Map.AdditionalParam", 0), ("Map.FreeParam", 0)), "apply", true, true, true);
grid.CopyGrid(gridMap, [new(20, 2)]);
var gridFieldBefore = gridSave.Main.Data.ToArray();
ExpectInvalid(() => grid.PasteGrid(gridRoomTarget, new(0, 0), [new(0, 0)], true));
Check(gridSave.Main.Data.SequenceEqual(gridFieldBefore), "field-object clipboard cannot be pasted into a house"); grid.Undo();
var gridTerrainTarget = new WorkspaceSession.GridTarget("terrain", advancedTerrainTarget);
var gridTerrainTiles = gridSave.Main.GetTerrainTiles();
gridTerrainTiles[0].UnitModel = TerrainUnitModel.Cliff5B; gridTerrainTiles[0].Variation = 123; gridTerrainTiles[0].Elevation = 2;
gridTerrainTiles[96].UnitModelRoad = TerrainUnitModel.River5B; gridTerrainTiles[96].LandMakingAngleRoad = 1; gridTerrainTiles[96].Elevation = 3;
gridSave.Main.SetTerrainTiles(gridTerrainTiles);
var gridTerrainBefore = gridSave.Main.Data.ToArray();
grid.CopyGrid(gridTerrainTarget, [new(0, 0), new(1, 0)]);
grid.PasteGrid(gridTerrainTarget, new(4, 5), [new(4, 5)], true);
Check(TerrainTile.SetArray([gridSave.Main.GetTerrainTiles()[4 * 96 + 5], gridSave.Main.GetTerrainTiles()[5 * 96 + 5]]).SequenceEqual(TerrainTile.SetArray([gridTerrainTiles[0], gridTerrainTiles[96]])), "terrain clipboard preserves all fourteen bytes per tile and rectangular layout");
grid.Undo(); Check(gridSave.Main.Data.SequenceEqual(gridTerrainBefore), "terrain paste has one exact undo step");
ExpectInvalid(() => grid.PasteGrid(gridMap, new(0, 0), [new(0, 0)], true));
ExpectInvalid(() => grid.CopyGrid(gridTerrainTarget, [new(16, 0)]));
ExpectInvalid(() => grid.PasteGrid(gridTerrainTarget with { Map = advancedTerrainTarget with { Acre = 61 } }, new(15, 15), [new(15, 15)], true));
Check(gridSave.Main.Data.SequenceEqual(gridTerrainBefore), "type mismatch and invalid clipboard coordinates reject atomically");
grid.EditGrid(gridTerrainTarget, [new(0, 0), new(1, 0)], "rotate");
Check(gridSave.Main.GetTerrainTiles()[0].LandMakingAngle == 1 && gridSave.Main.GetTerrainTiles()[96].LandMakingAngleRoad == 2, "selection rotation follows terrain and road native rotation rules"); grid.Undo();
grid.EditGrid(gridTerrainTarget, [new(0, 0), new(1, 0)], "delete");
Check(gridSave.Main.GetTerrainTiles()[0].UnitModel == 0 && gridSave.Main.GetTerrainTiles()[96].Elevation == 0, "terrain selection clear resets the complete tile record"); grid.Undo();
grid.CopyGrid(gridMap, [new(2, 2)]);
grid.PasteGrid(gridRoomTarget, new(8, 8), [new(8, 8)], true);
var recycleGridTarget = new WorkspaceSession.GridTarget("inventory", Player: 1, Container: "recycle", Slots: [38, 39], Columns: 2);
var recycleOtherPlayer = gridSave.Players[1].Personal.Data.ToArray();
grid.PasteGrid(recycleGridTarget, new(1, 0), [new(1, 0)], true);
Check(gridSave.Main.RecycleBin[39].RawValue == gridRawItem.RawValue && gridSave.Players[1].Personal.Data.SequenceEqual(recycleOtherPlayer), "map clipboard pastes into shared recycle storage without changing player data");
grid.CopyGrid(recycleGridTarget, [new(1, 0)]);
grid.PasteGrid(gridRoomTarget, new(10, 10), [new(10, 10)], true);
Check(gridSave.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[7].GetTile(10, 10).RawValue == gridRawItem.RawValue, "inventory clipboard also pastes into rooms");
var savedHouseFile = grid.ExportHousing("playerHouses", 0, 0, false); savedHouseFile[0] ^= 1;
grid.ImportHousing("playerHouses", 0, 0, false, savedHouseFile);
var savedResidentHouseFile = grid.ExportHousing("residentHouses", 0, 0, false); savedResidentHouseFile[0] ^= 1;
grid.ImportHousing("residentHouses", 0, 0, false, savedResidentHouseFile);
var savedRoomFile = grid.ExportHousing("playerHouses", 0, 5, true); savedRoomFile[^1] ^= 0x5A;
grid.ImportHousing("playerHouses", 0, 5, true, savedRoomFile);
var savedHouseAfterRoom = grid.ExportHousing("playerHouses", 0, 0, false);
grid.Save();
var gridReload = new HorizonSave(provider);
var housingFilesReload = new WorkspaceSession(gridReload);
Check(housingFilesReload.ExportHousing("playerHouses", 0, 0, false).SequenceEqual(savedHouseAfterRoom)
    && housingFilesReload.ExportHousing("playerHouses", 0, 5, true).SequenceEqual(savedRoomFile)
    && housingFilesReload.ExportHousing("residentHouses", 0, 0, false).SequenceEqual(savedResidentHouseFile), "house and room file imports survive encrypted save/reload byte-for-byte");
Check(gridReload.Main.RecycleBin[39].RawValue == gridRawItem.RawValue, "inventory grid edits survive encrypted save/reload");
Check(gridReload.Main.GetPlayerHouse(0).GetRoom(0).GetItemLayers()[7].GetTile(8, 8).RawValue == gridRawItem.RawValue && !gridReload.GetInvalidHashes().Any(), "clipboard edits survive encrypted save/reload with valid hashes");
// Museum records are island-wide and retain raw dates, item parameters and donor bytes.
var museumSave = new HorizonSave(provider);
var museumFixture = new Museum(new byte[Museum.SIZE]) { MuseumLevel = int.MinValue };
museumFixture.SetItems(Enumerable.Range(0, Museum.EntryCount).Select(_ => new Item(Item.NONE)).ToArray());
var museumDates = museumFixture.GetDates();
museumDates[7] = new GSaveDate { Year = ushort.MaxValue, Month = 255, Day = 255 }; museumFixture.SetDates(museumDates);
museumSave.Main.Museum = museumFixture;
var museumSession = new WorkspaceSession(museumSave);
var museumBefore = museumSave.Main.Data.ToArray();
var museumPersonalBefore = museumSave.Players[0].Personal.Data.ToArray();
var museumOtherBefore = museumSave.Players[1].Personal.Data.ToArray();
var museumData = museumSession.GetMuseum();
Check(museumData.Entries.Length == 1024 && museumData.Level == int.MinValue && museumData.Entries[7].Date == "65535-255-255", "museum exposes every record, signed level and unusual raw dates without normalization");
museumSession.SetMuseumEntry(7, museumData.Entries[7].Date, 0, museumData.Entries[7].Item);
museumSession.ImportMuseum(museumSession.ExportMuseum());
Check(!museumSession.IsDirty && museumSave.Main.Data.SequenceEqual(museumBefore), "unchanged museum forms and files preserve bytes and clean history");
museumSession.SetMuseumEntry(1023, "2024-02-29", 255, WorkspaceSession.RoomItem.From(item));
var museumEdited = museumSession.ExportMuseum();
Check(museumSave.Main.Museum.GetItems()[1023].RawValue == item.RawValue && museumSave.Main.Museum.GetPlayers()[1023] == 255
    && museumSession.GetMuseum().Entries[1023].Date == "2024-02-29", "museum final record retains all item parameters, leap date and full donor range");
museumSession.Undo(); Check(museumSave.Main.Data.SequenceEqual(museumBefore), "museum entry undo restores every main byte");
museumSession.Redo(); Check(museumSession.ExportMuseum().SequenceEqual(museumEdited), "museum entry redo restores the exact record");
ExpectInvalid(() => museumSession.SetMuseumEntry(1024, "", 0, WorkspaceSession.RoomItem.From(item)));
ExpectInvalid(() => museumSession.SetMuseumEntry(0, "2025-02-29", 0, WorkspaceSession.RoomItem.From(item)));
ExpectInvalid(() => museumSession.SetMuseumEntry(0, "0001-01-01", 0, WorkspaceSession.RoomItem.From(item)));
ExpectInvalid(() => museumSession.SetMuseumEntry(0, "", 256, WorkspaceSession.RoomItem.From(item)));
ExpectInvalid(() => museumSession.SetMuseumEntry(0, "", -1, WorkspaceSession.RoomItem.From(item)));
ExpectInvalid(() => museumSession.SetMuseumEntry(0, "", 0, new(65536, 0, 0, 0)));
ExpectInvalid(() => museumSession.ImportMuseum(new byte[Museum.SIZE + 1]));
ExpectInvalid(() => museumSession.MuseumBatch("sort"));
Check(museumSession.ExportMuseum().SequenceEqual(museumEdited), "invalid museum indexes, dates, fields, files and batches reject atomically");
museumSession.SetMuseumEntry(7, "", 0, new(Item.NONE, 0, 0, 0));
museumSession.SetMuseumLevel(int.MaxValue);
Check(museumSession.GetMuseum().Level == int.MaxValue, "museum level supports full signed integer range"); museumSession.Undo();
museumSession.SetMuseumEntry(0, "2026-03-03", 2, WorkspaceSession.RoomItem.From(item));
museumSession.SetMuseumEntry(1, "2026-01-01", 3, WorkspaceSession.RoomItem.From(nextFurniture));
var museumUnsorted = museumSession.ExportMuseum();
museumSession.MuseumBatch("sort");
var museumSorted = museumSave.Main.Museum;
Check(museumSorted.GetPlayers()[0] == 255 && museumSorted.GetItems()[0].RawValue == item.RawValue
    && museumSorted.GetPlayers()[1] == 3 && museumSorted.GetItems()[1].RawValue == nextFurniture.RawValue
    && museumSorted.GetDates()[1023].IsEmpty, "museum sort keeps items dates and donors together and moves empty dates last");
museumSession.Undo(); Check(museumSession.ExportMuseum().SequenceEqual(museumUnsorted), "museum sort is a single byte-exact undo step");
museumSession.MuseumBatch("giveAll");
var completedMuseum = museumSession.ExportMuseum();
var donationItems = museumSave.Main.Museum.GetItems();
var donationIds = donationItems.Where(i => !i.IsNone).Select(i => i.ItemId).ToHashSet();
Check(GameLists.Fish.ToArray().All(donationIds.Contains) && GameLists.Bugs.ToArray().All(donationIds.Contains)
    && GameLists.Fossils.ToArray().All(donationIds.Contains) && GameLists.Dive.ToArray().All(donationIds.Contains)
    && GameLists.Art.ToArray().Where(id => !GameInfo.GetStrings("en").itemlist[id].Contains('(')).All(donationIds.Contains), "museum completion includes every native species and genuine art");
Check(donationItems[0].RawValue == item.RawValue && museumSave.Main.Museum.GetPlayers()[0] == 2, "museum completion preserves existing donation bytes and donors");
museumSession.Undo(); Check(museumSession.ExportMuseum().SequenceEqual(museumUnsorted), "museum completion is one byte-exact undo step");
museumSession.Redo(); Check(museumSession.ExportMuseum().SequenceEqual(completedMuseum), "museum completion redo retains the original randomized order");
museumSession.MuseumBatch("giveAll");
museumSession.Undo(); Check(museumSession.ExportMuseum().SequenceEqual(museumUnsorted), "already complete museum adds no undo step");
var fullMuseum = new Museum(new byte[Museum.SIZE]);
fullMuseum.SetItems(Enumerable.Range(0, Museum.EntryCount).Select(_ => new Item(0)).ToArray());
museumSession.ImportMuseum(fullMuseum.Data.ToArray()); var fullBefore = museumSession.ExportMuseum();
ExpectInvalid(() => museumSession.MuseumBatch("giveAll"));
Check(museumSession.ExportMuseum().SequenceEqual(fullBefore), "full museum completion refuses without partial additions"); museumSession.Undo();
var maxDateMuseum = new Museum(museumSession.ExportMuseum()); var maxDates = maxDateMuseum.GetDates();
maxDates[0] = DateTime.MaxValue; maxDateMuseum.SetDates(maxDates); museumSession.ImportMuseum(maxDateMuseum.Data.ToArray());
var maxDateBefore = museumSession.ExportMuseum(); ExpectInvalid(() => museumSession.MuseumBatch("giveAll"));
Check(museumSession.ExportMuseum().SequenceEqual(maxDateBefore), "museum next-date overflow rejects atomically"); museumSession.Undo();
var importedMuseumFile = completedMuseum.ToArray(); museumSession.ImportMuseum(importedMuseumFile); importedMuseumFile[0] ^= 0xFF;
Check(museumSession.ExportMuseum().SequenceEqual(completedMuseum), "museum import isolates caller buffers");
Check(museumSave.Players[0].Personal.Data.SequenceEqual(museumPersonalBefore) && museumSave.Players[1].Personal.Data.SequenceEqual(museumOtherBefore), "island museum edits never change personal files");
museumSession.Save(); var museumReload = new HorizonSave(provider);
Check(new WorkspaceSession(museumReload).ExportMuseum().SequenceEqual(completedMuseum) && !museumReload.GetInvalidHashes().Any(), "museum edits survive encrypted save/reload with valid hashes");
CompletionChecks.Run(provider, originalSave);
// Catalogue/profile UI adapters use existing personal accessors and preserve unrelated bytes.
var catalogueSave = new HorizonSave(provider);
var catalogue = new WorkspaceSession(catalogueSave);
var cataloguePlayer = catalogueSave.Players[0].Personal;
var catalogueOther = catalogueSave.Players[1].Personal.Data.ToArray();
var catalogueMain = catalogueSave.Main.Data.ToArray();
var catalogueBefore = cataloguePlayer.Data.ToArray();
var collectionRows = System.Text.Json.JsonSerializer.SerializeToElement(catalogue.GetProgress(0, "collection")).GetProperty("entries");
var variantRows = System.Text.Json.JsonSerializer.SerializeToElement(catalogue.GetProgress(0, "remakes")).GetProperty("entries");
Check(collectionRows.GetArrayLength() == GameInfo.Strings.itemlistdisplay.Length && variantRows.GetArrayLength() == cataloguePlayer.Offsets.MaxRemakeBitFlag,
    "catalogue exposes full item and remake capacities without mutating either file");
Check(cataloguePlayer.Data.SequenceEqual(catalogueBefore) && !catalogue.IsDirty, "catalogue query is read only");
var lastCollection = collectionRows.GetArrayLength() - 1;
var lastVariant = variantRows.GetArrayLength() - 1;
var collectOffset = cataloguePlayer.Offsets.ItemCollectBit;
var variantOffset = cataloguePlayer.Offsets.ItemRemakeCollectBit;
var oldCollected = FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, lastCollection);
catalogue.SetCollection(0, "collection", lastCollection, !oldCollected);
Check(FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, lastCollection) == !oldCollected, "individual catalogue edits reach the final flag");
catalogue.Undo(); Check(cataloguePlayer.Data.SequenceEqual(catalogueBefore), "individual catalogue undo is byte exact");
var oldVariant = FlagUtil.GetFlag(cataloguePlayer.Data, variantOffset, lastVariant);
catalogue.SetCollection(0, "remakes", lastVariant, !oldVariant);
Check(FlagUtil.GetFlag(cataloguePlayer.Data, variantOffset, lastVariant) == !oldVariant, "individual remake editing reaches even unmapped raw flags");
catalogue.Undo();
catalogue.SetCollection(0, "collection", lastCollection, oldCollected);
Check(!catalogue.IsDirty, "unchanged catalogue flags create no history");
ExpectInvalid(() => catalogue.SetCollection(0, "collection", lastCollection + 1, true));
ExpectInvalid(() => catalogue.SetCollection(0, "remakes", -1, true));
ExpectInvalid(() => catalogue.SetCollection(0, "bad", 0, true));
ExpectInvalid(() => catalogue.CollectionBatch(0, "bad", true, false));
Check(cataloguePlayer.Data.SequenceEqual(catalogueBefore), "invalid catalogue targets reject before writes");
var sampleRemake = ItemRemakeInfoData.List.Values.First(v => v.Index >= 0 && v.HasBodyColor(0) && v.ItemUniqueID != 0);
var sampleIndex = sampleRemake.Index * ItemRemakeInfo.BodyColorCountMax;
catalogue.CollectionBatch(0, "everything", true, true);
Check(cataloguePlayer.Data.Slice(collectOffset, GameInfo.Strings.itemlistdisplay.Length / 8).SequenceEqual(catalogueBefore.AsSpan(collectOffset, GameInfo.Strings.itemlistdisplay.Length / 8))
    && FlagUtil.GetFlag(cataloguePlayer.Data, variantOffset, sampleIndex), "variants-only bulk marking leaves item collection flags unchanged");
catalogue.Undo(); Check(cataloguePlayer.Data.SequenceEqual(catalogueBefore), "all valid variants undo as one operation");
catalogue.CollectionBatch(0, "everything", false, false);
var cleanCatalogue = cataloguePlayer.Data.ToArray();
catalogue.CollectionBatch(0, "furniture", true, false);
Check(!FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, GameLists.Bugs[0])
    && !FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, GameLists.Fish[0])
    && !FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, GameLists.Art[0])
    && !FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, GameLists.Dive[0]), "furniture category excludes all four creature/art groups");
catalogue.Undo(); Check(cataloguePlayer.Data.SequenceEqual(cleanCatalogue), "furniture category is a single reversible batch");
catalogue.CollectionBatch(0, "bugs", true, false);
Check(GameLists.Bugs.ToArray().All(id => FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, id))
    && !FlagUtil.GetFlag(cataloguePlayer.Data, collectOffset, GameLists.Fish[0]), "creature category marks only its own group");
catalogue.Undo(); catalogue.Undo();
Check(cataloguePlayer.Data.SequenceEqual(catalogueBefore), "catalogue history restores all original bytes");
// Do not normalize untouched odd dates, birthday copies or raw boolean encodings.
cataloguePlayer.Birthday = new GSaveDateMD { Month = 0, Day = 255 };
cataloguePlayer.ProfileBirthday = new GSaveDateMD { Month = 4, Day = 9 };
cataloguePlayer.ProfileTimestamp = new GSaveDate { Year = 65535, Month = 255, Day = 255 };
cataloguePlayer.Data[cataloguePlayer.Offsets.ProfileIsMakeVillage] = 2;
var miscBefore = cataloguePlayer.Data.ToArray();
var newFruit = GameLists.Fruits.ToArray().First(id => id != cataloguePlayer.ProfileFruit);
catalogue.SetMiscProfile(0, 0, 255, true, "65535-255-255", newFruit);
Check(cataloguePlayer.Birthday.ToString() == "00-255" && cataloguePlayer.ProfileBirthday.ToString() == "04-09"
    && cataloguePlayer.ProfileTimestamp.ToString() == "65535-255-255" && cataloguePlayer.Data[cataloguePlayer.Offsets.ProfileIsMakeVillage] == 2,
    "unrelated profile edits preserve unusual date bytes, separate birthday copy and raw boolean");
catalogue.Undo(); Check(cataloguePlayer.Data.SequenceEqual(miscBefore), "profile fruit undo preserves unrelated bytes");
ExpectInvalid(() => catalogue.SetMiscProfile(0, 2, 30, false, "2024-02-29", newFruit));
ExpectInvalid(() => catalogue.SetMiscProfile(0, 13, 1, false, "2024-02-29", newFruit));
ExpectInvalid(() => catalogue.SetMiscProfile(0, 2, 29, false, "2023-02-29", newFruit));
ExpectInvalid(() => catalogue.SetMiscProfile(0, 2, 29, false, "2024-01-01", 65535));
Check(cataloguePlayer.Data.SequenceEqual(miscBefore), "invalid calendar dates and fruit reject the entire profile edit");
catalogue.SetMiscProfile(0, 2, 29, false, "2024-02-29", newFruit);
Check(cataloguePlayer.Birthday.ToString() == "02-29" && cataloguePlayer.ProfileBirthday.ToString() == "02-29"
    && cataloguePlayer.ProfileTimestamp.ToString() == "2024-02-29" && !cataloguePlayer.ProfileIsMakeVillage, "birthday changes synchronize the passport and accept leap dates");
var miscAfter = cataloguePlayer.Data.ToArray();
catalogue.Undo(); Check(cataloguePlayer.Data.SequenceEqual(miscBefore), "all profile fields undo in one byte-exact step");
catalogue.Redo(); Check(cataloguePlayer.Data.SequenceEqual(miscAfter), "profile redo restores all edited bytes");
catalogue.SetMiscProfile(0, 2, 29, false, "", newFruit);
Check(cataloguePlayer.ProfileTimestamp.IsEmpty, "empty profile date clears the date using the native empty value");
catalogue.Undo();
catalogue.SetMiscProfile(0, 2, 29, false, "0001-01-01", newFruit);
Check(cataloguePlayer.ProfileTimestamp.ToString() == "0001-01-01", "minimum valid calendar date does not become the empty-date sentinel");
catalogue.Undo();
catalogue.SetCollection(0, "collection", lastCollection, !oldCollected);
Check(catalogueSave.Main.Data.SequenceEqual(catalogueMain) && catalogueSave.Players[1].Personal.Data.SequenceEqual(catalogueOther),
    "catalogue and profile edits are isolated to the selected player");
catalogue.Save(); var catalogueReload = new HorizonSave(provider);
Check(catalogueReload.Players[0].Personal.Birthday.ToString() == "02-29"
    && catalogueReload.Players[0].Personal.ProfileTimestamp.ToString() == "2024-02-29"
    && FlagUtil.GetFlag(catalogueReload.Players[0].Personal.Data, collectOffset, lastCollection) == !oldCollected
    && !catalogueReload.GetInvalidHashes().Any(), "catalogue and profile changes survive encrypted save/reload with valid hashes");

var oldCatalogue = new WorkspaceSession(originalSave);
var oldCatalogueBefore = originalPersonal.Data.ToArray();
var oldRemakeLast = originalPersonal.Offsets.MaxRemakeBitFlag - 1;
var oldRemakeFlag = FlagUtil.GetFlag(originalPersonal.Data, originalPersonal.Offsets.ItemRemakeCollectBit, oldRemakeLast);
oldCatalogue.SetCollection(0, "remakes", oldRemakeLast, !oldRemakeFlag);
oldCatalogue.SetMiscProfile(0, 12, 31, true, "2024-12-31", GameLists.Fruits[0]);
Check(originalPersonal.Birthday.ToString() == "12-31" && originalPersonal.ProfileBirthday.ToString() == "12-31"
    && FlagUtil.GetFlag(originalPersonal.Data, originalPersonal.Offsets.ItemRemakeCollectBit, oldRemakeLast) == !oldRemakeFlag,
    "original save format uses its own catalogue and profile offsets");
oldCatalogue.Undo(); oldCatalogue.Undo();
Check(originalPersonal.Data.SequenceEqual(oldCatalogueBefore), "original catalogue and profile undo preserve every personal byte");
oldCatalogue.Redo(); oldCatalogue.Redo(); oldCatalogue.Save();
var oldCatalogueReload = new HorizonSave(originalProvider);
Check(oldCatalogueReload.Players[0].Personal.ProfileTimestamp.ToString() == "2024-12-31" && !oldCatalogueReload.GetInvalidHashes().Any(),
    "original catalogue and profile edits survive encrypted reload with valid hashes");
IslandRecordsChecks.Run(provider, originalProvider);
Console.WriteLine("All workspace integration checks passed.");
if (args.Length == 2 && args[0] == "--fixture") provider.Export(args[1]);

static byte[] DesignFixture(bool pro, string name)
{
    var bytes = new byte[pro ? DesignPatternPRO.SIZE : DesignPattern.SIZE];
    bytes[^1] = 0xCD;
    if (pro)
    {
        var p = new DesignPatternPRO(bytes) { DesignName = name, PlayerName = "Author", TownName = "Origin", PlayerID = 55, TownID = 66, UsageCompatibility = 0xEE01, Version = 123, Hash = 456 };
        for (var i = 0; i < 15; i++)
        {
            var offset = DesignPatternPRO.GetColorOffset(i);
            bytes[offset] = (byte)(0x11 + i); bytes[offset + 1] = (byte)(0x22 + i); bytes[offset + 2] = (byte)(0x33 + i);
        }
        for (var sheet = 0; sheet < 4; sheet++) for (var i = 0; i < 1024; i++) p.SetPixelAtIndex(sheet, i, sheet);
    }
    else
    {
        var p = new DesignPattern(bytes) { DesignName = name, PlayerName = "Author", TownName = "Origin", PlayerID = 55, TownID = 66, UsageCompatibility = 0xEE00, Version = 123, Hash = 456 };
        for (var i = 0; i < 15; i++)
        {
            var offset = DesignPattern.GetColorOffset(i);
            bytes[offset] = (byte)(0x11 + i); bytes[offset + 1] = (byte)(0x22 + i); bytes[offset + 2] = (byte)(0x33 + i);
        }
    }
    return bytes;
}

static Dictionary<string, System.Text.Json.JsonElement> HousingValues(params (string Key, object Value)[] fields) => fields.ToDictionary(f => f.Key, f => System.Text.Json.JsonSerializer.SerializeToElement(f.Value));

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
