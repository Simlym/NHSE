using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private WorkspaceSession.MapData MapWorkspace(WorkspaceSession.MapTarget target, bool includeImages = true)
    {
        var session = RequireSession();
        var data = session.GetMapWorkspace(target);
        if (!includeImages) return data;
        var editor = MapEditor.FromSaveFile(session.SaveFile.Main);
        editor.MapScale = 1;
        editor.ViewScale = 16;
        editor.Mutator.View.ItemLayerIndex = target.Layer;
        var acre = AcreCoordinate.Exterior[target.Acre];
        editor.Mutator.View.SetViewTo(acre.X * 32, acre.Y * 32);
        using var renderer = new MapRenderer(editor);
        var building = target.Mode == "buildings" ? target.Building : -1;
        static string Png(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        return data with {
            Overview = Png(renderer.UpdateMapTerrain(building)),
            // Tile model names overlap at workspace scale. The web UI supplies
            // accessible building labels separately from the terrain bitmap.
            Viewport = Png(renderer.UpdateViewportTerrain(Font, 180, 0, building, drawBuildingNames: false, drawGrid: false)),
            ItemImage = Png(renderer.UpdateViewportItems(unchecked((int)0xDDFFFFFF), drawGrid: false)),
        };
    }
}
