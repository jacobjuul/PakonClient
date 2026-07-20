using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pakon.Transport;

/// <summary>
/// TLC's read-only PPB interrupt-status query. This is an opt-in diagnostic
/// operation, not a generic packet sender. Its request is exactly 03 01 10.
/// </summary>
public static class PakonPpbInterruptStatusQuery
{
    // FX35USB/driver/ezusb.h: IOCTL_PAKON_SEND_AND_RECEIVE_PACKET.
    public const uint IoctlSendAndReceivePacket = 0x222090;

    private static readonly byte[] Request = [3, 1, 0x10];
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    public static PpbInterruptStatusResult Read(string devicePath)
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
            return new PpbInterruptStatusResult(devicePath, false, Marshal.GetLastWin32Error(), 0, []);
        }

        var response = new byte[36];
        var succeeded = NativeMethods.DeviceIoControl(
            handle,
            IoctlSendAndReceivePacket,
            Request,
            Request.Length,
            response,
            response.Length,
            out var bytesReturned,
            IntPtr.Zero);

        return succeeded
            // TLC reads its fixed 36-byte output buffer directly and does not
            // use the driver's byte-count result. Preserve the complete buffer
            // because this installed driver has already returned zero bytes for
            // a successful metadata IOCTL.
            ? new PpbInterruptStatusResult(devicePath, true, null, bytesReturned, response)
            : new PpbInterruptStatusResult(devicePath, false, Marshal.GetLastWin32Error(), 0, []);
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
            byte[] inBuffer,
            int inBufferSize,
            [Out] byte[] outBuffer,
            int outBufferSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
