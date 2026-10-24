using System;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

internal sealed partial class WorkspaceSession
{
    public object GetProgress(int player, string section)
    {
        var p = Player(player);
        var options = Array.Empty<object>();
        object[] entries;
        switch (section)
        {
            case "recipes":
                var book = p.GetRecipeBook();
                entries = Enumerable.Range(0, RecipeBook.RecipeCount).Select(i =>
                {
                    var name = RecipeList.Recipes.TryGetValue((ushort)i, out var item)
                        ? GameInfo.Strings.itemlist[item] : "#" + i;
                    return (object)new { index = i, name, known = book.GetIsKnown(i), made = book.GetIsMade(i),
                        fresh = book.GetIsNew(i), favorite = book.GetIsFavorite(i) };
                }).ToArray();
                break;
            case "reactions":
                var store = p.Reactions;
                options = Enumerable.Range(0, 256).Select(i => (object)new { id = i, name = ((Reaction)i).ToString() }).ToArray();
                entries = store.ManpuBit.Select((v, i) => (object)new { index = i, name = v.ToString(), value = (int)v,
                    wheel = false, slot = i, fresh = store.NewFlag[i] }).Concat(store.UIList.Select((v, i) => (object)new
                    { index = store.ManpuBit.Length + i, name = v.ToString(), value = (int)v, wheel = true, slot = i, fresh = false })).ToArray();
                break;
            case "achievements":
                var list = p.Achievements;
                entries = Enumerable.Range(0, list.Counts.Length).Select(i =>
                {
                    LifeSupportAchievement.List.TryGetValue(i, out var detail);
                    var name = detail?.Name ?? "#" + i;
                    if (GameInfo.Strings.InternalNameTranslation.TryGetValue(name, out var translated)) name = translated;
                    return (object)new { index = i, name, count = list.Counts[i], flag = list.Flags[i],
                        dates = list.Date[i].Dates.Select(d => d.IsEmpty ? "" : d.ToString()).ToArray(), read = list.Read[i].Read,
                        milestones = detail?.AchievementCount ?? 0,
                        thresholds = Enumerable.Range(0, 6).Select(n => detail?.GetThresholdValue(n) ?? 0).ToArray() };
                }).ToArray();
                break;
            default: throw new ArgumentException("invalidRequest");
        }
        return new { section, entries, options };
    }

    // Record only changed personal-file ranges so unrelated later edits remain undoable.
    private void EditProgress(int player, Action<Personal> edit)
    {
        var p = Player(player);
        var before = p.Data.ToArray();
        try { edit(p); }
        catch { before.CopyTo(p.Data); throw; }
        var patches = new System.Collections.Generic.List<IdentityPatch>();
        for (var i = 0; i < before.Length;)
        {
            if (before[i] == p.Data[i]) { i++; continue; }
            var start = i++;
            while (i < before.Length && before[i] != p.Data[i]) i++;
            patches.Add(new IdentityPatch(p, start, before[start..i], p.Data[start..i].ToArray()));
        }
        if (patches.Count == 0) return;
        void Apply(bool undo) { foreach (var patch in patches) (undo ? patch.Before : patch.After).CopyTo(p.Data[patch.Offset..]); }
        Commit(() => Apply(true), () => Apply(false));
    }

    public void SetRecipe(int player, int index, bool known, bool made, bool fresh, bool favorite)
    {
        if ((uint)index >= RecipeBook.RecipeCount) throw new ArgumentException("invalidRequest");
        EditProgress(player, p =>
        {
            var book = p.GetRecipeBook();
            book.SetIsKnown(index, known); book.SetIsMade(index, made);
            book.SetIsNew(index, fresh); book.SetIsFavorite(index, favorite);
            p.SetRecipeBook(book);
        });
    }

    public void SetReaction(int player, int index, int value, bool fresh)
    {
        var store = Player(player).Reactions;
        if ((uint)index >= store.ManpuBit.Length + store.UIList.Length || value is < 0 or > 255)
            throw new ArgumentException("invalidRequest");
        EditProgress(player, p =>
        {
            if (index < store.ManpuBit.Length) { store.ManpuBit[index] = (Reaction)value; store.NewFlag[index] = fresh; }
            else store.UIList[index - store.ManpuBit.Length] = (Reaction)value;
            p.Reactions = store;
        });
    }

    public void SetAchievement(int player, int index, uint count, int flag, string[] dates, bool[] read)
    {
        var list = Player(player).Achievements;
        if ((uint)index >= list.Counts.Length || flag is < 0 or > 255 || dates.Length != 6 || read.Length != 6)
            throw new ArgumentException("invalidRequest");
        var parsed = new GSaveDate[6];
        for (var i = 0; i < 6; i++)
        {
            var original = list.Date[index][i];
            if (dates[i] == (original.IsEmpty ? "" : original.ToString())) { parsed[i] = original; continue; }
            if (dates[i] == "") continue;
            if (!DateTime.TryParseExact(dates[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                throw new ArgumentException("invalidRequest");
            parsed[i] = dt;
        }
        EditProgress(player, p =>
        {
            list.Counts[index] = count; list.Flags[index] = (byte)flag;
            for (var i = 0; i < 6; i++) { list.Date[index][i] = parsed[i]; list.Read[index][i] = read[i]; }
            p.Achievements = list;
        });
    }

    public void ProgressBatch(int player, string section, string action, int index = -1)
    {
        if (section == "recipes" && action is "learnAll" or "clearAll" or "craftAll")
        {
            EditProgress(player, p =>
            {
                var book = p.GetRecipeBook();
                if (action == "learnAll") book.GiveAll(RecipeList.Recipes);
                else if (action == "clearAll") book.ClearAll();
                else book.CraftAll();
                p.SetRecipeBook(book);
            });
        }
        else if (section == "reactions" && action is "learnAll" or "clearAll")
        {
            EditProgress(player, p =>
            {
                var store = p.Reactions;
                if (action == "learnAll") store.AddMissingReactions();
                else { Array.Clear(store.ManpuBit); Array.Clear(store.UIList); Array.Clear(store.NewFlag); }
                p.Reactions = store;
            });
        }
        else if (section == "achievements" && action is "complete" or "clear")
        {
            if (index < -1 || index >= AchievementList.MaxCount) throw new ArgumentException("invalidRequest");
            var now = DateTime.Today;
            EditProgress(player, p =>
            {
                var list = p.Achievements;
                if (action == "complete") { if (index == -1) list.GiveAll(now); else list.GiveAll(index, now); }
                else { if (index == -1) list.ClearAll(); else list.ClearAll(index); }
                p.Achievements = list;
            });
        }
        else throw new ArgumentException("invalidRequest");
    }
}
