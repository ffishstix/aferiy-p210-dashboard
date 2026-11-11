using System.Drawing.Drawing2D;

namespace BatteryWatch;

internal static class RoundedWindow
{
    public static void Apply(Control form, int radius = 18)
    {
        if (form.Width < radius * 2 || form.Height < radius * 2) return;
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(form.Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(form.Width - d - 1, form.Height - d - 1, d, d, 0, 90);
        path.AddArc(0, form.Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        var old = form.Region;
        form.Region = new Region(path);
        old?.Dispose();
    }
}
