using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private static string MapFileExtension(string resource) => resource switch {
        "items" => "nhl", "terrain" => "nht", "buildings" => "nhb",
        "acres" => "nha", "flags" => "nhlf", "designs" => "nhmd",
        _ => throw new ArgumentException("invalidRequest"),
    };
    private void MapFile(WorkspaceSession.MapTarget target, string resource, bool whole, bool import)
    {
        var session = RequireSession();
        var expected = session.MapFileSize(target, resource, whole);
        var extension = MapFileExtension(resource);
        var name = $"{resource}-{(whole ? "all" : target.Acre.ToString())}-{target.Layer}.{extension}";
        var filter = $"NHSE (*.{extension})|*.{extension}|All files (*.*)|*.*";
        if (!import)
        {
            var bytes = session.ExportMap(target, resource, whole);
            using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("mapExport"), Filter = filter, FileName = name, AddExtension = true, DefaultExt = extension };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get("mapImport"), Filter = filter, FileName = name };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var size = new FileInfo(picker.FileName).Length;
        if (!session.IsMapFileSizeValid(target, resource, whole, size)) throw new ArgumentException("mapFileSize");
        if (size != expected && MessageBox.Show(this, WorkspaceText.Get("mapConvertConfirm"), WorkspaceText.Get("mapImport"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        session.ImportMap(target, resource, whole, File.ReadAllBytes(picker.FileName));
    }
    private void ExportMapPng(WorkspaceSession.MapTarget target)
    {
        RequireSession().GetMapWorkspace(target);
        var editor = MapEditor.FromSaveFile(RequireSession().SaveFile.Main);
        editor.MapScale = 4;
        using var renderer = new MapRenderer(editor);
        var bitmap = renderer.UpdateMapTerrain(target.Mode == "buildings" ? target.Building : -1);
        using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("mapPng"), Filter = "PNG (*.png)|*.png", FileName = "island-map.png", AddExtension = true, DefaultExt = "png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) bitmap.Save(dialog.FileName, ImageFormat.Png);
    }
}
