using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NHSE.Core;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private static WorkspacePreferences Preferences()
    {
        var s = Program.Settings;
        return new(s.LastFilePath, s.AutomaticBackup, s.BackupPrompted, s.Language,
            s.DarkMode == SystemColorMode.Dark ? "dark" : s.DarkMode == SystemColorMode.Classic ? "light" : "system",
            s.SysBot.IP, s.SysBot.Port, s.SysBot.Prompted, s.SysBot.PouchOffset.ToString("X8"), s.SysBot.GenericOffset.ToString("X8"));
    }
    private static void SavePreferences(WorkspacePreferences next, bool validate = true)
    {
        if (validate) next = next.Validated();
        if (next.Theme is not ("system" or "light" or "dark") || next.Language is not ("en" or "jp" or "de" or "es" or "fr" or "it" or "ko" or "zhs" or "zht")) throw new ArgumentException("preferencesInvalid");
        var candidate = new ApplicationSettings
        {
            LastFilePath = next.LastFilePath, AutomaticBackup = next.AutomaticBackup, BackupPrompted = next.BackupPrompted,
            Language = next.Language, DarkMode = next.Theme == "dark" ? SystemColorMode.Dark : next.Theme == "light" ? SystemColorMode.Classic : SystemColorMode.System,
            SysBot = new SysBotSettings { IP = next.Host, Port = next.Port, Prompted = next.ConsolePrompted,
                PouchOffset = uint.Parse(next.PouchOffset, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                GenericOffset = uint.Parse(next.GenericOffset, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) },
        };
        // Persist successfully before changing live settings. No save-data history is involved.
        Program.SaveSettingsChecked(candidate);
        var s = Program.Settings;
        s.LastFilePath = candidate.LastFilePath; s.AutomaticBackup = candidate.AutomaticBackup; s.BackupPrompted = candidate.BackupPrompted;
        s.Language = candidate.Language; s.DarkMode = candidate.DarkMode; s.SysBot = candidate.SysBot;
        if (GameInfo.CurrentLanguage != candidate.Language) WorkspaceText.SetLanguage(candidate.Language);
    }
    private string? UtilityOutputFolder(string title)
    {
        using var picker = new FolderBrowserDialog { Description = WorkspaceText.Get(title), UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return null;
        var folder = Path.Combine(picker.SelectedPath, "NHSE-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        return folder;
    }
    private void ExportAllHouses(string kind)
    {
        var files = RequireSession().ExportAllHousing(kind);
        var folder = UtilityOutputFolder("housingExportAll");
        if (folder is null) return;
        foreach (var file in files) File.WriteAllBytes(Path.Combine(folder, file.Name), file.Data);
    }
    private void HouseFlagsFile(int index, bool import)
    {
        var session = RequireSession(); var bytes = session.ExportHouseFlags(index);
        if (!import)
        {
            using var output = new SaveFileDialog { Title = WorkspaceText.Get("housingExportFlags"), Filter = "NHSE Flags (*.nhfl)|*.nhfl", FileName = $"house-{index + 1}-flags.nhfl", DefaultExt = "nhfl", AddExtension = true };
            if (output.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(output.FileName, bytes);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get("housingImportFlags"), Filter = "NHSE Flags (*.nhfl)|*.nhfl|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(picker.FileName).Length != bytes.Length) throw new ArgumentException("utilityFileSize");
        session.ImportHouseFlags(index, File.ReadAllBytes(picker.FileName));
    }
    private void DecryptedFiles(bool import)
    {
        var session = RequireSession();
        if (!import)
        {
            var folder = UtilityOutputFolder("utilityExportDecrypted");
            if (folder is not null) session.SaveFile.Dump(folder);
            return;
        }
        using var picker = new OpenFileDialog { Title = WorkspaceText.Get("utilityImportDecrypted"), Filter = "NHSE decrypted main.dat|main.dat", FileName = "main.dat" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var root = Path.GetDirectoryName(picker.FileName) ?? throw new ArgumentException("invalidRequest");
        if (File.Exists(Path.Combine(root, "mainHeader.dat"))) throw new ArgumentException("utilityEncryptedFolder");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, size) in session.DecryptedFileSizes())
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length != size) throw new ArgumentException("utilityFileSize");
            files.Add(name, File.ReadAllBytes(path));
        }
        if (!files.ContainsKey("main.dat")) throw new ArgumentException("utilityFileSize");
        var review = WorkspaceText.Get("utilityImportReview").Replace("{0}", files.Count.ToString(CultureInfo.InvariantCulture))
            + Environment.NewLine + string.Join(Environment.NewLine, files.Keys);
        if (MessageBox.Show(this, review, WorkspaceText.Get("utilityImportDecrypted"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        session.ImportDecrypted(files); Avatars.Clear();
    }
    private void ExportPlayerPhoto(int player)
    {
        var session = RequireSession();
        if ((uint)player >= session.SaveFile.Players.Count) throw new ArgumentException("invalidRequest");
        var bytes = session.SaveFile.Players[player].Personal.GetPhotoData();
        if (bytes.Length == 0) throw new ArgumentException("utilityPhotoMissing");
        using var input = new MemoryStream(bytes);
        using var photo = Image.FromStream(input);
        using var output = new SaveFileDialog { Title = WorkspaceText.Get("utilityExportPhoto"), Filter = "PNG (*.png)|*.png", FileName = $"player-{player + 1}-photo.png", DefaultExt = "png", AddExtension = true };
        if (output.ShowDialog(this) == DialogResult.OK) photo.Save(output.FileName, ImageFormat.Png);
    }
}
