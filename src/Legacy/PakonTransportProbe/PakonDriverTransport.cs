using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PakonTransportProbe
{
    internal static class PakonDriverTransport
    {
        // From FX35USB/driver/ezusb.h. This returns only the driver's fixed
        // 6-byte metadata structure; it does not communicate with scanner firmware.
        private const uint IoctlEzusbGetDriverVersion = 0x222074;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        public static bool ProbeDriverVersion(string devicePath, ProbeLog log)
        {
            log.Write("Driver open: {0}", devicePath);
            using (var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    log.Write("Driver open failed: Win32 error {0} ({1})", error, new Win32Exception(error).Message);
                    return false;
                }

                log.Write("Driver open succeeded: handle=0x{0:X}", handle.DangerousGetHandle().ToInt64());
                var response = new byte[6];
                int bytesReturned;
                log.Write("IOCTL request: code=0x{0:X8}; input=[]; outputCapacity={1}", IoctlEzusbGetDriverVersion, response.Length);
                var result = DeviceIoControl(handle, IoctlEzusbGetDriverVersion, null, 0, response, response.Length, out bytesReturned, IntPtr.Zero);
                if (!result)
                {
                    var error = Marshal.GetLastWin32Error();
                    log.Write("IOCTL response: failed; Win32 error {0} ({1})", error, new Win32Exception(error).Message);
                    return false;
                }

                log.Write("IOCTL response: bytesReturned={0}; bytes=[{1}]", bytesReturned, ToHex(response, bytesReturned));
                if (bytesReturned == 6)
                {
                    log.Write("Driver version decoded from six unsigned 8-bit fields: {0}.{1}.{2}.{3}.{4}.{5}", response[0], response[1], response[2], response[3], response[4], response[5]);
                }
                else
                {
                    log.Write("Driver returned an unexpected metadata length. Bytes are logged verbatim; no version interpretation was assumed.");
                }

                return true;
            }
        }

        private static string ToHex(byte[] bytes, int count)
        {
            var actualCount = Math.Max(0, Math.Min(count, bytes.Length));
            var parts = new string[actualCount];
            for (var index = 0; index < actualCount; index++) parts[index] = bytes[index].ToString("X2");
            return string.Join(" ", parts);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            byte[] inBuffer,
            int inBufferSize,
            [Out] byte[] outBuffer,
            int outBufferSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
