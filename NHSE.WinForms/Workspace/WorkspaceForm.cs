using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NHSE.Core;
using NHSE.Sprites;

namespace NHSE.WinForms;

/// <summary>Local-only Vue shell. Native file access stays behind a bounded command bridge.</summary>
public sealed class WorkspaceForm : Form
{
    private const string Origin = "https://nhse.local";
    private readonly WebView2 Browser = new() { Dock = DockStyle.Fill };
    private WorkspaceSession? Session;
    private bool HandlingMessage;
    private bool FullScreen;
    private Rectangle PreviousBounds;
    private FormWindowState PreviousState;
    private string? StartupPath;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WorkspaceForm(bool processCommandLine = true)
    {
        StartupPath = processCommandLine ? Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => Directory.Exists(x)
            || (File.Exists(x) && (x.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))) : null;
        WorkspaceText.SetLanguage(Program.Settings.Language);
        Text = "NHSE";
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
                Reply(new { id, ok = false, error = ex is ArgumentException ? "invalidRequest" : "operationFailed", detail = ex.Message });
        }
        finally { if (claimed) HandlingMessage = false; }
    }

    private void Reply(object value)
    {
        if (!IsDisposed && Browser.CoreWebView2 is { } core)
            core.PostWebMessageAsJson(JsonSerializer.Serialize(value, Json));
    }

    private object State() => new { language = GameInfo.CurrentLanguage, recent = Program.Settings.LastFilePath, session = Session?.Snapshot(), fullScreen = FullScreen };
    private WorkspaceSession RequireSession() => Session ?? throw new ArgumentException("invalidRequest");
    private static int Int(JsonElement p, string key) => p.GetProperty(key).GetInt32();
    private static string Str(JsonElement p, string key) => p.GetProperty(key).GetString() ?? throw new ArgumentException("invalidRequest");

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
                var query = Str(p, "query");
                return GameInfo.Strings.ItemDataSource.Where(x => x.Value < Item.FieldItemMin && x.Value > 0 && x.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .Take(60).Select(x => ItemView(new Item((ushort)x.Value), -1, true)).ToArray();
            case "setItem":
                var session = RequireSession();
                var player = Int(p, "player"); var container = Str(p, "container"); var index = Int(p, "index");
                var items = session.GetItems(player, container);
                if ((uint)index >= items.Length) throw new ArgumentException("invalidRequest");
                var itemId = Int(p, "itemId"); var quantity = Int(p, "quantity");
                if (!GameInfo.Strings.ItemDataSource.Any(x => x.Value == itemId) || itemId >= Item.FieldItemMin || itemId <= 0)
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
                RequireSession().SetPlayer(Int(p, "player"), p.GetProperty("wallet").GetUInt32(), p.GetProperty("bank").GetUInt32(), p.GetProperty("miles").GetUInt32(), p.GetProperty("earnedMiles").GetUInt32());
                return State();
            case "legacy":
                using (var editor = new Editor(RequireSession().SaveFile))
                {
                    editor.SelectWorkspaceSection(Str(p, "section"));
                    editor.ShowDialog(this);
                    editor.ApplyWorkspaceChanges();
                }
                RequireSession().InvalidateHistory();
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

    private static object ItemView(Item item, int index, bool available) => new
    {
        index, itemId = item.ItemId, name = item.IsNone ? "" : GameInfo.Strings.GetItemName(item),
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

    private void OpenPath(string path)
    {
        if (SaveFileLoader.TryGetSaveFile(path, out var save))
        {
            if (save.Players.Count == 0) throw new ArgumentException("invalidRequest");
            Session = new WorkspaceSession(save);
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
