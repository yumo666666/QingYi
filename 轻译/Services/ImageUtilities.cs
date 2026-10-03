using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using GraphicsUnit = System.Drawing.GraphicsUnit;

namespace QingYi.Services;

public sealed class ScreenFrame : IDisposable
{
    public System.Drawing.Rectangle Bounds { get; }
    public Bitmap Bitmap { get; }
    public BitmapSource Source { get; }
    public double ScaleX { get; }
    public double ScaleY { get; }

    public ScreenFrame(System.Drawing.Rectangle bounds, Bitmap bitmap, uint dpiX, uint dpiY)
    {
        Bounds = bounds;
        Bitmap = bitmap;
        ScaleX = dpiX / 96d;
        ScaleY = dpiY / 96d;
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        Source = image;
    }

    public void Dispose() => Bitmap.Dispose();
}

public static class ImageUtilities
{
    public static IReadOnlyList<ScreenFrame> CaptureScreens()
    {
        var frames = new List<ScreenFrame>();
        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

                var point = new NativeMethods.POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
                var monitor = NativeMethods.MonitorFromPoint(point, 2);
                uint dpiX = 96, dpiY = 96;
                _ = NativeMethods.GetDpiForMonitor(monitor, 0, out dpiX, out dpiY);
                frames.Add(new ScreenFrame(bounds, bitmap, dpiX == 0 ? 96 : dpiX, dpiY == 0 ? 96 : dpiY));
            }
            return frames;
        }
        catch
        {
            foreach (var frame in frames) frame.Dispose();
            throw;
        }
    }

    public static byte[] CropPng(IReadOnlyList<ScreenFrame> frames, System.Drawing.Rectangle selection)
    {
        if (selection.Width < 2 || selection.Height < 2) throw new InvalidOperationException("请框选一块更大的区域。");
        if ((long)selection.Width * selection.Height > 16_000_000) throw new InvalidOperationException("所选区域过大，请缩小后再试。");
        using var output = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.White);
            foreach (var frame in frames)
            {
                var overlap = System.Drawing.Rectangle.Intersect(selection, frame.Bounds);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;
                var source = new System.Drawing.Rectangle(overlap.Left - frame.Bounds.Left, overlap.Top - frame.Bounds.Top, overlap.Width, overlap.Height);
                var destination = new System.Drawing.Rectangle(overlap.Left - selection.Left, overlap.Top - selection.Top, overlap.Width, overlap.Height);
                graphics.DrawImage(frame.Bitmap, destination, source, GraphicsUnit.Pixel);
            }
        }
        using var memory = new MemoryStream();
        output.Save(memory, ImageFormat.Png);
        if (memory.Length > 18_000_000) throw new InvalidOperationException("图片文件较大，请缩小框选范围后再试。");
        return memory.ToArray();
    }

    public static byte[] CreateTestImage()
    {
        using var bitmap = new Bitmap(420, 150, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 30, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Color.FromArgb(36, 38, 43)))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("HELLO", font, brush, new PointF(28, 34));
        }
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    public static BitmapSource ToPreview(byte[] png)
    {
        using var memory = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
