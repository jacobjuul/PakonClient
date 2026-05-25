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

        public static SaveControl SizeOriginal => new SaveControl(SAVE_CONTROL_000.SAV_SizeOriginal);

        public static SaveControl SizeLimitForDisplay => FromName("SAV_SizeLimitForDisplay");

        public static SaveControl SizeLimitForSave => FromName("SAV_SizeLimitForSave");

        public static SaveControl UseCurrentRotation => FromName("SAV_UseCurrentRotation");

        public static SaveControl UseLoResBuffer => new SaveControl(SAVE_CONTROL_000.SAV_UseLoResBuffer);

        public static SaveControl UseScratchRemovalIfAvailable => FromName("SAV_UseScratchRemovalIfAvailable");

        public static SaveControl UseColorCorrection => FromName("SAV_UseColorCorrection");

        public static SaveControl UseColorSceneBalance => FromName("SAV_UseColorSceneBalance");

        public static SaveControl UseColorAdjustments => FromName("SAV_UseColorAdjustments");

        public static SaveControl FileHeader => FromName("SAV_FileHeader");

        public static SaveControl FastUpdate8BitDib => FromName("SAV_FastUpdate8BitDib");

        public static SaveControl TopDownDib => FromName("SAV_TopDownDib");

        public static SaveControl DoNotScaleUp => FromName("SAV_DoNotScaleUp");

        public static SaveControl UseColorKcdfs => FromName("SAV_UseColorKcdfs");

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
