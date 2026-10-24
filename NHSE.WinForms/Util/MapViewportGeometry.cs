using System;
using System.Drawing;

namespace NHSE.WinForms;

internal static class MapViewportGeometry
{
    public static Rectangle FitSquare(Size available)
    {
        int side = Math.Max(1, Math.Min(available.Width, available.Height));
        // Include the border and use whole pixels per tile to keep the grid sharp.
        if (side >= 34)
            side = ((side - 2) / 32 * 32) + 2;
        return new Rectangle(Math.Max(0, (available.Width - side) / 2), Math.Max(0, (available.Height - side) / 2), side, side);
    }

    public static Point ToImage(Point location, Size client, Size image)
    {
        int width = Math.Max(1, image.Width);
        int height = Math.Max(1, image.Height);
        int x = (int)Math.Clamp((long)location.X * width / Math.Max(1, client.Width), 0, width - 1);
        int y = (int)Math.Clamp((long)location.Y * height / Math.Max(1, client.Height), 0, height - 1);
        return new Point(x, y);
    }
}
