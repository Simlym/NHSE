using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Buffers.Binary;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    private int MapFlagOffset(int layer) => layer == 0 ? SaveFile.Main.FieldItemFlag0 : SaveFile.Main.FieldItemFlag1;
    private int MapFlagSize => SaveFile.Main.FieldItemAcreWidth * 32 * 192 / 8;
    public bool? MapActive(MapTarget target)
    {
        CheckMapTarget(target, MapMarkerCount);
        if (target.Mode != "items") return null;
        var (x, y) = MapCoordinates(target, target.X, target.Y);
        if (!ItemConfig.IsCoordinateValidRelative(x, y)) return null;
        return FlagUtil.GetFlag(SaveFile.Main.Data.Slice(MapFlagOffset(target.Layer), MapFlagSize), y * ItemConfig.LayerTotalWidth + x);
    }
    public void SetMapActive(MapTarget target, bool active)
    {
        if (MapActive(target) is null) throw new ArgumentException("invalidRequest");
        var (x, y) = MapCoordinates(target, target.X, target.Y);
        var bytes = SaveFile.Main.Data.Slice(MapFlagOffset(target.Layer), MapFlagSize).ToArray();
        FlagUtil.SetFlag(bytes, y * ItemConfig.LayerTotalWidth + x, active);
        CommitMapPatches([MapPatch(MapFlagOffset(target.Layer), bytes)]);
    }

    private (int X, int Y) MapAcreOrigin(MapTarget target, string resource)
    {
        var mode = resource == "items" ? "items" : "terrain";
        var (x, y) = MapCoordinates(target with { Mode = mode }, 0, 0);
        var config = mode == "items" ? ItemConfig : TerrainConfig;
        var size = mode == "items" ? 32 : 16;
        if (!config.IsCoordinateValidRelative(x, y) || !config.IsCoordinateValidRelative(x + size - 1, y + size - 1))
            throw new ArgumentException("mapInvalidAcre");
        return (x, y);
    }
    public int MapFileSize(MapTarget target, string resource, bool whole)
    {
        CheckMapTarget(target, MapMarkerCount);
        if (!whole && resource is not ("items" or "terrain")) throw new ArgumentException("invalidRequest");
        if (!whole) MapAcreOrigin(target, resource);
        return resource switch
        {
            "items" => (whole ? SaveFile.Main.FieldItemAcreWidth * 32 * 192 : 32 * 32) * Item.SIZE,
            "terrain" => (whole ? 112 * 96 : 16 * 16) * TerrainTile.SIZE,
            "buildings" => Building.SIZE * MainSaveOffsets.BuildingCount,
            "acres" => 72 * 2,
            "flags" => MapFlagSize,
            "designs" => MapMarkerCount * 2,
            _ => throw new ArgumentException("invalidRequest"),
        };
    }
    public bool IsMapFileSizeValid(MapTarget target, string resource, bool whole, long size)
    {
        var expected = MapFileSize(target, resource, whole);
        return size == expected || whole && (resource switch
        {
            "items" => FieldItemUpgrade.IsUpdateNeeded(size, expected),
            "buildings" => size == 40 * Building.SIZE,
            "flags" => size is 5376 or 6912,
            "designs" => size is 0x5400 or 0x6C00,
            _ => false,
        });
    }
    public byte[] ExportMap(MapTarget target, string resource, bool whole)
    {
        MapFileSize(target, resource, whole);
        var m = SaveFile.Main;
        if (resource == "items")
        {
            var layer = new LayerFieldItem(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6);
            if (whole) return layer.DumpAll();
            var (x, y) = MapAcreOrigin(target, resource);
            return layer.DumpAcre(x, y);
        }
        if (resource == "terrain")
        {
            var layer = new LayerTerrain(m.GetTerrainTiles(), m.GetAcreBytes());
            if (whole) return layer.DumpAll();
            var (x, y) = MapAcreOrigin(target, resource);
            return layer.DumpAcre(x, y);
        }
        var offset = resource switch {
            "buildings" => m.Offsets.MainFieldStructure, "acres" => m.Offsets.OutsideField,
            "flags" => MapFlagOffset(target.Layer), "designs" => m.Offsets.MyDesignMap,
            _ => throw new ArgumentException("invalidRequest"),
        };
        return m.Data.Slice(offset, MapFileSize(target, resource, whole)).ToArray();
    }
    public void ImportMap(MapTarget target, string resource, bool whole, byte[] input)
    {
        if (!IsMapFileSizeValid(target, resource, whole, input.Length)) throw new ArgumentException("mapFileSize");
        var m = SaveFile.Main;
        var expected = MapFileSize(target, resource, whole);
        var bytes = input.ToArray();
        if (resource == "items" && whole) FieldItemUpgrade.DetectUpdate(ref bytes, expected);
        if (resource == "flags" && bytes.Length != expected)
        {
            var converted = new byte[expected];
            var oldWidth = bytes.Length * 8 / 192;
            var newWidth = expected * 8 / 192;
            for (var y = 0; y < 192; y++) for (var x = 0; x < Math.Min(oldWidth, newWidth); x++)
            {
                var sourceX = x + Math.Max(0, (oldWidth - newWidth) / 2);
                var destX = x + Math.Max(0, (newWidth - oldWidth) / 2);
                FlagUtil.SetFlag(converted, y * newWidth + destX, FlagUtil.GetFlag(bytes, y * oldWidth + sourceX));
            }
            bytes = converted;
        }
        if (resource == "designs" && bytes.Length != expected)
        {
            if (bytes.Length > expected) bytes = bytes.AsSpan((bytes.Length - expected) / 2, expected).ToArray();
            else
            {
                var converted = new byte[expected];
                for (var i = 0; i < converted.Length; i += 2) BinaryPrimitives.WriteUInt16LittleEndian(converted.AsSpan(i), MainSave.MapDesignNone);
                bytes.CopyTo(converted, (expected - bytes.Length) / 2);
                bytes = converted;
            }
        }
        int offset;
        if (resource == "items")
        {
            var layer = new LayerFieldItem(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6);
            if (!whole)
            {
                var (x, y) = MapAcreOrigin(target, resource);
                layer.ImportAcre(bytes, x, y);
                bytes = layer.DumpAll();
            }
            offset = m.Offsets.FieldItem + target.Layer * layer.Tiles.Length * Item.SIZE;
        }
        else if (resource == "terrain")
        {
            if (!whole)
            {
                var layer = new LayerTerrain(m.GetTerrainTiles(), m.GetAcreBytes());
                var (x, y) = MapAcreOrigin(target, resource);
                layer.ImportAcre(bytes, x, y);
                bytes = layer.DumpAll();
            }
            offset = m.Offsets.LandMakingMap;
        }
        else offset = resource switch {
            "buildings" => m.Offsets.MainFieldStructure, "acres" => m.Offsets.OutsideField,
            "flags" => MapFlagOffset(target.Layer), "designs" => m.Offsets.MyDesignMap,
            _ => throw new ArgumentException("invalidRequest"),
        };
        // A legacy 40-building file intentionally leaves the six remaining entries unchanged.
        CommitMapPatches([MapPatch(offset, bytes)]);
    }

    public void PaintMap(MapTarget target, int radius, bool randomVariation, Dictionary<string, JsonElement> values)
    {
        CheckMapTarget(target, MapMarkerCount);
        if (target.Mode != "terrain" || radius is < 1 or > 8) throw new ArgumentException("invalidRequest");
        var (cx, cy) = MapCoordinates(target, target.X, target.Y);
        if (!TerrainConfig.IsCoordinateValidRelative(cx, cy)) throw new ArgumentException("mapInvalidAcre");
        var layer = new LayerTerrain(SaveFile.Main.GetTerrainTiles(), SaveFile.Main.GetAcreBytes());
        var template = TerrainTile.GetArray(TerrainTile.SetArray([layer.GetTile(cx, cy)]))[0];
        foreach (var (key, value) in values)
        {
            if (!key.StartsWith("Map.", StringComparison.Ordinal) || !TerrainKeys.Contains(key[4..]) || !value.TryGetInt64(out var n) || n is < 0 or > ushort.MaxValue)
                throw new ArgumentException("invalidRequest");
            var p = typeof(TerrainTile).GetProperty(key[4..])!;
            p.SetValue(template, p.PropertyType.IsEnum ? Enum.ToObject(p.PropertyType, n) : (object)(ushort)n);
        }
        for (var dx = -radius; dx < radius; dx++) for (var dy = -radius; dy < radius; dy++)
        {
            if (radius == 1 ? dx != 0 || dy != 0 : dx * dx + dy * dy >= radius * radius / 2) continue;
            var x = cx + dx; var y = cy + dy;
            if (!TerrainConfig.IsCoordinateValidRelative(x, y)) continue;
            var tile = layer.GetTile(x, y);
            tile.CopyFrom(template);
            if (randomVariation && template.UnitModel is TerrainUnitModel.Cliff5B or TerrainUnitModel.River5B) tile.Variation = (ushort)Random.Shared.Next(4);
        }
        CommitMapPatches([MapPatch(SaveFile.Main.Offsets.LandMakingMap, layer.DumpAll())]);
    }

    public void BatchMap(MapTarget target, string operation, bool whole, bool interiorOnly, Dictionary<string, JsonElement> values)
    {
        CheckMapTarget(target, MapMarkerCount);
        var m = SaveFile.Main;
        if (target.Mode == "markers" && operation == "clearDesigns")
        {
            if (!whole || values.Count != 0) throw new ArgumentException("invalidRequest");
            var bytes = new byte[MapMarkerCount * 2];
            for (var i = 0; i < bytes.Length; i += 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i), MainSave.MapDesignNone);
            CommitMapPatches([MapPatch(m.Offsets.MyDesignMap, bytes)]);
            return;
        }
        if (target.Mode == "terrain")
        {
            if (operation is not ("setTerrain" or "setRoad" or "zeroElevation")) throw new ArgumentException("invalidRequest");
            var schema = TerrainKeys.ToDictionary(key => "Map." + key, key => typeof(TerrainTile).GetProperty(key)!);
            foreach (var (key, value) in values)
                if (!schema.ContainsKey(key) || !value.TryGetInt64(out var n) || n is < 0 or > ushort.MaxValue) throw new ArgumentException("invalidRequest");
            var layer = new LayerTerrain(m.GetTerrainTiles(), m.GetAcreBytes());
            // The chosen tile supplies any fields not explicitly provided in the draft.
            var (sx, sy) = MapCoordinates(target, target.X, target.Y);
            if (!TerrainConfig.IsCoordinateValidRelative(sx, sy)) throw new ArgumentException("mapInvalidAcre");
            var template = TerrainTile.GetArray(TerrainTile.SetArray([layer.GetTile(sx, sy)]))[0];
            foreach (var (key, value) in values)
            {
                var property = schema[key]; var n = value.GetInt64();
                property.SetValue(template, property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType, n) : (object)(ushort)n);
            }
            var (x, y) = whole ? (0, 0) : MapAcreOrigin(target, "terrain");
            var width = whole ? 112 : 16; var height = whole ? 96 : 16;
            for (var tx = x; tx < x + width; tx++) for (var ty = y; ty < y + height; ty++)
            {
                if (interiorOnly && (tx < 16 || tx >= 96 || ty < 16 || ty >= 80)) continue;
                var tile = layer.GetTile(tx, ty);
                if (operation == "zeroElevation") tile.Elevation = 0;
                else if (operation == "setRoad") tile.CopyRoadFrom(template);
                else tile.CopyFrom(template);
            }
            CommitMapPatches([MapPatch(m.Offsets.LandMakingMap, layer.DumpAll())]);
            return;
        }
        if (target.Mode != "items" || values.Count != 0) throw new ArgumentException("invalidRequest");
        var items = new LayerFieldItem(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6);
        var (xmin, ymin) = whole ? (0, 0) : MapAcreOrigin(target, "items");
        var w = whole ? m.FieldItemAcreWidth * 32 : 32; var h = whole ? 192 : 32;
        if (operation is "waterFlowers" or "waterAllFlowers") items.WaterAllFlowers(xmin, ymin, w, h, operation == "waterAllFlowers");
        else
        {
            bool Match(Item item) => operation switch {
                "removeAll" => true,
                "removeWeeds" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var weed) && weed.Kind.IsWeed,
                "removeTrees" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var tree) && tree.Kind.IsTree,
                "removePlants" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var plant) && plant.Kind.IsPlant,
                "removeFences" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var fence) && fence.Kind.IsFence,
                "removeObjects" => FieldItemList.Items.ContainsKey(item.DisplayItemId),
                "removePlaced" => !item.IsNone && !FieldItemList.Items.ContainsKey(item.DisplayItemId),
                "removeShells" => GameLists.Shells.Contains(item.DisplayItemId),
                "removeBranches" => item.DisplayItemId == 2500,
                "removeFlowers" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var flower) && flower.Kind.IsFlower,
                "removeBushes" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var bush) && bush.Kind.IsBush,
                "fillHoles" => FieldItemList.Items.TryGetValue(item.DisplayItemId, out var hole) && hole.Kind == FieldItemKind.UnitIconHole,
                _ => throw new ArgumentException("invalidRequest"),
            };
            // Validate the operation even if the selected area is empty.
            Match(new Item(Item.NONE));
            var roots = new HashSet<(int X, int Y)>();
            for (var x = xmin; x < xmin + w; x++) for (var y = ymin; y < ymin + h; y++)
            {
                var item = items.GetTile(x, y);
                var rx = item.IsExtension ? x - item.ExtensionX : x;
                var ry = item.IsExtension ? y - item.ExtensionY : y;
                if (!items.Contains(rx, ry)) continue;
                var root = items.GetTile(rx, ry);
                if (item.IsExtension && (!root.IsRoot || root.ItemId != item.ExtensionItemId)) continue;
                if (!root.IsNone && Match(root)) roots.Add((rx, ry));
            }
            foreach (var (x, y) in roots)
            {
                var root = items.GetTile(x, y);
                items.DeleteExtensionTiles(root, x, y);
                root.Delete();
            }
            if (operation == "removeAll")
                for (var x = xmin; x < xmin + w; x++) for (var y = ymin; y < ymin + h; y++) items.GetTile(x, y).Delete();
        }
        CommitMapPatches([MapPatch(m.Offsets.FieldItem + target.Layer * items.Tiles.Length * Item.SIZE, items.DumpAll())]);
    }
}
