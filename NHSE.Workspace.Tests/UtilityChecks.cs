using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NHSE.Core;
using NHSE.WinForms;

internal static class UtilityChecks
{
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static void Invalid(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected utility validation failure."); }
    public static void Run(MemoryProvider provider, MemoryProvider oldProvider)
    {
        var preferences = new WorkspacePreferences("C:\\Save", false, true, "zhs", "dark", "127.0.0.1", 65535, true, "ffffffff", "0");
        var normalized = preferences.Validated();
        Check(normalized.PouchOffset == "FFFFFFFF" && normalized.GenericOffset == "00000000" && !normalized.AutomaticBackup && normalized.ConsolePrompted,
            "preferences keep explicit backup choices and normalize full-width hexadecimal defaults");
        foreach (var bad in new[] { preferences with { Host = "not an IP" }, preferences with { Port = 0 }, preferences with { Port = 65536 },
            preferences with { Theme = "invalid" }, preferences with { Language = "invalid" }, preferences with { PouchOffset = "100000000" },
            preferences with { GenericOffset = "GG" }, preferences with { LastFilePath = "bad\0path" } }) Invalid(() => bad.Validated());
        Check((preferences with { Host = "::1", Port = 1 }).Validated().Host == "::1", "settings accept IPv6 and legal port boundaries without connecting");
        var temporary = Path.Combine(Path.GetTempPath(), "nhse-utility-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var config = Path.Combine(temporary, "settings.json");
            WorkspacePreferences.WriteConfiguration(config, "{\"AutomaticBackup\":true}");
            WorkspacePreferences.WriteConfiguration(config, "{\"AutomaticBackup\":false}");
            Check(File.ReadAllText(config) == "{\"AutomaticBackup\":false}" && Directory.GetFiles(temporary).Length == 1,
                "configuration replacement persists exact JSON and removes temporary files");
            var blocked = Path.Combine(temporary, "blocked"); Directory.CreateDirectory(blocked);
            var failed = false; try { WorkspacePreferences.WriteConfiguration(blocked, "{}"); } catch (IOException) { failed = true; }
            Check(failed && Directory.Exists(blocked) && Directory.GetFiles(temporary).Length == 1 && File.ReadAllText(config) == "{\"AutomaticBackup\":false}",
                "configuration persistence failures leave the previous configuration and unrelated paths unchanged");
            foreach (var source in new[] { provider, oldProvider })
            {
                var save = new HorizonSave(source); var session = new WorkspaceSession(save);
                var before = save.Main.Data.ToArray(); var personal = save.Players.Select(p => p.Personal.Data.ToArray()).ToArray();
                var players = session.ExportAllHousing("playerHouses"); var residents = session.ExportAllHousing("residentHouses");
                Check(players.Length == 8 && residents.Length == 10 && players.Select(f => f.Name).Distinct().Count() == 8
                    && residents.Select(f => f.Name).Distinct().Count() == 10, "all-house exports include every physical house with unique stable file names");
                Check(players[^1].Data.SequenceEqual(session.ExportHousing("playerHouses", 7, 0, false))
                    && residents[^1].Data.SequenceEqual(session.ExportHousing("residentHouses", 9, 0, false)), "batch exports preserve native house versions and exact final-slot bytes");
                players[0].Data[0] ^= 0xFF; residents[0].Data[0] ^= 0xFF;
                Check(save.Main.Data.SequenceEqual(before) && !session.IsDirty, "house exports are read only and isolate caller buffers");
                Invalid(() => session.ExportAllHousing("bad"));
                var flags = session.ExportHouseFlags(7); var changed = flags.ToArray();
                BinaryPrimitives.WriteInt16LittleEndian(changed, short.MinValue); BinaryPrimitives.WriteInt16LittleEndian(changed.AsSpan(changed.Length - 2), short.MaxValue);
                session.ImportHouseFlags(7, changed); changed[0] ^= 0xFF;
                var house = save.Main.GetPlayerHouse(7);
                Check(house.EventFlags[0] == short.MinValue && house.EventFlags[^1] == short.MaxValue, "house flag files preserve signed boundaries and own the imported buffer");
                session.Undo(); Check(save.Main.Data.SequenceEqual(before), "house flags undo as one byte-exact operation without changing furniture or ownership");
                session.Redo(); Check(save.Main.GetPlayerHouse(7).EventFlags[0] == short.MinValue, "house flag redo restores the whole import"); session.Undo();
                session.ImportHouseFlags(7, flags); Check(!session.IsDirty, "unchanged house flag imports create no history");
                Invalid(() => session.ImportHouseFlags(7, new byte[flags.Length + 1])); Invalid(() => session.ExportHouseFlags(8));
                Check(save.Main.Data.SequenceEqual(before), "invalid flag sizes and house targets reject without writes");
                var hashes = session.VerifyHashes(); Check(!hashes.Dirty && hashes.Issues.Length == 0, "hash inspection starts from a clean saved revision and is read only");
                var region = RevisionChecker.HashInfo[save.Main.Info.GetKnownRevisionIndex()].GetFile(save.Main.NameData)!.HashRegions[0];
                save.Main.Data[region.EndOffset - 1] ^= 1;
                Check(session.VerifyHashes().Issues.Any(i => i.File == "main.dat") && !session.IsDirty, "hash results identify the file and distinguish mismatches from edit history");
                save.Main.Data[region.EndOffset - 1] ^= 1;
                var sizes = session.DecryptedFileSizes();
                var folder = Path.Combine(temporary, Guid.NewGuid().ToString("N")); save.Dump(folder);
                var exported = sizes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(folder, p.Key.Replace('/', Path.DirectorySeparatorChar))));
                Check(exported.Count == 1 + save.Players.Sum(p => p.Count()) && exported["main.dat"].SequenceEqual(before)
                    && !File.Exists(Path.Combine(folder, "mainHeader.dat")), "decrypted exports use the existing native layout and omit encrypted headers");
                session.ImportDecrypted(exported); Check(!session.IsDirty, "importing unchanged decrypted exports does not add history");
                var mainBytes = before.ToArray(); mainBytes[^1] ^= 0x5A;
                var playerKey = save.Players[0].DirectoryName + "/personal.dat"; var personalBytes = personal[0].ToArray(); personalBytes[^1] ^= 0xA5;
                Invalid(() => session.ImportDecrypted(new() { [playerKey] = personalBytes }));
                Invalid(() => session.ImportDecrypted(new() { ["main.dat"] = mainBytes, [playerKey] = new byte[1] }));
                Invalid(() => session.ImportDecrypted(new() { ["main.dat"] = mainBytes, ["../main.dat"] = mainBytes }));
                Check(save.Main.Data.SequenceEqual(before) && save.Players[0].Personal.Data.SequenceEqual(personal[0]) && !session.IsDirty,
                    "missing main files, wrong lengths and unknown paths reject the entire decrypted import");
                var input = new Dictionary<string, byte[]> { ["main.dat"] = mainBytes, [playerKey] = personalBytes };
                session.ImportDecrypted(input); mainBytes[^1] ^= 0xFF; personalBytes[^1] ^= 0xFF; input.Clear();
                Check(save.Main.Data[^1] == (byte)(before[^1] ^ 0x5A) && save.Players[0].Personal.Data[^1] == (byte)(personal[0][^1] ^ 0xA5)
                    && save.Players.Skip(1).Select((p, i) => p.Personal.Data.SequenceEqual(personal[i + 1])).All(same => same), "decrypted import owns buffers and changes only explicitly supplied files");
                Check(session.VerifyHashes().Dirty, "hash reports explicitly identify unsaved in-memory edits");
                session.Undo(); Check(save.Main.Data.SequenceEqual(before) && save.Players[0].Personal.Data.SequenceEqual(personal[0]) && !session.IsDirty,
                    "a whole multi-file decrypted import undoes in one exact step");
                session.Redo(); session.Save(); var reloaded = new HorizonSave(source);
                Check(reloaded.Main.Data[^1] == (byte)(before[^1] ^ 0x5A) && reloaded.Players[0].Personal.Data[^1] == (byte)(personal[0][^1] ^ 0xA5)
                    && !reloaded.GetInvalidHashes().Any(), "decrypted imports survive encrypted save/reload with native valid hashes");
            }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
