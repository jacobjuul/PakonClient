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

        public void Convert(byte[] data, string outputPath, RawOutputFormat outputFormat, bool isBwImage, double gamma, float contrast, float saturation, int quality)
        {
            if (outputFormat == RawOutputFormat.Raw)
            {
                File.WriteAllBytes(outputPath, data);
                return;
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

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new CommandException("Failed to start raw converter executable '" + executablePath + "'.");
                }

                process.StandardInput.BaseStream.Write(data, 0, data.Length);
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var message = string.IsNullOrWhiteSpace(error) ? output : error;
                    throw new CommandException("Raw conversion failed for '" + outputPath + "': " + message.Trim());
                }
            }
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
}
