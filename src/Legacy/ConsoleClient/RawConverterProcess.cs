using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleClient
{
    internal sealed class RawConverterProcess
    {
        private readonly string executablePath;

        public RawConverterProcess(string executablePath)
        {
            this.executablePath = executablePath;
        }

        public static string ResolvePath(string overridePath, string launchDirectory)
        {
            if (!string.IsNullOrEmpty(overridePath))
            {
                return Path.IsPathRooted(overridePath) ? overridePath : Path.GetFullPath(Path.Combine(launchDirectory, overridePath));
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var besideConsole = Path.Combine(baseDirectory, "RawImageConverterCli.exe");
            if (File.Exists(besideConsole))
            {
                return besideConsole;
            }

            foreach (var candidate in CandidatePaths(baseDirectory, launchDirectory))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return besideConsole;
        }

        private static string[] CandidatePaths(params string[] startDirectories)
        {
            var candidates = new System.Collections.Generic.List<string>();
            foreach (var startDirectory in startDirectories)
            {
                var directory = new DirectoryInfo(startDirectory);
                while (directory != null)
                {
                    candidates.Add(Path.Combine(directory.FullName, "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "Release", "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "Debug", "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "RawImageConverterCli", "bin", "Debug", "net10.0", "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "RawImageConverterCli", "bin", "Release", "net10.0", "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "src", "RawImageConverterCli", "bin", "Debug", "net10.0", "RawImageConverterCli.exe"));
                    candidates.Add(Path.Combine(directory.FullName, "src", "RawImageConverterCli", "bin", "Release", "net10.0", "RawImageConverterCli.exe"));
                    directory = directory.Parent;
                }
            }

            return candidates.ToArray();
        }

        public static string ExtensionFor(RawOutputFormat format)
        {
            switch (format)
            {
                case RawOutputFormat.Raw:
                    return ".raw";
                case RawOutputFormat.Jpeg:
                    return ".jpg";
                case RawOutputFormat.Tiff:
                    return ".tiff";
                case RawOutputFormat.Bmp:
                    return ".bmp";
                default:
                    return ".png";
            }
        }

        public RawConversionTiming Convert(byte[] data, string outputPath, RawOutputFormat outputFormat, bool isBwImage, double gamma, float contrast, float saturation, int quality)
        {
            var total = Stopwatch.StartNew();

            if (outputFormat == RawOutputFormat.Raw)
            {
                var write = Stopwatch.StartNew();
                File.WriteAllBytes(outputPath, data);
                write.Stop();
                total.Stop();
                return RawConversionTiming.ForRawWrite(total.Elapsed, write.Elapsed);
            }

            if (!File.Exists(executablePath))
            {
                throw new CommandException("Raw converter executable was not found at '" + executablePath + "'. Build RawImageConverterCli or pass --raw-converter <path>.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = BuildArguments(outputPath, outputFormat, isBwImage, gamma, contrast, saturation, quality),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process;
            var start = Stopwatch.StartNew();
            using (process = Process.Start(startInfo))
            {
                start.Stop();
                if (process == null)
                {
                    throw new CommandException("Failed to start raw converter executable '" + executablePath + "'.");
                }

                var input = Stopwatch.StartNew();
                process.StandardInput.BaseStream.Write(data, 0, data.Length);
                process.StandardInput.Close();
                input.Stop();

                var wait = Stopwatch.StartNew();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                wait.Stop();
                total.Stop();

                if (process.ExitCode != 0)
                {
                    var message = string.IsNullOrWhiteSpace(error) ? output : error;
                    throw new CommandException("Raw conversion failed for '" + outputPath + "': " + message.Trim());
                }

                return RawConversionTiming.ForProcess(total.Elapsed, start.Elapsed, input.Elapsed, wait.Elapsed, ReadConverterTiming(output));
            }
        }

        private static string ReadConverterTiming(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    const string prefix = "timing ";
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(prefix.Length);
                    }
                }
            }

            return null;
        }

        private static string BuildArguments(string outputPath, RawOutputFormat outputFormat, bool isBwImage, double gamma, float contrast, float saturation, int quality)
        {
            var builder = new StringBuilder();
            AppendArgument(builder, "--output");
            AppendArgument(builder, outputPath);
            AppendArgument(builder, "--format");
            AppendArgument(builder, FormatName(outputFormat));
            AppendArgument(builder, "--gamma");
            AppendArgument(builder, gamma.ToString(CultureInfo.InvariantCulture));
            AppendArgument(builder, "--contrast");
            AppendArgument(builder, contrast.ToString(CultureInfo.InvariantCulture));
            AppendArgument(builder, "--saturation");
            AppendArgument(builder, saturation.ToString(CultureInfo.InvariantCulture));
            AppendArgument(builder, "--quality");
            AppendArgument(builder, quality.ToString(CultureInfo.InvariantCulture));
            if (isBwImage)
            {
                AppendArgument(builder, "--bw");
            }

            return builder.ToString();
        }

        private static string FormatName(RawOutputFormat outputFormat)
        {
            return outputFormat.ToString().ToLowerInvariant();
        }

        private static void AppendArgument(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"');
            builder.Append(value.Replace("\"", "\\\""));
            builder.Append('"');
        }
    }

    internal sealed class RawConversionTiming
    {
        private RawConversionTiming()
        {
        }

        public TimeSpan TotalElapsed { get; private set; }

        public TimeSpan? RawWriteElapsed { get; private set; }

        public TimeSpan? ProcessStartElapsed { get; private set; }

        public TimeSpan? InputWriteElapsed { get; private set; }

        public TimeSpan? ProcessWaitElapsed { get; private set; }

        public string ConverterTiming { get; private set; }

        public static RawConversionTiming ForRawWrite(TimeSpan totalElapsed, TimeSpan rawWriteElapsed)
        {
            return new RawConversionTiming
            {
                TotalElapsed = totalElapsed,
                RawWriteElapsed = rawWriteElapsed
            };
        }

        public static RawConversionTiming ForProcess(TimeSpan totalElapsed, TimeSpan processStartElapsed, TimeSpan inputWriteElapsed, TimeSpan processWaitElapsed, string converterTiming)
        {
            return new RawConversionTiming
            {
                TotalElapsed = totalElapsed,
                ProcessStartElapsed = processStartElapsed,
                InputWriteElapsed = inputWriteElapsed,
                ProcessWaitElapsed = processWaitElapsed,
                ConverterTiming = converterTiming
            };
        }
    }
}
