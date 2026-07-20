namespace Pakon.Transport;

/// <summary>Raw result of TLC's read-only PPB interrupt-status packet.</summary>
public sealed record PpbInterruptStatusResult(
    string DevicePath,
    bool IoctlSucceeded,
    int? Win32Error,
    int BytesReturned,
    byte[] ResponseBytes)
{
    /// <summary>
    /// TLC expects response type 3, payload length 3, and an echoed command
    /// byte 0x10. It then reads byte 3 as the interrupt-status value.
    /// </summary>
    public bool HasExpectedPacketShape => ResponseBytes.Length >= 5
        && ResponseBytes[0] == 3
        && ResponseBytes[1] == 3
        && ResponseBytes[2] == 0x10;

    public byte? InterruptStatus => HasExpectedPacketShape
        ? ResponseBytes[3]
        : null;
}
