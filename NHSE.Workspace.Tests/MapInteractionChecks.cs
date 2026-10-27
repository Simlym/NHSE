using System;
using System.Linq;
using System.Text.Json;
using NHSE.Core;
using NHSE.WinForms;

internal static class MapInteractionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static string State(WorkspaceSession session) => JsonSerializer.Serialize(session.Snapshot());
    public static void Run(MemoryProvider provider)
    {
        var save = new HorizonSave(provider); var main = save.Main;
        var layer = new LayerFieldItem(main.GetFieldItemLayer0().Select(_ => new Item(Item.NONE)).ToArray(), main.FieldItemAcreWidth, 6);
        var first = new Item(2500) { SystemParam = 0x20, AdditionalParam = 0xAB, FreeParam = int.MinValue + 97 };
        var second = new Item(2500) { SystemParam = 0x20, FreeParam = 777 };
        layer.GetTile(2, 2).CopyFrom(first); layer.SetExtensionTiles(first, 2, 2);
        layer.GetTile(6, 2).CopyFrom(second); layer.SetExtensionTiles(second, 6, 2);
        main.SetFieldItemLayer0(layer.Tiles);
        var session = new WorkspaceSession(save); var target = new WorkspaceSession.MapTarget("items", 10, 0, 0, 0, 0, 0);
        session.CopyGrid(new("mapItems", target), [new(2, 2)]);
        var initial = main.Data.ToArray(); var state = State(session);
        var preview = session.PreviewMoveMap(target, [new(3, 2), new(2, 2)], new(10, 10), true);
        Check(preview is { Valid: true, Placements: 1, ChangedCells: 8 } && main.Data.SequenceEqual(initial) && State(session) == state, "movement preview deduplicates root and extension without touching bytes or history");
        session.MoveMap(target, [new(3, 2), new(2, 2)], new(10, 10), true);
        var after = new LayerFieldItem(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(10, 10).RawValue == first.RawValue && after.GetTile(2, 2).IsNone && after.GetTile(3, 3).IsNone, "atomic movement preserves raw item parameters and clears complete source footprints");
        var moved = main.Data.ToArray(); session.Undo(); Check(main.Data.SequenceEqual(initial), "moving an object undoes source and destination in one byte-exact step");
        state = State(session); session.PreviewMoveMap(target, [new(2, 2)], new(3, 2), true);
        Check(State(session) == state, "movement preview preserves redo and clipboard state");
        session.Redo(); Check(main.Data.SequenceEqual(moved), "movement redo restores the complete atomic transaction"); session.Undo();
        preview = session.PreviewMoveMap(target, [new(2, 2)], new(2, 2), true);
        Check(preview is { Valid: true, ChangedCells: 0 }, "movement back onto its own root is a no-op");
        state = State(session); session.MoveMap(target, [new(2, 2)], new(2, 2), true);
        Check(State(session) == state, "no-op movement creates no transaction and preserves an existing redo entry");
        preview = session.PreviewMoveMap(target, [new(2, 2)], new(5, 2), true);
        Check(!preview.Valid && preview.Conflicts?.Contains(new(6, 2)) == true && main.Data.SequenceEqual(initial), "blocked movement reports the occupied destination and never partially clears its source");
        session.MoveMap(target, [new(2, 2)], new(3, 2), true);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(3, 2).RawValue == first.RawValue && after.GetTile(2, 2).IsNone, "movement may overlap its own old footprint after clearing the cloned source"); session.Undo();
        preview = session.PreviewMoveMap(target, [new(2, 2), new(6, 2)], new(10, 10), true);
        Check(preview is { Valid: true, Placements: 2 }, "multi-object movement plans all selected roots together");
        session.MoveMap(target, [new(2, 2), new(6, 2)], new(10, 10), true);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(10, 10).RawValue == first.RawValue && after.GetTile(14, 10).RawValue == second.RawValue, "multi-object movement keeps relative spacing and each item's exact data"); session.Undo();
        preview = session.PreviewMoveMap(target, [new(2, 2)], new(31, 12), true);
        Check(preview.Valid && preview.Cells.Contains(new(32, 12)), "movement previews retain complete footprints across acre edges");
        var stamp = new WorkspaceSession.RoomItem(2500, 0x20, 0x5A, int.MinValue + 7);
        state = State(session); preview = session.PreviewPlaceMap(target with { X = 2, Y = 2 }, stamp, true, true);
        Check(!preview.Valid && State(session) == state && main.Data.SequenceEqual(initial), "protected stamping never silently replaces a root under the pointer");
        preview = session.PreviewPlaceMap(target with { X = 5, Y = 2 }, stamp, false, true);
        Check(preview is { Valid: true, ReplacedItems: 1 } && preview.ClearedCells?.Contains(new(7, 2)) == true, "replacement stamping includes cleared cells outside the new footprint");
        session.PlaceMap(target with { X = 5, Y = 2 }, stamp, false, true);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(5, 2).RawValue == stamp.ToItem().RawValue && after.GetTile(7, 2).IsNone, "stamping clears a displaced object's full footprint before one commit"); session.Undo();
        Check(main.Data.SequenceEqual(initial), "stamping undo restores unrelated objects and every old extension byte");
        preview = session.PreviewPlaceMap(target with { Acre = 61, X = 31, Y = 31 }, stamp, true, true);
        Check(!preview.Valid && main.Data.SequenceEqual(initial), "out-of-map stamping rejects the full item before writing");
        session.PasteGrid(new("mapItems", target), new(20, 10), [new(20, 10)], true);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(20, 10).RawValue == first.RawValue, "move and stamp previews leave the shared editor clipboard unchanged"); session.Undo();
        var otherLayer = main.GetFieldItemLayer1().Select(item => item.RawValue).ToArray();
        session.PlaceMap(target with { X = 20, Y = 10 }, stamp, true, true);
        Check(main.GetFieldItemLayer1().Select(item => item.RawValue).SequenceEqual(otherLayer), "direct placement is isolated to the requested item layer");
        session.Undo(); Check(main.Data.SequenceEqual(initial), "all direct interactions restore the original fixture after undo");
        var fieldItem = new WorkspaceSession.RoomItem(60000, 0, 0, 0);
        session.PlaceMap(target with { X = 20, Y = 20 }, fieldItem, true, true);
        session.MoveMap(target, [new(2, 2), new(20, 20)], new(5, 5), true, true);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(4, 4).RawValue == first.RawValue && after.GetTile(22, 22).ItemId == 60000, "field-object moves snap the whole group's delta while preserving relative spacing"); session.Undo();
        session.MoveMap(target, [new(2, 2), new(20, 20)], new(5, 5), true, false);
        after = new(main.GetFieldItemLayer0(), main.FieldItemAcreWidth, 6);
        Check(after.GetTile(5, 5).RawValue == first.RawValue && after.GetTile(23, 23).ItemId == 60000, "disabling field-object snapping allows the same group to move by odd cell offsets"); session.Undo(); session.Undo();
        Check(main.Data.SequenceEqual(initial), "snapped and free group movement keep exact undo isolation");
    }
}
