using System;
using TLXLib;

namespace PakonLib.Enums
{
    /// <summary>
    /// Provides a friendly wrapper around TLX <see cref="SAVE_CONTROL_000"/> flag values.
    /// </summary>
    public readonly struct SaveControl : IEquatable<SaveControl>
    {
        private SaveControl(SAVE_CONTROL_000 nativeValue)
        {
            NativeValue = nativeValue;
        }

        /// <summary>
        /// Gets the underlying TLX value represented by this flag combination.
        /// </summary>
        public SAVE_CONTROL_000 NativeValue { get; }

        public static SaveControl None => new SaveControl(0);

        /// <summary>
        /// Uses the original framed image dimensions rather than a display or save size limit.
        /// </summary>
        public static SaveControl SizeOriginal => new SaveControl(SAVE_CONTROL_000.SAV_SizeOriginal);

        /// <summary>
        /// Limits the output dimensions to TLX's display-size limit.
        /// </summary>
        public static SaveControl SizeLimitForDisplay => FromName("SAV_SizeLimitForDisplay");

        /// <summary>
        /// Limits the output dimensions to TLX's save-size limit.
        /// </summary>
        public static SaveControl SizeLimitForSave => FromName("SAV_SizeLimitForSave");

        /// <summary>
        /// Applies the picture rotation recorded in TLX metadata.
        /// </summary>
        public static SaveControl UseCurrentRotation => FromName("SAV_UseCurrentRotation");

        /// <summary>
        /// Saves from TLX's low-resolution buffer instead of the normal framed buffer.
        /// </summary>
        public static SaveControl UseLoResBuffer => new SaveControl(SAVE_CONTROL_000.SAV_UseLoResBuffer);

        /// <summary>
        /// Applies scratch removal if the scan contains an eligible infrared/scratch-removal result.
        /// </summary>
        public static SaveControl UseScratchRemovalIfAvailable => FromName("SAV_UseScratchRemovalIfAvailable");

        /// <summary>
        /// Applies TLX/PakonImau color correction, including its configured color transforms and LUTs,
        /// to the decoded, cropped planar frame before TLX copies or encodes the rendered output.
        /// Native correction uses a matrix-derived save context and initialized lookup table; this is
        /// host-side processing, not an FX35 driver setting.
        /// </summary>
        public static SaveControl UseColorCorrection => FromName("SAV_UseColorCorrection");

        /// <summary>
        /// Applies automatic per-scene color balancing during save. Requires
        /// <see cref="UseColorCorrection"/>; TLX rejects this flag without it.
        /// </summary>
        public static SaveControl UseColorSceneBalance => FromName("SAV_UseColorSceneBalance");

        /// <summary>
        /// Applies the configured PakonImau post-correction adjustments after color correction/balancing,
        /// including adjustment LUTs, contrast, sharpening, and optional saturation/B&amp;W effect profiles.
        /// Requires <see cref="UseColorSceneBalance"/> (and therefore color correction);
        /// TLX rejects this flag without scene balance.
        /// </summary>
        public static SaveControl UseColorAdjustments => FromName("SAV_UseColorAdjustments");

        /// <summary>
        /// Includes TLX's client-memory file header in the returned buffer.
        /// </summary>
        public static SaveControl FileHeader => FromName("SAV_FileHeader");

        /// <summary>
        /// Requests TLX's legacy fast 8-bit DIB delivery mode. This is a destination-format behavior,
        /// not scanner acquisition or PakonImau color processing, and is not part of the replacement baseline.
        /// </summary>
        public static SaveControl FastUpdate8BitDib => FromName("SAV_FastUpdate8BitDib");

        /// <summary>
        /// Requests top-down row ordering when TLX emits DIB-formatted client/shared-memory output.
        /// </summary>
        public static SaveControl TopDownDib => FromName("SAV_TopDownDib");

        /// <summary>
        /// Prevents TLX from enlarging an image while fitting it to the requested output bounds.
        /// </summary>
        public static SaveControl DoNotScaleUp => FromName("SAV_DoNotScaleUp");

        /// <summary>
        /// Legacy KCDFS color processing flag. The installed TLX type library marks this control obsolete.
        /// </summary>
        public static SaveControl UseColorKcdfs => FromName("SAV_UseColorKcdfs");

        /// <summary>
        /// The complete host-side color-processing set used by default for C-41 color negatives:
        /// correction, automatic scene balance, and configured color adjustments.
        /// </summary>
        public static SaveControl C41ColorProcessingDefaults =>
            UseColorCorrection | UseColorSceneBalance | UseColorAdjustments;

        public static SaveControl DiskSaveDefaults => UseCurrentRotation | UseScratchRemovalIfAvailable;

        public static SaveControl ClientMemorySaveDefaults => FileHeader | UseScratchRemovalIfAvailable | UseCurrentRotation;

        public static SaveControl FourChannelClientMemorySaveDefaults => FileHeader | UseScratchRemovalIfAvailable;

        private static SaveControl FromName(string name)
        {
            if (Enum.IsDefined(typeof(SAVE_CONTROL_000), name))
            {
                return new SaveControl((SAVE_CONTROL_000)Enum.Parse(typeof(SAVE_CONTROL_000), name));
            }

            throw new NotSupportedException("Save control '" + name + "' is not supported by this installed Pakon TLX interop. Run 'values' to see values supported by this client, or pass a raw TLX integer if your TLX version supports it.");
        }

        public static SaveControl FromNative(SAVE_CONTROL_000 value) => new SaveControl(value);

        public static SaveControl FromRawValue(int value) => new SaveControl((SAVE_CONTROL_000)value);

        public static bool IsNativeNameDefined(string name) => Enum.IsDefined(typeof(SAVE_CONTROL_000), name);

        public bool Equals(SaveControl other) => NativeValue.Equals(other.NativeValue);

        public override bool Equals(object obj) => obj is SaveControl other && Equals(other);

        public override int GetHashCode() => NativeValue.GetHashCode();

        public override string ToString() => NativeValue.ToString();

        public static bool operator ==(SaveControl left, SaveControl right) => left.Equals(right);

        public static bool operator !=(SaveControl left, SaveControl right) => !left.Equals(right);

        public static SaveControl operator |(SaveControl left, SaveControl right) => new SaveControl(left.NativeValue | right.NativeValue);

        public static SaveControl operator &(SaveControl left, SaveControl right) => new SaveControl(left.NativeValue & right.NativeValue);

        public static SaveControl operator ~(SaveControl value) => new SaveControl(~value.NativeValue);

        public bool HasFlag(SaveControl flag) => (NativeValue & flag.NativeValue) == flag.NativeValue;

        public static implicit operator SAVE_CONTROL_000(SaveControl value) => value.NativeValue;

        public static implicit operator SaveControl(SAVE_CONTROL_000 value) => FromNative(value);
    }
}
