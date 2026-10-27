using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record MapPoint(int X, int Y);
    internal sealed record MapSpawnOptions(string Source, string Arrangement, int Count, int RecipeMin, int RecipeMax);
    internal sealed record MapEditRule(string Key, string Operator, string Value);
    internal sealed record MapParameter(string Key, string Type, string Min, string Max, bool Writable);
    internal sealed record MapEditCount(int Requested, int Applied);
    private static readonly PropertyInfo[] MapProperties = typeof(Item).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && (p.PropertyType == typeof(bool) || p.PropertyType.IsEnum || p.PropertyType.IsPrimitive)).ToArray();
    private static (decimal Min, decimal Max) MapParameterLimits(PropertyInfo p) => p.Name switch
    {
        "Rotation" => (0, 3), "BodyType" => (0, 7), "PatternSource" => (0, 3), "PatternChoice" => (0, 2047),
        "DaysWatered" => (0, 31), "WrappingType" => (0, 4), "WrappingPaper" => (0, 15),
        _ when p.PropertyType == typeof(ulong) => (0, ulong.MaxValue),
        _ when p.PropertyType == typeof(bool) => (0, 1),
        _ => Limits(p.PropertyType),
    };
    private static bool MapParameterWritable(PropertyInfo p) => p.CanWrite && !p.Name.StartsWith("Extension", StringComparison.Ordinal);
    public static MapParameter[] MapParameters() => MapProperties.Select(p => {
        var (min, max) = MapParameterLimits(p);
        return new MapParameter(p.Name, p.PropertyType == typeof(bool) ? "boolean" : "number",
            min.ToString(CultureInfo.InvariantCulture), max.ToString(CultureInfo.InvariantCulture), MapParameterWritable(p));
    }).ToArray();
    private static (PropertyInfo Property, object Value) CheckMapRule(MapEditRule rule, bool write)
    {
        var p = MapProperties.FirstOrDefault(p => p.Name == rule.Key) ?? throw new ArgumentException("mapInvalidRule");
        if (write ? !MapParameterWritable(p) || rule.Operator != "set" : rule.Operator is not ("eq" or "ne" or "gt" or "ge" or "lt" or "le"))
            throw new ArgumentException("mapInvalidRule");
        if (p.PropertyType == typeof(bool))
        {
            if (!bool.TryParse(rule.Value, out var value)) throw new ArgumentException("mapInvalidRule");
            return (p, value);
        }
        var (min, max) = MapParameterLimits(p);
        if (!decimal.TryParse(rule.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new ArgumentException("mapInvalidRule");
        var type = p.PropertyType.IsEnum ? Enum.GetUnderlyingType(p.PropertyType) : p.PropertyType;
        var converted = Convert.ChangeType(n, type, CultureInfo.InvariantCulture);
        return (p, p.PropertyType.IsEnum ? Enum.ToObject(p.PropertyType, converted) : converted);
    }
    private LayerFieldItem MapItemLayer(MapTarget target)
    {
        CheckMapTarget(target, MapMarkerCount);
        if (target.Mode != "items") throw new ArgumentException("invalidRequest");
        var m = SaveFile.Main;
        return new(target.Layer == 0 ? m.GetFieldItemLayer0() : m.GetFieldItemLayer1(), m.FieldItemAcreWidth, 6);
    }
    private static void ValidateMapItem(Item item)
    {
        if (item.IsNone) return;
        if (!item.IsRoot || (!WorkspaceCatalog.Contains(item.ItemId) && !FieldItemList.Items.ContainsKey(item.ItemId)))
            throw new ArgumentException("mapInvalidItem");
    }
    private void CommitMapItems(MapTarget target, LayerFieldItem layer) => CommitMapPatches([
        MapPatch(SaveFile.Main.Offsets.FieldItem + target.Layer * layer.Tiles.Length * Item.SIZE, layer.DumpAll())]);
    private List<(int X, int Y, Item Item)> MapRoots(MapTarget target, bool whole, LayerFieldItem layer)
    {
        var (x0, y0) = whole ? (0, 0) : MapAcreOrigin(target, "items");
        var width = whole ? layer.TileInfo.TotalWidth : 32; var height = whole ? layer.TileInfo.TotalHeight : 32;
        var result = new List<(int X, int Y, Item Item)>();
        for (var x = x0; x < x0 + width; x++) for (var y = y0; y < y0 + height; y++)
        {
            var item = layer.GetTile(x, y);
            if (item.IsRoot) result.Add((x, y, item));
        }
        return result;
    }
    private static void ClearMapOwnedExtensions(LayerFieldItem layer, int x, int y, Item root)
    {
        var size = ItemInfo.GetItemSize(root);
        var width = (root.Rotation & 1) == 0 ? size.Width : size.Height;
        var height = (root.Rotation & 1) == 0 ? size.Height : size.Width;
        for (var dx = 0; dx < width; dx++) for (var dy = 0; dy < height; dy++)
        {
            if (!layer.Contains(x + dx, y + dy)) continue;
            var tile = layer.GetTile(x + dx, y + dy);
            if (tile.IsExtension && tile.ExtensionItemId == root.ItemId && tile.ExtensionX == dx && tile.ExtensionY == dy) tile.Delete();
        }
    }
    private MapEditCount ApplyMapRootEdits(MapTarget target, LayerFieldItem layer, List<(int X, int Y, Item Before, Item After)> edits, int requested)
    {
        var prepared = PrepareMapRootEdits(target, layer, edits, requested);
        prepared.Plan.Commit(); return prepared.Count;
    }
    private (GridPastePlan Plan, MapEditCount Count) PrepareMapRootEdits(MapTarget target, LayerFieldItem layer, List<(int X, int Y, Item Before, Item After)> edits, int requested)
    {
        var before = layer.Tiles.Select(item => item.RawValue).ToArray();
        edits = edits.Where(e => e.Before.RawValue != e.After.RawValue).ToList();
        foreach (var e in edits) ValidateMapItem(e.After);
        foreach (var e in edits) { ClearMapOwnedExtensions(layer, e.X, e.Y, e.Before); layer.GetTile(e.X, e.Y).Delete(); }
        foreach (var e in edits)
        {
            if (e.After.IsNone) continue;
            if (layer.IsOccupied(e.After, e.X, e.Y) != PlacedItemPermission.NoCollision) throw new GridPlacementException(PlacementConflicts(layer, e.After, e.X, e.Y));
            layer.SetExtensionTiles(e.After, e.X, e.Y);
            layer.GetTile(e.X, e.Y).CopyFrom(e.After);
        }
        var preview = ItemLayerPreview(new("mapItems", target), layer, before, requested, edits.Count);
        return (new(preview, () => CommitMapItems(target, layer)), new(requested, edits.Count));
    }
    public MapEditCount EditMapParameters(MapTarget target, bool whole, MapEditRule[] filters, MapEditRule[] changes)
    {
        var prepared = PrepareMapParameters(target, whole, filters, changes);
        prepared.Plan.Commit(); return prepared.Count;
    }
    public GridPastePreview PreviewMapParameters(MapTarget target, bool whole, MapEditRule[] filters, MapEditRule[] changes)
    {
        try { return PrepareMapParameters(target, whole, filters, changes).Plan.Preview; }
        catch (ArgumentException error) { return PreviewFailure(new("mapItems", target), error); }
    }
    private (GridPastePlan Plan, MapEditCount Count) PrepareMapParameters(MapTarget target, bool whole, MapEditRule[] filters, MapEditRule[] changes)
    {
        if (filters.Length > 32 || changes.Length is < 1 or > 32) throw new ArgumentException("mapInvalidRule");
        var f = filters.Select(r => CheckMapRule(r, false)).ToArray();
        var c = changes.Select(r => CheckMapRule(r, true)).ToArray();
        var layer = MapItemLayer(target);
        var roots = MapRoots(target, whole, layer).Where(root => filters.Select((r, i) => {
            var current = f[i].Property.GetValue(root.Item)!;
            var comparison = current is bool b ? b.CompareTo((bool)f[i].Value) : Convert.ToDecimal(current).CompareTo(Convert.ToDecimal(f[i].Value));
            return r.Operator switch { "eq" => comparison == 0, "ne" => comparison != 0, "gt" => comparison > 0, "ge" => comparison >= 0, "lt" => comparison < 0, _ => comparison <= 0 };
        }).All(v => v)).ToList();
        var edits = new List<(int X, int Y, Item Before, Item After)>();
        foreach (var root in roots)
        {
            var next = new Item(root.Item.RawValue);
            foreach (var (p, value) in c) p.SetValue(next, value);
            edits.Add((root.X, root.Y, new(root.Item.RawValue), next));
        }
        return PrepareMapRootEdits(target, layer, edits, roots.Count);
    }
    public MapEditCount ReplaceMapMatches(MapTarget target, bool whole, RoomItem replacement, bool remove)
    {
        var prepared = PrepareReplaceMapMatches(target, whole, replacement, remove);
        prepared.Plan.Commit(); return prepared.Count;
    }
    public GridPastePreview PreviewReplaceMapMatches(MapTarget target, bool whole, RoomItem replacement, bool remove)
    {
        try { return PrepareReplaceMapMatches(target, whole, replacement, remove).Plan.Preview; }
        catch (ArgumentException error) { return PreviewFailure(new("mapItems", target), error); }
    }
    private (GridPastePlan Plan, MapEditCount Count) PrepareReplaceMapMatches(MapTarget target, bool whole, RoomItem replacement, bool remove)
    {
        var layer = MapItemLayer(target);
        var (x, y) = MapCoordinates(target, target.X, target.Y);
        if (!layer.Contains(x, y)) throw new ArgumentException("mapInvalidAcre");
        var selected = layer.GetTile(x, y);
        if (selected.IsExtension)
        {
            var rootId = selected.ExtensionItemId;
            x -= selected.ExtensionX; y -= selected.ExtensionY;
            if (!layer.Contains(x, y)) throw new ArgumentException("mapInvalidItem");
            selected = layer.GetTile(x, y);
            if (selected.ItemId != rootId) throw new ArgumentException("mapInvalidItem");
        }
        if (!selected.IsRoot) throw new ArgumentException("mapInvalidItem");
        var next = remove ? new Item(Item.NONE) : replacement.ToItem();
        var roots = MapRoots(target, whole, layer).Where(r => r.Item.RawValue == selected.RawValue).ToList();
        return PrepareMapRootEdits(target, layer, roots.Select(r => (r.X, r.Y, new Item(r.Item.RawValue), new Item(next.RawValue))).ToList(), roots.Count);
    }
    public MapEditCount SpawnMapItems(MapTarget target, MapSpawnOptions options, RoomItem current, byte[]? file = null)
    {
        var layer = MapItemLayer(target);
        var (x0, y0) = MapCoordinates(target, target.X, target.Y);
        if (!layer.Contains(x0, y0) || options.Count is < 1 or > 1000 || options.Arrangement is not ("square" or "horizontal" or "vertical")) throw new ArgumentException("invalidRequest");
        Item[] source;
        switch (options.Source)
        {
            case "current": source = [current.ToItem()]; break;
            case "recipes": case "alphabetical":
                if (options.RecipeMin is < 0 or > ushort.MaxValue || options.RecipeMax < options.RecipeMin || options.RecipeMax > ushort.MaxValue) throw new ArgumentException("invalidRequest");
                var recipes = RecipeList.Recipes.Keys.Where(k => k is not (RecipeList.BridgeConstructionKit or RecipeList.CampsiteConstructionKit) && k >= options.RecipeMin && k <= options.RecipeMax);
                source = recipes.Select(k => new Item(Item.DIYRecipe) { FreeParam = k }).ToArray();
                if (options.Source == "alphabetical") source = source.OrderBy(i => GameInfo.Strings.GetItemName(i), StringComparer.CurrentCulture).ThenBy(i => i.FreeParam).ToArray();
                break;
            case "nhi":
                if (file is null || file.Length is < 8 or > 80000 || file.Length % 8 != 0) throw new ArgumentException("mapNhiSize");
                source = Item.GetArray(file).Where(i => !i.IsNone && !i.IsExtension).ToArray();
                foreach (var item in source) item.SystemParam = 0x20;
                break;
            default: throw new ArgumentException("invalidRequest");
        }
        if (source.Length == 0 || source.Length * (long)options.Count > 10000) throw new ArgumentException("mapSpawnLimit");
        foreach (var item in source) { ValidateMapItem(item); if (item.IsNone) throw new ArgumentException("mapInvalidItem"); }
        if (source.Any(i => i.IsFieldItem)) { x0 &= ~1; y0 &= ~1; }
        var sizes = source.Select(item => { var size = ItemInfo.GetItemSize(item); return (W: (item.Rotation & 1) == 0 ? size.Width : size.Height, H: (item.Rotation & 1) == 0 ? size.Height : size.Width); }).ToArray();
        var stepX = Math.Max(2, (sizes.Max(s => s.W) + 1) & ~1); var stepY = Math.Max(2, (sizes.Max(s => s.H) + 1) & ~1);
        var requested = source.Length * options.Count;
        var availableColumns = (layer.TileInfo.TotalWidth - x0 + stepX - 1) / stepX;
        var columns = options.Arrangement == "vertical" ? 1 : options.Arrangement == "horizontal" ? Math.Min(requested, availableColumns) : Math.Min(availableColumns, (int)Math.Ceiling(Math.Sqrt(requested * (double)stepY / stepX)));
        var placed = 0;
        for (var y = y0; y < layer.TileInfo.TotalHeight && placed < requested; y += stepY)
            for (var col = 0; col < columns && placed < requested; col++)
            {
                var x = x0 + col * stepX;
                var item = source[placed % source.Length];
                if (layer.IsOccupied(item, x, y) != PlacedItemPermission.NoCollision) continue;
                layer.SetExtensionTiles(item, x, y); layer.GetTile(x, y).CopyFrom(item); placed++;
            }
        CommitMapItems(target, layer);
        return new(requested, placed);
    }
}
