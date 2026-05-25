using System;
using System.Collections.Generic;
using PakonLib.Enums;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private static void PrintHelp(string command)
        {
            if (!string.IsNullOrEmpty(command))
            {
                PrintCommandHelp(command);
                return;
            }

            Console.WriteLine("Pakon command line scanner");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  ConsoleClient.exe <command> [options]");
            Console.WriteLine("  ConsoleClient.exe                 Starts interactive mode");
            Console.WriteLine("  help <command>                    Shows detailed help for one command");
            Console.WriteLine();
            PrintCommandList();
            Console.WriteLine();
            Console.WriteLine("Typical scan and save workflow:");
            Console.WriteLine("  ConsoleClient.exe");
            Console.WriteLine("  pakon> init");
            Console.WriteLine("  pakon> scan --scan-control scratch,dx");
            Console.WriteLine("  pakon> save --directory C:\\Scans --prefix roll01 --format jpeg");
            Console.WriteLine("  pakon> quit");
            Console.WriteLine();
            Console.WriteLine("One-shot startup command for the same basic workflow:");
            Console.WriteLine("  ConsoleClient.exe scan-save --resolution base16 --film-color negative --directory C:\\Scans --prefix roll01");
            Console.WriteLine();
            Console.WriteLine("Other examples:");
            Console.WriteLine("  ConsoleClient.exe info");
            Console.WriteLine("  ConsoleClient.exe save --directory C:\\Raw --format png --raw");
            Console.WriteLine();
            Console.WriteLine("Run 'help scan-save' for the one-shot workflow, 'help scan' or 'help save' for the separate steps, and 'values' for supported option values.");
        }

        private static void PrintCommandHelp(string command)
        {
            switch (Normalize(command))
            {
                case "init":
                    PrintHelpBlock(
                        "init",
                        "Initializes TLX and keeps the scanner session open in interactive mode.",
                        new[] { "init [--percent-progress] [--init-timeout-seconds 5]" },
                        new[]
                        {
                            "--percent-progress       Requests percent-style initialization progress callbacks.",
                            "--init-timeout-seconds  Seconds to wait for TLX initialization before failing. Default: 5."
                        },
                        new[] { "init" });
                    return;
                case "info":
                    PrintHelpBlock(
                        "info",
                        "Initializes TLX if needed and prints scanner model, serial, and hardware capability values.",
                        new[] { "info" },
                        new string[0],
                        new[] { "info" });
                    return;
                case "warnings":
                    PrintHelpBlock(
                        "warnings",
                        "Prints TLX initialization warnings for the current scanner session.",
                        new[] { "warnings" },
                        new string[0],
                        new[] { "warnings" });
                    return;
                case "errors":
                    PrintHelpBlock(
                        "errors",
                        "Reads and clears the last TLX error for one worker operation.",
                        new[] { "errors [--operation tlx]" },
                        new[] { "--operation  Worker operation name or integer. Defaults to tlx." },
                        new[] { "errors", "errors --operation TlxScan" });
                    return;
                case "close":
                    PrintHelpBlock(
                        "close",
                        "Closes the current scanner session. In interactive mode, quit also closes it.",
                        new[] { "close" },
                        new string[0],
                        new[] { "close" });
                    return;
                case "diagnostics":
                case "diagnose":
                    PrintHelpBlock(
                        "diagnostics",
                        "Prints paths and file checks used to diagnose TLX COM server setup problems.",
                        new[] { "diagnostics [--com-server-dir <path>]" },
                        new[] { "--com-server-dir  Override the Pakon F-X35 COM SERVER directory." },
                        new[] { "diagnostics", "diagnostics --com-server-dir \"C:\\Program Files (x86)\\Pakon\\F-X35 COM SERVER\"" });
                    return;
                case "scan":
                    PrintHelpBlock(
                        "scan",
                        "Scans a roll and moves it to the TLX save group by default. Use scan-save to scan and save files from program startup.",
                        new[] { "scan [options]" },
                        new[]
                        {
                            "--resolution         base4, base8, base16. Default: base16.",
                            "--film-color         negative, positive, bw, bw-c41. Default: negative.",
                            "--film-format        35mm or another value from 'values'. Default: 35mm.",
                            "--strip-mode         full-roll. Default: full-roll.",
                            "--scan-control       Comma-separated flags such as scratch,dx. Default: none.",
                            "--move-to-save-group Moves the newest scanned roll to the save group. Default: true.",
                            "--percent-progress   Requests percent-style initialization progress callbacks.",
                            "--init-timeout-seconds Seconds to wait for TLX initialization before failing. Default: 5."
                        },
                        new[]
                        {
                            "scan --scan-control scratch,dx",
                            "scan --resolution base16 --film-color positive",
                            "scan --move-to-save-group false"
                        });
                    return;
                case "scansave":
                case "workflow":
                    PrintHelpBlock(
                        "scan-save",
                        "One-shot startup workflow: initializes TLX, scans, moves the newest roll to the save group, saves it to disk, and exits.",
                        new[] { "ConsoleClient.exe scan-save [scan options] [save options]" },
                        new[]
                        {
                            "--resolution, --film-color, --film-format, --strip-mode, --scan-control",
                            "--directory          Output directory. If omitted, TLX uses the picture metadata already set in the save group.",
                            "--prefix             Output filename prefix when --directory is supplied. Default: pakon.",
                            "--format             jpeg, bmp, tiff, exif, png, raw. Default: jpeg.",
                            "--compression        JPEG/TIFF compression or quality value. Default: 90.",
                            "--dpi                Output DPI. Default: 300.",
                            "--color-bits         Output color bits. Default: 24.",
                            "--save-control       Comma-separated save flags. Default: size-original.",
                            "--width, --height    Optional scaled output size. Defaults: 0, 0.",
                            "--scaling            Scaling method. Default: bicubic."
                        },
                        new[]
                        {
                            "ConsoleClient.exe scan-save --resolution base16 --film-color negative --scan-control scratch,dx --directory C:\\Scans --prefix roll01",
                            "ConsoleClient.exe scan-save --resolution base16 --format png --directory C:\\Scans --prefix raw-roll01"
                        });
                    return;
                case "cancelscan":
                    PrintHelpBlock(
                        "cancel-scan",
                        "Requests cancellation of the current scan operation.",
                        new[] { "cancel-scan" },
                        new string[0],
                        new[] { "cancel-scan" });
                    return;
                case "focuscorrection":
                    PrintHelpBlock(
                        "focus-correction",
                        "Runs TLX focus correction.",
                        new[] { "focus-correction [options]" },
                        new[]
                        {
                            "--resolution    base4, base8, base16. Default: base16.",
                            "--film-color    negative, positive, bw, bw-c41. Default: negative.",
                            "--film-format   35mm or another value from 'values'. Default: 35mm.",
                            "--advance-film  Advances film during correction."
                        },
                        new[] { "focus-correction --resolution base16 --film-color negative" });
                    return;
                case "lightcorrection":
                    PrintHelpBlock(
                        "light-correction",
                        "Runs TLX light correction.",
                        new[] { "light-correction [options]" },
                        new[]
                        {
                            "--resolution    base4, base8, base16. Default: base16.",
                            "--film-color    negative, positive, bw, bw-c41. Default: negative.",
                            "--film-format   35mm or another value from 'values'. Default: 35mm.",
                            "--include-ir    Includes the infrared channel. Alias: --ir."
                        },
                        new[] { "light-correction --include-ir" });
                    return;
                case "filmtracktest":
                    PrintHelpBlock(
                        "film-track-test",
                        "Runs a film track test and prints the resulting TLX status.",
                        new[] { "film-track-test [--adjust-pots]" },
                        new[] { "--adjust-pots  Allows TLX to adjust potentiometers during the test." },
                        new[] { "film-track-test", "film-track-test --adjust-pots" });
                    return;
                case "filmtrackresults":
                    PrintHelpBlock(
                        "film-track-results",
                        "Prints the most recent film track test result.",
                        new[] { "film-track-results" },
                        new string[0],
                        new[] { "film-track-results" });
                    return;
                case "advance":
                    PrintHelpBlock(
                        "advance",
                        "Advances film for a fixed time at a fixed speed.",
                        new[] { "advance [--milliseconds 500] [--speed 5]" },
                        new[]
                        {
                            "--milliseconds  Motor run time. Default: 500.",
                            "--speed         Motor speed. Default: 5."
                        },
                        new[] { "advance --milliseconds 750 --speed 5" });
                    return;
                case "moveoldest":
                    PrintHelpBlock(
                        "move-oldest",
                        "Moves the oldest roll from the scan group to the save group.",
                        new[] { "move-oldest" },
                        new string[0],
                        new[] { "move-oldest" });
                    return;
                case "scancount":
                    PrintHelpBlock(
                        "scan-count",
                        "Prints strip, picture, and warning counts for a scan group roll.",
                        new[] { "scan-count [--roll 0]" },
                        new[] { "--roll  Scan group roll index. Default: 0." },
                        new[] { "scan-count", "scan-count --roll 1" });
                    return;
                case "savecount":
                    PrintHelpBlock(
                        "save-count",
                        "Prints roll, strip, picture, selected, and hidden counts for the save group.",
                        new[] { "save-count" },
                        new string[0],
                        new[] { "save-count" });
                    return;
                case "save":
                case "savedisk":
                case "savememory":
                    PrintHelpBlock(
                        "save",
                        "Saves pictures from the current save group. By default jpeg, bmp, tiff, and exif use TLX disk save. Use --raw, --format raw, or --format png to save through client memory and the .NET raw converter.",
                        new[] { "save [options]" },
                        new[]
                        {
                            "--directory     Output directory. Created if needed.",
                            "--prefix        Output filename prefix when --directory is supplied. Default: pakon.",
                            "--index         all, selected, current, first, end. Default: all.",
                            "--format        jpeg, bmp, tiff, exif, png, raw. Default: jpeg.",
                            "--raw           Uses client-memory raw conversion for jpeg, bmp, or tiff output.",
                            "--compression   JPEG/TIFF compression or quality value. Alias: --quality. Default: 90.",
                            "--dpi           Output DPI. Default: 300.",
                            "--color-bits    Output color bits. Default: 24.",
                            "--save-control  Comma-separated flags such as size-original,scratch. Default: size-original.",
                            "--width, --height Optional scaled output size. Defaults: 0, 0.",
                            "--scaling       Scaling method. Default: bicubic.",
                            "--memory-format  planar16, planar8, dib8. Default: planar16.",
                            "--gamma          Raw conversion gamma. Values over 1 are interpreted as display gamma, so 2.2 becomes 1/2.2. Default: 0.4545.",
                            "--contrast       Raw conversion contrast multiplier for colour output. Default: 1.08.",
                            "--saturation     Raw conversion saturation multiplier for colour output. Default: 1.08.",
                            "--raw-converter  Path to RawImageConverterCli.exe. Defaults to beside ConsoleClient.exe.",
                            "--conversion-workers  Parallel raw conversion process count. Default: 2 for converted output, 1 for raw.",
                            "--four-channel   Requests four-channel memory output.",
                            "--buffer-mb      Client buffer size in MB. Default: 64."
                        },
                        new[]
                        {
                            "save --directory C:\\Scans --prefix roll01 --format jpeg",
                            "save --directory C:\\Scans --format png",
                            "save --directory C:\\Scans --format raw",
                            "save --directory C:\\Scans --raw --format tiff --contrast 1.12"
                        });
                    return;
                case "cancelsave":
                    PrintHelpBlock(
                        "cancel-save",
                        "Requests cancellation of the current save operation.",
                        new[] { "cancel-save" },
                        new string[0],
                        new[] { "cancel-save" });
                    return;
                case "pictures":
                    PrintHelpBlock(
                        "pictures",
                        "Lists picture metadata for the current save group.",
                        new[] { "pictures [--limit <count>]" },
                        new[] { "--limit  Maximum number of pictures to print. Default: all." },
                        new[] { "pictures", "pictures --limit 6" });
                    return;
                case "pictureinfo":
                    PrintHelpBlock(
                        "picture-info",
                        "Prints full metadata for one picture in the save group.",
                        new[] { "picture-info [--index 0]" },
                        new[] { "--index  Zero-based picture index. Default: 0." },
                        new[] { "picture-info --index 0" });
                    return;
                case "putpictureinfo":
                    PrintHelpBlock(
                        "put-picture-info",
                        "Updates metadata for one picture in the save group. Omitted values keep their current setting.",
                        new[] { "put-picture-info [options]" },
                        new[]
                        {
                            "--index      Zero-based picture index. Default: 0.",
                            "--frame      Frame number.",
                            "--file       File name.",
                            "--directory  Output directory.",
                            "--rotation   Rotation value.",
                            "--selection  none, selected, hidden."
                        },
                        new[] { "put-picture-info --index 0 --file roll01-0001.jpg --directory C:\\Scans" });
                    return;
                case "select":
                    PrintHelpBlock(
                        "select",
                        "Updates selection state for pictures in the save group.",
                        new[] { "select [options]" },
                        new[]
                        {
                            "--index       all, selected, current, first, end. Default: current.",
                            "--selection   none, selected, hidden. Default: selected.",
                            "--skip-hidden Skips hidden pictures while updating selection."
                        },
                        new[] { "select --index all --selection selected", "select --index current --selection hidden" });
                    return;
                case "framing":
                    PrintHelpBlock(
                        "framing",
                        "Prints framing rectangle values for one picture.",
                        new[] { "framing [--index 0] [--low-res]" },
                        new[]
                        {
                            "--index    Zero-based picture index. Default: 0.",
                            "--low-res  Reads low-resolution framing information."
                        },
                        new[] { "framing --index 0", "framing --index 0 --low-res" });
                    return;
                case "values":
                    PrintHelpBlock(
                        "values",
                        "Prints supported friendly values for enum-like options. Raw TLX integer values are also accepted.",
                        new[] { "values" },
                        new string[0],
                        new[] { "values" });
                    return;
                case "commands":
                    PrintHelpBlock(
                        "commands",
                        "Prints the short command list.",
                        new[] { "commands" },
                        new string[0],
                        new[] { "commands" });
                    return;
                case "help":
                    PrintHelpBlock(
                        "help",
                        "Prints general help or detailed help for one command.",
                        new[] { "help", "help <command>" },
                        new string[0],
                        new[] { "help", "help scan-save" });
                    return;
                case "quit":
                case "exit":
                    PrintHelpBlock(
                        "quit",
                        "Exits interactive mode and closes the scanner session.",
                        new[] { "quit", "exit" },
                        new string[0],
                        new[] { "quit" });
                    return;
                default:
                    Console.WriteLine("Unknown command '" + command + "'.");
                    Console.WriteLine("Run 'commands' for the command list or 'help' for general usage.");
                    return;
            }
        }

        private static void PrintHelpBlock(string name, string description, string[] usage, string[] options, string[] examples)
        {
            Console.WriteLine(name);
            Console.WriteLine();
            Console.WriteLine(description);
            Console.WriteLine();
            Console.WriteLine("Usage:");
            foreach (var line in usage)
            {
                Console.WriteLine("  " + line);
            }

            if (options.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Options:");
                foreach (var line in options)
                {
                    Console.WriteLine("  " + line);
                }
            }

            if (examples.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Examples:");
                foreach (var line in examples)
                {
                    Console.WriteLine("  " + line);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Global options: --verbose, --com-server-dir <path>");
            Console.WriteLine("Run 'values' for friendly option values. Raw TLX integer values are accepted for enum-like options.");
        }

        private static void PrintCommandList()
        {
            Console.WriteLine("Commands:");
            Console.WriteLine("  init, info, warnings, errors, close");
            Console.WriteLine("  diagnostics");
            Console.WriteLine("  scan, scan-save, cancel-scan");
            Console.WriteLine("  focus-correction, light-correction, film-track-test, film-track-results, advance");
            Console.WriteLine("  move-oldest, scan-count, save-count, save, cancel-save");
            Console.WriteLine("  pictures, picture-info, put-picture-info, select, framing");
            Console.WriteLine("  values, commands, help, quit");
        }

        private static void PrintValues()
        {
            Console.WriteLine("Values:");
            Console.WriteLine("  --resolution: base4, base8, base16");
            Console.WriteLine("  --film-color: " + string.Join(", ", GetSupportedFilmColorNames()));
            Console.WriteLine("  --film-format: " + string.Join(", ", GetSupportedFilmFormatNames()));
            Console.WriteLine("  --strip-mode: full-roll");
            Console.WriteLine("  --scan-control flags: " + string.Join(", ", GetSupportedScanControlNames()));
            Console.WriteLine("  --save-control flags: " + string.Join(", ", GetSupportedSaveControlNames()));
            Console.WriteLine("  --index: all, selected, current, first, end");
            Console.WriteLine("  --selection: none, selected, hidden");
            Console.WriteLine("  --format: jpeg, bmp, tiff, exif, png, raw");
            Console.WriteLine("  --memory-format: planar16, planar8, dib8");
            Console.WriteLine("  --output-format: png, jpeg, tiff, bmp, raw");
            Console.WriteLine("  --scaling: bicubic");
        }

        private static IEnumerable<string> GetSupportedFilmColorNames()
        {
            yield return "negative";
            yield return "positive";

            if (FilmColor.IsNativeNameDefined("FILM_COLOR_BnW_NORMAL"))
            {
                yield return "bw";
            }

            if (FilmColor.IsNativeNameDefined("FILM_COLOR_BnW_C41"))
            {
                yield return "bw-c41";
            }
        }

        private static IEnumerable<string> GetSupportedFilmFormatNames()
        {
            yield return "35mm";
            yield return "24mm";

            if (FilmFormat.IsNativeNameDefined("FILM_FORMAT_24MM_CART"))
            {
                yield return "24-cart";
            }

            if (FilmFormat.IsNativeNameDefined("FILM_FORMAT_24MM_CART_MOF_READER"))
            {
                yield return "24-cart-mof-reader";
            }

            if (FilmFormat.IsNativeNameDefined("FILM_FORMAT_24MM_CART_MOF_FILE"))
            {
                yield return "24-cart-mof-file";
            }

            if (FilmFormat.IsNativeNameDefined("FILM_FORMAT_24MM_CART_MOF_FILE_OR_READER"))
            {
                yield return "24-cart-mof-file-or-reader";
            }

            if (FilmFormat.IsNativeNameDefined("FILM_FORMAT_24MM_FILE"))
            {
                yield return "24-file";
            }
        }

        private static IEnumerable<string> GetSupportedScanControlNames()
        {
            yield return "none";

            if (ScanControl.IsNativeNameDefined("SCAN_AggressiveFraming"))
            {
                yield return "aggressive-framing";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_UseScratchRemoval"))
            {
                yield return "scratch";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_HasFilmDrag"))
            {
                yield return "film-drag";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_Read_DX"))
            {
                yield return "dx";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_RFT_SenseSplice"))
            {
                yield return "splice";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_Use24mmExternalFileMOF"))
            {
                yield return "24-mof";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_Use24mmAutoLoader"))
            {
                yield return "24-autoloader";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_Use24mmAutoLoaderMOF"))
            {
                yield return "24-autoloader-mof";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_LampWarmUp"))
            {
                yield return "lamp-warmup";
            }

            if (ScanControl.IsNativeNameDefined("SCAN_PreScan"))
            {
                yield return "prescan";
            }
        }

        private static IEnumerable<string> GetSupportedSaveControlNames()
        {
            yield return "none";
            yield return "size-original";
            yield return "lores";

            if (SaveControl.IsNativeNameDefined("SAV_SizeLimitForDisplay"))
            {
                yield return "display-limit";
            }

            if (SaveControl.IsNativeNameDefined("SAV_SizeLimitForSave"))
            {
                yield return "save-limit";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseCurrentRotation"))
            {
                yield return "current-rotation";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseScratchRemovalIfAvailable"))
            {
                yield return "scratch";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseColorCorrection"))
            {
                yield return "color-correction";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseColorSceneBalance"))
            {
                yield return "scene-balance";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseColorAdjustments"))
            {
                yield return "color-adjustments";
            }

            if (SaveControl.IsNativeNameDefined("SAV_FileHeader"))
            {
                yield return "file-header";
            }

            if (SaveControl.IsNativeNameDefined("SAV_FastUpdate8BitDib"))
            {
                yield return "fast-dib";
            }

            if (SaveControl.IsNativeNameDefined("SAV_TopDownDib"))
            {
                yield return "top-down-dib";
            }

            if (SaveControl.IsNativeNameDefined("SAV_DoNotScaleUp"))
            {
                yield return "do-not-scale-up";
            }

            if (SaveControl.IsNativeNameDefined("SAV_UseColorKcdfs"))
            {
                yield return "kcdfs";
            }
        }
    }
}
