using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

internal sealed class PakonRawProcessor
{
    public string LastTiming { get; private set; } = "";

    public Image<Rgb48> ProcessImage(string filename, bool isBwImage, double gamma, float contrast, float saturation)
    {
        var readStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
            var bitCount = BitConverter.ToUInt32(header, 12);

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Pakon raw file has invalid dimensions " + width + "x" + height + ".");
            }

            if (width > 5000 || height > 5000 || bitCount != 48)
            {
                throw new InvalidOperationException("You are probably not processing a Pakon raw file.");
            }

            buffer = new byte[width * height * 6];
            interleaved = new byte[width * height * 6];
            fileStream.ReadExactly(buffer, 0, width * height * 6);
        }
        readStopwatch.Stop();

        var interleaveStopwatch = System.Diagnostics.Stopwatch.StartNew();
        InterleaveBuffer(width, height, buffer, interleaved);
        interleaveStopwatch.Stop();

        var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var image = Image.LoadPixelData<Rgb48>(interleaved, width, height);
        loadStopwatch.Stop();

        var levelsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        SetWhiteAndBlackpoint(image, isBwImage);
        levelsStopwatch.Stop();

        var gammaStopwatch = System.Diagnostics.Stopwatch.StartNew();
        GammaCorrection(image, gamma);
        gammaStopwatch.Stop();

        var adjustStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
        adjustStopwatch.Stop();

        LastTiming =
            "read-raw=" + FormatDuration(readStopwatch.Elapsed) +
            ", interleave=" + FormatDuration(interleaveStopwatch.Elapsed) +
            ", load-pixels=" + FormatDuration(loadStopwatch.Elapsed) +
            ", levels=" + FormatDuration(levelsStopwatch.Elapsed) +
            ", gamma=" + FormatDuration(gammaStopwatch.Elapsed) +
            ", adjust=" + FormatDuration(adjustStopwatch.Elapsed);

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
        ushort darkestR = 0;
        ushort darkestG = 0;
        ushort darkestB = 0;
        ushort smallestR = 65_534;
        ushort smallestG = 65_534;
        ushort smallestB = 65_534;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var pixelRowSpan = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = pixelRowSpan[x];
                    if (pixel.R > darkestR)
                        darkestR = pixel.R;
                    if (pixel.G > darkestG)
                        darkestG = pixel.G;
                    if (pixel.B > darkestB)
                        darkestB = pixel.B;

                    if (pixel.R < smallestR)
                        smallestR = pixel.R;
                    if (pixel.G < smallestG)
                        smallestG = pixel.G;
                    if (pixel.B < smallestB)
                        smallestB = pixel.B;
                }
            }
        });

        if (bwNegative)
        {
            darkestR -= darkestR > 99 ? (ushort)100 : darkestR;
            darkestG -= darkestG > 99 ? (ushort)100 : darkestG;
            darkestB -= darkestB > 99 ? (ushort)100 : darkestB;

            smallestR = Math.Clamp(smallestR, (ushort)0, (ushort)65_454);
            smallestG = Math.Clamp(smallestG, (ushort)0, (ushort)65_454);
            smallestB = Math.Clamp(smallestB, (ushort)0, (ushort)65_454);

            smallestR += 80;
            smallestG += 80;
            smallestB += 80;
        }

        return (
            new Rgb48(darkestR, darkestG, darkestB),
            new Rgb48(smallestR, smallestG, smallestB));
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s"
            : elapsed.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
    }
}
