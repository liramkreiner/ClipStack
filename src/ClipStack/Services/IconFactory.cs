using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ClipStack.Services;

/// <summary>Draws the app's clipboard glyph: monochrome for the tray, colored for the app icon.</summary>
public static class IconFactory
{
    /// <summary>Tray icon in the taskbar's foreground color; dimmed with a pause badge when paused.</summary>
    public static Icon CreateTrayIcon(int size, bool lightTaskbar, bool paused)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var fg = lightTaskbar ? Color.FromArgb(paused ? 110 : 255, 25, 25, 25) : Color.FromArgb(paused ? 120 : 255, 255, 255, 255);
            DrawGlyph(g, size, fg, filled: false);
            if (paused) DrawPauseBadge(g, size, lightTaskbar);
        }
        var hicon = bmp.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(hicon);
            return (Icon)borrowed.Clone(); // owns its own handle
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Colored application icon: white clipboard on an accent-colored rounded square.</summary>
    public static Bitmap CreateAppBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float r = size * 0.22f;
        using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
        using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(size, size), Color.FromArgb(0x2B, 0x88, 0xF0), Color.FromArgb(0x10, 0x5C, 0xC8)))
            g.FillPath(brush, path);

        g.TranslateTransform(size * 0.14f, size * 0.12f);
        g.ScaleTransform(0.72f, 0.76f);
        DrawGlyph(g, size, Color.White, filled: false);
        return bmp;
    }

    private static void DrawGlyph(Graphics g, int size, Color color, bool filled)
    {
        float s = size;
        float stroke = Math.Max(1.2f, s / 13f);
        var board = new RectangleF(s * 0.17f, s * 0.14f, s * 0.66f, s * 0.78f);
        using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using (var path = RoundedRect(board, s * 0.1f))
            g.DrawPath(pen, path);

        // Clip at the top
        var clip = new RectangleF(s * 0.34f, s * 0.05f, s * 0.32f, s * 0.17f);
        using (var path = RoundedRect(clip, s * 0.05f))
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);

        // Lines of "text"
        if (size >= 20)
        {
            float x1 = s * 0.31f, x2 = s * 0.69f;
            g.DrawLine(pen, x1, s * 0.42f, x2, s * 0.42f);
            g.DrawLine(pen, x1, s * 0.58f, x2, s * 0.58f);
            g.DrawLine(pen, x1, s * 0.74f, s * 0.55f, s * 0.74f);
        }
        else
        {
            g.DrawLine(pen, s * 0.34f, s * 0.5f, s * 0.66f, s * 0.5f);
            g.DrawLine(pen, s * 0.34f, s * 0.72f, s * 0.58f, s * 0.72f);
        }
    }

    private static void DrawPauseBadge(Graphics g, int size, bool lightTaskbar)
    {
        float d = size * 0.55f;
        var rect = new RectangleF(size - d, size - d, d - 0.5f, d - 0.5f);
        using (var bg = new SolidBrush(Color.FromArgb(0xE8, 0x9A, 0x1E)))
            g.FillEllipse(bg, rect);
        using var bar = new SolidBrush(Color.White);
        float bw = d * 0.16f, bh = d * 0.45f, cy = rect.Top + (d - bh) / 2;
        g.FillRectangle(bar, rect.Left + d * 0.29f, cy, bw, bh);
        g.FillRectangle(bar, rect.Left + d * 0.55f, cy, bw, bh);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Write a multi-resolution .ico (PNG-compressed entries).</summary>
    public static void ExportAppIcon(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(sz =>
        {
            using var bmp = CreateAppBitmap(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }
}
