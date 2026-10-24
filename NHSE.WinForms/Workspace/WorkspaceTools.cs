using System;
using System.Windows.Forms;
using NHSE.Core;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private void OpenWorkspaceTool(string tool, int playerIndex, int residentIndex)
    {
        var session = RequireSession();
        var save = session.SaveFile;
        if ((uint)playerIndex >= save.Players.Count || (uint)residentIndex >= MainSaveOffsets.VillagerCount)
            throw new ArgumentException("invalidRequest");
        var player = save.Players[playerIndex];
        var main = save.Main;
        session.EditNative(() =>
        {
            switch (tool)
            {
                case "terrain":
                    using (var editor = new FieldItemEditor(main)) editor.ShowDialog(this);
                    break;
                case "playerHouses":
                    var playerHouses = main.GetPlayerHouses();
                    using (var editor = new PlayerHouseEditor(playerHouses, save.Players, main, playerIndex))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.SetPlayerHouses(playerHouses);
                    break;
                case "residentHouses":
                    var houses = main.GetVillagerHouses();
                    using (var editor = new VillagerHouseEditor(houses, main.GetVillagers(), main, residentIndex))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.SetVillagerHouses(houses);
                    break;
                case "campsite":
                    using (var editor = new CampsiteEditor(save)) editor.ShowDialog(this);
                    break;
                case "fruitFlowers":
                    using (var editor = new FruitsFlowersEditor(player, main)) editor.ShowDialog(this);
                    break;
                case "recycle":
                    var items = main.RecycleBin;
                    using (var editor = new PlayerItemEditor(items, 10, 4))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.RecycleBin = items;
                    break;
                case "turnips":
                    var prices = main.Turnips;
                    using (var editor = new SingleObjectEditor<TurnipStonk>(prices, PropertySort.Categorized, false))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.Turnips = prices;
                    break;
                case "patterns":
                    var patterns = main.GetDesigns();
                    using (var editor = new PatternEditor(patterns, player))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.SetDesigns(patterns);
                    break;
                case "proDesigns":
                    var pro = main.GetDesignsPRO();
                    using (var editor = new PatternEditorPRO(pro, player))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.SetDesignsPRO(pro);
                    break;
                case "flag":
                    var flag = new[] { main.FlagMyDesign };
                    using (var editor = new PatternEditor(flag, player))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.FlagMyDesign = flag[0];
                    break;
                case "tailor":
                    var tailor = main.GetDesignsTailor();
                    using (var editor = new PatternEditorPRO(tailor, player))
                        if (editor.ShowDialog(this) == DialogResult.OK) main.SetDesignsTailor(tailor);
                    break;
                default: throw new ArgumentException("invalidRequest");
            }
        });
    }
}
