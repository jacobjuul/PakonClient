using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pakon.Transport;

/// <summary>
/// Opens a Pakon driver endpoint and performs only its documented metadata query.
/// It deliberately contains no scanner packet, scan, movement, lamp, calibration,
/// firmware, or EEPROM operation.
/// </summary>
public static class PakonDriverMetadataProbe
{
    // FX35USB/driver/ezusb.h: IOCTL_EZUSB_GET_DRIVER_VERSION.
    public const uint IoctlGetDriverVersion = 0x222074;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    public static DriverMetadataProbeResult Probe(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        using var handle = NativeMethods.CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return new DriverMetadataProbeResult(
                devicePath, false, false, Marshal.GetLastWin32Error(), 0, []);
        }

        var response = new byte[6];
        var result = NativeMethods.DeviceIoControl(
            handle,
            IoctlGetDriverVersion,
            null,
            0,
            response,
            response.Length,
            out var bytesReturned,
            IntPtr.Zero);

        return result
            ? new DriverMetadataProbeResult(devicePath, true, true, null, bytesReturned, response[..Math.Min(bytesReturned, response.Length)])
            : new DriverMetadataProbeResult(devicePath, true, false, Marshal.GetLastWin32Error(), 0, []);
    }

    public static string DescribeWin32Error(int error) => new Win32Exception(error).Message;

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            byte[]? inBuffer,
            int inBufferSize,
            [Out] byte[] outBuffer,
            int outBufferSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
