namespace Pakon.Transport;

/// <summary>Known user-mode device names used by installed Pakon drivers.</summary>
public static class KnownPakonEndpoints
{
    public const string F135 = @"\\.\Pakon135";
    public const string X35 = @"\\.\PakonX35";

    /// <summary>Returns endpoints in the order currently safest to probe.</summary>
    public static IReadOnlyList<string> ProbeOrder { get; } = [F135, X35];
}
