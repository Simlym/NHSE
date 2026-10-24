#requires -Version 7.6
param([ValidateSet(1, 1.25, 1.5)][float]$Scale = 1, [string]$Source = 'current', [switch]$UseSystemDpi, [string]$Language = 'en', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Add-Type -AssemblyName System.Windows.Forms
$sources = [System.Collections.Generic.List[string]]::new()
$files = @(Get-ChildItem "$repo\NHSE.WinForms\Subforms" -Recurse -Filter '*.Designer.cs') + @(Get-Item "$repo\NHSE.WinForms\Editor.Designer.cs") + @(Get-ChildItem "$repo\NHSE.WinForms\Controls" -Filter '*.Designer.cs')
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $text = if ($Source -eq 'original') { ((git -C $repo show "HEAD:$relative") -join "`n").TrimStart([char]0xFEFF) } else { Get-Content $file.FullName -Raw }
    $name = [regex]::Match($text, 'partial class (\w+(?:<T>)?)').Groups[1].Value
    $simpleName = $name.Replace('<T>', '')
    $namespace = [regex]::Match($text, 'namespace ([\w.]+)').Groups[1].Value
    # Exercise the actual Designer layout without save data, resources, or event side effects.
    $text = [regex]::Replace($text, '(?m)^.*(?:\+=|Icon =|(?:\.Image|\.BackgroundImage) =|using NHSE.Core;).*\r?\n', '')
    if (!$UseSystemDpi) {
        $text = [regex]::Replace($text, '(new System.Drawing.Font\("[^"]+", )([\d.]+)F', { param($m) $m.Groups[1].Value + (([float]$m.Groups[2].Value * $Scale).ToString([cultureinfo]::InvariantCulture)) + 'F' })
    }
    $sources.Add($text)
    if ($simpleName -eq 'ItemGrid') { continue }
    $baseType = if ($file.DirectoryName -like '*\Controls') { 'System.Windows.Forms.UserControl' } else { 'System.Windows.Forms.Form' }
    $extra = if ($simpleName -eq 'PlayerItemEditor') { 'private System.Windows.Forms.Control ItemGrid = new System.Windows.Forms.Control();' } else { '' }
    if ($simpleName -eq 'Editor') { $extra = 'private System.Windows.Forms.Control Villagers = new System.Windows.Forms.Control();' }
    $sources.Add("namespace $namespace { public partial class $name : $baseType { public $simpleName() { InitializeComponent(); } $extra } }")
}
$sources.Add((Get-Content "$repo\NHSE.WinForms\Controls\ItemGrid.cs" -Raw).Replace('namespace NHSE.WinForms;', 'namespace NHSE.WinForms {') + "`n}")
if ($Source -ne 'original') {
    foreach ($layoutSource in 'Subforms\Map\FieldItemEditor.Layout.cs') {
        $sources.Add((Get-Content "$repo\NHSE.WinForms\$layoutSource" -Raw).Replace('namespace NHSE.WinForms;', 'namespace NHSE.WinForms {') + "`n}")
    }
}
$sources.Add((Get-Content "$repo\NHSE.WinForms\Util\MapViewportGeometry.cs" -Raw).Replace('namespace NHSE.WinForms;', 'namespace NHSE.WinForms {') + "`n}")
$sources.Add(@'
namespace NHSE.Core { public interface IGridItem { int Width { get; } int Height { get; } } }
namespace NHSE.WinForms { public class InterpolatingPictureBox : System.Windows.Forms.PictureBox {} }
'@)
$sources.Add(@'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
public static class LayoutAudit
{
    private sealed class GridSize(float scale) : NHSE.Core.IGridItem { public int Width => (int)Math.Round(64 * scale); public int Height => Width; }
    public static void Run(float scale, bool systemDpi, string languagePath, string output)
    {
        Application.SetHighDpiMode(systemDpi ? HighDpiMode.SystemAware : HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetDefaultFont(new Font(SystemFonts.MessageBoxFont.FontFamily, 9 * scale));
        var translations = System.IO.File.ReadAllLines(languagePath).Where(s => s.Contains('=')).Select(s => s.Split('=', 2)).GroupBy(s => s[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        var lines = new List<string>();
        var failures = new List<string>();
        var buttonCount = 0;
        var formTypes = typeof(LayoutAudit).Assembly.GetTypes().Where(t => typeof(Form).IsAssignableFrom(t));
        foreach (var originalType in formTypes)
        {
            var type = originalType.IsGenericTypeDefinition ? originalType.MakeGenericType(typeof(object)) : originalType;
            using var form = (Form)Activator.CreateInstance(type);
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            Translate(form, form.Name, translations);
            if (type.Name == "Editor")
            {
                var tab = (TabPage)form.Controls.Find("Tab_Villagers", true).Single();
                tab.Controls.Add(new NHSE.WinForms.VillagerEditor { Dock = DockStyle.Fill });
                ((TabControl)tab.Parent).SelectedTab = tab;
            }
            if (type.Name == "PlayerItemEditor")
            {
                var grid = new NHSE.WinForms.ItemGridEditor { Dock = DockStyle.Fill };
                ((NHSE.WinForms.ItemGrid)grid.Controls.Find("ItemGrid", true).Single()).InitializeGrid(10, 4, new GridSize(systemDpi ? 1 : scale));
                form.Controls.Find("PAN_Items", true).Single().Controls.Add(grid);
            }
            form.Show();
            form.PerformLayout();
            Application.DoEvents();
            CheckChildren(form, failures, form.Name);
            if (form.Name == "FieldItemEditor" && form.Controls.Find("TLP_Workspace", true).Length != 0) CheckMapWorkspace(form, failures);
            if (form.FormBorderStyle == FormBorderStyle.Sizable && form.Name != "FieldItemEditor") {
                var initial = form.ClientSize;
                form.Size = new Size(1, 1);
                if (form.ClientSize.Width < initial.Width || form.ClientSize.Height < initial.Height)
                    failures.Add($"MINIMUM SIZE {form.Name}");
            }
            foreach (var button in form.Controls.OfType<Button>().Where(b => b.Visible))
            {
                buttonCount++;
                lines.Add($"{form.Name}.{button.Name}: client={form.ClientSize}, bounds={button.Bounds}, font={button.Font.Name}/{button.Font.Size}, text={button.Text}, preferred={button.PreferredSize}");
                if (!form.ClientRectangle.Contains(button.Bounds)) failures.Add($"OUTSIDE {form.Name}.{button.Name}: {button.Bounds} / {form.ClientSize}");
                if (button.Height < TextRenderer.MeasureText(button.Text, button.Font).Height + 4) failures.Add($"TEXT HEIGHT {form.Name}.{button.Name}");
            }
            if (type.Name == "SingleItemEditor" || type.Name == "PlayerItemEditor" || type.Name == "Editor" || type.Name == "FieldItemEditor")
            {
                foreach (var controlName in new[] { "ItemEditor", "FLP_Controls", "FLP_Count", "FLP_Uses", "L_Count", "L_Uses", "NUD_Count" })
                    foreach (var child in form.Controls.Find(controlName, true))
                        lines.Add($"DETAIL {form.Name}.{child.Name}: {child.Bounds}, margin={child.Margin}, padding={child.Padding}, anchor={child.Anchor}, font={child.Font}, preferred={child.PreferredSize}");
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(System.IO.Path.Combine(output, type.Name + ".png"));
            }
            form.Hide();
        }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output, "bounds.txt"), lines);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output, "failures.txt"), failures.Distinct());
        Console.WriteLine($"Scale={scale}; systemDpi={systemDpi}; forms={formTypes.Count()}; buttons={buttonCount}; issues={failures.Distinct().Count()}");
        foreach (var failure in failures.Distinct()) Console.WriteLine(failure);
        if (failures.Count != 0) throw new InvalidOperationException("Layout checks failed; see failures.txt.");
    }
    private static void Translate(Control control, string formName, Dictionary<string, string> translations)
    {
        if (translations.TryGetValue(control is Form ? formName : formName + "." + control.Name, out var value)) control.Text = value;
        foreach (Control child in control.Controls) Translate(child, formName, translations);
    }
    private static void CheckMapWorkspace(Form form, List<string> failures)
    {
        if (form.WindowState != FormWindowState.Maximized || !form.MaximizeBox)
            failures.Add("MAP MUST OPEN MAXIMIZED");
        var viewport = form.Controls.Find("PB_Viewport", true).Single();
        int maximizedWidth = viewport.Width;
        CheckMapBounds(form, failures);
        var tabs = (TabControl)form.Controls.Find("TC_Editor", true).Single();
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            Application.DoEvents();
            CheckChildren(form, failures, form.Name);
        }
        tabs.SelectedIndex = 0;
        form.WindowState = FormWindowState.Normal;
        form.Size = form.MinimumSize;
        Application.DoEvents();
        CheckMapBounds(form, failures);
        if (viewport.Width >= maximizedWidth) failures.Add("MAP VIEW DOES NOT GROW WITH WINDOW");
        form.WindowState = FormWindowState.Maximized;
        Application.DoEvents();
        var fullScreen = (Button)form.Controls.Find("B_FullScreen", true).Single();
        fullScreen.PerformClick();
        Application.DoEvents();
        if (form.FormBorderStyle != FormBorderStyle.None || form.Bounds != Screen.FromControl(form).Bounds)
            failures.Add("MAP FULL SCREEN DOES NOT FILL MONITOR");
        CheckMapBounds(form, failures);
        var command = form.GetType().GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        command.Invoke(form, new object[] { new Message(), Keys.Escape });
        Application.DoEvents();
        if (form.FormBorderStyle != FormBorderStyle.Sizable || form.WindowState != FormWindowState.Maximized)
            failures.Add("MAP ESC DOES NOT RESTORE MAXIMIZED WINDOW");
        CheckMapBounds(form, failures);

        foreach (int side in new[] { 512, 788, 1000, 1920, 2160 })
        {
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    var position = new Point((2 * x + 1) * side / 64, (2 * y + 1) * side / 64);
                    var mapped = NHSE.WinForms.MapViewportGeometry.ToImage(position, new Size(side, side), new Size(512, 512));
                    if (mapped.X / 16 != x || mapped.Y / 16 != y) failures.Add("MAP TILE COORDINATE DRIFT");
                }
            var edge = NHSE.WinForms.MapViewportGeometry.ToImage(new Point(side, side), new Size(side, side), new Size(512, 512));
            if (edge != new Point(511, 511)) failures.Add("MAP EDGE WRAPS TO OPPOSITE CORNER");
        }
        var overview = NHSE.WinForms.MapViewportGeometry.ToImage(new Point(300, 264), new Size(600, 528), new Size(288, 256));
        if (overview != new Point(144, 128)) failures.Add("MINIMAP COORDINATE DRIFT");
    }
    private static void CheckMapBounds(Form form, List<string> failures)
    {
        var viewport = form.Controls.Find("PB_Viewport", true).Single();
        if (!viewport.Parent.ClientRectangle.Contains(viewport.Bounds) || viewport.Width != viewport.Height)
            failures.Add("MAP VIEWPORT MUST FIT AS SQUARE");
        if (viewport.Width < 256) failures.Add("MAP VIEWPORT TOO SMALL TO EDIT AT MINIMUM SIZE");
        foreach (var name in new[] { "B_Save", "B_Cancel", "B_FullScreen", "TC_Editor" })
        {
            var control = form.Controls.Find(name, true).Single();
            var bounds = form.RectangleToClient(control.Parent.RectangleToScreen(control.Bounds));
            if (!form.ClientRectangle.Contains(bounds)) failures.Add("MAP CONTROL OUTSIDE WINDOW: " + name);
        }
    }
    private static void CheckChildren(Control parent, List<string> failures, string path)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible || child is ToolStrip) continue;
            if (child is Button && !(parent is ScrollableControl sc && sc.AutoScroll) && !parent.ClientRectangle.Contains(child.Bounds))
                failures.Add($"CLIPPED {path}.{child.Name}: {child.Bounds} / {parent.ClientSize}");
            if (child is Button b && TextRenderer.MeasureText(b.Text, b.Font,
                new Size(Math.Max(1, b.ClientSize.Width - 8), int.MaxValue), TextFormatFlags.WordBreak).Height + 4 > b.ClientSize.Height)
                failures.Add($"BUTTON TEXT CLIPPED {path}.{child.Name}");
            if (child is NumericUpDown && parent is FlowLayoutPanel && !parent.ClientRectangle.Contains(child.Bounds))
                failures.Add($"NUMBER CLIPPED {path}.{child.Name}");
            if ((path.Contains(".ItemEditor") || path.Contains(".VillagerEditor")) && child is Label label && !label.AutoSize && !string.IsNullOrEmpty(label.Text))
            {
                var textSize = TextRenderer.MeasureText(label.Text, label.Font);
                if (textSize.Width > label.ClientSize.Width || textSize.Height > label.ClientSize.Height)
                    failures.Add($"LABEL CLIPPED {path}.{child.Name}: {label.ClientSize} / {textSize}");
            }
            if (parent is NHSE.WinForms.VillagerEditor && !parent.ClientRectangle.Contains(child.Bounds))
                failures.Add($"VILLAGER CONTROL CLIPPED {path}.{child.Name}: {child.Bounds} / {parent.ClientSize}");
            CheckChildren(child, failures, path + "." + child.Name);
        }
    }
}
'@)
$references = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + @('System.Windows.Forms','System.Windows.Forms.Primitives','System.Drawing.Common','System.Private.Windows.Core','System.Private.Windows.GdiPlus' | ForEach-Object { Join-Path $PSHOME ($_ + '.dll') })
$combinedSource = $sources -join "`n"
$usingDirectives = [regex]::Matches($combinedSource, '(?m)^using [^;]+;') | ForEach-Object Value | Sort-Object -Unique
$combinedSource = ($usingDirectives -join "`n") + "`n" + [regex]::Replace($combinedSource, '(?m)^using [^;]+;', '')
Add-Type -TypeDefinition $combinedSource -ReferencedAssemblies $references -CompilerOptions '/nullable:disable' -IgnoreWarnings -WarningAction SilentlyContinue
$output = if ($OutputDirectory) { $OutputDirectory } else { Join-Path ([System.IO.Path]::GetTempPath()) "nhse-layout-$Source-$Scale-$Language-$UseSystemDpi" }
New-Item -ItemType Directory -Path $output -Force | Out-Null
[LayoutAudit]::Run($Scale, $UseSystemDpi.IsPresent, "$repo\NHSE.WinForms\Resources\text\lang_$Language.txt", $output)
