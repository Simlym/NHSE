using System;
using System.Linq;
using System.Text.Json;
using NHSE.Core;
using NHSE.WinForms;

internal static class GridPreviewChecks
{
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static string State(WorkspaceSession session) => JsonSerializer.Serialize(session.Snapshot());
    private static WorkspaceSession.MapPoint[] Changes(LayerItem before, LayerItem after) => Enumerable.Range(0, before.Tiles.Length)
        .Where(i => before.Tiles[i].RawValue != after.Tiles[i].RawValue)
        .Select(i => new WorkspaceSession.MapPoint(i / before.TileInfo.TotalHeight, i % before.TileInfo.TotalHeight))
        .OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
    public static void Run(MemoryProvider provider)
    {
        var save = new HorizonSave(provider);
        var main = save.Main;
        var layer = new LayerFieldItem(main.GetFieldItemLayer0().Select(_ => new Item(Item.NONE)).ToArray(), main.FieldItemAcreWidth, 6);
        var first = new Item(2500) { SystemParam = 0x20, AdditionalParam = 0xAB, FreeParam = int.MinValue + 97 };
        var second = new Item(2500) { SystemParam = 0x20, FreeParam = 12345 };
        layer.GetTile(2, 2).CopyFrom(first); layer.SetExtensionTiles(first, 2, 2);
        layer.GetTile(6, 2).CopyFrom(second); layer.SetExtensionTiles(second, 6, 2);
        main.SetFieldItemLayer0(layer.Tiles);
        var map = new WorkspaceSession.MapTarget("items", 10, 0, 0, 0, 0, 0);
        var target = new WorkspaceSession.GridTarget("mapItems", map);
        var session = new WorkspaceSession(save);
        WorkspaceSession.GridPastePreview Read(WorkspaceSession.GridTarget destination, WorkspaceSession.MapPoint anchor, WorkspaceSession.MapPoint[] selection, bool noOverwrite = true)
        {
            var before = main.Data.ToArray(); var state = State(session);
            var result = session.PreviewGridPaste(destination, anchor, selection, noOverwrite);
            Check(main.Data.SequenceEqual(before) && State(session) == state, "paste preview preserves every main byte, dirty state, clipboard and history");
            return result;
        }
        session.CopyGrid(target, [new(2, 2), new(3, 2), new(6, 2)]);
        var preview = Read(target, new(10, 10), [new(10, 10)]);
        Check(preview is { Valid: true, Placements: 2, ChangedCells: 8, ReplacedItems: 0 }
            && preview.Cells.Contains(new(14, 10)) && !preview.Cells.Contains(new(12, 10)), "multi-object preview includes complete footprints while preserving clipboard gaps");
        session.PasteGrid(target, new(10, 10), [new(10, 10)], true);
        var pasted = main.Data.ToArray();
        var actual = new LayerFieldItem(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(Changes(layer, actual).SequenceEqual(preview.Cells), "preview changed cells exactly match the subsequently committed item bytes");
        session.Undo();
        preview = Read(target, new(10, 10), [new(10, 10)]);
        session.Redo(); Check(main.Data.SequenceEqual(pasted), "preview does not consume or invalidate an existing redo transaction");
        session.Undo(); session.Undo(); Check(!session.IsDirty, "read-only preview creates no extra undo entry");
        preview = Read(target, new(2, 2), [new(2, 2)]);
        Check(preview is { Valid: true, Placements: 2, ChangedCells: 0 } && preview.Cells.Length == 0, "identical paste preview reports zero actual byte changes");
        session.CopyGrid(target, [new(3, 2)]);
        preview = Read(target, new(3, 2), [new(3, 2)]);
        Check(preview is { Valid: true, Placements: 1, ChangedCells: 0 }, "single-object preview redirects selected extensions to the existing root");
        var selection = Enumerable.Range(0, 16).Select(i => new WorkspaceSession.MapPoint(10 + i % 4, 10 + i / 4)).ToArray();
        preview = Read(target, new(10, 10), selection);
        Check(preview is { Valid: true, Placements: 4, ChangedCells: 16 }, "repeated paste preview counts packed objects rather than every selected extension cell");
        preview = Read(target, new(1, 2), [new(1, 2)]);
        Check(preview is { Valid: false, Error: "roomPlacementBlocked", ChangedCells: 0 }, "occupied footprints produce a blocked preview without partial writes");
        preview = Read(target, new(1, 2), [new(1, 2)], false);
        Check(preview is { Valid: true, Placements: 1, ReplacedItems: 1, ChangedCells: 6 }
            && preview.Cells.Contains(new(3, 3)), "overlap preview includes cleared cells beyond the new object's footprint");
        session.PasteGrid(target, new(1, 2), [new(1, 2)], false);
        actual = new LayerFieldItem(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(Changes(layer, actual).SequenceEqual(preview.Cells), "replacement preview and actual paste use exactly the same collision and cleanup plan"); session.Undo();
        preview = Read(target, new(31, 12), [new(31, 12)]);
        Check(preview.Valid && preview.Cells.Contains(new(32, 12)), "preview coordinates retain changed cells in neighboring acres instead of clipping them");
        preview = Read(target with { Map = map with { Acre = 61 } }, new(31, 31), [new(31, 31)]);
        Check(preview is { Valid: false, Error: "roomPlacementBlocked" }, "outside-map preview rejects complete object footprints at map boundaries");
        var roomTarget = new WorkspaceSession.GridTarget("roomItems", House: 0, Room: 0, Layer: 7);
        var house = main.GetPlayerHouse(0); var room = house.GetRoom(0); var roomLayers = room.GetItemLayers();
        foreach (var tile in roomLayers[7].Tiles) tile.Delete();
        room.SetItemLayers(roomLayers); house.SetRoom(0, room); main.SetPlayerHouse(house, 0);
        preview = Read(roomTarget, new(4, 5), [new(4, 5)]);
        Check(preview is { Valid: true, Placements: 1, ChangedCells: 4 } && preview.Cells.Contains(new(5, 6)), "room previews use room-relative coordinates and preserve the cloned house");
        preview = Read(roomTarget, new(19, 19), [new(19, 19)]);
        Check(!preview.Valid, "room boundary previews reject before changing cloned or saved furniture");
        preview = Read(new("inventory"), new(0, 0), [new(0, 0)]);
        Check(preview is { Valid: false, Error: "invalidRequest" }, "unsupported inventory previews return an explicit read-only validation result");
        var terrain = main.GetTerrainTiles().Select(_ => new TerrainTile()).ToArray();
        terrain[0].Elevation = 2; terrain[0].Variation = 123;
        terrain[96].Elevation = 3; terrain[96].UnitModelRoad = TerrainUnitModel.River5B;
        main.SetTerrainTiles(terrain);
        var terrainTarget = new WorkspaceSession.GridTarget("terrain", map with { Mode = "terrain" });
        session.CopyGrid(terrainTarget, [new(0, 0), new(1, 0)]);
        preview = Read(terrainTarget, new(15, 5), [new(15, 5)]);
        Check(preview is { Valid: true, Placements: 2, ChangedCells: 2, ReplacedItems: 0 }
            && preview.Cells.SequenceEqual(new WorkspaceSession.MapPoint[] { new(15, 5), new(16, 5) }), "terrain preview preserves raw fourteen-byte tiles and cross-acre changes");
        var terrainBefore = main.GetTerrainTiles();
        session.PasteGrid(terrainTarget, new(15, 5), [new(15, 5)], true);
        var terrainAfter = main.GetTerrainTiles();
        var terrainChanges = Enumerable.Range(0, terrainBefore.Length)
            .Where(i => !terrainBefore[i].ToBytesClass().SequenceEqual(terrainAfter[i].ToBytesClass()))
            .Select(i => new WorkspaceSession.MapPoint(i / 96, i % 96)).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
        Check(terrainChanges.SequenceEqual(preview.Cells), "terrain preview cells exactly match the committed native tile differences"); session.Undo();
        preview = Read(terrainTarget, new(0, 0), [new(0, 0)]);
        Check(preview is { Valid: true, Placements: 2, ChangedCells: 0 }, "unchanged terrain previews do not report bounding-box cells as changes");
        preview = Read(target, new(10, 10), [new(10, 10)]);
        Check(preview is { Valid: false, Error: "gridTypeMismatch" }, "clipboard type mismatches return a blocked preview without replacing the buffer");
    }
}
