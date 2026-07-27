using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Windows.Media.Imaging;

namespace Pakon.Client;

internal static class ImageAdjustmentService
{
    public static async Task<BitmapImage> CreatePreviewAsync(FrameItem frame, bool blackAndWhite, string outputPath)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgb24>(frame.SourcePath);
            Apply(image, frame, blackAndWhite);
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(900, 620) }));
            image.Save(outputPath, new JpegEncoder { Quality = 88 });
        });
        return LoadUnlocked(outputPath);
    }

    public static async Task SaveJpegAsync(string sourcePath, string outputPath, FrameItem frame, bool blackAndWhite)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgb24>(sourcePath);
            Apply(image, frame, blackAndWhite);
            image.Save(outputPath, new JpegEncoder { Quality = 95 });
        });
    }

    public static BitmapImage LoadUnlocked(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void Apply(Image<Rgb24> image, FrameItem frame, bool blackAndWhite)
    {
        image.Mutate(x => x.Brightness((float)(1 + frame.Brightness / 100d)));
        image.Mutate(x => x.Contrast((float)(1 + frame.Contrast / 100d)));
        if (blackAndWhite)
        {
            image.Mutate(x => x.Invert());
            image.Mutate(x => x.Grayscale());
        }
        else
        {
            var red = 1 + frame.RedBalance / 100d;
            var green = 1 + frame.GreenBalance / 100d;
            var blue = 1 + frame.BlueBalance / 100d;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        ref var pixel = ref row[x];
                        pixel.R = (byte)Math.Clamp(pixel.R * red, 0, 255);
                        pixel.G = (byte)Math.Clamp(pixel.G * green, 0, 255);
                        pixel.B = (byte)Math.Clamp(pixel.B * blue, 0, 255);
                    }
                }
            });
        }
        if (frame.Rotation != 0) image.Mutate(x => x.Rotate(frame.Rotation));
    }
}
