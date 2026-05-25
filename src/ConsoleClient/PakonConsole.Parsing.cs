using System;
using System.Collections.Generic;
using System.Globalization;
using PakonLib;
using PakonLib.Enums;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private static Resolution ParseResolution(string value)
        {
            switch (Normalize(value))
            {
                case "base4":
                case "4":
                    return Resolution.Base4;
                case "base8":
                case "8":
                    return Resolution.Base8;
                case "base16":
                case "16":
                    return Resolution.Base16;
                default:
                    return Resolution.FromRawValue(ParseRaw(value, "resolution"));
            }
        }

        private static FilmColor ParseFilmColor(string value)
        {
            switch (Normalize(value))
            {
                case "negative":
                case "neg":
                    return FilmColor.Negative;
                case "positive":
                case "pos":
                case "slide":
                    return FilmColor.Positive;
                case "bw":
                case "blackandwhite":
                    return FilmColor.BlackAndWhite;
                case "bwc41":
                case "blackandwhitec41":
                    return FilmColor.BlackAndWhiteC41;
                default:
                    return FilmColor.FromRawValue(ParseRaw(value, "film color"));
            }
        }

        private static FilmFormat ParseFilmFormat(string value)
        {
            switch (Normalize(value))
            {
                case "24mm":
                case "24":
                    return FilmFormat.Format24mm;
                case "35mm":
                case "35":
                    return FilmFormat.Format35mm;
                case "24mmcartridge":
                case "24cart":
                    return FilmFormat.Format24mmCartridge;
                case "24mmcartridgemofreader":
                case "24cartmofreader":
                    return FilmFormat.Format24mmCartridgeMofReader;
                case "24mmcartridgemoffile":
                case "24cartmoffile":
                    return FilmFormat.Format24mmCartridgeMofFile;
                case "24mmcartridgemoffileorreader":
                case "24cartmoffileorreader":
                    return FilmFormat.Format24mmCartridgeMofFileOrReader;
                case "24mmfile":
                case "24file":
                    return FilmFormat.Format24mmFile;
                default:
                    return FilmFormat.FromRawValue(ParseRaw(value, "film format"));
            }
        }

        private static StripMode ParseStripMode(string value)
        {
            switch (Normalize(value))
            {
                case "fullroll":
                case "full":
                    return StripMode.FullRoll;
                default:
                    return StripMode.FromRawValue(ParseRaw(value, "strip mode"));
            }
        }

        private static ScanControl ParseScanControl(string value)
        {
            var result = ScanControl.None;
            foreach (var part in SplitFlags(value))
            {
                switch (Normalize(part))
                {
                    case "":
                    case "none":
                        break;
                    case "aggressiveframing":
                        result |= ScanControl.AggressiveFraming;
                        break;
                    case "scratch":
                    case "usescratchremoval":
                        result |= ScanControl.UseScratchRemoval;
                        break;
                    case "filmdrag":
                    case "hasfilmdrag":
                        result |= ScanControl.HasFilmDrag;
                        break;
                    case "dx":
                    case "readdx":
                        result |= ScanControl.ReadDx;
                        break;
                    case "splice":
                    case "rftsensesplice":
                        result |= ScanControl.RftSenseSplice;
                        break;
                    case "24mof":
                    case "use24mmexternalfilemof":
                        result |= ScanControl.Use24mmExternalFileMof;
                        break;
                    case "24autoloader":
                    case "use24mmautoloader":
                        result |= ScanControl.Use24mmAutoLoader;
                        break;
                    case "24autoloadermof":
                    case "use24mmautoloadermof":
                        result |= ScanControl.Use24mmAutoLoaderMof;
                        break;
                    case "lampwarmup":
                        result |= ScanControl.LampWarmUp;
                        break;
                    case "prescan":
                        result |= ScanControl.PreScan;
                        break;
                    default:
                        result |= ScanControl.FromRawValue(ParseRaw(part, "scan control"));
                        break;
                }
            }

            return result;
        }

        private static SaveControl ParseSaveControl(string value)
        {
            var result = SaveControl.None;
            foreach (var part in SplitFlags(value))
            {
                switch (Normalize(part))
                {
                    case "":
                    case "none":
                        break;
                    case "original":
                    case "sizeoriginal":
                        result |= SaveControl.SizeOriginal;
                        break;
                    case "displaylimit":
                    case "sizelimitfordisplay":
                        result |= SaveControl.SizeLimitForDisplay;
                        break;
                    case "savelimit":
                    case "sizelimitforsave":
                        result |= SaveControl.SizeLimitForSave;
                        break;
                    case "currentrotation":
                    case "usecurrentrotation":
                        result |= SaveControl.UseCurrentRotation;
                        break;
                    case "lores":
                    case "useloresbuffer":
                        result |= SaveControl.UseLoResBuffer;
                        break;
                    case "scratch":
                    case "usescratchremovalifavailable":
                        result |= SaveControl.UseScratchRemovalIfAvailable;
                        break;
                    case "colorcorrection":
                    case "usecolorcorrection":
                        result |= SaveControl.UseColorCorrection;
                        break;
                    case "scenebalance":
                    case "usecolorscenebalance":
                        result |= SaveControl.UseColorSceneBalance;
                        break;
                    case "coloradjustments":
                    case "usecoloradjustments":
                        result |= SaveControl.UseColorAdjustments;
                        break;
                    case "fileheader":
                        result |= SaveControl.FileHeader;
                        break;
                    case "fastdib":
                    case "fastupdate8bitdib":
                        result |= SaveControl.FastUpdate8BitDib;
                        break;
                    case "topdowndib":
                        result |= SaveControl.TopDownDib;
                        break;
                    case "donotscaleup":
                        result |= SaveControl.DoNotScaleUp;
                        break;
                    case "kcdfs":
                    case "usecolorkcdfs":
                        result |= SaveControl.UseColorKcdfs;
                        break;
                    default:
                        result |= SaveControl.FromRawValue(ParseRaw(part, "save control"));
                        break;
                }
            }

            return result;
        }

        private static PictureIndex ParsePictureIndex(string value)
        {
            switch (Normalize(value))
            {
                case "all":
                    return PictureIndex.All;
                case "selected":
                case "allselected":
                    return PictureIndex.AllSelected;
                case "current":
                    return PictureIndex.Current;
                case "first":
                    return PictureIndex.First;
                case "end":
                case "insertpictureatend":
                    return PictureIndex.InsertPictureAtEnd;
                default:
                    return PictureIndex.FromRawValue(ParseRaw(value, "picture index"));
            }
        }

        private static PictureSelection ParsePictureSelection(string value)
        {
            switch (Normalize(value))
            {
                case "none":
                    return PictureSelection.None;
                case "selected":
                case "select":
                    return PictureSelection.Selected;
                case "hidden":
                case "hide":
                    return PictureSelection.Hidden;
                default:
                    return PictureSelection.FromRawValue(ParseRaw(value, "picture selection"));
            }
        }

        private static ScalingMethod ParseScalingMethod(string value)
        {
            switch (Normalize(value))
            {
                case "bicubic":
                    return ScalingMethod.Bicubic;
                default:
                    return ScalingMethod.FromRawValue(ParseRaw(value, "scaling method"));
            }
        }

        private static FileFormat ParseFileFormat(string value)
        {
            switch (Normalize(value))
            {
                case "jpg":
                case "jpeg":
                    return FileFormat.Jpeg;
                case "bmp":
                case "bitmap":
                    return FileFormat.Bitmap;
                case "tif":
                case "tiff":
                    return FileFormat.Tiff;
                case "exif":
                    return FileFormat.Exif;
                default:
                    return FileFormat.FromRawValue(ParseRaw(value, "file format"));
            }
        }

        private static MemoryFileFormat ParseMemoryFileFormat(string value)
        {
            switch (Normalize(value))
            {
                case "dib8":
                case "dib":
                    return MemoryFileFormat.Dib8;
                case "planar16":
                case "raw16":
                    return MemoryFileFormat.Planar16;
                case "planar8":
                case "raw8":
                    return MemoryFileFormat.Planar8;
                default:
                    return MemoryFileFormat.FromRawValue(ParseRaw(value, "memory format"));
            }
        }

        private static RawOutputFormat ParseRawOutputFormat(string value)
        {
            switch (Normalize(value))
            {
                case "raw":
                    return RawOutputFormat.Raw;
                case "png":
                case "png8":
                case "png16":
                    return RawOutputFormat.Png;
                case "jpg":
                case "jpeg":
                    return RawOutputFormat.Jpeg;
                case "tif":
                case "tiff":
                    return RawOutputFormat.Tiff;
                case "bmp":
                case "bitmap":
                    return RawOutputFormat.Bmp;
                default:
                    throw new CommandException("Unknown raw output format '" + value + "'. Use png, jpeg, tiff, bmp, or raw.");
            }
        }

        private static WorkerThreadOperation ParseWorkerOperation(string value)
        {
            if (string.Equals(value, "tlx", StringComparison.OrdinalIgnoreCase))
            {
                return WorkerThreadOperation.TlxError;
            }

            WorkerThreadOperation parsed;
            if (Enum.TryParse(value, true, out parsed))
            {
                return parsed;
            }

            return (WorkerThreadOperation)ParseRaw(value, "worker operation");
        }

        private static int ParseRaw(string value, string name)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            throw new CommandException("Unknown " + name + " value '" + value + "'. Run 'values' to see friendly names, or pass a raw TLX integer.");
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

        private static IEnumerable<string> SplitFlags(string value)
        {
            return (value ?? string.Empty).Split(new[] { ',', '+', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ExtensionFor(FileFormat format)
        {
            if (format == FileFormat.Bitmap)
            {
                return ".bmp";
            }

            if (format == FileFormat.Tiff)
            {
                return ".tif";
            }

            if (format == FileFormat.Exif)
            {
                return ".exif";
            }

            return ".jpg";
        }

        private static IEnumerable<string> SplitCommandLine(string commandLine)
        {
            var args = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Length = 0;
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                args.Add(current.ToString());
            }

            return args;
        }
    }
}
