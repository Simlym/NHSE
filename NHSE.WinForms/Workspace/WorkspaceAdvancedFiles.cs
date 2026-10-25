using System;
using System.IO;
using System.Windows.Forms;
using NHSE.Core;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private void AdvancedFile(WorkspaceSession.AdvancedTarget target, bool import, bool all)
    {
        var session = RequireSession(); var data = session.GetAdvanced(target);
        var collection = all && target.Kind is "item" or "wear" or "furniture";
        var bytes = collection ? session.ExportAdvancedCollection(target) : session.ExportAdvanced(target); var extension = data.Extension;
        if (all && !import && !collection)
        {
            using var folder = new FolderBrowserDialog { Description = WorkspaceText.Get("advancedExportAll"), UseDescriptionForTitle = true };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            var entries = target.Kind == "resident" ? MainSaveOffsets.VillagerCount : data.Entries.Length;
            if (target.Kind.StartsWith("flags", StringComparison.Ordinal)) entries = 1;
            for (var i = 0; i < entries; i++)
            {
                var current = target.Kind == "resident" ? target with { Resident = i } : target.Kind.StartsWith("flags", StringComparison.Ordinal) ? target : target with { Index = i };
                if (target.Kind == "item" && !session.IsSlotAvailable(target.Player, target.Container, i)) continue;
                File.WriteAllBytes(Path.Combine(folder.SelectedPath, $"{target.Kind}-{i:0000}.{extension}"), session.ExportAdvanced(current));
            }
            return;
        }
        if (!import)
        {
            using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("advancedExport"), Filter = $"NHSE (*.{extension})|*.{extension}", FileName = $"{target.Kind}-{target.Resident}-{target.Index}.{extension}", DefaultExt = extension, AddExtension = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get("advancedImport"), Filter = $"NHSE (*.{extension})|*.{extension}|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var size = new FileInfo(picker.FileName).Length;
        if (size != bytes.Length && !(target.Kind == "resident" && size <= int.MaxValue && VillagerConverter.IsCompatible((int)size, bytes.Length))) throw new ArgumentException("advancedFileSize");
        if (size != bytes.Length && MessageBox.Show(this, WorkspaceText.Get("housingConvertConfirm"), WorkspaceText.Get("advancedImport"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (collection) session.ImportAdvancedCollection(target, File.ReadAllBytes(picker.FileName));
        else session.ImportAdvanced(target, File.ReadAllBytes(picker.FileName));
    }
}
