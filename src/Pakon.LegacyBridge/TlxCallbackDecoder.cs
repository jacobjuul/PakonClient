using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pakon.LegacyBridge
{
    /// <summary>Human-readable labels for recovered TLX callback values. Raw values remain authoritative.</summary>
    internal static class TlxCallbackDecoder
    {
        public static string OperationName(int operation)
        {
            switch (operation)
            {
                case 0: return "InitializeProgress"; case 1: return "InitializeError";
                case 12: return "HardwareStatus"; case 13: return "HardwareError";
                case 14: return "APSStatus"; case 15: return "APSError";
                case 34: return "ScanProgress"; case 35: return "ScanError";
                case 38: return "SaveProgress"; case 39: return "SaveError";
                case 40: return "TLXProgress"; case 41: return "TLXError";
                case 42: return "OrderAnalysisProgress";
                default: return "Operation" + operation.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string StatusMeaning(int operation, int status)
        {
            if (operation == 12 || operation == 13) return DescribeHardwareBits(status);
            switch (status)
            {
                case 0: return "Initial"; case 1000: return "Start"; case 2000: return "End"; case 3000: return "Complete";
                default: return "raw=" + status.ToString(CultureInfo.InvariantCulture) + " (0x" + unchecked((uint)status).ToString("X8", CultureInfo.InvariantCulture) + ")";
            }
        }

        private static string DescribeHardwareBits(int status)
        {
            var labels = new List<string>();
            Add(labels, status, 0x40000000, "film sense entry");
            Add(labels, status, unchecked((int)0x80000000), "film sense exit");
            Add(labels, status, 0x00000001, "host board fault"); Add(labels, status, 0x00000002, "DX board fault");
            Add(labels, status, 0x00000004, "lamp board fault"); Add(labels, status, 0x00000008, "CCD board fault"); Add(labels, status, 0x00000010, "motor board fault");
            Add(labels, status, 0x00000100, "lamp warning"); Add(labels, status, 0x00000200, "lamp error"); Add(labels, status, 0x00000400, "temperature warning"); Add(labels, status, 0x00000800, "temperature error");
            Add(labels, status, 0x00001000, "lamp burned out"); Add(labels, status, 0x00002000, "lamp fan warning"); Add(labels, status, 0x00004000, "lamp fan error");
            Add(labels, status, 0x00040000, "power warning"); Add(labels, status, 0x00080000, "power error"); Add(labels, status, 0x00200000, "CCD stepper indeterminate");
            Add(labels, status, 0x00400000, "lens stepper indeterminate"); Add(labels, status, 0x00800000, "filter wheel indeterminate"); Add(labels, status, 0x01000000, "film guide indeterminate");
            Add(labels, status, 0x02000000, "film in guides error"); Add(labels, status, 0x04000000, "blower warning"); Add(labels, status, 0x08000000, "cleaning required");
            Add(labels, status, 0x10000000, "film emulsion down"); Add(labels, status, 0x20000000, "film tail first");
            return labels.Count == 0 ? "no recovered flags" : string.Join(", ", labels.ToArray());
        }

        private static void Add(ICollection<string> labels, int status, int flag, string label) { if ((status & flag) != 0) labels.Add(label); }
    }
}
