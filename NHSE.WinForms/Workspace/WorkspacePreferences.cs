using System;
using System.Globalization;
using System.Net;
using System.IO;

namespace NHSE.WinForms;

internal sealed record WorkspacePreferences(string LastFilePath, bool AutomaticBackup, bool BackupPrompted,
    string Language, string Theme, string Host, int Port, bool ConsolePrompted, string PouchOffset, string GenericOffset)
{
    internal static void WriteConfiguration(string path, string json)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public WorkspacePreferences Validated()
    {
        if (LastFilePath is null || LastFilePath.Length > 4096 || LastFilePath.Contains('\0')
            || Language is not ("en" or "jp" or "de" or "es" or "fr" or "it" or "ko" or "zhs" or "zht")
            || Theme is not ("system" or "light" or "dark") || !IPAddress.TryParse(Host, out _) || Port is < 1 or > 65535)
            throw new ArgumentException("preferencesInvalid");
        static string Offset(string value)
        {
            if (value is null || value.Length is < 1 or > 8 || !uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var offset))
                throw new ArgumentException("preferencesInvalid");
            return offset.ToString("X8");
        }
        return this with { PouchOffset = Offset(PouchOffset), GenericOffset = Offset(GenericOffset) };
    }
}
