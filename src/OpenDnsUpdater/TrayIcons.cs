using System.Drawing.Drawing2D;

namespace OpenDnsUpdater;

/// <summary>Small tray glyphs drawn at runtime so the app ships with no binary
/// icon assets. Two icons live for the process lifetime, so the small GDI handle
/// each retains (via Icon.FromHandle) is never an issue in practice.</summary>
internal static class TrayIcons
{
    public static readonly Icon Idle = Build(Color.FromArgb(0, 122, 204));
    public static readonly Icon Warning = Build(Color.FromArgb(216, 59, 1));

    private static Icon Build(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);

            using var pen = new Pen(Color.White, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // Simple up-arrow glyph suggesting "address changed / pushed".
            g.DrawLine(pen, 16, 9, 16, 24);
            g.DrawLine(pen, 16, 9, 11, 14);
            g.DrawLine(pen, 16, 9, 21, 14);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
