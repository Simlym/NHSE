using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record HousingChoice(long Id, string Name);
    internal sealed record HousingField(string Key, object Value, string Type, long Min = 0, long Max = uint.MaxValue, HousingChoice[]? Options = null);
    internal sealed record HousingEntry(int Index, string Name);
    internal sealed record RoomItem(int ItemId, int SystemParam, int AdditionalParam, int FreeParam)
    {
        public static RoomItem From(Item item) => new(item.ItemId, item.SystemParam, item.AdditionalParam, item.FreeParam);
        public Item ToItem()
        {
            if (ItemId is < 0 or > ushort.MaxValue || SystemParam is < 0 or > 255 || AdditionalParam is < 0 or > 255)
                throw new ArgumentException("invalidRequest");
            return new Item((ushort)ItemId) { SystemParam = (byte)SystemParam, AdditionalParam = (byte)AdditionalParam, FreeParam = FreeParam };
        }
    }
    internal sealed record HousingTile(int X, int Y, int RootX, int RootY, bool Empty, bool Extension, string Name, RoomItem Item, string? Image);
    internal sealed record HousingRoom(int Room, int Layer, HousingTile[] Tiles);
    internal sealed record HousingData(string Kind, int Index, HousingEntry[] Entries, HousingField[] Fields, HousingField[] Flags, HousingRoom? Room, bool Locked = false);
    private static readonly string[] HouseKeys = ["HouseLevel", "HouseStatus", "WallUniqueID", "RoofUniqueID", "DoorUniqueID", "OrderWallUniqueID", "OrderRoofUniqueID", "OrderDoorUniqueID", "Loan", "Unknown", "NPC1", "NPC2", "BuildPlayer", "PlayerHouseFlag", "Flag", "DoorDecoItemName", "PostItemName", "OrderPostItemName", "ExtraItem"];
    private static readonly string[] ItemKeys = ["ItemId", "SystemParam", "AdditionalParam", "FreeParam"];

    public int FindResidentHouse(int resident)
    {
        if ((uint)resident >= MainSaveOffsets.VillagerCount) throw new ArgumentException("invalidRequest");
        var result = Array.FindIndex(SaveFile.Main.GetVillagerHouses(), h => h.NPC1 == resident);
        if (result < 0) throw new ArgumentException("invalidRequest");
        return result;
    }

    private object ReadHouse(string kind, int index)
    {
        if (kind == "playerHouses" && (uint)index < MainSaveOffsets.PlayerCount) return SaveFile.Main.GetPlayerHouse(index);
        if (kind == "residentHouses" && (uint)index < MainSaveOffsets.VillagerCount) return SaveFile.Main.GetVillagerHouse(index);
        throw new ArgumentException("invalidRequest");
    }
    private static byte[] HouseBytes(object house) => house is IPlayerHouse p ? p.Write() : ((IVillagerHouse)house).Write();
    private object CloneHouse(string kind, byte[] bytes) => kind == "playerHouses" ? SaveFile.Main.Offsets.ReadPlayerHouse(bytes.ToArray()) : SaveFile.Main.Offsets.ReadVillagerHouse(bytes.ToArray());
    private void WriteHouse(string kind, int index, byte[] bytes)
    {
        if (kind == "playerHouses") SaveFile.Main.SetPlayerHouse((IPlayerHouse)CloneHouse(kind, bytes), index);
        else SaveFile.Main.SetVillagerHouse((IVillagerHouse)CloneHouse(kind, bytes), index);
    }
    private void CommitHouse(string kind, int index, byte[] before, byte[] after)
    {
        if (!before.SequenceEqual(after)) Commit(() => WriteHouse(kind, index, before), () => WriteHouse(kind, index, after));
    }
    private static PropertyInfo Property(object house, string key) => house.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException("invalidRequest");
    private static (long Min, long Max) Limits(Type type)
    {
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (type == typeof(byte)) return (0, byte.MaxValue);
        if (type == typeof(sbyte)) return (sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(short)) return (short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (0, ushort.MaxValue);
        if (type == typeof(int)) return (int.MinValue, int.MaxValue);
        return (0, uint.MaxValue);
    }
    private static HousingField Numeric(string key, long value, Type type)
    {
        var (min, max) = Limits(type);
        var choices = type.IsEnum ? Enum.GetValues(type).Cast<object>().Select(v => new HousingChoice(Convert.ToInt64(v), v.ToString()!)).DistinctBy(v => v.Id).ToArray() : null;
        return new HousingField(key, value, "number", min, max, choices);
    }
    private static HousingField[] HouseFields(object house)
    {
        var fields = new List<HousingField>();
        foreach (var key in HouseKeys)
        {
            var prop = house.GetType().GetProperty(key);
            if (prop is null || !prop.CanWrite) continue;
            var value = prop.GetValue(house)!;
            if (value is Item item)
            {
                foreach (var part in ItemKeys)
                {
                    var partProp = typeof(Item).GetProperty(part)!;
                    fields.Add(Numeric(key + "." + part, Convert.ToInt64(partProp.GetValue(item)), partProp.PropertyType));
                }
            }
            else if (value is EncryptedInt32 encrypted) fields.Add(Numeric(key, encrypted.Value, typeof(uint)));
            else if (value is bool flag) fields.Add(new HousingField(key, flag, "boolean"));
            else fields.Add(Numeric(key, Convert.ToInt64(value), prop.PropertyType));
        }
        return fields.ToArray();
    }

    public HousingData GetHousing(string kind, int player, int index, int room, int layer, Func<Item, string?>? sprite = null)
    {
        var personal = Player(player);
        var main = SaveFile.Main;
        if (kind == "campsite")
        {
            var fields = new HousingField[] { new("Occupied", main.CampsiteStatus == 3, "boolean"),
                new("VisitorCode", (main.CampsiteVillagerSpeciesID * 256) + main.CampsiteVillagerVariantID, "number", 0, ushort.MaxValue,
                    WorkspaceVillagers.All().Select(v => new HousingChoice((v.Species * 256) + int.Parse(v.Id.AsSpan(3), CultureInfo.InvariantCulture), v.Name + " · " + v.Id)).OrderBy(v => v.Name).ToArray()),
                new("VisitDate", main.CampTimestamp.IsEmpty ? "" : main.CampTimestamp.ToString(), "date") };
            return new(kind, 0, [], fields, [], null, main.CampsiteStatus is not (2 or 3));
        }
        if (kind == "fruitFlowers")
        {
            var fruits = GameLists.Fruits.ToArray().Select(id => new HousingChoice(id, GameInfo.Strings.itemlist[id])).ToArray();
            return new(kind, 0, [], [new("ProfileFruit", personal.ProfileFruit, "number", 0, ushort.MaxValue, fruits),
                new("SpecialtyFruit", main.SpecialtyFruit, "number", 0, ushort.MaxValue, fruits), new("SisterFruit", main.SisterFruit, "number", 0, ushort.MaxValue, fruits),
                Numeric("SpecialtyFlower", (byte)main.SpecialtyFlower, typeof(IslandFlowers)), Numeric("SisterFlower", (byte)main.SisterFlower, typeof(IslandFlowers))], [], null);
        }
        var house = ReadHouse(kind, index);
        var entries = kind == "playerHouses" ? main.GetPlayerHouses().Select((_, i) => new HousingEntry(i, i < SaveFile.Players.Count ? SaveFile.Players[i].Personal.PlayerName : "#" + (i + 1))).ToArray()
            : main.GetVillagerHouses().Select((h, i) => new HousingEntry(i, (uint)h.NPC1 < MainSaveOffsets.VillagerCount ? GameInfo.Strings.GetVillager(main.GetVillager(h.NPC1).InternalName) : "#" + (i + 1))).ToArray();
        var flags = Array.Empty<HousingField>();
        HousingRoom? roomData = null;
        if (house is IPlayerHouse ph)
        {
            if ((uint)room >= 6 || (uint)layer >= 8) throw new ArgumentException("invalidRequest");
            flags = ph.EventFlags.ToArray().Select((v, i) => Numeric("EventFlag." + i, v, typeof(short))).ToArray();
            var tiles = ph.GetRoom(room).GetItemLayers()[layer];
            var cache = new Dictionary<ulong, string?>();
            var result = new List<HousingTile>();
            for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++)
            {
                var tile = tiles.GetTile(x, y);
                var rx = tile.IsExtension ? x - tile.ExtensionX : x;
                var ry = tile.IsExtension ? y - tile.ExtensionY : y;
                string? image = null;
                if (!tile.IsNone && !tile.IsExtension && sprite is not null)
                {
                    if (!cache.TryGetValue(tile.RawValue, out image)) cache[tile.RawValue] = image = sprite(tile);
                }
                result.Add(new HousingTile(x, y, rx, ry, tile.IsNone, tile.IsExtension, tile.IsNone || tile.IsExtension ? "" : GameInfo.Strings.GetItemName(tile), RoomItem.From(tile), image));
            }
            roomData = new(room, layer, result.ToArray());
        }
        return new(kind, index, entries, HouseFields(house), flags, roomData);
    }

    public void SetHousing(string kind, int player, int index, Dictionary<string, JsonElement> values)
    {
        var current = GetHousing(kind, player, index, 0, 0);
        if (current.Locked) throw new ArgumentException("invalidRequest");
        var schema = current.Fields.Concat(current.Flags).ToDictionary(f => f.Key);
        // Validate every key/value before changing any save byte.
        foreach (var (key, value) in values)
        {
            if (!schema.TryGetValue(key, out var field)) throw new ArgumentException("invalidRequest");
            if (field.Type == "boolean") { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("invalidRequest"); }
            else if (field.Type == "date") { if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("invalidRequest"); }
            else if (!value.TryGetInt64(out var number) || number < field.Min || number > field.Max) throw new ArgumentException("invalidRequest");
        }
        bool Changed(string key) => values.TryGetValue(key, out var value) && value.ToString() != Convert.ToString(schema[key].Value, CultureInfo.InvariantCulture);
        // JSON booleans use lowercase; compare booleans as booleans instead.
        bool Different(string key) => schema[key].Type == "boolean" ? values.TryGetValue(key, out var v) && v.GetBoolean() != (bool)schema[key].Value : Changed(key);
        long Number(string key) => values.TryGetValue(key, out var v) ? v.GetInt64() : Convert.ToInt64(schema[key].Value);
        if (!values.Keys.Any(Different)) return;
        if (kind == "campsite")
        {
            var occupied = values.TryGetValue("Occupied", out var occ) ? occ.GetBoolean() : SaveFile.Main.CampsiteStatus == 3;
            var species = Number("VisitorCode") / 256; var variant = Number("VisitorCode") % 256;
            if (occupied && (Different("VisitorCode") || Different("Occupied")) && (species >= 35 || !WorkspaceVillagers.Contains(VillagerUtil.GetInternalVillagerName((VillagerSpecies)species, (int)variant))))
                throw new ArgumentException("invalidRequest");
            var date = SaveFile.Main.CampTimestamp;
            if (Different("VisitDate"))
            {
                var text = values["VisitDate"].GetString();
                if (text == "") date = new GSaveDate();
                else if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) date = dt;
                else throw new ArgumentException("invalidRequest");
            }
            EditNative(() =>
            {
                var m = SaveFile.Main;
                m.CampsiteStatus = occupied ? (byte)3 : (byte)2;
                m.CampsiteVillagerSpeciesID = occupied ? (byte)species : (byte)35;
                m.CampsiteVillagerVariantID = occupied ? (byte)variant : (byte)0;
                if (Different("VisitDate")) m.CampTimestamp = date;
            });
            return;
        }
        if (kind == "fruitFlowers")
        {
            foreach (var key in new[] { "ProfileFruit", "SpecialtyFruit", "SisterFruit" })
                if (Different(key) && !GameLists.Fruits.Contains((ushort)Number(key))) throw new ArgumentException("invalidRequest");
            foreach (var key in new[] { "SpecialtyFlower", "SisterFlower" })
                if (Different(key) && Number(key) > 7) throw new ArgumentException("invalidRequest");
            EditNative(() =>
            {
                var m = SaveFile.Main;
                if (Different("ProfileFruit")) Player(player).ProfileFruit = (ushort)Number("ProfileFruit");
                if (Different("SpecialtyFruit"))
                {
                    m.SpecialtyFruit = (ushort)Number("SpecialtyFruit");
                    var ids = new ushort[] { 2213, 2214, 2285, 2286, 2287 };
                    m.FruitFlags = ids.Select(id => (byte)(id == m.SpecialtyFruit ? 1 : 0)).ToArray();
                }
                if (Different("SisterFruit")) m.SisterFruit = (ushort)Number("SisterFruit");
                if (Different("SpecialtyFlower")) m.SpecialtyFlower = (IslandFlowers)Number("SpecialtyFlower");
                if (Different("SisterFlower")) m.SisterFlower = (IslandFlowers)Number("SisterFlower");
            });
            return;
        }
        var before = HouseBytes(ReadHouse(kind, index));
        var house = CloneHouse(kind, before);
        foreach (var key in values.Keys.Where(Different))
        {
            if (key.StartsWith("EventFlag.", StringComparison.Ordinal))
                ((IPlayerHouse)house).EventFlags[int.Parse(key.AsSpan(10), CultureInfo.InvariantCulture)] = (short)Number(key);
            else
            {
                var path = key.Split('.');
                var prop = Property(house, path[0]);
                if (path.Length == 2)
                {
                    var item = (Item)prop.GetValue(house)!;
                    var part = typeof(Item).GetProperty(path[1])!;
                    part.SetValue(item, Convert.ChangeType(Number(key), part.PropertyType, CultureInfo.InvariantCulture));
                    prop.SetValue(house, item);
                }
                else if (prop.PropertyType == typeof(EncryptedInt32)) prop.SetValue(house, ((EncryptedInt32)prop.GetValue(house)!) with { Value = (uint)Number(key) });
                else if (prop.PropertyType == typeof(bool)) prop.SetValue(house, values[key].GetBoolean());
                else prop.SetValue(house, prop.PropertyType.IsEnum ? Enum.ToObject(prop.PropertyType, Number(key)) : Convert.ChangeType(Number(key), prop.PropertyType, CultureInfo.InvariantCulture));
            }
        }
        CommitHouse(kind, index, before, HouseBytes(house));
    }

    public void SetRoomItem(int index, int room, int layer, int x, int y, RoomItem next, bool autoExtensions, bool noOverwrite, bool delete)
    {
        if ((uint)room >= 6 || (uint)layer >= 8 || (uint)x >= 20 || (uint)y >= 20) throw new ArgumentException("invalidRequest");
        var before = HouseBytes(ReadHouse("playerHouses", index));
        var house = (IPlayerHouse)CloneHouse("playerHouses", before);
        var manager = new RoomManager(house.GetRoom(room));
        var tiles = manager.Layers[layer];
        var tile = tiles.GetTile(x, y);
        if (autoExtensions && tile.IsExtension)
        {
            x -= tile.ExtensionX; y -= tile.ExtensionY;
            if ((uint)x >= 20 || (uint)y >= 20) throw new ArgumentException("invalidRequest");
            var root = tiles.GetTile(x, y);
            if (!root.IsRoot || root.ItemId != tile.ExtensionItemId) throw new ArgumentException("invalidRequest");
            tile = root;
        }
        if (autoExtensions && tile.IsRoot) tiles.DeleteExtensionTiles(tile, x, y);
        tile.Delete();
        if (!delete)
        {
            var item = next.ToItem();
            if (item.IsExtension || (!item.IsNone && !WorkspaceCatalog.Contains(item.ItemId))) throw new ArgumentException("invalidRequest");
            var permission = tiles.IsOccupied(item, x, y);
            if (permission == PlacedItemPermission.OutOfBounds || (noOverwrite && permission == PlacedItemPermission.Collision)) throw new ArgumentException("roomPlacementBlocked");
            if (autoExtensions && item.IsRoot) tiles.SetExtensionTiles(item, x, y);
            tile.CopyFrom(item);
        }
        manager.Save(); house.SetRoom(room, manager.Room);
        CommitHouse("playerHouses", index, before, house.Write());
    }
}
