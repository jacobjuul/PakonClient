using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using PakonLib;
using PakonLib.Enums;
using PakonLib.Models;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private void Scan(OptionSet options)
        {
            EnsureScanner(options.GetBool("percent-progress"));
            ResetProgress(WorkerThreadOperation.ScanError);

            var resolution = ParseResolution(options.Get("resolution", "base16"));
            var filmColor = ParseFilmColor(options.Get("film-color", "negative"));
            var filmFormat = ParseFilmFormat(options.Get("film-format", "35mm"));
            var stripMode = ParseStripMode(options.Get("strip-mode", "full-roll"));
            var scanControl = ParseScanControl(options.Get("scan-control", "none"));

            Console.WriteLine("Scanning: {0}, {1}, {2}, {3}, {4}", resolution, filmColor, filmFormat, stripMode, scanControl);
            scanner.IScan.ScanPictures(resolution, filmColor, filmFormat, stripMode, scanControl);
            lastScanFilmColor = filmColor;
            WaitForCompletion("scan");
            Console.WriteLine("Scan complete.");

            if (options.GetBool("move-to-save-group", "move"))
            {
                scanner.ISave.MoveOldestRollToSaveGroup();
                Console.WriteLine("Moved oldest roll to save group.");
            }
        }

        private void ScanAndSave(OptionSet options)
        {
            Scan(options.With("move-to-save-group", "true"));
            Save(options, "save");
        }

        private void FocusCorrection(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.CorrectionsError);
            var resolution = ParseResolution(options.Get("resolution", "base16"));
            var filmColor = ParseFilmColor(options.Get("film-color", "negative"));
            var filmFormat = ParseFilmFormat(options.Get("film-format", "35mm"));
            var advance = options.GetBool("advance-film");
            Console.WriteLine("Running focus correction...");
            scanner.IScan.FocusCorrection(resolution, filmColor, filmFormat, advance);
            WaitForCompletion("focus correction");
            Console.WriteLine("Focus correction complete.");
        }

        private void LightCorrection(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.CorrectionsError);
            var resolution = ParseResolution(options.Get("resolution", "base16"));
            var filmColor = ParseFilmColor(options.Get("film-color", "negative"));
            var filmFormat = ParseFilmFormat(options.Get("film-format", "35mm"));
            var includeIr = options.GetBool("include-ir", "ir");
            Console.WriteLine("Running light correction...");
            scanner.IScan.LightCorrection(resolution, filmColor, filmFormat, includeIr);
            WaitForCompletion("light correction");
            Console.WriteLine("Light correction complete.");
        }

        private void FilmTrackTest(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.FilmTrackTestError);
            Console.WriteLine("Running film track test...");
            scanner.IScan.FilmTrackTest(options.GetBool("adjust-pots"));
            WaitForCompletion("film track test");
            Console.WriteLine("Film track result: {0}", scanner.IScan.FilmTrackTestResults());
        }

        private void AdvanceFilm(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.AdvanceFilmError);
            var milliseconds = options.GetInt("milliseconds", 500);
            var speed = options.GetInt("speed", 5);
            Console.WriteLine("Advancing film for {0} ms at speed {1}...", milliseconds, speed);
            scanner.IScan.AdvanceFilm(milliseconds, speed);
            WaitForCompletion("advance film");
            Console.WriteLine("Advance complete.");
        }

        private void PrintScanGroupCount(OptionSet options)
        {
            EnsureScanner(false);
            var roll = options.GetInt("roll", 0);
            var count = scanner.ISave.GetPictureCountScanGroup(roll);
            Console.WriteLine("Scan group roll {0}", roll);
            Console.WriteLine("  Strips:   {0}", count.StripCount);
            Console.WriteLine("  Pictures: {0}", count.PictureCount);
            Console.WriteLine("  Warnings: {0}", count.ScanWarnings);
        }

        private void PrintSaveGroupCount()
        {
            EnsureScanner(false);
            var count = scanner.ISave.GetPictureCountSaveGroup();
            Console.WriteLine("Save group");
            Console.WriteLine("  Rolls:     {0}", count.RollCount);
            Console.WriteLine("  Strips:    {0}", count.StripCount);
            Console.WriteLine("  Pictures:  {0}", count.PictureCount);
            Console.WriteLine("  Selected:  {0}", count.PictureSelectedCount);
            Console.WriteLine("  Hidden:    {0}", count.PictureHiddenCount);
        }

        private void Save(OptionSet options, string command)
        {
            if (ShouldUseRawSave(options, command))
            {
                SaveToClientMemory(options);
            }
            else
            {
                SaveToDisk(options);
            }
        }

        private bool ShouldUseRawSave(OptionSet options, string command)
        {
            if (IsCommand(command, "save-memory"))
            {
                return true;
            }

            if (IsCommand(command, "save-disk"))
            {
                return false;
            }

            if (options.GetBool("raw", "use-raw", "memory"))
            {
                return true;
            }

            var outputFormat = options.Get("output-format", options.Get("raw-output-format", null));
            if (!string.IsNullOrEmpty(outputFormat))
            {
                return true;
            }

            var requestedFormat = Normalize(options.Get("format", "jpeg"));
            return requestedFormat == "raw" || requestedFormat == "png" || requestedFormat == "png16";
        }

        private void SaveToDisk(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.SaveError);

            ApplyOutputDirectory(options);

            var index = ParsePictureIndex(options.Get("index", "all"));
            var saveControl = ParseSaveControl(options.Get("save-control", "size-original"));
            var width = options.GetInt("width", 0);
            var height = options.GetInt("height", 0);
            var scaling = ParseScalingMethod(options.Get("scaling", "bicubic"));
            var format = ParseFileFormat(options.Get("format", "jpeg"));
            var compression = options.GetInt("compression", options.GetInt("quality", 90));
            var dpi = options.GetInt("dpi", 300);
            var colorBits = options.GetInt("color-bits", 24);
            var invertSavedImages = ShouldInvertSavedImages(options);

            Console.WriteLine("Saving to disk: {0}, {1}, {2}, {3} dpi, {4} bit", index, format, saveControl, dpi, colorBits);
            scanner.ISave.SaveToDisk(index, saveControl, width, height, scaling, format, compression, dpi, colorBits);
            WaitForCompletion("save");
            if (invertSavedImages)
            {
                InvertSavedImages(options, format);
            }

            Console.WriteLine("Save complete.");
        }

        private void ApplyOutputDirectory(OptionSet options)
        {
            var directory = ResolveUserPath(options.Get("directory", null));
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var count = scanner.ISave.GetPictureCountSaveGroup();
            var prefix = options.Get("prefix", "pakon");

            for (var index = 0; index < count.PictureCount; index++)
            {
                var info = scanner.ISave3.GetPictureInfo3(index);
                var frameNumber = info.FrameNumber <= 0 ? index + 1 : info.FrameNumber;
                var fileName = prefix + "-" + (index + 1).ToString("0000", CultureInfo.InvariantCulture);
                scanner.ISave.PutPictureInfo(index, frameNumber, fileName, directory, info.Rotation, info.SelectedHidden);
            }
        }

        private bool ShouldInvertSavedImages(OptionSet options)
        {
            var filmColorValue = options.Get("film-color", null);
            var filmColor = string.IsNullOrEmpty(filmColorValue) ? lastScanFilmColor : ParseFilmColor(filmColorValue);
            return filmColor.HasValue && IsBlackAndWhite(filmColor.Value);
        }

        private static bool IsBlackAndWhite(FilmColor filmColor)
        {
            var nativeName = filmColor.ToString();
            return nativeName.IndexOf("BnW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   nativeName.IndexOf("BlackAndWhite", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void InvertSavedImages(OptionSet options, FileFormat format)
        {
            var overrideDirectory = ResolveUserPath(options.Get("directory", null));
            var savedIndex = ParsePictureIndex(options.Get("index", "all"));
            var extension = ExtensionFor(format);
            var count = scanner.ISave.GetPictureCountSaveGroup();
            for (var index = 0; index < count.PictureCount; index++)
            {
                var info = scanner.ISave3.GetPictureInfo3(index);
                if (savedIndex == PictureIndex.AllSelected && info.SelectedHidden != PictureSelection.Selected)
                {
                    continue;
                }

                var directory = string.IsNullOrEmpty(overrideDirectory) ? ResolveUserPath(info.Directory) : overrideDirectory;
                if (string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                var fileName = Path.HasExtension(info.FileName) ? info.FileName : info.FileName + extension;
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    InvertImage(path, format);
                }
            }
        }

        private static void InvertImage(string path, FileFormat format)
        {
            using (var source = new Bitmap(path))
            using (var inverted = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            {
                inverted.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                using (var graphics = Graphics.FromImage(inverted))
                using (var attributes = new ImageAttributes())
                {
                    var matrix = new ColorMatrix(new[]
                    {
                        new[] { -1f, 0f, 0f, 0f, 0f },
                        new[] { 0f, -1f, 0f, 0f, 0f },
                        new[] { 0f, 0f, -1f, 0f, 0f },
                        new[] { 0f, 0f, 0f, 1f, 0f },
                        new[] { 1f, 1f, 1f, 0f, 1f }
                    });
                    attributes.SetColorMatrix(matrix);
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, source.Width, source.Height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }

                var tempPath = path + ".tmp";
                inverted.Save(tempPath, ImageFormatFor(format));
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static ImageFormat ImageFormatFor(FileFormat format)
        {
            if (format == FileFormat.Bitmap)
            {
                return ImageFormat.Bmp;
            }

            if (format == FileFormat.Tiff)
            {
                return ImageFormat.Tiff;
            }

            return ImageFormat.Jpeg;
        }

        private void SaveToClientMemory(OptionSet options)
        {
            EnsureScanner(false);
            ResetProgress(WorkerThreadOperation.SaveError);

            var directory = ResolveUserPath(options.Get("directory", "."));
            Directory.CreateDirectory(directory);
            var prefix = options.Get("prefix", "pakon");
            var index = ParsePictureIndex(options.Get("index", "all"));
            var saveControl = ParseSaveControl(options.Get("save-control", "size-original"));
            var width = options.GetInt("width", 0);
            var height = options.GetInt("height", 0);
            var scaling = ParseScalingMethod(options.Get("scaling", "bicubic"));
            var format = ParseMemoryFileFormat(options.Get("memory-format", "planar16"));
            var fourChannel = options.GetBool("four-channel");
            var bufferBytes = checked(options.GetInt("buffer-mb", 64) * 1024 * 1024);
            var outputFormat = ParseRawOutputFormat(options.Get("output-format", options.Get("raw-output-format", options.Get("format", "png"))));
            var gamma = options.GetDouble("gamma", 0.4545454545454545);
            var contrast = options.GetFloat("contrast", 1.08f);
            var saturation = options.GetFloat("saturation", 1.08f);
            var quality = options.GetInt("quality", 90);
            var isBwImage = ShouldInvertSavedImages(options);
            var rawConverter = new RawConverterProcess(RawConverterProcess.ResolvePath(options.Get("raw-converter", null), launchDirectory));
            var chunk = 0;

            if (outputFormat != RawOutputFormat.Raw && format != MemoryFileFormat.Planar16)
            {
                throw new CommandException("Raw conversion currently requires --memory-format planar16. Use --format raw to keep unsupported memory buffers.");
            }

            if (fourChannel && outputFormat != RawOutputFormat.Raw)
            {
                throw new CommandException("Raw conversion currently supports three-channel planar16 buffers. Remove --four-channel or use --format raw.");
            }

            var bounds = ResolveClientMemorySaveBounds(index, saveControl, width, height, format, fourChannel);
            width = bounds.Width;
            height = bounds.Height;
            bufferBytes = Math.Max(bufferBytes, bounds.BufferByteCount);

            scanner.Unsafe.MemoryFormat = format;
            scanner.Unsafe.ImageFromClientBufferReceived += delegate(byte[] data, uint bytesToCopy)
            {
                chunk++;
                var path = Path.Combine(directory, prefix + "-" + chunk.ToString("0000", CultureInfo.InvariantCulture) + RawConverterProcess.ExtensionFor(outputFormat));
                var timing = rawConverter.Convert(data, path, outputFormat, isBwImage, gamma, contrast, saturation, quality);
                Console.WriteLine("Wrote {0} ({1}) in {2}. {3}", path, FormatMegabytes(bytesToCopy), FormatDuration(timing.TotalElapsed), FormatRawConversionTiming(timing));
            };

            Console.WriteLine("Saving to client memory: {0}, {1}, {2}, {3} byte buffers", index, format, outputFormat, bufferBytes);
            scanner.Unsafe.Allocate(bufferBytes);
            scanner.Unsafe.NextBuffer(scanner);
            scanner.Unsafe.NextBuffer(scanner);
            scanner.ISave.SaveToClientMemory(GetScannerInfo().ScannerType, index, saveControl, width, height, scaling, format, fourChannel);
            WaitForCompletion("memory save");
            Console.WriteLine("Memory save complete.");
        }

        private BoundingRectangleMetrics ResolveClientMemorySaveBounds(PictureIndex index, SaveControl saveControl, int width, int height, MemoryFileFormat format, bool fourChannel)
        {
            if (width > 0 && height > 0)
            {
                return new BoundingRectangleMetrics(width, height, Global.BufferSize(width, height, format, fourChannel));
            }

            var count = scanner.ISave.GetPictureCountSaveGroup();
            var startIndex = 0;
            var endIndex = count.PictureCount;

            if (index == PictureIndex.Current || index == PictureIndex.First)
            {
                endIndex = Math.Min(1, count.PictureCount);
            }
            else if (index != PictureIndex.All && index != PictureIndex.AllSelected)
            {
                var rawIndex = (int)index.NativeValue;
                if (rawIndex < 0 || rawIndex >= count.PictureCount)
                {
                    throw new CommandException("Picture index " + rawIndex + " is out of range.");
                }

                startIndex = rawIndex;
                endIndex = rawIndex + 1;
            }

            var boundingWidth = 0;
            var boundingHeight = 0;
            var bufferByteCount = 0;

            for (var currentIndex = startIndex; currentIndex < endIndex; currentIndex++)
            {
                if (index == PictureIndex.AllSelected)
                {
                    var info = scanner.ISave3.GetPictureInfo3(currentIndex);
                    if (info.SelectedHidden != PictureSelection.Selected)
                    {
                        continue;
                    }
                }

                var framing = saveControl.HasFlag(SaveControl.UseLoResBuffer)
                    ? scanner.ISave.GetPictureFramingUserInfoLowRes(currentIndex)
                    : scanner.ISave.GetPictureFramingUserInfo(currentIndex);
                var frameWidth = framing.Right + 1 - framing.Left;
                var frameHeight = framing.Bottom + 1 - framing.Top;
                if (frameWidth <= 0 || frameHeight <= 0)
                {
                    throw new CommandException("Picture #" + currentIndex + " has invalid framing dimensions " + frameWidth + "x" + frameHeight + ".");
                }

                if (width > 0)
                {
                    frameWidth = width;
                }

                if (height > 0)
                {
                    frameHeight = height;
                }

                boundingWidth = Math.Max(boundingWidth, frameWidth);
                boundingHeight = Math.Max(boundingHeight, frameHeight);
                bufferByteCount = Math.Max(bufferByteCount, Global.BufferSize(frameWidth, frameHeight, format, fourChannel));
            }

            if (boundingWidth <= 0 || boundingHeight <= 0 || bufferByteCount <= 0)
            {
                throw new CommandException("No pictures to save.");
            }

            return new BoundingRectangleMetrics(boundingWidth, boundingHeight, bufferByteCount);
        }

        private void PrintPictureInfo(OptionSet options)
        {
            EnsureScanner(false);
            PrintPictureInfo(scanner.ISave3.GetPictureInfo3(options.GetInt("index", 0)));
        }

        private void ListPictures(OptionSet options)
        {
            EnsureScanner(false);
            var count = scanner.ISave.GetPictureCountSaveGroup();
            var limit = options.GetInt("limit", count.PictureCount);
            for (var index = 0; index < Math.Min(limit, count.PictureCount); index++)
            {
                var info = scanner.ISave3.GetPictureInfo3(index);
                Console.WriteLine("#{0}: frame={1} name={2} file={3} dir={4} rotation={5} state={6}",
                    index,
                    info.FrameNumber,
                    info.FrameName,
                    info.FileName,
                    info.Directory,
                    info.Rotation,
                    info.SelectedHidden);
            }
        }

        private void PutPictureInfo(OptionSet options)
        {
            EnsureScanner(false);
            var index = options.GetInt("index", 0);
            var current = scanner.ISave3.GetPictureInfo3(index);
            var frame = options.GetInt("frame", current.FrameNumber);
            var file = options.Get("file", current.FileName);
            var directory = options.Get("directory", current.Directory);
            var rotation = options.GetInt("rotation", current.Rotation);
            var state = ParsePictureSelection(options.Get("selection", current.SelectedHidden.ToString()));
            scanner.ISave.PutPictureInfo(index, frame, file, directory, rotation, state);
            Console.WriteLine("Updated picture #{0}.", index);
        }

        private void PutPictureSelection(OptionSet options)
        {
            EnsureScanner(false);
            var index = ParsePictureIndex(options.Get("index", "current"));
            var state = ParsePictureSelection(options.Get("selection", "selected"));
            var skipHidden = options.GetBool("skip-hidden");
            scanner.ISave.PutPictureSelection(index, state, skipHidden);
            Console.WriteLine("Selection updated: {0} -> {1}.", index, state);
        }

        private void PrintFraming(OptionSet options)
        {
            EnsureScanner(false);
            var index = options.GetInt("index", 0);
            var framing = options.GetBool("low-res")
                ? scanner.ISave.GetPictureFramingUserInfoLowRes(index)
                : scanner.ISave.GetPictureFramingUserInfo(index);
            Console.WriteLine("Picture #{0} framing: left={1} top={2} right={3} bottom={4}",
                index,
                framing.Left,
                framing.Top,
                framing.Right,
                framing.Bottom);
        }

        private static string FormatMegabytes(uint bytes)
        {
            return ((double)bytes / (1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
        }

        private static string FormatDuration(TimeSpan elapsed)
        {
            return elapsed.TotalSeconds >= 1
                ? elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s"
                : elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        private static string FormatRawConversionTiming(RawConversionTiming timing)
        {
            if (timing.RawWriteElapsed.HasValue)
            {
                return "Timing: file-write=" + FormatDuration(timing.RawWriteElapsed.Value) + ".";
            }

            var details = "Timing: process-start=" + FormatDuration(timing.ProcessStartElapsed.Value) +
                          ", stdin=" + FormatDuration(timing.InputWriteElapsed.Value) +
                          ", process-wait=" + FormatDuration(timing.ProcessWaitElapsed.Value);

            if (!string.IsNullOrWhiteSpace(timing.ConverterTiming))
            {
                details += ", converter=[" + timing.ConverterTiming + "]";
            }

            return details + ".";
        }

        private void PrintScannerInfo(ScannerInfo info)
        {
            Console.WriteLine("Scanner");
            Console.WriteLine("  Type:          {0}", info.ScannerType);
            Console.WriteLine("  Serial:        {0}", info.ScannerSerialNumber);
            Console.WriteLine("  Hardware 135:  {0}", info.Hardware135);
            Console.WriteLine("  Hardware 235:  {0}", info.Hardware235);
            Console.WriteLine("  Hardware 335:  {0}", info.Hardware335);
        }

        private void PrintInitializeWarnings()
        {
            Console.WriteLine("Initialize warnings: {0}", scanner.GetInitializeWarnings());
        }

        private static void PrintPictureInfo(PictureInfo info)
        {
            Console.WriteLine("Picture");
            Console.WriteLine("  Roll index:      {0}", info.RollIndexFromStrip);
            Console.WriteLine("  Strip index:     {0}", info.StripIndexFromStrip);
            Console.WriteLine("  Film product:    {0}", info.FilmProductFromStrip);
            Console.WriteLine("  Film specifier:  {0}", info.FilmSpecifierFromStrip);
            Console.WriteLine("  Frame name:      {0}", info.FrameName);
            Console.WriteLine("  Frame number:    {0}", info.FrameNumber);
            Console.WriteLine("  Aspect ratio:    {0}", info.PrintAspectRatio);
            Console.WriteLine("  File name:       {0}", info.FileName);
            Console.WriteLine("  Directory:       {0}", info.Directory);
            Console.WriteLine("  Rotation:        {0}", info.Rotation);
            Console.WriteLine("  Selection:       {0}", info.SelectedHidden);
        }
        private void PrintDiagnostics(OptionSet options)
        {
            var directory = ResolveComServerDirectory(options.Get("com-server-dir", null));
            Console.WriteLine("Pakon diagnostics");
            Console.WriteLine("  Launch directory:      {0}", launchDirectory);
            Console.WriteLine("  Current directory:     {0}", Environment.CurrentDirectory);
            Console.WriteLine("  Registered tlx.dll:    {0}", ReadRegisteredTlxPath() ?? "(not found)");
            Console.WriteLine("  COM server directory:  {0}", directory);
            Console.WriteLine("  Directory exists:      {0}", Directory.Exists(directory));
            Console.WriteLine("  tlx.dll exists:        {0}", File.Exists(Path.Combine(directory, "tlx.dll")));
            Console.WriteLine("  TLA.dll exists:        {0}", File.Exists(Path.Combine(directory, "TLA.dll")));
            Console.WriteLine("  PakonImau.dll exists:  {0}", File.Exists(Path.Combine(directory, "PakonImau.dll")));
            Console.WriteLine("  anselinstalldir exists:{0}", Directory.Exists(Path.Combine(directory, "anselinstalldir")));
            Console.WriteLine("  Config exists:         {0}", Directory.Exists(Path.Combine(directory, "Config")));
            Console.WriteLine("  Logs exists:           {0}", Directory.Exists(Path.Combine(directory, "Logs")));
        }

    }
}
