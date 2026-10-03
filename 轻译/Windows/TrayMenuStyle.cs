using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QingYi.Windows;

internal enum TrayMenuGlyph
{
    Settings,
    Pause,
    Resume,
    Window,
    Exit,
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayMenuColorTable())
    {
        RoundedEdges = true;
    }

}

internal sealed class TrayMenuColorTable : ProfessionalColorTable
{
    private static readonly Color Surface = Color.FromArgb(250, 249, 253);
    private static readonly Color Hover = Color.FromArgb(240, 237, 255);
    private static readonly Color Pressed = Color.FromArgb(231, 226, 255);
    private static readonly Color Line = Color.FromArgb(229, 226, 240);

    public override Color ToolStripDropDownBackground => Surface;
    public override Color MenuBorder => Line;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Pressed;
    public override Color MenuItemPressedGradientEnd => Pressed;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Surface;
}

internal static class TrayMenuGlyphs
{
    private static readonly Color Accent = Color.FromArgb(116, 100, 232);
    private static readonly Color Danger = Color.FromArgb(190, 78, 88);

    public static Bitmap Create(TrayMenuGlyph glyph)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(0.8f, 0.8f);
        graphics.Clear(Color.Transparent);
        var color = glyph switch
        {
            TrayMenuGlyph.Exit => Danger,
            _ => Accent
        };
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        switch (glyph)
        {
            case TrayMenuGlyph.Settings:
                graphics.DrawEllipse(pen, 6, 6, 8, 8);
                graphics.DrawEllipse(pen, 8.5f, 8.5f, 3, 3);
                graphics.DrawLine(pen, 10, 2.5f, 10, 5);
                graphics.DrawLine(pen, 10, 15, 10, 17.5f);
                graphics.DrawLine(pen, 2.5f, 10, 5, 10);
                graphics.DrawLine(pen, 15, 10, 17.5f, 10);
                graphics.DrawLine(pen, 4.7f, 4.7f, 6.5f, 6.5f);
                graphics.DrawLine(pen, 13.5f, 13.5f, 15.3f, 15.3f);
                graphics.DrawLine(pen, 4.7f, 15.3f, 6.5f, 13.5f);
                graphics.DrawLine(pen, 13.5f, 6.5f, 15.3f, 4.7f);
                break;
            case TrayMenuGlyph.Pause:
                graphics.FillRectangle(brush, 5, 4, 3, 12);
                graphics.FillRectangle(brush, 12, 4, 3, 12);
                break;
            case TrayMenuGlyph.Resume:
                graphics.FillPolygon(brush, new[] { new PointF(6, 3.5f), new PointF(15.5f, 10), new PointF(6, 16.5f) });
                break;
            case TrayMenuGlyph.Window:
                graphics.DrawRectangle(pen, 3, 4, 14, 12);
                graphics.DrawLine(pen, 3, 7.5f, 17, 7.5f);
                break;
            case TrayMenuGlyph.Exit:
                graphics.DrawArc(pen, 4.5f, 5, 11, 11, 42, 276);
                graphics.DrawLine(pen, 10, 2.5f, 10, 9);
                break;
        }

        return bitmap;
    }
}
