using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

internal sealed class PakonRawProcessor
{
    public Image<Rgb48> ProcessImage(string filename, bool isBwImage, double gamma, float contrast, float saturation)
    {
        var header = new byte[16];
        byte[] buffer;
        byte[] interleaved;
        int width;
        int height;

        using (var fileStream = File.OpenRead(filename))
        {
            fileStream.ReadExactly(header, 0, 16);
            width = (int)BitConverter.ToUInt32(header, 4);
            height = (int)BitConverter.ToUInt32(header, 8);

            if (width > 5000 || height > 5000)
            {
                throw new InvalidOperationException("You are probably not processing a Pakon raw file.");
            }

            buffer = new byte[width * height * 6];
            interleaved = new byte[width * height * 6];
            fileStream.ReadExactly(buffer, 0, width * height * 6);
        }

        InterleaveBuffer(width, height, buffer, interleaved);
        var image = Image.LoadPixelData<Rgb48>(interleaved, width, height);
        SetWhiteAndBlackpoint(image, isBwImage);
        GammaCorrection(image, gamma);

        if (isBwImage)
        {
            image.Mutate(x => x.Invert());
            image.Mutate(x => x.Saturate(0f));
        }
        else
        {
            image.Mutate(x => x.Contrast(contrast));
            image.Mutate(x => x.Saturate(saturation));
        }

        return image;
    }

    private static void InterleaveBuffer(int width, int height, byte[] buffer, byte[] interleaved)
    {
        const int pixelSize = 6;

        for (var i = 0; i != width * height * 2; i += 2)
        {
            interleaved[i / 2 * pixelSize + 0] = buffer[i];
            interleaved[i / 2 * pixelSize + 1] = buffer[i + 1];
            interleaved[i / 2 * pixelSize + 2] = buffer[(2 * width * height) + i];
            interleaved[i / 2 * pixelSize + 3] = buffer[(2 * width * height) + i + 1];
            interleaved[i / 2 * pixelSize + 4] = buffer[(2 * 2 * width * height) + i];
            interleaved[i / 2 * pixelSize + 5] = buffer[(2 * 2 * width * height) + i + 1];
        }
    }

    private static void GammaCorrection(Image<Rgb48> image, double gamma)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (ref var pixel in row)
                {
                    var rangeR = (double)pixel.R / 65500;
                    var correctionR = Math.Pow(rangeR, gamma * 0.98);
                    pixel.R = (ushort)(correctionR * 65500);

                    var rangeG = (double)pixel.G / 65500;
                    var correctionG = Math.Pow(rangeG, gamma * 1.02);
                    pixel.G = (ushort)(correctionG * 65500);

                    var rangeB = (double)pixel.B / 65500;
                    var correctionB = Math.Pow(rangeB, gamma * 1.03);
                    pixel.B = (ushort)(correctionB * 65500);
                }
            }
        });
    }

    private static void SetWhiteAndBlackpoint(Image<Rgb48> image, bool bwNegative)
    {
        var (darkest, brightest) = FindDarkestAndBrightestValues(image, bwNegative);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var pixelRowSpan = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = pixelRowSpan[x];
                    var r = (double)(pixel.R - brightest.R) / (darkest.R - brightest.R);
                    var g = (double)(pixel.G - brightest.G) / (darkest.G - brightest.G);
                    var b = (double)(pixel.B - brightest.B) / (darkest.B - brightest.B);
                    r = Math.Clamp(r, 0, 1);
                    g = Math.Clamp(g, 0, 1);
                    b = Math.Clamp(b, 0, 1);

                    pixelRowSpan[x] = new Rgb48(
                        (ushort)(65_534 * r),
                        (ushort)(65_534 * g),
                        (ushort)(65_534 * b));
                }
            }
        });
    }

    private static (Rgb48, Rgb48) FindDarkestAndBrightestValues(Image<Rgb48> image, bool bwNegative)
    {
        var darkestValues = new ConcurrentDictionary<string, ushort>();
        darkestValues.TryAdd("R", 0);
        darkestValues.TryAdd("G", 0);
        darkestValues.TryAdd("B", 0);

        var smallestValues = new ConcurrentDictionary<string, ushort>();
        smallestValues.TryAdd("R", 65_534);
        smallestValues.TryAdd("G", 65_534);
        smallestValues.TryAdd("B", 65_534);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var pixelRowSpan = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    if (pixelRowSpan[x].R > darkestValues["R"])
                        darkestValues.TryUpdate("R", pixelRowSpan[x].R, darkestValues["R"]);
                    if (pixelRowSpan[x].G > darkestValues["G"])
                        darkestValues.TryUpdate("G", pixelRowSpan[x].G, darkestValues["G"]);
                    if (pixelRowSpan[x].B > darkestValues["B"])
                        darkestValues.TryUpdate("B", pixelRowSpan[x].B, darkestValues["B"]);

                    if (pixelRowSpan[x].R < smallestValues["R"])
                        smallestValues.TryUpdate("R", pixelRowSpan[x].R, smallestValues["R"]);
                    if (pixelRowSpan[x].G < smallestValues["G"])
                        smallestValues.TryUpdate("G", pixelRowSpan[x].G, smallestValues["G"]);
                    if (pixelRowSpan[x].B < smallestValues["B"])
                        smallestValues.TryUpdate("B", pixelRowSpan[x].B, smallestValues["B"]);
                }
            }
        });

        if (bwNegative)
        {
            darkestValues["R"] -= darkestValues["R"] > 99 ? (ushort)100 : darkestValues["R"];
            darkestValues["G"] -= darkestValues["G"] > 99 ? (ushort)100 : darkestValues["G"];
            darkestValues["B"] -= darkestValues["B"] > 99 ? (ushort)100 : darkestValues["B"];

            smallestValues["R"] = Math.Clamp(smallestValues["R"], (ushort)0, (ushort)65_454);
            smallestValues["G"] = Math.Clamp(smallestValues["G"], (ushort)0, (ushort)65_454);
            smallestValues["B"] = Math.Clamp(smallestValues["B"], (ushort)0, (ushort)65_454);

            smallestValues["R"] += 80;
            smallestValues["G"] += 80;
            smallestValues["B"] += 80;
        }

        return (
            new Rgb48(darkestValues["R"], darkestValues["G"], darkestValues["B"]),
            new Rgb48(smallestValues["R"], smallestValues["G"], smallestValues["B"]));
    }
}
