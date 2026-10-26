using System;
using System.Collections.Generic;
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
            case "collection":
                entries = Enumerable.Range(0, GameInfo.Strings.itemlistdisplay.Length).Select(i => (object)new
                {
                    index = i, name = GameInfo.Strings.itemlistdisplay[i],
                    known = FlagUtil.GetFlag(p.Data, p.Offsets.ItemCollectBit, i),
                }).ToArray();
                break;
            case "remakes":
                var inverse = ItemRemakeUtil.GetInvertedDictionary();
                entries = Enumerable.Range(0, p.Offsets.MaxRemakeBitFlag).Select(i =>
                {
                    var remake = (short)(i / ItemRemakeInfo.BodyColorCountMax);
                    var variant = i % ItemRemakeInfo.BodyColorCountMax;
                    var itemId = inverse.GetValueOrDefault(remake, (ushort)0);
                    var name = remake == 652 ? "photo" : GameInfo.Strings.itemlistdisplay[itemId];
                    if (ItemRemakeInfoData.List.TryGetValue(remake, out var info))
                        name += $" ({info.GetBodyDescription(variant, GameInfo.Strings)})";
                    return (object)new { index = i, name = $"{remake:0000} V{variant} · {name}",
                        known = FlagUtil.GetFlag(p.Data, p.Offsets.ItemRemakeCollectBit, i) };
                }).ToArray();
                break;
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

    public void SetCollection(int player, string section, int index, bool known)
    {
        var p = Player(player);
        var max = section switch { "collection" => GameInfo.Strings.itemlistdisplay.Length,
            "remakes" => p.Offsets.MaxRemakeBitFlag, _ => throw new ArgumentException("invalidRequest") };
        if ((uint)index >= max) throw new ArgumentException("invalidRequest");
        EditProgress(player, value => FlagUtil.SetFlag(value.Data,
            section == "collection" ? value.Offsets.ItemCollectBit : value.Offsets.ItemRemakeCollectBit, index, known));
    }

    // Match ItemReceivedEditor: bulk categories include valid body variants, with optional variants-only mode.
    public void CollectionBatch(int player, string category, bool known, bool remakeOnly)
    {
        ushort[] indexes;
        switch (category)
        {
            case "bugs": indexes = GameLists.Bugs.ToArray(); break;
            case "fish": indexes = GameLists.Fish.ToArray(); break;
            case "art": indexes = GameLists.Art.ToArray(); break;
            case "dive": indexes = GameLists.Dive.ToArray(); break;
            case "everything":
            case "furniture":
                var clearEverything = category == "everything" && !known;
                var start = clearEverything ? 0 : 1;
                indexes = Enumerable.Range(start, GameInfo.Strings.itemlistdisplay.Length - start)
                    .Where(i => clearEverything ||
                        (!string.IsNullOrEmpty(GameInfo.Strings.itemlist[i]) && !GameLists.NoCheckReceived.Contains((ushort)i)
                        && (category != "furniture" || !(GameLists.Bugs.Contains((ushort)i) || GameLists.Fish.Contains((ushort)i)
                            || GameLists.Art.Contains((ushort)i) || GameLists.Dive.Contains((ushort)i)))))
                    .Select(i => (ushort)i).ToArray();
                break;
            default: throw new ArgumentException("invalidRequest");
        }
        EditProgress(player, p =>
        {
            foreach (var item in indexes)
            {
                if (!remakeOnly) FlagUtil.SetFlag(p.Data, p.Offsets.ItemCollectBit, item, known);
                var remake = ItemRemakeUtil.GetRemakeIndex(item);
                if (!ItemRemakeInfoData.List.TryGetValue(remake, out var info)) continue;
                for (var variant = 0; variant < ItemRemakeInfo.BodyColorCountMax; variant++)
                {
                    var index = remake * ItemRemakeInfo.BodyColorCountMax + variant;
                    if (info.HasBodyColor(variant) && index < p.Offsets.MaxRemakeBitFlag)
                        FlagUtil.SetFlag(p.Data, p.Offsets.ItemRemakeCollectBit, index, known);
                }
            }
        });
    }

    private static object GetMiscProfile(Personal p) => new
    {
        month = (int)p.Birthday.Month, day = (int)p.Birthday.Day,
        madeVillage = p.ProfileIsMakeVillage,
        timestamp = p.ProfileTimestamp.IsEmpty ? "" : p.ProfileTimestamp.ToString(),
        fruit = (int)p.ProfileFruit,
        fruits = GameLists.Fruits.ToArray().Select(id => new { id = (int)id, name = GameInfo.Strings.itemlistdisplay[id] }).ToArray(),
    };

    public void SetMiscProfile(int player, int month, int day, bool madeVillage, string timestamp, int fruit)
    {
        var p = Player(player);
        var birthdayChanged = month != p.Birthday.Month || day != p.Birthday.Day;
        if (birthdayChanged && (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month)))
            throw new ArgumentException("invalidRequest");
        if (fruit != p.ProfileFruit && (fruit is < 0 or > ushort.MaxValue || !GameLists.Fruits.Contains((ushort)fruit)))
            throw new ArgumentException("invalidRequest");
        var originalDate = p.ProfileTimestamp.IsEmpty ? "" : p.ProfileTimestamp.ToString();
        var date = p.ProfileTimestamp;
        if (timestamp != originalDate)
        {
            if (timestamp == "") date = default;
            else if (DateTime.TryParseExact(timestamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                date = new GSaveDate { Year = (ushort)dt.Year, Month = (byte)dt.Month, Day = (byte)dt.Day };
            else throw new ArgumentException("invalidRequest");
        }
        EditProgress(player, value =>
        {
            if (birthdayChanged) value.Birthday = value.ProfileBirthday = new GSaveDateMD { Month = (byte)month, Day = (byte)day };
            if (madeVillage != value.ProfileIsMakeVillage) value.ProfileIsMakeVillage = madeVillage;
            if (fruit != value.ProfileFruit) value.ProfileFruit = (ushort)fruit;
            if (timestamp != originalDate) value.ProfileTimestamp = date;
        });
    }
}
