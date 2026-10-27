using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    public GridPastePreview PreviewMoveMap(MapTarget target, MapPoint[] selection, MapPoint anchor, bool noOverwrite, bool snapFieldItems = true)
    {
        try { return PrepareMoveMap(target, selection, anchor, noOverwrite, snapFieldItems).Preview; }
        catch (ArgumentException error) { return PreviewFailure(new("mapItems", target), error); }
    }
    public void MoveMap(MapTarget target, MapPoint[] selection, MapPoint anchor, bool noOverwrite, bool snapFieldItems = true)
        => PrepareMoveMap(target, selection, anchor, noOverwrite, snapFieldItems).Commit();
    private GridPastePlan PrepareMoveMap(MapTarget target, MapPoint[] selection, MapPoint anchor, bool noOverwrite, bool snapFieldItems)
    {
        var gridTarget = new GridTarget("mapItems", target);
        var layer = MapItemLayer(target);
        var before = layer.Tiles.Select(item => item.RawValue).ToArray();
        var roots = GridPoints(gridTarget, selection).Select(point => GridRoot(layer, point)).OfType<MapPoint>().Distinct().ToArray();
        if (roots.Length == 0) throw new ArgumentException("gridEmptyCopy");
        var destination = InteractionCoordinate(target, anchor);
        var minX = roots.Min(point => point.X); var minY = roots.Min(point => point.Y);
        if (snapFieldItems && roots.Any(point => layer.GetTile(point.X, point.Y).IsFieldItem))
            destination = new(minX + ((destination.X - minX) & ~1), minY + ((destination.Y - minY) & ~1));
        var entries = roots.Select(point => (Point: new MapPoint(destination.X + point.X - minX, destination.Y + point.Y - minY), Item: new Item(layer.GetTile(point.X, point.Y).RawValue))).ToArray();
        foreach (var root in roots) ClearGridRoot(layer, root);
        var replaced = PlaceInteractionItems(layer, entries, noOverwrite);
        return new(ItemLayerPreview(gridTarget, layer, before, entries.Length, replaced), () => CommitMapItems(target, layer));
    }
    public GridPastePreview PreviewPlaceMap(MapTarget target, RoomItem current, bool noOverwrite, bool snapFieldItems)
    {
        try { return PreparePlaceMap(target, current, noOverwrite, snapFieldItems).Preview; }
        catch (ArgumentException error) { return PreviewFailure(new("mapItems", target), error); }
    }
    public void PlaceMap(MapTarget target, RoomItem current, bool noOverwrite, bool snapFieldItems)
        => PreparePlaceMap(target, current, noOverwrite, snapFieldItems).Commit();
    private GridPastePlan PreparePlaceMap(MapTarget target, RoomItem current, bool noOverwrite, bool snapFieldItems)
    {
        var layer = MapItemLayer(target);
        var before = layer.Tiles.Select(item => item.RawValue).ToArray();
        var item = current.ToItem(); ValidateMapItem(item);
        if (item.IsNone || item.IsExtension) throw new ArgumentException("mapInvalidItem");
        var point = InteractionCoordinate(target, new(target.X, target.Y));
        if (snapFieldItems && item.IsFieldItem) point = new(point.X & ~1, point.Y & ~1);
        var replaced = PlaceInteractionItems(layer, [(point, item)], noOverwrite);
        return new(ItemLayerPreview(new("mapItems", target), layer, before, 1, replaced), () => CommitMapItems(target, layer));
    }
    private MapPoint InteractionCoordinate(MapTarget target, MapPoint point)
    {
        if (point.X is < -288 or > 288 || point.Y is < -256 or > 256) throw new ArgumentException("invalidRequest");
        var (x, y) = MapCoordinates(target, point.X, point.Y);
        return new(x, y);
    }
    private static int PlaceInteractionItems(LayerItem layer, (MapPoint Point, Item Item)[] entries, bool noOverwrite)
    {
        var remove = new HashSet<MapPoint>();
        foreach (var (point, item) in entries)
        {
            ValidateMapItem(item);
            var (width, height) = GridItemSize(item);
            if (!layer.Contains(point.X, point.Y) || !layer.Contains(point.X + width - 1, point.Y + height - 1))
                throw new GridPlacementException(PlacementConflicts(layer, item, point.X, point.Y));
            if (!noOverwrite) for (var dx = 0; dx < width; dx++) for (var dy = 0; dy < height; dy++)
                if (GridRoot(layer, new(point.X + dx, point.Y + dy)) is { } root) remove.Add(root);
        }
        foreach (var point in remove) ClearGridRoot(layer, point);
        foreach (var (point, item) in entries)
        {
            if (layer.IsOccupied(item, point.X, point.Y) != PlacedItemPermission.NoCollision)
                throw new GridPlacementException(PlacementConflicts(layer, item, point.X, point.Y));
            layer.SetExtensionTiles(item, point.X, point.Y); layer.GetTile(point.X, point.Y).CopyFrom(item);
        }
        return remove.Count;
    }
}
