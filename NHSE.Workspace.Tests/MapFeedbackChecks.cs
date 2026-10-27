using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NHSE.Core;
using NHSE.WinForms;

internal static class MapFeedbackChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static Dictionary<string, JsonElement> Values(params (string Key, int Value)[] entries) => entries.ToDictionary(e => e.Key, e => JsonSerializer.SerializeToElement(e.Value));
    public static void Run(MemoryProvider provider)
    {
        var save = new HorizonSave(provider); var main = save.Main;
        var initialLayer = new LayerFieldItem(main.GetFieldItemLayer0().Select(_ => new Item(Item.NONE)).ToArray(), main.FieldItemAcreWidth, 6);
        var first = new Item(2500) { SystemParam = 0x20, FreeParam = 17 };
        var second = new Item(2500) { SystemParam = 0x20, FreeParam = 12345 };
        initialLayer.GetTile(2, 2).CopyFrom(first); initialLayer.SetExtensionTiles(first, 2, 2);
        initialLayer.GetTile(6, 2).CopyFrom(second); initialLayer.SetExtensionTiles(second, 6, 2);
        main.SetFieldItemLayer0(initialLayer.Tiles);
        main.SetTerrainTiles(main.GetTerrainTiles().Select(_ => new TerrainTile()).ToArray());
        var session = new WorkspaceSession(save);
        var map = new WorkspaceSession.MapTarget("items", 10, 0, 2, 2, 0, 0);
        var target = new WorkspaceSession.GridTarget("mapItems", map);
        WorkspaceSession.GridPastePreview Read(Func<WorkspaceSession.GridPastePreview> action)
        {
            var bytes = main.Data.ToArray(); var personal = save.Players.Select(p => p.Personal.Data.ToArray()).ToArray();
            var state = JsonSerializer.Serialize(session.Snapshot());
            var result = action();
            Check(main.Data.SequenceEqual(bytes) && save.Players.Select((p, i) => p.Personal.Data.SequenceEqual(personal[i])).All(v => v)
                && state == JsonSerializer.Serialize(session.Snapshot()), "map feedback is read-only for all save bytes, clipboard and undo/redo state");
            return result;
        }
        session.CopyGrid(target, [new(2, 2)]);
        var preview = Read(() => session.PreviewGridPaste(target, new(1, 2), [new(1, 2)], true));
        Check(!preview.Valid && preview.Conflicts is { Length: 2 } && preview.Conflicts.Contains(new(2, 2)) && preview.Conflicts.Contains(new(2, 3)), "blocked paste pinpoints occupied destination cells instead of the whole selection");
        preview = Read(() => session.PreviewGridPaste(target, new(10, 10), [new(10, 10)], true));
        Check(preview.Valid && preview.ChangedCells == 4 && preview.PlacedCells is { Length: 4 } && preview.ClearedCells is { Length: 0 }, "new item preview classifies every changed root and extension as placed");
        var before = main.Data.ToArray();
        session.PasteGrid(target, new(10, 10), [new(10, 10)], true);
        Check(main.GetFieldItemLayer0().Count(i => !i.IsNone) == initialLayer.Tiles.Count(i => !i.IsNone) + preview.ChangedCells, "successful paste changes exactly the previewed complete footprint");
        session.Undo(); Check(main.Data.SequenceEqual(before), "feedback adds no history entry before the one real placement");
        preview = Read(() => session.PreviewGridPaste(target, new(1, 2), [new(1, 2)], false));
        Check(preview.Valid && preview.ChangedCells == 6 && preview.PlacedCells is { Length: 4 } && preview.ClearedCells is { Length: 2 }
            && preview.ClearedCells.Contains(new(3, 3)), "overlap replacement distinguishes new placement from cleared old footprint cells");
        preview = Read(() => session.PreviewGridPaste(target with { Map = map with { Acre = 61 } }, new(31, 31), [new(31, 31)], true));
        Check(!preview.Valid && preview.Conflicts is { Length: > 0 }, "boundary failures retain the outside-map conflict coordinates");
        preview = Read(() => session.PreviewBatchMap(map, "removeAll", false, false, []));
        Check(preview.Valid && preview.Placements == 2 && preview.ChangedCells == 8 && preview.ClearedCells is { Length: 8 }, "batch deletion preview counts deduplicated roots and their actual cleared footprint");
        before = main.Data.ToArray(); session.BatchMap(map, "removeAll", false, false, []);
        Check(main.GetFieldItemLayer0().All(i => i.IsNone), "batch preview and actual operation share complete footprint removal");
        session.Undo(); Check(main.Data.SequenceEqual(before), "batch review preserves the single byte-exact undo transaction");
        WorkspaceSession.MapEditRule[] filters = [new("FreeParam", "eq", "12345")], changes = [new("FreeParam", "set", "777")];
        preview = Read(() => session.PreviewMapParameters(map, false, filters, changes));
        Check(preview.Valid && preview.Placements == 1 && preview.ChangedCells == 1 && preview.Cells.Contains(new(6, 2)), "parameter preview applies the same filters and reports only changed raw cells");
        var result = session.EditMapParameters(map, false, filters, changes);
        Check(result.Applied == 1 && main.GetFieldItemLayer0()[6 * 192 + 2].FreeParam == 777, "parameter commit agrees with the read-only hit and location"); session.Undo();
        preview = Read(() => session.PreviewReplaceMapMatches(map, false, new(2500, 0x20, 0, 456), false));
        Check(preview.Valid && preview.Placements == 1 && preview.ChangedCells == 1, "replacement preview matches the selected exact raw item rather than all shared item IDs");
        session.ReplaceMapMatches(map, false, new(2500, 0x20, 0, 456), false);
        Check(main.GetFieldItemLayer0()[2 * 192 + 2].FreeParam == 456 && main.GetFieldItemLayer0()[6 * 192 + 2].FreeParam == 12345, "exact replacement commit preserves unmatched objects"); session.Undo();
        preview = Read(() => session.PreviewMapParameters(map, false, [new("FreeParam", "eq", "9999")], changes));
        Check(preview.Valid && preview.Placements == 0 && preview.ChangedCells == 0, "empty parameter matches return an honest zero-change review");
        preview = Read(() => session.PreviewBatchMap(map, "unknown", false, false, []));
        Check(!preview.Valid && preview.Error == "invalidRequest", "invalid batch requests cannot become an actionable preview");
        var terrainMap = map with { Mode = "terrain", X = 8, Y = 8 };
        var masks = WorkspaceSession.MapBrushMasks();
        Check(masks.Length == 8 && masks.Select(m => m.Radius).SequenceEqual(Enumerable.Range(1, 8)) && masks[0].Offsets.SequenceEqual([new WorkspaceSession.MapPoint(0, 0)]), "brush masks expose all supported radii and the single-cell brush");
        foreach (var mask in masks)
        {
            before = main.Data.ToArray();
            session.PaintMapStroke(terrainMap, mask.Radius, false, Values(("Map.Elevation", 2)), [new(8, 8)]);
            var actual = main.GetTerrainTiles().Select((tile, index) => (tile, index)).Where(e => e.tile.Elevation == 2)
                .Select(e => new WorkspaceSession.MapPoint(e.index / 96 - 8, e.index % 96 - 8)).ToHashSet();
            Check(actual.SetEquals(mask.Offsets), $"brush radius {mask.Radius} preview mask exactly matches written terrain cells");
            session.Undo(); Check(main.Data.SequenceEqual(before), $"brush radius {mask.Radius} remains one exact undo step");
        }
        preview = Read(() => session.PreviewBatchMap(terrainMap, "setTerrain", false, false, Values(("Map.Elevation", 3))));
        Check(preview.Valid && preview.Placements == 256 && preview.ChangedCells == 256 && preview.PlacedCells is { Length: 256 }, "terrain batch preview counts all fourteen-byte tile changes in the requested acre");
        session.BatchMap(terrainMap, "setTerrain", false, false, Values(("Map.Elevation", 3)));
        Check(main.GetTerrainTiles().Count(t => t.Elevation == 3) == preview.ChangedCells, "terrain batch actual writes agree with reviewed changed-cell count"); session.Undo();
        preview = Read(() => session.PreviewBatchMap(terrainMap with { Mode = "markers" }, "clearDesigns", true, false, []));
        Check(preview.Valid && preview.Cells.Length == 0 && preview.PlacedCells is { Length: 0 } && preview.ClearedCells is { Length: 0 }, "design-slot clearing reports counts without inventing spatial map cells");
    }
}
