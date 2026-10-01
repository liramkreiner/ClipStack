using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipStack.UI;

internal static class Imaging
{
    public static ImageSource ToImageSource(System.Drawing.Bitmap bitmap)
    {
        using (bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return FromPng(ms.ToArray());
        }
    }

    public static BitmapImage FromPng(byte[] png, int decodeHeight = 0)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeHeight > 0) img.DecodePixelHeight = decodeHeight;
        img.StreamSource = new MemoryStream(png);
        img.EndInit();
        img.Freeze();
        return img;
    }
}
