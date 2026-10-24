using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Buffers.Binary;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record MapTarget(string Mode, int Acre, int Layer, int X, int Y, int Building, int Marker);
    internal sealed record MapCell(int X, int Y, int RelativeX, int RelativeY, bool Valid, bool Extension, int RootX, int RootY, string Name);
    internal sealed record MapData(MapTarget Target, int GridSize, int ItemWidth, int ItemHeight, int MarkerCount,
        HousingChoice[] Acres, HousingChoice[] Buildings, HousingChoice[] FieldObjects, HousingField[] Fields, MapCell[] Cells,
        string? Overview = null, string? Viewport = null, string? ItemImage = null);
    private static readonly string[] TerrainKeys = ["UnitModel", "Variation", "LandMakingAngle", "UnitModelRoad", "VariationRoad", "LandMakingAngleRoad", "Elevation"];
    private static readonly (string Key, int Offset, Type Type)[] BuildingKeys =
    [
        ("BuildingType", 0, typeof(BuildingType)), ("X", 2, typeof(ushort)), ("Y", 4, typeof(ushort)),
        ("Angle", 6, typeof(byte)), ("Bit", 7, typeof(sbyte)), ("Type", 8, typeof(ushort)),
        ("TypeArg", 10, typeof(byte)), ("UniqueID", 12, typeof(ushort)), ("Unused", 16, typeof(uint)),
    ];
    private static void CheckMapTarget(MapTarget target, int markerCount)
    {
        if (target.Mode is not ("terrain" or "items" or "buildings" or "acres" or "markers")
            || (uint)target.Acre >= AcreCoordinate.Exterior.Length || target.Layer is < 0 or > 1
            || (uint)target.Building >= MainSaveOffsets.BuildingCount || (uint)target.Marker >= markerCount)
            throw new ArgumentException("invalidRequest");
        var size = target.Mode == "items" ? 32 : 16;
        if ((uint)target.X >= size || (uint)target.Y >= size) throw new ArgumentException("invalidRequest");
    }
    private int MapMarkerCount => (SaveFile.Main.Info.GetKnownRevisionIndex() >= 31 ? 9 : 7) * 6 * 16 * 16;
    private static HousingField MapNumber(string key, long value, Type type) => Numeric("Map." + key, value, type);
    private static LayerPositionConfig TerrainConfig => LayerPositionConfig.Create(7, 6, 16, 1, 1, 1);
    private LayerPositionConfig ItemConfig => LayerPositionConfig.Create(SaveFile.Main.FieldItemAcreWidth, 6, 32, 1, (byte)((9 - SaveFile.Main.FieldItemAcreWidth) / 2), 1);
    private (int X, int Y) MapCoordinates(MapTarget target, int x, int y)
    {
        var acre = AcreCoordinate.Exterior[target.Acre];
        var config = target.Mode == "items" ? ItemConfig : TerrainConfig;
        var scale = target.Mode == "items" ? 32 : 16;
        return config.GetCoordinatesRelative(acre.X * scale + x, acre.Y * scale + y);
    }
    public MapData GetMapWorkspace(MapTarget target)
    {
        CheckMapTarget(target, MapMarkerCount);
        var m = SaveFile.Main;
        var size = target.Mode == "items" ? 32 : 16;
        var config = target.Mode == "items" ? ItemConfig : TerrainConfig;
        var terrain = target.Mode == "items" ? null : new LayerTerrain(m.GetTerrainTiles(), m.GetAcreBytes());
        var items = target.Mode == "items" ? new LayerFieldItem(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6) : null;
        var cells = new List<MapCell>();
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var (rx, ry) = MapCoordinates(target, x, y);
            var valid = config.IsCoordinateValidRelative(rx, ry);
            var tile = valid ? items?.GetTile(rx, ry) : null;
            var rootX = tile?.IsExtension == true ? rx - tile.ExtensionX : rx;
            var rootY = tile?.IsExtension == true ? ry - tile.ExtensionY : ry;
            var name = tile is null || tile.IsNone || tile.IsExtension ? "" : MapItemName(tile);
            cells.Add(new(x, y, rx, ry, valid, tile?.IsExtension == true, rootX, rootY, name));
        }
        var fields = new List<HousingField>();
        var (selectedX, selectedY) = MapCoordinates(target, target.X, target.Y);
        if (target.Mode == "terrain" && config.IsCoordinateValidRelative(selectedX, selectedY))
        {
            var tile = terrain!.GetTile(selectedX, selectedY);
            foreach (var key in TerrainKeys)
            {
                var p = typeof(TerrainTile).GetProperty(key)!;
                fields.Add(MapNumber(key, Convert.ToInt64(p.GetValue(tile)), p.PropertyType));
            }
        }
        else if (target.Mode == "items" && config.IsCoordinateValidRelative(selectedX, selectedY))
        {
            var tile = items!.GetTile(selectedX, selectedY);
            foreach (var key in ItemKeys)
            {
                var p = typeof(Item).GetProperty(key)!;
                fields.Add(MapNumber(key, Convert.ToInt64(p.GetValue(tile)), p.PropertyType));
            }
        }
        else if (target.Mode == "buildings")
        {
            var building = m.Buildings[target.Building];
            foreach (var (key, _, type) in BuildingKeys)
                fields.Add(MapNumber(key, Convert.ToInt64(typeof(Building).GetProperty(key)!.GetValue(building)), type));
        }
        else if (target.Mode == "acres") fields.Add(MapNumber("AcreTemplate", m.GetAcre(target.Acre), typeof(OutsideAcre)));
        else if (target.Mode == "markers")
        {
            fields.Add(MapNumber("PlazaX", m.EventPlazaLeftUpX, typeof(uint)));
            fields.Add(MapNumber("PlazaY", m.EventPlazaLeftUpZ, typeof(uint)));
            fields.Add(MapNumber("OutsideTemplate", m.OutsideFieldTemplateUniqueId, typeof(ushort)));
            fields.Add(MapNumber("MainTemplate", m.MainFieldParamUniqueID, typeof(ushort)));
            fields.Add(MapNumber("DesignValue", BinaryPrimitives.ReadUInt16LittleEndian(m.Data[(m.Offsets.MyDesignMap + (target.Marker * 2))..]), typeof(ushort)));
        }
        var buildings = m.Buildings.Select((b, i) => new HousingChoice(i, $"{i + 1} · {b}")).ToArray();
        var acres = AcreCoordinate.Exterior.Select((a, i) => new HousingChoice(i, a.Name)).ToArray();
        var objects = FieldItemList.Items.Select(v => new HousingChoice(v.Key, GameInfo.Strings.InternalNameTranslation.TryGetValue(v.Value.Name, out var translated) ? translated : v.Value.Name)).ToArray();
        return new(target, size, m.FieldItemAcreWidth * 32, 6 * 32, MapMarkerCount, acres, buildings, objects, fields.ToArray(), cells.ToArray());
    }
    private static string MapItemName(Item tile)
    {
        if (FieldItemList.Items.TryGetValue(tile.ItemId, out var item))
            return GameInfo.Strings.InternalNameTranslation.TryGetValue(item.Name, out var name) ? name : item.Name;
        return GameInfo.Strings.GetItemName(tile);
    }
    private void CommitMapPatches(List<IdentityPatch> patches)
    {
        patches = patches.Where(p => !p.Before.SequenceEqual(p.After)).ToList();
        if (patches.Count == 0) return;
        void Apply(bool undo) { foreach (var p in patches) (undo ? p.Before : p.After).CopyTo(p.File.Data[p.Offset..]); }
        Commit(() => Apply(true), () => Apply(false));
    }
    private IdentityPatch MapPatch(int offset, byte[] after) => new(SaveFile.Main, offset, SaveFile.Main.Data.Slice(offset, after.Length).ToArray(), after.ToArray());

    public void EditMap(MapTarget target, Dictionary<string, JsonElement> values, string action, bool autoExtensions, bool noOverwrite, bool snapFieldItems)
    {
        var data = GetMapWorkspace(target);
        var schema = data.Fields.ToDictionary(f => f.Key);
        foreach (var (key, value) in values)
            if (!schema.TryGetValue(key, out var f) || !value.TryGetInt64(out var n) || n < f.Min || n > f.Max)
                throw new ArgumentException("invalidRequest");
        if (action is not ("apply" or "delete" or "rotate")) throw new ArgumentException("invalidRequest");
        long Number(string key) => values.TryGetValue("Map." + key, out var v) ? v.GetInt64() : Convert.ToInt64(schema["Map." + key].Value);
        var m = SaveFile.Main;
        var (x, y) = MapCoordinates(target, target.X, target.Y);
        if (target.Mode == "items")
        {
            if (data.Fields.Length == 0 || action == "rotate") throw new ArgumentException("invalidRequest");
            var next = new RoomItem((int)Number("ItemId"), (int)Number("SystemParam"), (int)Number("AdditionalParam"), (int)Number("FreeParam")).ToItem();
            var layer = new LayerFieldItem(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6);
            if (snapFieldItems && next.IsFieldItem && action != "delete") { x &= ~1; y &= ~1; }
            var tile = layer.GetTile(x, y);
            if (autoExtensions && tile.IsExtension)
            {
                var id = tile.ExtensionItemId;
                x -= tile.ExtensionX; y -= tile.ExtensionY;
                if (!ItemConfig.IsCoordinateValidRelative(x, y)) throw new ArgumentException("invalidRequest");
                tile = layer.GetTile(x, y);
                if (!tile.IsRoot || tile.ItemId != id) throw new ArgumentException("invalidRequest");
            }
            if (autoExtensions && tile.IsRoot) layer.DeleteExtensionTiles(tile, x, y);
            tile.Delete();
            if (action != "delete")
            {
                if (next.IsExtension || (!next.IsNone && !WorkspaceCatalog.Contains(next.ItemId) && !FieldItemList.Items.ContainsKey(next.ItemId))) throw new ArgumentException("invalidRequest");
                var permission = layer.IsOccupied(next, x, y);
                if (permission == PlacedItemPermission.OutOfBounds || noOverwrite && permission == PlacedItemPermission.Collision) throw new ArgumentException("roomPlacementBlocked");
                if (autoExtensions && next.IsRoot) layer.SetExtensionTiles(next, x, y);
                tile.CopyFrom(next);
            }
            var offset = m.Offsets.FieldItem + target.Layer * layer.Tiles.Length * Item.SIZE;
            CommitMapPatches([MapPatch(offset, Item.SetArray(layer.Tiles))]);
            return;
        }
        if (target.Mode == "terrain")
        {
            if (data.Fields.Length == 0) throw new ArgumentException("invalidRequest");
            var layer = new LayerTerrain(m.GetTerrainTiles(), m.GetAcreBytes());
            var tile = layer.GetTile(x, y);
            if (action == "delete") tile.Clear();
            else if (action == "rotate") { if (!tile.TryRotate()) return; }
            else foreach (var key in TerrainKeys)
            {
                var p = typeof(TerrainTile).GetProperty(key)!;
                var value = Number(key);
                p.SetValue(tile, p.PropertyType.IsEnum ? Enum.ToObject(p.PropertyType, value) : Convert.ChangeType(value, p.PropertyType, CultureInfo.InvariantCulture));
            }
            CommitMapPatches([MapPatch(m.Offsets.LandMakingMap + layer.TileInfo.GetTileIndex(x, y) * TerrainTile.SIZE, TerrainTile.SetArray([tile]))]);
            return;
        }
        if (action != "apply") throw new ArgumentException("invalidRequest");
        var patches = new List<IdentityPatch>();
        void AddNumber(int offset, long value, Type type)
        {
            var bytes = new byte[type == typeof(uint) ? 4 : type == typeof(byte) || type == typeof(sbyte) ? 1 : 2];
            if (bytes.Length == 4) BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
            else if (bytes.Length == 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
            else bytes[0] = unchecked((byte)value);
            patches.Add(MapPatch(offset, bytes));
        }
        if (target.Mode == "buildings")
            foreach (var (key, offset, type) in BuildingKeys) AddNumber(m.Offsets.MainFieldStructure + target.Building * Building.SIZE + offset, Number(key), type);
        else if (target.Mode == "acres") AddNumber(m.Offsets.OutsideField + target.Acre * 2, Number("AcreTemplate"), typeof(ushort));
        else
        {
            var offset = m.Offsets.OutsideField + m.GetAcreBytes().Length;
            AddNumber(offset, Number("OutsideTemplate"), typeof(ushort));
            AddNumber(offset + 2, Number("MainTemplate"), typeof(ushort));
            AddNumber(offset + 4, Number("PlazaX"), typeof(uint));
            AddNumber(offset + 8, Number("PlazaY"), typeof(uint));
            AddNumber(m.Offsets.MyDesignMap + target.Marker * 2, Number("DesignValue"), typeof(ushort));
        }
        CommitMapPatches(patches);
    }
}
