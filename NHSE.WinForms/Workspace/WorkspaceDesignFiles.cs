using System;
using System.IO;
using System.Linq;
using System.Drawing.Imaging;
using System.Windows.Forms;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private static string DesignExtension(string kind) => WorkspaceSession.IsProDesign(kind) ? "nhpd" : "nhd";
    private void ImportDesignFiles(string kind, int index, int player, bool all, bool useCurrentPlayer)
    {
        var session = RequireSession();
        session.DesignBytes(kind, index); // Validate the target before showing a dialog.
        if ((uint)player >= session.SaveFile.Players.Count) throw new ArgumentException("invalidRequest");
        var extension = DesignExtension(kind);
        var expected = WorkspaceSession.IsProDesign(kind) ? DesignPatternPRO.SIZE : DesignPattern.SIZE;
        string[] paths;
        if (all)
        {
            using var dialog = new FolderBrowserDialog { Description = WorkspaceText.Get("designImportAll"), UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            paths = Directory.EnumerateFiles(dialog.SelectedPath).Where(p => string.Equals(Path.GetExtension(p), "." + extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToArray();
            if (paths.Length == 0) throw new ArgumentException("designNoFiles");
            if (paths.Length > session.DesignCount(kind)) throw new ArgumentException("designCapacity");
        }
        else
        {
            using var dialog = new OpenFileDialog { Title = WorkspaceText.Get("designImport"), Filter = $"NHSE Design (*.{extension})|*.{extension}|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            paths = [dialog.FileName];
        }
        // Bound file reads, including every file in the batch, before touching the model.
        foreach (var path in paths) if (new FileInfo(path).Length != expected) throw new ArgumentException("designFileSize");
        var files = paths.Select(File.ReadAllBytes).ToArray();
        session.ImportDesigns(kind, all ? 0 : index, player, files, useCurrentPlayer);
    }

    private void ExportDesignFiles(string kind, int index, bool all)
    {
        var session = RequireSession();
        var bytes = session.DesignBytes(kind, index);
        var extension = DesignExtension(kind);
        string Name(byte[] data) => WorkspaceSession.IsProDesign(kind) ? new DesignPatternPRO(data).DesignName : new DesignPattern(data).DesignName;
        var name = StringUtil.CleanFileName(Name(bytes));
        if (string.IsNullOrWhiteSpace(name)) name = "Design";
        if (all)
        {
            using var dialog = new FolderBrowserDialog { Description = WorkspaceText.Get("designExportAll"), UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            for (var i = 0; i < session.DesignCount(kind); i++)
            {
                var data = session.DesignBytes(kind, i);
                var fileName = StringUtil.CleanFileName($"{i:000} - {Name(data)}.{extension}");
                File.WriteAllBytes(Path.Combine(dialog.SelectedPath, fileName), data);
            }
        }
        else
        {
            using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("designExport"), Filter = $"NHSE Design (*.{extension})|*.{extension}", FileName = name + "." + extension, AddExtension = true, DefaultExt = extension };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes);
        }
    }

    private void ExportDesignPng(string kind, int index, int sheet, bool palette)
    {
        var bytes = RequireSession().DesignBytes(kind, index);
        var pro = WorkspaceSession.IsProDesign(kind);
        if ((uint)sheet >= (pro ? 4 : 1)) throw new ArgumentException("invalidRequest");
        using var image = pro
            ? palette ? new DesignPatternPRO(bytes).GetPalette() : new DesignPatternPRO(bytes).GetImage(sheet)
            : palette ? new DesignPattern(bytes).GetPalette() : new DesignPattern(bytes).GetImage();
        using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("designSavePng"), Filter = "PNG (*.png)|*.png", FileName = palette ? "palette.png" : $"design-{index + 1}-{sheet + 1}.png", AddExtension = true, DefaultExt = "png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) image.Save(dialog.FileName, ImageFormat.Png);
    }
}
