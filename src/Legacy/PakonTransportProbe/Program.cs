using System;
using System.Collections.Generic;
using System.IO;

namespace PakonTransportProbe
{
    internal static class Program
    {
        private const string DefaultDevicePath = @"\\.\PakonX35";

        private static int Main(string[] args)
        {
            string devicePath;
            string logPath;
            if (!TryParseArguments(args, out devicePath, out logPath))
            {
                return 2;
            }

            using (var log = new ProbeLog(logPath))
            {
                log.Write("Pakon direct transport probe");
                log.Write("Safety: this tool only sends IOCTL_EZUSB_GET_DRIVER_VERSION (0x222074).");
                log.Write("Safety: it never calls IOCTL_PAKON_SEND_AND_RECEIVE_PACKET (0x222090), starts a scan, initializes TLC, or moves hardware.");
                log.Write("Process architecture: {0}", IntPtr.Size * 8);

                var driverSucceeded = PakonDriverTransport.ProbeDriverVersion(devicePath, log);
                var comSucceeded = TlcComProbe.CreateAndRelease(log);

                log.Write("Summary: driver probe {0}; direct TLC activation {1}.",
                    driverSucceeded ? "succeeded" : "failed",
                    comSucceeded ? "succeeded" : "failed");
                return driverSucceeded && comSucceeded ? 0 : 1;
            }
        }

        private static bool TryParseArguments(string[] args, out string devicePath, out string logPath)
        {
            devicePath = DefaultDevicePath;
            logPath = null;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--device":
                        if (!TryReadValue(args, ref index, out devicePath)) return false;
                        break;
                    case "--log":
                        if (!TryReadValue(args, ref index, out logPath)) return false;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return false;
                    default:
                        Console.Error.WriteLine("Unknown argument: {0}", args[index]);
                        PrintUsage();
                        return false;
                }
            }

            return true;
        }

        private static bool TryReadValue(IReadOnlyList<string> args, ref int index, out string value)
        {
            value = null;
            if (++index >= args.Count)
            {
                Console.Error.WriteLine("Missing value for {0}", args[index - 1]);
                PrintUsage();
                return false;
            }

            value = args[index];
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: PakonTransportProbe.exe [--device \\\\.\\PakonX35] [--log C:\\path\\probe.log]");
            Console.WriteLine("The default device is intentionally PakonX35. Use --device only to select a different installed driver endpoint.");
        }
    }
}
