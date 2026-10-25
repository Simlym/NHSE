using System;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    internal sealed record MuseumEntry(int Index, string Name, string Date, int Player, RoomItem Item);
    internal sealed record MuseumData(int Level, MuseumEntry[] Entries);
    private static string MuseumDate(GSaveDate date) => date.IsEmpty ? "" : date.ToString();
    public MuseumData GetMuseum()
    {
        var museum = SaveFile.Main.Museum;
        var dates = museum.GetDates(); var players = museum.GetPlayers(); var items = museum.GetItems();
        return new(museum.MuseumLevel, items.Select((item, i) => new MuseumEntry(i,
            item.IsNone ? "" : GameInfo.Strings.GetItemName(item), MuseumDate(dates[i]), players[i], RoomItem.From(item))).ToArray());
    }
    public byte[] ExportMuseum() => SaveFile.Main.Museum.Data.ToArray();
    private void CommitMuseum(byte[] before, byte[] after)
    {
        if (!before.SequenceEqual(after)) Commit(() => SaveFile.Main.Museum = new Museum(before.ToArray()), () => SaveFile.Main.Museum = new Museum(after.ToArray()));
    }
    public void ImportMuseum(byte[] bytes)
    {
        if (bytes.Length != Museum.SIZE) throw new ArgumentException("museumFileSize");
        CommitMuseum(ExportMuseum(), bytes.ToArray());
    }
    public void SetMuseumLevel(int level)
    {
        var before = ExportMuseum(); var museum = new Museum(before.ToArray()) { MuseumLevel = level };
        CommitMuseum(before, museum.Data.ToArray());
    }
    public void SetMuseumEntry(int index, string date, int player, RoomItem item)
    {
        if ((uint)index >= Museum.EntryCount || (uint)player > byte.MaxValue) throw new ArgumentException("invalidRequest");
        var replacement = item.ToItem();
        var before = ExportMuseum(); var museum = new Museum(before.ToArray());
        var dates = museum.GetDates();
        if (date != MuseumDate(dates[index]))
        {
            if (date.Length == 0) dates[index] = default;
            else if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                && parsed != DateTime.MinValue) dates[index] = parsed;
            else throw new ArgumentException("museumDateInvalid");
            museum.SetDates(dates);
        }
        var items = museum.GetItems(); items[index] = replacement; museum.SetItems(items);
        var players = museum.GetPlayers(); players[index] = (byte)player; museum.SetPlayers(players);
        CommitMuseum(before, museum.Data.ToArray());
    }
    public void MuseumBatch(string action)
    {
        if (action is not ("giveAll" or "sort")) throw new ArgumentException("invalidRequest");
        var before = ExportMuseum(); var museum = new Museum(before.ToArray());
        var editor = new Core.MuseumEditor(museum);
        // Core operations require calendar-valid dates. Reject on the detached copy.
        foreach (var date in editor.Dates)
            if (!date.IsEmpty && (date.Year is < 1 or > 9999 || date.Month is < 1 or > 12 || date.Day < 1 || date.Day > DateTime.DaysInMonth(date.Year, date.Month)))
                throw new ArgumentException("museumDateInvalid");
        if (action == "sort") editor.SortAll();
        else
        {
            var english = GameInfo.GetStrings("en").itemlist;
            var wanted = GameLists.Art.ToArray().Where(id => !english[id].Contains('('))
                .Concat(GameLists.Fish.ToArray()).Concat(GameLists.Fossils.ToArray()).Concat(GameLists.Bugs.ToArray()).Concat(GameLists.Dive.ToArray()).Distinct();
            var missing = wanted.Except(editor.Items.Select(i => i.ItemId)).Count();
            if (missing == 0) return;
            if (missing > editor.Items.Count(i => i.IsNone)) throw new ArgumentException("museumCapacity");
            if (editor.Dates.Max(d => (DateTime)d).Date == DateTime.MaxValue.Date) throw new ArgumentException("museumDateInvalid");
            editor.GiveAll(english, 1000);
        }
        editor.Save(); CommitMuseum(before, museum.Data.ToArray());
    }
}
