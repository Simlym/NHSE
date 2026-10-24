using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private WorkspaceSession.MapData MapWorkspace(WorkspaceSession.MapTarget target)
    {
        var session = RequireSession();
        var data = session.GetMapWorkspace(target);
        var editor = MapEditor.FromSaveFile(session.SaveFile.Main);
        editor.MapScale = 1;
        editor.ViewScale = 8;
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
            Viewport = Png(renderer.UpdateViewportTerrain(Font, 180, 255, building)),
            ItemImage = Png(renderer.UpdateViewportItems(unchecked((int)0xDDFFFFFF))),
        };
    }
}
