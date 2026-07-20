namespace Pakon.Transport;

/// <summary>The result of the driver's read-only version-metadata IOCTL.</summary>
public sealed record DriverMetadataProbeResult(
    string DevicePath,
    bool Opened,
    bool IoctlSucceeded,
    int? Win32Error,
    int BytesReturned,
    byte[] ResponseBytes)
{
    public bool Succeeded => Opened && IoctlSucceeded;
}
