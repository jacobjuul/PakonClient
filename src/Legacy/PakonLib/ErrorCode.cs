using System;
using TLXLib;

namespace PakonLib
{
    public readonly struct ErrorCode : IEquatable<ErrorCode>
    {
        private readonly ERROR_CODES_000 _value;

        private ErrorCode(ERROR_CODES_000 value)
        {
            _value = value;
        }

        public int RawValue => (int)_value;

        public string Name => _value.ToString();

        public string DisplayName => GetKnownDescription(RawValue) ?? FormatName(Name);

        public bool IsDefined => Enum.IsDefined(typeof(ERROR_CODES_000), _value);

        public static ErrorCode FromInterop(ERROR_CODES_000 value) => new ErrorCode(value);

        public ERROR_CODES_000 ToInterop() => _value;

        public static ErrorCode FromValue(int value) => new ErrorCode((ERROR_CODES_000)value);

        public static bool TryFromValue(int value, out ErrorCode result)
        {
            if (Enum.IsDefined(typeof(ERROR_CODES_000), value))
            {
                result = new ErrorCode((ERROR_CODES_000)value);
                return true;
            }

            result = default;
            return false;
        }

        public static bool TryParse(string name, out ErrorCode result)
        {
            if (Enum.TryParse(name, out ERROR_CODES_000 parsed))
            {
                result = new ErrorCode(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public static ErrorCode Parse(string name)
        {
            if (!TryParse(name, out var result))
            {
                throw new ArgumentException("Unknown error code name.", nameof(name));
            }

            return result;
        }

        public override string ToString() => DisplayName;

        public bool Equals(ErrorCode other) => _value == other._value;

        public override bool Equals(object obj) => obj is ErrorCode other && Equals(other);

        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(ErrorCode left, ErrorCode right) => left.Equals(right);

        public static bool operator !=(ErrorCode left, ErrorCode right) => !left.Equals(right);

        public static bool TryParseComExceptionMessage(string message, out ErrorCode result)
        {
            result = default;
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            message = message.Trim().Trim('\'', '"');
            if (!int.TryParse(message, out int value))
            {
                return false;
            }

            return TryFromValue(value, out result);
        }

        public static string GetKnownDescription(int value)
        {
            switch ((ERROR_CODES_000)value)
            {
                case ERROR_CODES_000.EC_InvalidPtrToClientCallback:
                    return "Invalid Pointer To Client Callback";
                case ERROR_CODES_000.EC_WorkerThreadExists:
                    return "Worker Thread Already Exists";
                case ERROR_CODES_000.EC_QueryInterface:
                    return "QueryInterface for Client CallBack";
                case ERROR_CODES_000.EC_CoMarshalInterThreadInterfaceInStream:
                    return "CoMarshalInterThreadInterfaceInStream";
                case ERROR_CODES_000.EC_UnableToCreateWorkerThread:
                    return "Unable To Create Worker Thread";
                case ERROR_CODES_000.EC_WorkerThreadCoInitialize:
                    return "WorkerThreadCoInitialize";
                case ERROR_CODES_000.EC_WorkerThreadCoGetInterfaceAndReleaseStream:
                    return "Worker Thread CoGetInterfaceAndReleaseStream";
                case ERROR_CODES_000.EC_WorkerThreadClientSignal:
                    return "Worker Thread Client Signal";
                case ERROR_CODES_000.EC_WorkerThreadStartTimeout:
                    return "Worker Thread Start Timeout";
                case ERROR_CODES_000.EC_ScannerNotInitialized:
                    return "Scanner Not Initialized";
                case ERROR_CODES_000.EC_NoPicturesOrStrips:
                    return "No Pictures Or Strips";
                case ERROR_CODES_000.EC_TooManyRolls:
                    return "Too Many Rolls";
                case ERROR_CODES_000.EC_InvalidIndex:
                    return "Invalid Index";
                case ERROR_CODES_000.EC_InvalidMemberVariable:
                    return "Invalid Member Variable";
                case ERROR_CODES_000.EC_InvalidParameter:
                    return "Invalid Parameter";
                case ERROR_CODES_000.EC_NoWorkerThreadForMultipleSaveToMemory:
                    return "No Worker Thread For Multiple SaveToMemory";
                case ERROR_CODES_000.EC_NoClientMemoryBuffer:
                    return "No Client Memory Buffer";
                case ERROR_CODES_000.EC_OneFileNameForMultipleSaves:
                    return "One File Name For Multiple Saves";
                case ERROR_CODES_000.EC_StartUpError:
                    return "Start Up Error";
                case ERROR_CODES_000.EC_CBAdviseAlreadyCalled:
                    return "CBAdvise Already Called";
                case ERROR_CODES_000.EC_CBAdviseNotCalled:
                    return "CBAdvise Not Called";
                case ERROR_CODES_000.EC_InitializeScannerAlreadyCalled:
                    return "Initialize Scanner Already Called";
                case ERROR_CODES_000.EC_AdjustMotorSpeedIsZero:
                    return "Motor Adjust Speed is Zero";
                case ERROR_CODES_000.EC_NotSupportedByHW:
                    return "This function is not implemented in the hardware";
                case ERROR_CODES_000.EC_PreviousError:
                    return "Previous Error";
                case ERROR_CODES_000.EC_CallEnableFullCalibration:
                    return "Call Enable Full Calibration";
                case ERROR_CODES_000.EC_FileNameListEmpty:
                    return "File Name List Empty";
                case ERROR_CODES_000.EC_LampError:
                    return "Lamp Error";
                case ERROR_CODES_000.EC_ChangingFrameNumberWithAps:
                    return "Changing Frame Number With APS";
                case ERROR_CODES_000.EC_NotAllowedWithAps:
                    return "Not Allowed With APS";
                default:
                    return null;
            }
        }

        private static string FormatName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int prefixSeparator = name.IndexOf('_');
            if (prefixSeparator >= 0 && prefixSeparator + 1 < name.Length)
            {
                name = name.Substring(prefixSeparator + 1);
            }

            return name.Replace('_', ' ');
        }
    }
}
