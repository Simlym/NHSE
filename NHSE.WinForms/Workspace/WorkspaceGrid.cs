using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record GridTarget(string Kind, MapTarget? Map = null, int House = 0, int Room = 0, int Layer = 0,
        int Player = 0, string Container = "pocket", int[]? Slots = null, int Columns = 10);
    internal sealed record GridClipboardInfo(string Kind, int Count, int Width, int Height);
    internal sealed record GridPastePreview(bool Valid, string? Error, int Placements, int ChangedCells, int ReplacedItems, MapPoint[] Cells,
        MapPoint[]? Conflicts = null, MapPoint[]? PlacedCells = null, MapPoint[]? ClearedCells = null);
    private sealed class GridPlacementException(MapPoint[] cells) : ArgumentException("roomPlacementBlocked")
    {
        public MapPoint[] Cells { get; } = cells;
    }
    private static MapPoint[] PlacementConflicts(LayerItem layer, Item item, int x, int y)
    {
        var (width, height) = GridItemSize(item);
        var result = new List<MapPoint>();
        for (var dx = 0; dx < width; dx++) for (var dy = 0; dy < height; dy++)
            if (!layer.Contains(x + dx, y + dy) || !layer.GetTile(x + dx, y + dy).IsNone) result.Add(new(x + dx, y + dy));
        return result.ToArray();
    }
    private GridPastePreview PreviewFailure(GridTarget target, ArgumentException error) => new(false, error.Message, 0, 0, 0, [],
        error is GridPlacementException placement ? placement.Cells.Select(p => GridPreviewCoordinate(target, p.X, p.Y)).Distinct().ToArray() : []);
    private GridPastePreview ItemLayerPreview(GridTarget target, LayerItem layer, ulong[] before, int placements, int replaced)
    {
        var changed = Enumerable.Range(0, layer.Tiles.Length).Where(i => layer.Tiles[i].RawValue != before[i]).ToArray();
        MapPoint Coordinate(int index) => GridPreviewCoordinate(target, index / layer.TileInfo.TotalHeight, index % layer.TileInfo.TotalHeight);
        var cells = changed.Select(Coordinate).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
        return new(true, null, placements, cells.Length, replaced, cells, [],
            changed.Where(i => !layer.Tiles[i].IsNone).Select(Coordinate).ToArray(),
            changed.Where(i => layer.Tiles[i].IsNone).Select(Coordinate).ToArray());
    }
    private sealed record GridPastePlan(GridPastePreview Preview, Action Commit);
    private sealed record GridEntry(int X, int Y, byte[] Data);
    private sealed record GridClipboard(GridClipboardInfo Info, GridEntry[] Entries);
    private GridClipboard? GridBuffer;
    private sealed record ItemGrid(LayerItem Layer, Action Save);
    private ItemGrid ReadItemGrid(GridTarget target)
    {
        if (target.Kind == "mapItems")
        {
            if (target.Map is not { Mode: "items" } map) throw new ArgumentException("invalidRequest");
            var layer = MapItemLayer(map);
            return new(layer, () => CommitMapItems(map, layer));
        }
        if (target.Kind != "roomItems" || (uint)target.Room >= 6 || (uint)target.Layer >= 8) throw new ArgumentException("invalidRequest");
        var before = HouseBytes(ReadHouse("playerHouses", target.House));
        var house = (IPlayerHouse)CloneHouse("playerHouses", before);
        var manager = new RoomManager(house.GetRoom(target.Room));
        return new(manager.Layers[target.Layer], () => {
            manager.Save(); house.SetRoom(target.Room, manager.Room);
            CommitHouse("playerHouses", target.House, before, house.Write());
        });
    }
    private LayerTerrain ReadTerrainGrid(GridTarget target)
    {
        if (target.Kind != "terrain" || target.Map is not { Mode: "terrain" } map) throw new ArgumentException("invalidRequest");
        CheckMapTarget(map, MapMarkerCount);
        return new(SaveFile.Main.GetTerrainTiles(), SaveFile.Main.GetAcreBytes());
    }
    private MapPoint GridCoordinate(GridTarget target, MapPoint p)
    {
        var size = target.Kind == "terrain" ? 16 : target.Kind == "mapItems" ? 32 : 20;
        if ((uint)p.X >= size || (uint)p.Y >= size) throw new ArgumentException("invalidRequest");
        if (target.Kind == "roomItems") return p;
        if (target.Map is null) throw new ArgumentException("invalidRequest");
        var (x, y) = MapCoordinates(target.Map, p.X, p.Y);
        var config = target.Kind == "terrain" ? TerrainConfig : ItemConfig;
        if (!config.IsCoordinateValidRelative(x, y)) throw new ArgumentException("mapInvalidAcre");
        return new(x, y);
    }
    private MapPoint[] GridPoints(GridTarget target, MapPoint[] points)
    {
        if (points.Length is < 1 or > 1024) throw new ArgumentException("invalidRequest");
        return points.Select(p => GridCoordinate(target, p)).Distinct().OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
    }
    private static MapPoint? GridRoot(LayerItem layer, MapPoint p)
    {
        if (!layer.Contains(p.X, p.Y)) throw new ArgumentException("roomPlacementBlocked");
        var tile = layer.GetTile(p.X, p.Y);
        if (tile.IsNone) return null;
        if (!tile.IsExtension) return p;
        var root = new MapPoint(p.X - tile.ExtensionX, p.Y - tile.ExtensionY);
        if (!layer.Contains(root.X, root.Y)) throw new ArgumentException("mapInvalidItem");
        var item = layer.GetTile(root.X, root.Y);
        if (!item.IsRoot || item.ItemId != tile.ExtensionItemId) throw new ArgumentException("mapInvalidItem");
        return root;
    }
    private static (int W, int H) GridItemSize(Item item)
    {
        var size = ItemInfo.GetItemSize(item);
        return (item.Rotation & 1) == 0 ? (size.Width, size.Height) : (size.Height, size.Width);
    }
    private static void ClearGridRoot(LayerItem layer, MapPoint p)
    {
        var root = layer.GetTile(p.X, p.Y);
        var (width, height) = GridItemSize(root);
        for (var x = 0; x < width; x++) for (var y = 0; y < height; y++)
        {
            if (!layer.Contains(p.X + x, p.Y + y)) continue;
            var tile = layer.GetTile(p.X + x, p.Y + y);
            if (tile.IsExtension && tile.ExtensionItemId == root.ItemId && tile.ExtensionX == x && tile.ExtensionY == y) tile.Delete();
        }
        root.Delete();
    }
    public void CopyGrid(GridTarget target, MapPoint[] selection)
    {
        if (target.Kind == "inventory") { CopyInventoryGrid(target, selection); return; }
        var terrain = target.Kind == "terrain" ? ReadTerrainGrid(target) : null;
        var items = terrain is null ? ReadItemGrid(target).Layer : null;
        var points = GridPoints(target, selection);
        var entries = terrain is not null
            ? points.Select(p => new GridEntry(p.X, p.Y, TerrainTile.SetArray([terrain.GetTile(p.X, p.Y)]))).ToArray()
            : points.Select(p => GridRoot(items!, p)).Where(p => p is not null).Distinct().Select(p => new GridEntry(p!.X, p.Y, Item.SetArray([items!.GetTile(p.X, p.Y)]))).ToArray();
        if (entries.Length == 0) throw new ArgumentException("gridEmptyCopy");
        var minX = Math.Min(points.Min(p => p.X), entries.Min(e => e.X));
        var minY = Math.Min(points.Min(p => p.Y), entries.Min(e => e.Y));
        var maxX = points.Max(p => p.X) + 1; var maxY = points.Max(p => p.Y) + 1;
        foreach (var entry in entries)
        {
            var (w, h) = terrain is null ? GridItemSize(new Item(BitConverter.ToUInt64(entry.Data))) : (1, 1);
            maxX = Math.Max(maxX, entry.X + w); maxY = Math.Max(maxY, entry.Y + h);
        }
        GridBuffer = new(new(terrain is null ? "items" : "terrain", entries.Length, maxX - minX, maxY - minY),
            entries.Select(e => new GridEntry(e.X - minX, e.Y - minY, e.Data.ToArray())).ToArray());
    }
    public void PasteGrid(GridTarget target, MapPoint anchor, MapPoint[] selection, bool noOverwrite)
    {
        if (target.Kind == "inventory") { ChangeInventoryGrid(target, selection, "paste", anchor); return; }
        PrepareGridPaste(target, anchor, selection, noOverwrite).Commit();
    }
    // Both operations validate and edit independent decoded layers. Only PasteGrid invokes the resulting commit.
    public GridPastePreview PreviewGridPaste(GridTarget target, MapPoint anchor, MapPoint[] selection, bool noOverwrite)
    {
        try { return PrepareGridPaste(target, anchor, selection, noOverwrite).Preview; }
        catch (ArgumentException ex) { return PreviewFailure(target, ex); }
    }
    private MapPoint GridPreviewCoordinate(GridTarget target, int x, int y)
    {
        if (target.Map is null) return new(x, y);
        var (offsetX, offsetY) = MapCoordinates(target.Map, 0, 0);
        return new(x - offsetX, y - offsetY);
    }
    private GridPastePlan PrepareGridPaste(GridTarget target, MapPoint anchor, MapPoint[] selection, bool noOverwrite)
    {
        if (target.Kind == "inventory") throw new ArgumentException("invalidRequest");
        var buffer = GridBuffer ?? throw new ArgumentException("gridNoClipboard");
        if (buffer.Info.Kind != (target.Kind == "terrain" ? "terrain" : "items")) throw new ArgumentException("gridTypeMismatch");
        var terrain = target.Kind == "terrain" ? ReadTerrainGrid(target) : null;
        var grid = terrain is null ? ReadItemGrid(target) : null;
        var points = GridPoints(target, selection);
        var origin = GridCoordinate(target, anchor);
        var repeat = buffer.Entries.Length == 1 && points.Length > 1;
        if (!repeat && grid is not null && buffer.Entries.Length == 1)
            origin = GridRoot(grid.Layer, origin) ?? origin;
        var entries = repeat
            ? points.Select(p => new GridEntry(p.X, p.Y, buffer.Entries[0].Data)).ToList()
            : buffer.Entries.Select(e => new GridEntry(origin.X + e.X, origin.Y + e.Y, e.Data)).ToList();
        if (terrain is not null)
        {
            var before = terrain.DumpAll();
            foreach (var e in entries)
            {
                if (!terrain.Contains(e.X, e.Y)) throw new GridPlacementException([new(e.X, e.Y)]);
                terrain.GetTile(e.X, e.Y).CopyFrom(TerrainTile.GetArray(e.Data)[0]);
            }
            var after = terrain.DumpAll();
            var changed = Enumerable.Range(0, terrain.Tiles.Length)
                .Where(i => !before.AsSpan(i * TerrainTile.SIZE, TerrainTile.SIZE).SequenceEqual(after.AsSpan(i * TerrainTile.SIZE, TerrainTile.SIZE)))
                .Select(i => GridPreviewCoordinate(target, i / terrain.TileInfo.TotalHeight, i % terrain.TileInfo.TotalHeight))
                .OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
            return new(new(true, null, entries.Count, changed.Length, 0, changed, [], changed, []),
                () => CommitMapPatches([MapPatch(SaveFile.Main.Offsets.LandMakingMap, after)]));
        }
        var layer = grid!.Layer;
        var beforeItems = layer.Tiles.Select(item => item.RawValue).ToArray();
        var planned = new List<(MapPoint Point, Item Item)>();
        var covered = new HashSet<MapPoint>();
        foreach (var entry in entries)
        {
            var point = new MapPoint(entry.X, entry.Y);
            if (repeat && covered.Contains(point)) continue;
            var item = Item.GetArray(entry.Data)[0]; ValidateMapItem(item);
            if (target.Kind == "roomItems" && !WorkspaceCatalog.Contains(item.ItemId)) throw new ArgumentException("gridRoomItem");
            var (w, h) = GridItemSize(item);
            if (!layer.Contains(point.X, point.Y) || !layer.Contains(point.X + w - 1, point.Y + h - 1)) throw new GridPlacementException(PlacementConflicts(layer, item, point.X, point.Y));
            planned.Add((point, item));
            for (var x = 0; x < w; x++) for (var y = 0; y < h; y++) covered.Add(new(point.X + x, point.Y + y));
        }
        var remove = new HashSet<MapPoint>();
        foreach (var (point, item) in planned)
        {
            if (!layer.GetTile(point.X, point.Y).IsExtension && GridRoot(layer, point) is { } root) remove.Add(root);
            if (noOverwrite) continue;
            var (w, h) = GridItemSize(item);
            for (var x = 0; x < w; x++) for (var y = 0; y < h; y++)
                if (GridRoot(layer, new(point.X + x, point.Y + y)) is { } overlap) remove.Add(overlap);
        }
        foreach (var point in remove) ClearGridRoot(layer, point);
        foreach (var (point, item) in planned)
        {
            if (layer.IsOccupied(item, point.X, point.Y) != PlacedItemPermission.NoCollision) throw new GridPlacementException(PlacementConflicts(layer, item, point.X, point.Y));
            layer.SetExtensionTiles(item, point.X, point.Y); layer.GetTile(point.X, point.Y).CopyFrom(item);
        }
        return new(ItemLayerPreview(target, layer, beforeItems, planned.Count, remove.Count), grid.Save);
    }
    public void EditGrid(GridTarget target, MapPoint[] selection, string operation)
    {
        if (operation is not ("delete" or "rotate")) throw new ArgumentException("invalidRequest");
        if (target.Kind == "inventory") { ChangeInventoryGrid(target, selection, operation, new(0, 0)); return; }
        if (target.Kind == "terrain")
        {
            var terrain = ReadTerrainGrid(target);
            foreach (var point in GridPoints(target, selection))
            {
                var tile = terrain.GetTile(point.X, point.Y);
                if (operation == "delete") tile.Clear(); else tile.TryRotate();
            }
            CommitMapPatches([MapPatch(SaveFile.Main.Offsets.LandMakingMap, terrain.DumpAll())]);
            return;
        }
        var grid = ReadItemGrid(target); var layer = grid.Layer;
        var roots = GridPoints(target, selection).Select(p => GridRoot(layer, p)).Where(p => p is not null).Distinct().Select(p => p!).ToArray();
        var changed = roots.Select(p => (Point: p, Item: new Item(layer.GetTile(p.X, p.Y).RawValue))).ToArray();
        foreach (var point in roots) ClearGridRoot(layer, point);
        if (operation == "rotate") foreach (var (point, item) in changed)
        {
            item.Rotation = (item.Rotation + 1) & 3;
            if (layer.IsOccupied(item, point.X, point.Y) != PlacedItemPermission.NoCollision) throw new GridPlacementException(PlacementConflicts(layer, item, point.X, point.Y));
            layer.SetExtensionTiles(item, point.X, point.Y); layer.GetTile(point.X, point.Y).CopyFrom(item);
        }
        grid.Save();
    }

    private int InventorySlot(GridTarget target, MapPoint point)
    {
        var items = GetItems(target.Player, target.Container);
        if (target.Columns is < 1 or > 60 || target.Slots is not { Length: > 0 and <= 60 } slots
            || slots.Distinct().Count() != slots.Length || slots.Any(i => (uint)i >= items.Length)
            || point.X < 0 || point.X >= target.Columns || point.Y < 0 || point.Y >= 60)
            throw new ArgumentException("invalidRequest");
        var position = point.Y * target.Columns + point.X;
        if (position >= slots.Length || !IsSlotAvailable(target.Player, target.Container, slots[position]))
            throw new ArgumentException("invalidRequest");
        return slots[position];
    }
    private MapPoint[] InventoryPoints(GridTarget target, MapPoint[] selection)
    {
        if (selection.Length is < 1 or > 60) throw new ArgumentException("invalidRequest");
        var points = selection.Distinct().OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
        foreach (var point in points) InventorySlot(target, point);
        return points;
    }
    private void CopyInventoryGrid(GridTarget target, MapPoint[] selection)
    {
        var points = InventoryPoints(target, selection);
        var items = GetItems(target.Player, target.Container);
        var minX = points.Min(p => p.X); var minY = points.Min(p => p.Y);
        var entries = points.Where(p => !items[InventorySlot(target, p)].IsNone)
            .Select(p => new GridEntry(p.X - minX, p.Y - minY, Item.SetArray([items[InventorySlot(target, p)]]))).ToArray();
        if (entries.Length == 0) throw new ArgumentException("gridEmptyCopy");
        GridBuffer = new(new("items", entries.Length, points.Max(p => p.X) - minX + 1, points.Max(p => p.Y) - minY + 1), entries);
    }
    private void ChangeInventoryGrid(GridTarget target, MapPoint[] selection, string operation, MapPoint anchor)
    {
        var points = InventoryPoints(target, selection);
        var before = GetItems(target.Player, target.Container).Select(i => i.RawValue).ToArray();
        var after = before.ToArray();
        if (operation == "paste")
        {
            var buffer = GridBuffer ?? throw new ArgumentException("gridNoClipboard");
            if (buffer.Info.Kind != "items") throw new ArgumentException("gridTypeMismatch");
            InventorySlot(target, anchor);
            var entries = buffer.Entries.Length == 1 && points.Length > 1
                ? points.Select(p => new GridEntry(p.X, p.Y, buffer.Entries[0].Data))
                : buffer.Entries.Select(e => new GridEntry(anchor.X + e.X, anchor.Y + e.Y, e.Data));
            foreach (var entry in entries)
            {
                var slot = InventorySlot(target, new(entry.X, entry.Y));
                var item = Item.GetArray(entry.Data)[0];
                if (item.IsExtension) throw new ArgumentException("mapInvalidItem");
                after[slot] = item.RawValue;
            }
        }
        else foreach (var point in points)
        {
            var slot = InventorySlot(target, point);
            var item = new Item(after[slot]);
            if (operation == "delete") item.Delete();
            else if (!item.IsNone) item.Rotation = (item.Rotation + 1) & 3;
            after[slot] = item.RawValue;
        }
        var changes = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
        if (changes.Length == 0) return;
        void Apply(ulong[] data)
        {
            var current = GetItems(target.Player, target.Container);
            foreach (var slot in changes) current[slot] = new Item(data[slot]);
            PutItems(target.Player, target.Container, current);
        }
        Commit(() => Apply(before), () => Apply(after));
    }
}
