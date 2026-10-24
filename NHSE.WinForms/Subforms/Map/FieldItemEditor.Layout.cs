using System;
using System.Drawing;
using System.Windows.Forms;

namespace NHSE.WinForms;

public sealed partial class FieldItemEditor
{
    private Action? ViewportResized;
    private System.Windows.Forms.Timer ViewportResizeTimer = null!;
    private bool FullScreen;
    private FormWindowState PreviousWindowState;
    private Rectangle PreviousWindowBounds;

    private void InitializeWorkspaceLayout()
    {
        ViewportResizeTimer = new System.Windows.Forms.Timer(components) { Interval = 150 };
        ViewportResizeTimer.Tick += (_, _) =>
        {
            ViewportResizeTimer.Stop();
            ViewportResized?.Invoke();
        };
        PAN_Viewport.Resize += (_, _) => LayoutViewport();
        PB_Viewport.SizeChanged += (_, _) =>
        {
            ViewportResizeTimer.Stop();
            ViewportResizeTimer.Start();
        };
        B_FullScreen.Click += (_, _) => ToggleFullScreen();

        var area = Screen.FromControl(this).WorkingArea;
        var metrics = CurrentAutoScaleDimensions;
        var minimum = SizeFromClientSize(new Size((int)Math.Ceiling(960 * metrics.Width / 7), (int)Math.Ceiling(560 * metrics.Height / 15)));
        MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
        LayoutViewport();
    }

    private void LayoutViewport() => PB_Viewport.Bounds = MapViewportGeometry.FitSquare(PAN_Viewport.ClientSize);

    private void ToggleFullScreen()
    {
        if (!FullScreen)
        {
            PreviousWindowState = WindowState;
            PreviousWindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            FullScreen = true;
        }
        else
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = PreviousWindowBounds;
            WindowState = PreviousWindowState;
            FullScreen = false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11 || (FullScreen && keyData == Keys.Escape))
        {
            ToggleFullScreen();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
