using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

/// <summary>Local-only Vue shell. Native file access stays behind a bounded command bridge.</summary>
public sealed partial class WorkspaceForm : Form
{
    private const string Origin = "https://nhse.local";
    private readonly WebView2 Browser = new() { Dock = DockStyle.Fill };
    private WorkspaceSession? Session;
    private bool HandlingMessage;
    private bool FullScreen;
    private Rectangle PreviousBounds;
    private FormWindowState PreviousState;
    private string? StartupPath;
    private readonly Dictionary<int, string?> Avatars = new();
    private readonly IVillager[]? PickerResidents;
    private readonly int PickerInitialIndex;
    public int PickedResidentIndex { get; private set; }
    public string? PickedVillager { get; private set; }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WorkspaceForm(bool processCommandLine = true, IVillager[]? pickerResidents = null, int pickerIndex = 0)
    {
        PickerResidents = pickerResidents;
        PickerInitialIndex = PickedResidentIndex = pickerIndex;
        StartupPath = processCommandLine ? Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => Directory.Exists(x)
            || (File.Exists(x) && (x.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))) : null;
        WorkspaceText.SetLanguage(Program.Settings.Language);
        Text = PickerResidents is null ? "NHSE" : "NHSE · " + WorkspaceText.Get("villagerCatalogTitle");
        Icon = Properties.Resources.icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(Math.Min(LogicalToDeviceUnits(1180), area.Width - 20), Math.Min(LogicalToDeviceUnits(780), area.Height - 40));
        MinimumSize = new Size(Math.Min(LogicalToDeviceUnits(800), area.Width), Math.Min(LogicalToDeviceUnits(550), area.Height));
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Controls.Add(Browser);
        Shown += async (_, _) => await InitializeBrowser();
    }

    private async Task InitializeBrowser()
    {
        try
        {
            var assets = Path.Combine(Program.WorkingDirectory, "Workspace");
            if (!File.Exists(Path.Combine(assets, "index.html")))
                throw new FileNotFoundException(WorkspaceText.Get("missingAssets"));
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NHSE", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (IsDisposed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            var core = Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("nhse.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NavigationStarting += (_, e) => e.Cancel = !IsTrusted(e.Uri);
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessage;
            Browser.Source = new Uri(Origin + "/index.html");
        }
        catch (Exception ex)
        {
            Browser.Visible = false;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(40), AutoScroll = true };
            panel.Controls.Add(new Label { Text = WorkspaceText.Get("runtimeMissing"), AutoSize = true, MaximumSize = new Size(600, 0) });
            panel.Controls.Add(new Label { Text = ex.Message, AutoSize = true, MaximumSize = new Size(600, 0) });
            var fallback = new Button { Text = WorkspaceText.Get("legacy"), AutoSize = true, Padding = new Padding(12) };
            fallback.Click += (_, _) => { using var main = new Main(); main.ShowDialog(this); };
            panel.Controls.Add(fallback);
            Controls.Add(panel);
            panel.BringToFront();
        }
    }

    private static bool IsTrusted(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == "https" && parsed.Host == "nhse.local" && parsed.IsDefaultPort;

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrusted(e.Source))
            return;
        string? id = null;
        var claimed = false;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            id = root.GetProperty("id").GetString();
            if (id is null || id.Length > 80)
                return;
            if (HandlingMessage)
            {
                Reply(new { id, ok = false, error = "busy" });
                return;
            }
            HandlingMessage = true;
            claimed = true;
            var command = root.GetProperty("command").GetString();
            var payload = root.GetProperty("payload");
            // Yield so the page can paint its progress state before native dialogs / save work.
            await Task.Yield();
            if (IsDisposed) return;
            object? data = Dispatch(command, payload, e);
            Reply(new { id, ok = true, data });
        }
        catch (Exception ex)
        {
            if (id is not null)
                Reply(new { id, ok = false, error = ex.Message is "villagerHouseMissing" or "roomPlacementBlocked" or "designFileSize" or "designCapacity" or "designNoFiles" ? ex.Message : ex is ArgumentException ? "invalidRequest" : "operationFailed", detail = ex.Message });
        }
        finally { if (claimed) HandlingMessage = false; }
    }

    private void Reply(object value)
    {
        if (!IsDisposed && Browser.CoreWebView2 is { } core)
            core.PostWebMessageAsJson(JsonSerializer.Serialize(value, Json));
    }

    private object State()
    {
        if (Session is not null) Text = Session.SaveFile.GetSaveTitle("NHSE");
        return new { language = GameInfo.CurrentLanguage, recent = Program.Settings.LastFilePath, session = Session?.Snapshot(PlayerAvatar, VillagerImage), fullScreen = FullScreen,
            villagerPicker = PickerResidents is null ? null : new { index = PickerInitialIndex,
                residents = PickerResidents.Select((v, i) => new { index = i, id = v.InternalName, name = GameInfo.Strings.GetVillager(v.InternalName), image = VillagerImage(v.InternalName), empty = v.Species >= (byte)VillagerSpecies.non }).ToArray() } };
    }

    private string? PlayerAvatar(int index)
    {
        if (Avatars.TryGetValue(index, out var cached)) return cached;
        string? result = null;
        try
        {
            var data = RequireSession().SaveFile.Players[index].Personal.GetPhotoData();
            if (data.Length != 0)
            {
                using var source = new MemoryStream(data);
                using var original = Image.FromStream(source);
                using var thumbnail = new Bitmap(original, new Size(96, 96));
                using var output = new MemoryStream();
                thumbnail.Save(output, ImageFormat.Png);
                result = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException) { }
        Avatars[index] = result;
        return result;
    }
    private WorkspaceSession RequireSession() => Session ?? throw new ArgumentException("invalidRequest");
    private static int Int(JsonElement p, string key) => p.GetProperty(key).GetInt32();
    private static string Str(JsonElement p, string key) => p.GetProperty(key).GetString() ?? throw new ArgumentException("invalidRequest");
    private static uint? OptionalUInt(JsonElement p, string key) => p.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetUInt32() : null;

    private object? Dispatch(string? command, JsonElement p, CoreWebView2WebMessageReceivedEventArgs message)
    {
        switch (command)
        {
            case "bootstrap":
                var argument = StartupPath;
                StartupPath = null;
                if (Session is null && argument is not null)
                    OpenPath(argument);
                return State();
            case "language":
                WorkspaceText.SetLanguage(Str(p, "language"));
                return State();
            case "villagerCatalog":
                var villagers = WorkspaceVillagers.Search(Str(p, "query"), Int(p, "species"), Int(p, "personality"), Int(p, "page"));
                return new { villagers.Page, villagers.Pages, villagers.Total, villagers.Species, villagers.Personalities,
                    items = villagers.Items.Select(v => new { v.Id, v.Name, v.Species, v.Personality, image = VillagerImage(v.Id) }).ToArray() };
            case "pickVillager":
            case "pickResident":
                if (PickerResidents is null) throw new ArgumentException("invalidRequest");
                var target = Int(p, "index");
                if ((uint)target >= PickerResidents.Length) throw new ArgumentException("invalidRequest");
                var chosen = command == "pickVillager" ? Str(p, "id") : null;
                if (chosen is not null && (!WorkspaceVillagers.Contains(chosen) || PickerResidents.Where((_, i) => i != target).Any(v => v.InternalName == chosen)))
                    throw new ArgumentException("invalidRequest");
                PickedResidentIndex = target;
                PickedVillager = chosen;
                DialogResult = DialogResult.OK;
                Close();
                return null;
            case "cancelVillager":
                if (PickerResidents is null) throw new ArgumentException("invalidRequest");
                DialogResult = DialogResult.Cancel;
                Close();
                return null;
            case "open":
                if (!ConfirmLeave()) return State();
                if (Str(p, "kind") == "folder")
                {
                    using var dialog = new FolderBrowserDialog { Description = WorkspaceText.Get("openFolder"), UseDescriptionForTitle = true };
                    if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.SelectedPath);
                }
                else
                {
                    using var dialog = new OpenFileDialog { Title = WorkspaceText.Get("openSave"), Filter = "Animal Crossing Save (main.dat;*.zip)|main.dat;*.zip", FileName = "main.dat" };
                    if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.FileName);
                }
                return State();
            case "openRecent":
                if (ConfirmLeave()) OpenPath(Program.Settings.LastFilePath);
                return State();
            case "drop":
                if (message.AdditionalObjects.Count == 1 && message.AdditionalObjects[0] is CoreWebView2File file && ConfirmLeave())
                    OpenPath(file.Path);
                return State();
            case "closeSave":
                if (ConfirmLeave()) { Session = null; Text = "NHSE"; }
                return State();
            case "save": RequireSession().Save(); return State();
            case "undo": RequireSession().Undo(); return State();
            case "redo": RequireSession().Redo(); return State();
            case "inventory": return Inventory(Int(p, "player"), Str(p, "container"), Int(p, "page"), Str(p, "query"));
            case "catalog":
                var catalog = WorkspaceCatalog.Search(Str(p, "query"), Str(p, "category"), Int(p, "page"), p.TryGetProperty("subcategory", out var sub) ? sub.GetString() ?? "all" : "all");
                return new { catalog.Page, catalog.Pages, catalog.Total, catalog.TotalAll, catalog.Categories, catalog.Subcategories,
                    items = catalog.Items.Select(x => ItemView(new Item((ushort)x.ItemId), -1, true, x.Name)).ToArray() };
            case "setItem":
                var session = RequireSession();
                var player = Int(p, "player"); var container = Str(p, "container"); var index = Int(p, "index");
                var items = session.GetItems(player, container);
                if ((uint)index >= items.Length) throw new ArgumentException("invalidRequest");
                var itemId = Int(p, "itemId"); var quantity = Int(p, "quantity");
                if (!WorkspaceCatalog.Contains(itemId))
                    throw new ArgumentException("invalidRequest");
                var item = items[index].ItemId == itemId ? new Item(items[index].RawValue) : new Item((ushort)itemId);
                var max = WorkspaceSession.MaxQuantity(item);
                if (quantity < 1 || quantity > max) throw new ArgumentException("invalidRequest");
                if (max > 1) item.Count = (ushort)(quantity - 1);
                session.SetItem(player, container, index, item);
                return State();
            case "deleteItem":
                RequireSession().SetItem(Int(p, "player"), Str(p, "container"), Int(p, "index"), new Item(Item.NONE));
                return State();
            case "detailItem":
                var s = RequireSession(); var pi = Int(p, "player"); var c = Str(p, "container"); var ix = Int(p, "index");
                var array = s.GetItems(pi, c);
                if ((uint)ix >= array.Length || !s.IsSlotAvailable(pi, c, ix)) throw new ArgumentException("invalidRequest");
                using (var dialog = new SingleItemEditor(array[ix]))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) s.SetItem(pi, c, ix, dialog.Item);
                }
                return State();
            case "setPlayer":
                RequireSession().SetPlayer(Int(p, "player"), p.GetProperty("wallet").GetUInt32(), p.GetProperty("bank").GetUInt32(), p.GetProperty("miles").GetUInt32(), p.GetProperty("earnedMiles").GetUInt32(),
                    Str(p, "name"), Str(p, "island"), OptionalUInt(p, "pocketCapacity"), OptionalUInt(p, "storageCapacity"), OptionalUInt(p, "hotelTickets"), OptionalUInt(p, "poki"));
                return State();
            case "designs": return RequireSession().GetDesignWorkspace(Str(p, "kind"), Int(p, "index"));
            case "designImport":
                ImportDesignFiles(Str(p, "kind"), Int(p, "index"), Int(p, "player"), p.GetProperty("all").GetBoolean(), p.GetProperty("useCurrentPlayer").GetBoolean());
                return State();
            case "designExport":
                ExportDesignFiles(Str(p, "kind"), Int(p, "index"), p.GetProperty("all").GetBoolean());
                return State();
            case "designPng":
                ExportDesignPng(Str(p, "kind"), Int(p, "index"), Int(p, "sheet"), p.GetProperty("palette").GetBoolean());
                return State();
            case "housing": return RequireSession().GetHousing(Str(p, "kind"), Int(p, "player"), p.TryGetProperty("resident", out var houseResident) ? RequireSession().FindResidentHouse(houseResident.GetInt32()) : Int(p, "index"), Int(p, "room"), Int(p, "layer"), Sprite);
            case "setHousing":
                RequireSession().SetHousing(Str(p, "kind"), Int(p, "player"), Int(p, "index"), p.GetProperty("values").Deserialize<Dictionary<string, JsonElement>>() ?? throw new ArgumentException("invalidRequest"));
                return State();
            case "setRoomItem":
                RequireSession().SetRoomItem(Int(p, "index"), Int(p, "room"), Int(p, "layer"), Int(p, "x"), Int(p, "y"),
                    new WorkspaceSession.RoomItem(Int(p, "itemId"), Int(p, "systemParam"), Int(p, "additionalParam"), Int(p, "freeParam")),
                    p.GetProperty("autoExtensions").GetBoolean(), p.GetProperty("noOverwrite").GetBoolean(), p.GetProperty("delete").GetBoolean());
                return State();
            case "progress": return RequireSession().GetProgress(Int(p, "player"), Str(p, "section"));
            case "setRecipe":
                RequireSession().SetRecipe(Int(p, "player"), Int(p, "index"), p.GetProperty("known").GetBoolean(),
                    p.GetProperty("made").GetBoolean(), p.GetProperty("fresh").GetBoolean(), p.GetProperty("favorite").GetBoolean());
                return State();
            case "setReaction":
                RequireSession().SetReaction(Int(p, "player"), Int(p, "index"), Int(p, "value"), p.GetProperty("fresh").GetBoolean());
                return State();
            case "setAchievement":
                RequireSession().SetAchievement(Int(p, "player"), Int(p, "index"), p.GetProperty("count").GetUInt32(), Int(p, "flag"),
                    p.GetProperty("dates").EnumerateArray().Select(x => x.GetString() ?? "").ToArray(),
                    p.GetProperty("read").EnumerateArray().Select(x => x.GetBoolean()).ToArray());
                return State();
            case "progressBatch":
                RequireSession().ProgressBatch(Int(p, "player"), Str(p, "section"), Str(p, "action"), Int(p, "index"));
                return State();
            case "setTurnips":
                RequireSession().SetTurnips(new WorkspaceSession.TurnipSettings(p.GetProperty("buyPrice").GetUInt32(),
                    p.GetProperty("sellPrices").EnumerateArray().Select(x => x.GetUInt32()).ToArray(),
                    p.GetProperty("pattern").GetUInt32(), p.GetProperty("feverStart").GetUInt32()));
                return State();
            case "setIsland":
                RequireSession().SetIsland(new WorkspaceSession.IslandSettings(Int(p, "airportColor"), Int(p, "hemisphere"), Int(p, "tourHemisphere"),
                    p.GetProperty("weatherSeed").GetUInt32(), p.GetProperty("tourWeatherSeed").GetUInt32()));
                return State();
            case "setResident":
                RequireSession().SetResident(Int(p, "index"), Int(p, "personality"), Str(p, "catchphrase"), p.GetProperty("movingOut").GetBoolean());
                return State();
            case "replaceResident":
                RequireSession().ReplaceResident(Int(p, "index"), Str(p, "id"));
                return State();
            case "tool":
                OpenWorkspaceTool(Str(p, "tool"), Int(p, "player"), p.TryGetProperty("resident", out var toolResident) ? toolResident.GetInt32() : 0);
                return State();
            case "legacy":
                using (var editor = new Editor(RequireSession().SaveFile))
                {
                    editor.SelectWorkspaceSection(Str(p, "section"), p.TryGetProperty("player", out var selectedPlayer) ? selectedPlayer.GetInt32() : 0,
                        p.TryGetProperty("resident", out var selectedResident) ? selectedResident.GetInt32() : 0);
                    editor.ShowDialog(this);
                    editor.ApplyWorkspaceChanges();
                }
                RequireSession().InvalidateHistory();
                Avatars.Clear();
                WorkspaceText.SetLanguage(Program.Settings.Language);
                return State();
            case "fullScreen": ToggleFullScreen(); return State();
            default: throw new ArgumentException("invalidRequest");
        }
    }

    private object Inventory(int player, string container, int page, string query)
    {
        var session = RequireSession();
        var items = session.GetItems(player, container);
        const int pageSize = 60;
        // Storage can physically contain more slots than the player has unlocked.
        // Keep the pocket's familiar 40-slot shape, but avoid pages of locked storage.
        var visible = items.Select((item, index) => (item, index))
            .Where(x => container != "storage" || session.IsSlotAvailable(player, container, x.index)).ToArray();
        var filtered = visible.Where(x => string.IsNullOrWhiteSpace(query)
            || (!x.item.IsNone && GameInfo.Strings.GetItemName(x.item).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        var pages = Math.Max(1, (filtered.Length + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 0, pages - 1);
        return new { page, pages, total = visible.Length, matched = filtered.Length, used = visible.Count(x => !x.item.IsNone),
            items = filtered.Skip(page * pageSize).Take(pageSize).Select(x => ItemView(x.item, x.index, session.IsSlotAvailable(player, container, x.index))).ToArray() };
    }

    private static object ItemView(Item item, int index, bool available, string? name = null) => new
    {
        index, itemId = item.ItemId, name = name ?? (item.IsNone ? "" : GameInfo.Strings.GetItemName(item)),
        empty = item.IsNone, available, quantity = WorkspaceSession.Quantity(item), maxQuantity = WorkspaceSession.MaxQuantity(item),
        image = Sprite(item),
    };

    private static string? Sprite(Item item)
    {
        if (item.IsNone) return null;
        using var image = ItemSprite.GetItemSpriteCopy(item);
        if (image is null) return null;
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static string? VillagerImage(string id)
    {
        var image = VillagerSprite.GetVillagerSprite(id);
        if (image is null) return null;
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }

    private void OpenPath(string path)
    {
        if (SaveFileLoader.TryGetSaveFile(path, out var save))
        {
            if (save.Players.Count == 0) throw new ArgumentException("invalidRequest");
            Session = new WorkspaceSession(save);
            Avatars.Clear();
            Program.Settings.LastFilePath = path;
            Program.SaveSettings();
            Text = save.GetSaveTitle("NHSE");
        }
    }

    private bool ConfirmLeave()
    {
        if (Session is not { IsDirty: true }) return true;
        var answer = MessageBox.Show(this, WorkspaceText.Get("unsavedPrompt"), "NHSE", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) Session.Save();
        return true;
    }

    private void ToggleFullScreen()
    {
        if (!FullScreen)
        {
            PreviousState = WindowState;
            PreviousBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = PreviousBounds;
            WindowState = PreviousState;
        }
        FullScreen = !FullScreen;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11 || (FullScreen && keyData == Keys.Escape))
        {
            ToggleFullScreen();
            Reply(new { eventName = "state", data = State() });
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { if (!ConfirmLeave()) e.Cancel = true; }
        catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message, WorkspaceText.Get("operationFailed")); }
        Program.SaveSettings();
        base.OnFormClosing(e);
    }
}
