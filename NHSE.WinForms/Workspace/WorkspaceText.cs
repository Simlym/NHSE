using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

internal static class WorkspaceText
{
    private static Dictionary<string, string> Current = Load("en");
    public static string Get(string key) => Current.GetValueOrDefault(key, key);

    public static void SetLanguage(string language)
    {
        var index = GameLanguage.GetLanguageIndex(language);
        var code = GameInfo.SetLanguage2Char(index);
        Current = Load("en");
        foreach (var pair in Load(code))
            Current[pair.Key] = pair.Value;
        // Reset before applying translations so switching languages cannot leak old strings.
        TranslationUtil.SetLocalization(typeof(MessageStrings), "en");
        TranslationUtil.SetLocalization(typeof(MessageStrings), code);
        TranslationUtil.SetLocalization(GameInfo.Strings.InternalNameTranslation, code);
        ItemSprite.Initialize(GameInfo.GetStrings("en").itemlist);
        Program.Settings.Language = code;
        Program.SaveSettings();
    }

    private static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Workspace.{code}.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
