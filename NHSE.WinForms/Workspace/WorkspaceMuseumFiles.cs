using System;
using System.IO;
using System.Windows.Forms;
using NHSE.Core;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private void MuseumFile(bool import)
    {
        var session = RequireSession();
        const string filter = "NHSE Museum (*.nhm)|*.nhm|All files (*.*)|*.*";
        if (!import)
        {
            var bytes = session.ExportMuseum();
            using var dialog = new SaveFileDialog { Title = WorkspaceText.Get("museumExport"), Filter = filter, FileName = "museum.nhm", AddExtension = true, DefaultExt = "nhm" };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(dialog.FileName, bytes);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get("museumImport"), Filter = filter };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(picker.FileName).Length != Museum.SIZE) throw new ArgumentException("museumFileSize");
        session.ImportMuseum(File.ReadAllBytes(picker.FileName));
    }
}
