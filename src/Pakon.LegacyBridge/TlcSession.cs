using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Pakon.LegacyBridge
{
    /// <summary>
    /// Owns the direct TLC COM object for one bridge-process lifetime. Keeping this object alive is
    /// essential: initialization and scanning report asynchronous progress through ICallBackClient.
    /// No TLX interface or TLX coclass is involved.
    /// </summary>
    internal sealed class TlcSession
    {
        // TLC's recovered initialization path only reads bit 0x2 (firmware update).
        // The old TLX-only "CSharpClient" bit 0x40000000 has no TLC-side effect, so the
        // direct-TLC default retains only the useful percent-progress callback request.
        private const int DefaultInitializationFlags = 0x00000001;
        // Firmware update is intentionally unsupported and must never be sent to the scanner.
        private const int FirmwareUpdateInitializationFlag = 0x00000002;
        private const int DefaultMemoryTimeoutMilliseconds = 200000;
        private static readonly Guid TlcMainClassId = new Guid("6449DE65-60A9-4A45-A3A1-337F5E6B41E0");
        private readonly object sync = new object();
        private object instance;
        private ILongOpsCB longOperations;
        private TlcProgressCallback callback;
        private int callbackCookie;
        private bool initializationRequested;

        public IDictionary<string, string> Initialize(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                if (initializationRequested)
                {
                    throw new InvalidOperationException("A TLC initialization request is already active in this bridge session. Close the session before initializing again.");
                }

                var flags = GetInt32(arguments, "initializationFlags", DefaultInitializationFlags);
                var timeout = GetInt32(arguments, "memoryTimeoutMilliseconds", DefaultMemoryTimeoutMilliseconds);
                var sharedMemoryBytes = GetInt32(arguments, "sharedMemoryBytes", 0);
                if ((flags & FirmwareUpdateInitializationFlag) != 0)
                {
                    throw new InvalidOperationException("Firmware update is permanently prohibited by this bridge and will not be sent to TLC.");
                }

                // TLC itself rejects a timeout below 1,000 ms. A non-zero sharedMemoryBytes asks TLC
                // to create a GUID-named, pagefile-backed mapping; zero deliberately opts out.
                if (timeout < 1000 || sharedMemoryBytes < 0)
                {
                    throw new ArgumentOutOfRangeException("TLC initialization timeout must be at least 1,000 ms and shared-memory size must be non-negative.");
                }

                try
                {
                    instance = Activator.CreateInstance(Type.GetTypeFromCLSID(TlcMainClassId, true));
                    var main = (ITlcMain)instance;
                    longOperations = (ILongOpsCB)instance;
                    callback = new TlcProgressCallback();
                    callbackCookie = longOperations.CBAdvise(callback);
                    if (callbackCookie == 0)
                    {
                        throw new InvalidOperationException("TLC rejected the progress callback; scanner initialization was not started.");
                    }

                    // This is the exact three-argument TLC type-library call, not TLX's two-argument facade.
                    main.InitializeScanner(flags, timeout, sharedMemoryBytes);
                    initializationRequested = true;
                }
                catch
                {
                    CloseCore();
                    throw;
                }

                var result = GetStatusCore();
                result["initializationFlags"] = "0x" + unchecked((uint)flags).ToString("X8", CultureInfo.InvariantCulture);
                result["memoryTimeoutMilliseconds"] = timeout.ToString(CultureInfo.InvariantCulture);
                result["sharedMemoryBytes"] = sharedMemoryBytes.ToString(CultureInfo.InvariantCulture);
                result["note"] = "TLC initialization has been requested; query get-tlc-session-status for asynchronous callback progress.";
                return result;
            }
        }

        public IDictionary<string, string> GetStatus()
        {
            lock (sync)
            {
                return GetStatusCore();
            }
        }

        public IDictionary<string, string> Close()
        {
            lock (sync)
            {
                CloseCore();
                return new Dictionary<string, string> { { "sessionOpen", "False" }, { "closed", "True" } };
            }
        }

        private IDictionary<string, string> GetStatusCore()
        {
            var result = new Dictionary<string, string>
            {
                { "sessionOpen", (instance != null).ToString() },
                { "initializationRequested", initializationRequested.ToString() },
                { "callbackCookie", callbackCookie.ToString(CultureInfo.InvariantCulture) }
            };

            if (callback != null)
            {
                result["callbackCount"] = callback.Count.ToString(CultureInfo.InvariantCulture);
                result["lastOperation"] = callback.LastOperation.ToString(CultureInfo.InvariantCulture);
                result["lastStatus"] = callback.LastStatus.ToString(CultureInfo.InvariantCulture);
            }

            return result;
        }

        private void CloseCore()
        {
            if (longOperations != null && callbackCookie != 0)
            {
                try { longOperations.CBUnadvise(callbackCookie); }
                catch (COMException) { }
            }

            callbackCookie = 0;
            callback = null;
            longOperations = null;
            initializationRequested = false;
            if (instance != null && Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
            instance = null;
        }

        private static int GetInt32(IDictionary<string, string> arguments, string key, int fallback)
        {
            string raw;
            int value;
            if (arguments == null || !arguments.TryGetValue(key, out raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new ArgumentException("Bridge argument '" + key + "' must be a signed 32-bit integer.");
            }
            return value;
        }

        [ComImport, Guid("2E37F1D3-50D0-4345-8C0D-4481AFF29EB9"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface ITlcMain
        {
            [DispId(1)]
            void InitializeScanner(int initializationFlags, int memoryTimeoutMilliseconds, int sharedMemoryBytes);
        }

        [ComImport, Guid("E4724BBF-4C27-4AB3-92A8-CD2D45B87682"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ILongOpsCB
        {
            int CBAdvise([MarshalAs(UnmanagedType.Interface)] ICallBackClient callbackClient);
            void CBUnadvise(int cookie);
            void CBHelperNTS(int operation, int status);
        }

        [ComImport, Guid("31E3E438-9AAD-408A-81FA-BBCA917907D2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICallBackClient
        {
            void Awake(int operation, int status);
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class TlcProgressCallback : ICallBackClient
        {
            private readonly object callbackSync = new object();
            private int count;
            private int lastOperation;
            private int lastStatus;

            public int Count { get { lock (callbackSync) return count; } }
            public int LastOperation { get { lock (callbackSync) return lastOperation; } }
            public int LastStatus { get { lock (callbackSync) return lastStatus; } }

            public void Awake(int operation, int status)
            {
                lock (callbackSync)
                {
                    count++;
                    lastOperation = operation;
                    lastStatus = status;
                }
            }
        }
    }
}
