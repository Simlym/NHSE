using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace NHSE.WinForms;

/// <summary>The same item IDs as the original inventory editor, with complete paging and game-derived groups.</summary>
internal static class WorkspaceCatalog
{
    public const int PageSize = 96;
    public static readonly string[] CategoryIds = ["all", "furniture", "clothing", "tools", "nature", "materials", "creatures", "interior", "food", "recipes", "other"];
    public static readonly string[] ClothingGroups = ["all", "tops", "bottoms", "dresses", "headwear", "accessories", "socks", "shoes", "bags", "umbrellas", "other"];
    internal sealed record Entry(int ItemId, string Name, string Category, string Subcategory);
    internal sealed record Category(string Id, int Count);
    internal sealed record Result(int Page, int Pages, int Total, int TotalAll, Category[] Categories, Category[] Subcategories, Entry[] Items);
    private static GameStrings? CachedStrings;
    private static Entry[] CachedEntries = [];
    private static int CachedSourceCount;

    public static bool Contains(int id) => id is >= 0 and <= ushort.MaxValue && id is not (Item.NONE or Item.EXTENSION)
        && GameInfo.Strings.ItemDataSource.Any(x => x.Value == id);

    public static Result Search(string query, string category, int page, string subcategory = "all")
    {
        if (!CategoryIds.Contains(category) || !ClothingGroups.Contains(subcategory) || (category != "clothing" && subcategory != "all"))
            throw new ArgumentException("invalidRequest");
        if (CachedStrings != GameInfo.Strings || CachedSourceCount != GameInfo.Strings.ItemDataSource.Count)
        {
            CachedStrings = GameInfo.Strings;
            CachedSourceCount = CachedStrings.ItemDataSource.Count;
            CachedEntries = CachedStrings.ItemDataSource.Where(x => x.Value is >= 0 and <= ushort.MaxValue && x.Value is not (Item.NONE or Item.EXTENSION))
                .DistinctBy(x => x.Value).Select(x => new Entry(x.Value, x.Text,
                    x.Text.StartsWith("(Item #", StringComparison.Ordinal) ? "other" : GetCategory((ushort)x.Value),
                    x.Text.StartsWith("(Item #", StringComparison.Ordinal) ? "other" : GetClothingGroup((ushort)x.Value)))
                .OrderBy(x => x.Name.StartsWith("(Item #", StringComparison.Ordinal) ? 2 : x.Name.StartsWith('(') ? 1 : 0)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.ItemId).ToArray();
        }
        var all = CachedEntries;
        query = query.Trim();
        var searched = all.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.ItemId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)).ToArray();
        var categories = CategoryIds.Select(id => new Category(id, id == "all" ? searched.Length : searched.Count(x => x.Category == id))).ToArray();
        var matches = category == "all" ? searched : searched.Where(x => x.Category == category).ToArray();
        var subcategories = category == "clothing" ? ClothingGroups.Select(id => new Category(id, id == "all" ? matches.Length : matches.Count(x => x.Subcategory == id))).ToArray() : [];
        if (category == "clothing" && subcategory != "all") matches = matches.Where(x => x.Subcategory == subcategory).ToArray();
        var pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        return new Result(page, pages, matches.Length, all.Length, categories, subcategories, matches.Skip(page * PageSize).Take(PageSize).ToArray());
    }

    internal static string GetClothingGroup(ushort id) => ItemInfo.GetItemKind(id) switch
    {
        ItemKind.Top_Long or ItemKind.Top_Middle or ItemKind.Top_Short => "tops",
        ItemKind.Bottoms_Long or ItemKind.Bottoms_Middle or ItemKind.Bottoms_Short => "bottoms",
        ItemKind.Onepiece_Dress or ItemKind.Onepiece_Long or ItemKind.Onepiece_Middle or ItemKind.Onepiece_Short => "dresses",
        ItemKind.Kind_Cap or ItemKind.Kind_Helmet => "headwear",
        ItemKind.Kind_Accessory => "accessories", ItemKind.Kind_Socks => "socks",
        ItemKind.Kind_Shoes or ItemKind.Shoes_Boots or ItemKind.Shoes_Pumps => "shoes",
        ItemKind.Kind_Bag => "bags", ItemKind.Kind_Umbrella => "umbrellas", _ => "other",
    };

    internal static string GetCategory(ushort id)
    {
        var kind = ItemInfo.GetItemKind(id);
        if (kind.IsFlower) return "nature";
        if (kind.IsClothing || kind is ItemKind.Kind_Shoes or ItemKind.Kind_Umbrella) return "clothing";
        if (kind.IsFurniture || kind is ItemKind.Kind_FtrWall or ItemKind.Kind_Counter or ItemKind.Kind_Pillar or ItemKind.Kind_DoorDeco) return "furniture";
        if (kind.IsCrafting) return "materials";
        return kind switch
        {
            ItemKind.Kind_Axe or ItemKind.Kind_Shovel or ItemKind.Kind_FishingRod or ItemKind.Kind_Net or ItemKind.Kind_Watering
                or ItemKind.Kind_Slingshot or ItemKind.Kind_Ladder or ItemKind.Kind_WoodenStickTool or ItemKind.Kind_ChangeStick
                or ItemKind.Kind_SettingLadder or ItemKind.Kind_GroundMaker or ItemKind.Kind_RiverMaker or ItemKind.Kind_CliffMaker => "tools",
            ItemKind.Kind_Tree or ItemKind.Kind_TreeSeedling or ItemKind.Kind_Bush or ItemKind.Kind_BushSeedling
                or ItemKind.Kind_Fruit or ItemKind.Kind_Vegetable or ItemKind.Kind_VegeTree or ItemKind.Kind_VegeSeedling
                or ItemKind.Kind_Weed or ItemKind.Kind_WeedLight or ItemKind.Kind_Vine or ItemKind.Kind_Mushroom => "nature",
            ItemKind.Kind_Ore or ItemKind.Kind_StarPiece or ItemKind.Kind_ShellDrift or ItemKind.Kind_Honeycomb
                or ItemKind.Kind_AutumnLeaf or ItemKind.Kind_Sakurapetal or ItemKind.Kind_SnowCrystal or ItemKind.Kind_EasterEgg => "materials",
            ItemKind.Kind_Fish or ItemKind.Kind_Insect or ItemKind.Kind_DiveFish or ItemKind.Kind_Fossil or ItemKind.Kind_FossilUnknown => "creatures",
            ItemKind.Kind_RoomFloor or ItemKind.Kind_RoomWall or ItemKind.Kind_Rug or ItemKind.Kind_RugMyDesign => "interior",
            ItemKind.Kind_Dishes or ItemKind.Kind_Drink or ItemKind.Kind_CookingMaterial or ItemKind.Kind_Candy
                or ItemKind.Kind_BdayCupcake or ItemKind.Kind_SouvenirChocolate => "food",
            ItemKind.Kind_DIYRecipe or ItemKind.Kind_DummyDIYRecipe or ItemKind.Kind_DummyRecipe => "recipes",
            _ => "other",
        };
    }
}
