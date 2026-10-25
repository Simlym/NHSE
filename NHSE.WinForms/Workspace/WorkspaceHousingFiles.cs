using System;
using System.IO;
using System.Windows.Forms;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private void HousingFile(string kind, int index, int room, bool roomOnly, bool import)
    {
        var session = RequireSession();
        var bytes = session.ExportHousing(kind, index, room, roomOnly);
        var extension = session.HousingFileExtension(kind, index, room, roomOnly);
        var extensions = roomOnly ? "*.nhpr;*.nhpr2" : kind == "playerHouses" ? "*.nhph;*.nhph2" : "*.nhvh;*.nhvh2";
        var name = roomOnly ? $"house-{index + 1}-room-{room + 1}.{extension}" : $"{kind}-{index + 1}.{extension}";
        if (!import)
        {
            using var dialog = new SaveFileDialog { Title = WorkspaceText.Get(roomOnly ? "housingExportRoom" : "housingExportHouse"),
                Filter = $"NHSE (*.{extension})|*.{extension}", FileName = name, AddExtension = true, DefaultExt = extension };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get(roomOnly ? "housingImportRoom" : "housingImportHouse"),
            Filter = $"NHSE ({extensions})|{extensions}|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var size = new FileInfo(picker.FileName).Length;
        if (!WorkspaceSession.IsHousingFileSizeValid(kind, roomOnly, size)) throw new ArgumentException("housingFileSize");
        if (size != bytes.Length && MessageBox.Show(this, WorkspaceText.Get("housingConvertConfirm"), WorkspaceText.Get("housingImportHouse"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        session.ImportHousing(kind, index, room, roomOnly, File.ReadAllBytes(picker.FileName));
    }
}
