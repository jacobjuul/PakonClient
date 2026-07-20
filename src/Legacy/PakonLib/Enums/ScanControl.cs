using System;
using TLXLib;

namespace PakonLib.Enums
{
    /// <summary>
    /// Provides a friendly wrapper around TLX <see cref="SCAN_CONTROL_000"/> flag values.
    /// </summary>
    public readonly struct ScanControl : IEquatable<ScanControl>
    {
        private ScanControl(SCAN_CONTROL_000 nativeValue)
        {
            NativeValue = nativeValue;
        }

        /// <summary>
        /// Gets the underlying TLX value represented by this scan control flag combination.
        /// </summary>
        public SCAN_CONTROL_000 NativeValue { get; }

        public static ScanControl None => new ScanControl(SCAN_CONTROL_000.SCAN_None);

        /// <summary>
        /// Skips image-content frame detection and immediately places frames at the expected spacing.
        /// Use when normal framing cannot reliably find picture boundaries.
        /// </summary>
        public static ScanControl AggressiveFraming => FromName("SCAN_AggressiveFraming");

        /// <summary>
        /// Requests scratch-capable acquisition. TLA allocates an expanded capture/ring-buffer layout
        /// when this bit is set, consistent with carrying the IR data needed by later scratch removal.
        /// It does not itself apply correction; output also needs <see cref="SaveControl.UseScratchRemovalIfAvailable"/>.
        /// </summary>
        public static ScanControl UseScratchRemoval => FromName("SCAN_UseScratchRemoval");

        /// <summary>
        /// Declares film drag to the native scan state. The 24 mm auto-loader path also forces this state.
        /// </summary>
        public static ScanControl HasFilmDrag => FromName("SCAN_HasFilmDrag");

        /// <summary>
        /// Requests DX-code reading. This name is not present in the installed TLX 1.1 type library,
        /// so accessing it throws unless another TLX version supplies the value.
        /// </summary>
        public static ScanControl ReadDx => FromName("SCAN_Read_DX");

        /// <summary>
        /// Requests splice sensing during native scan setup.
        /// </summary>
        public static ScanControl RftSenseSplice => FromName("SCAN_RFT_SenseSplice");

        /// <summary>
        /// Requests a 24 mm external-file MOF mode. This name is not present in the installed TLX 1.1
        /// type library, so its native behavior is not yet established here.
        /// </summary>
        public static ScanControl Use24mmExternalFileMof => FromName("SCAN_Use24mmExternalFileMOF");

        /// <summary>
        /// Enables the 24 mm auto-loader transport path; TLA also enables film-drag handling for it.
        /// </summary>
        public static ScanControl Use24mmAutoLoader => FromName("SCAN_Use24mmAutoLoader");

        /// <summary>
        /// Requests a 24 mm auto-loader MOF mode. This name is not present in the installed TLX 1.1
        /// type library, so its native behavior is not yet established here.
        /// </summary>
        public static ScanControl Use24mmAutoLoaderMof => FromName("SCAN_Use24mmAutoLoaderMOF");

        /// <summary>
        /// Requests native lamp warm-up mode. This name is not present in the installed TLX 1.1 type
        /// library, so its native behavior is not yet established here.
        /// </summary>
        public static ScanControl LampWarmUp => FromName("SCAN_LampWarmUp");

        /// <summary>
        /// Selects TLA's pre-scan flow, which takes a distinct setup/early-return path rather than a
        /// normal scan flow. The user-visible result still needs a controlled hardware test.
        /// </summary>
        public static ScanControl PreScan => FromName("SCAN_PreScan");

        /// <summary>
        /// Requests TLX's premium color-negative mode. This is host-side PakonImau processing,
        /// not an FX35 driver command; the exact recipe selected by this installed TLX build is
        /// still being traced.
        /// </summary>
        public static ScanControl UsePremiumColorPath => FromName("SCAN_UsePremiumColorPath");

        private static ScanControl FromName(string name)
        {
            if (Enum.IsDefined(typeof(SCAN_CONTROL_000), name))
            {
                return new ScanControl((SCAN_CONTROL_000)Enum.Parse(typeof(SCAN_CONTROL_000), name));
            }

            throw new NotSupportedException("Scan control '" + name + "' is not supported by this installed Pakon TLX interop. Run 'values' to see values supported by this client, or pass a raw TLX integer if your TLX version supports it.");
        }

        public static ScanControl FromNative(SCAN_CONTROL_000 value) => new ScanControl(value);

        public static ScanControl FromRawValue(int value) => new ScanControl((SCAN_CONTROL_000)value);

        public static bool IsNativeNameDefined(string name) => Enum.IsDefined(typeof(SCAN_CONTROL_000), name);

        public bool Equals(ScanControl other) => NativeValue.Equals(other.NativeValue);

        public override bool Equals(object obj) => obj is ScanControl other && Equals(other);

        public override int GetHashCode() => NativeValue.GetHashCode();

        public override string ToString() => NativeValue.ToString();

        public static bool operator ==(ScanControl left, ScanControl right) => left.Equals(right);

        public static bool operator !=(ScanControl left, ScanControl right) => !left.Equals(right);

        public static ScanControl operator |(ScanControl left, ScanControl right) => new ScanControl(left.NativeValue | right.NativeValue);

        public static ScanControl operator &(ScanControl left, ScanControl right) => new ScanControl(left.NativeValue & right.NativeValue);

        public static ScanControl operator ~(ScanControl value) => new ScanControl(~value.NativeValue);

        public bool HasFlag(ScanControl flag) => (NativeValue & flag.NativeValue) == flag.NativeValue;

        public static implicit operator SCAN_CONTROL_000(ScanControl value) => value.NativeValue;

        public static implicit operator ScanControl(SCAN_CONTROL_000 value) => FromNative(value);
    }
}
