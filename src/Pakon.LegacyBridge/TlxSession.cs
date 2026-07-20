using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using TLXLib;

namespace Pakon.LegacyBridge
{
    /// <summary>
    /// Owns the established TLX scan/save facade inside the x86 bridge. The public pipe contract
    /// uses only behaviour and scalar values; COM interfaces and their lifetime never leave here.
    /// Direct TLC remains available separately for protocol research.
    /// </summary>
    internal sealed class TlxSession
    {
        private const int DefaultInitializationFlags = 0x40000001;
        private const int DefaultMemoryTimeoutMilliseconds = 200000;
        private const int DefaultInitializationReturnTimeoutSeconds = 60;
        private static readonly Guid TlxMainClassId = new Guid("EA82986B-E47C-4C0F-97EA-FB50ED216D2E");
        private readonly object sync = new object();
        private TLXMainClass instance;
        private TlxProgressCallback callback;
        private int callbackCookie;
        private string state = "Closed";
        private TlxTraceRecorder trace;
        private Dictionary<string, PfsBufferState> pfsBuffersAtOpen;

        public IDictionary<string, string> BeginTrace(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                if (trace != null) throw new InvalidOperationException("A TLX trace is already active. End it before starting another.");
                var directory = GetString(arguments, "directory");
                if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Bridge argument 'directory' is required.");
                trace = new TlxTraceRecorder(directory);
                trace.CaptureRuntimeEvidence();
                Log("trace started: " + trace.DirectoryPath);
                Trace("session-status", GetStatusCore());
                var result = GetStatusCore(); result["traceDirectory"] = trace.DirectoryPath;
                return result;
            }
        }

        public IDictionary<string, string> EndTrace()
        {
            lock (sync)
            {
                if (trace == null) throw new InvalidOperationException("No TLX trace is active.");
                var directory = trace.DirectoryPath;
                trace.CaptureRuntimeEvidence();
                Trace("trace-ended", GetStatusCore());
                trace.Dispose(); trace = null;
                Log("trace ended: " + directory);
                var result = GetStatusCore(); result["traceDirectory"] = directory;
                return result;
            }
        }

        public IDictionary<string, string> Initialize(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                RequireClosed();
                var flags = GetInt32(arguments, "initializationFlags", DefaultInitializationFlags);
                var timeout = GetInt32(arguments, "memoryTimeoutMilliseconds", DefaultMemoryTimeoutMilliseconds);
                var returnTimeoutSeconds = GetInt32(arguments, "initializationReturnTimeoutSeconds", DefaultInitializationReturnTimeoutSeconds);
                if ((flags & 0x2) != 0) throw new InvalidOperationException("Firmware update is permanently prohibited by this bridge.");
                if (timeout < 1000) throw new ArgumentOutOfRangeException("TLC initialization timeout must be at least 1,000 ms.");
                if (returnTimeoutSeconds < 1) throw new ArgumentOutOfRangeException("TLX initialization return timeout must be at least one second.");

                PrepareNativeRuntime(GetString(arguments, "comServerDirectory"));
                pfsBuffersAtOpen = CapturePfsBuffers();
                Log("initializing TLX facade");
                Trace("initialize-requested", new Dictionary<string, string>(arguments));
                try
                {
                    instance = new TLXMainClass();
                    callback = new TlxProgressCallback(this);
                    callbackCookie = instance.CBAdvise(callback);
                    if (callbackCookie == 0) throw new InvalidOperationException("TLX rejected the progress callback; initialization was not started.");
                    Log("TLX callback registered; cookie=" + callbackCookie.ToString(CultureInfo.InvariantCulture));
                    state = "Initializing";
                    // InitializeScanner can take a while. Do not hold the session lock across the
                    // native call: COM delivers progress/error callbacks concurrently, and the
                    // pipe controller must be able to observe them.
                    Monitor.Exit(sync);
                    // A healthy TLX facade accepts this asynchronous request promptly. The legacy
                    // client has observed pathological installations where this call never returns;
                    // terminate the isolated bridge rather than retain a wedged COM server.
                    try
                    {
                        using (var initializationReturned = new ManualResetEvent(false))
                        {
                            var watchdog = new Thread(
                                () =>
                                {
                                    if (!initializationReturned.WaitOne(TimeSpan.FromSeconds(returnTimeoutSeconds)))
                                    {
                                        Console.Error.WriteLine("TLX InitializeScanner did not return within " + returnTimeoutSeconds + " seconds; terminating the isolated bridge.");
                                        Environment.FailFast("TLX InitializeScanner blocked in the isolated bridge.");
                                    }
                                });
                            watchdog.IsBackground = true;
                            watchdog.Start();
                            Log("initialization watchdog armed for " + returnTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds");
                            try { instance.InitializeScanner(flags, timeout); }
                            finally { initializationReturned.Set(); }
                        }
                    }
                    finally { Monitor.Enter(sync); }
                }
                catch
                {
                    CloseCore();
                    throw;
                }

                var result = GetStatusCore(); Trace("initialize-accepted", result); return result;
            }
        }

        public IDictionary<string, string> QueueInitialize(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                if (state != "Closed" || instance != null) throw new InvalidOperationException("A TLX session is already opening or open.");
                state = "InitializationQueued";
                Trace("initialize-queued", new Dictionary<string, string>(arguments));
                return GetStatusCore();
            }
        }

        public void ReportAsynchronousFailure(Exception exception)
        {
            lock (sync)
            {
                state = "Faulted";
                Log("asynchronous TLX failure: " + exception.GetType().Name + ": " + exception.Message);
                Trace("asynchronous-failure", new Dictionary<string, string> { { "exception", exception.GetType().FullName + ": " + exception.Message } });
            }
        }

        public IDictionary<string, string> ScanRoll(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                RequireReady();
                state = "Scanning";
                Log("starting scan");
                Trace("scan-requested", new Dictionary<string, string>(arguments));
                instance.ScanPictures(
                    GetRequiredInt32(arguments, "resolution"), GetRequiredInt32(arguments, "filmColor"),
                    GetRequiredInt32(arguments, "filmFormat"), GetRequiredInt32(arguments, "stripMode"),
                    GetRequiredInt32(arguments, "scanControl"), "1000");
                var result = GetStatusCore(); Trace("scan-accepted", result); return result;
            }
        }

        /// <summary>Known conservative F135 trace profile. It intentionally does not enable scratch removal or blind framing.</summary>
        public IDictionary<string, string> ScanTraceProfile()
        {
            lock (sync)
            {
                if (trace == null) throw new InvalidOperationException("A trace must be active before starting the trace scan profile.");
                var profile = new Dictionary<string, string>
                {
                    { "resolution", "2" }, { "filmColor", "1" }, { "filmFormat", "1" },
                    { "stripMode", "0" }, { "scanControl", "0" }, { "profile", "base16-negative-35mm-full-roll-normal-framing" }
                };
                Trace("trace-scan-profile", profile);
                return ScanRoll(profile);
            }
        }

        /// <summary>Writes an observable scanner and group-state snapshot without changing scanner state.</summary>
        public IDictionary<string, string> SnapshotTrace(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                if (trace == null) throw new InvalidOperationException("No TLX trace is active.");
                var label = GetString(arguments, "label");
                if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Bridge argument 'label' is required.");
                var values = SnapshotMetadata(label);
                Trace("metadata-snapshot", values);
                trace.WriteMetadataSnapshot(label, values);
                return values;
            }
        }

        /// <summary>Saves every trace-run picture as a deterministic JPEG in the trace folder.</summary>
        public IDictionary<string, string> SaveTraceJpegs()
        {
            lock (sync)
            {
                RequireReady();
                if (trace == null) throw new InvalidOperationException("A trace must be active before saving trace JPEGs.");
                var outputDirectory = Path.Combine(trace.DirectoryPath, "output-jpeg");
                Directory.CreateDirectory(outputDirectory);
                int rolls = 0, strips = 0, pictureCount = 0, selected = 0, hidden = 0;
                instance.GetPictureCountSaveGroup(ref rolls, ref strips, ref pictureCount, ref selected, ref hidden);
                if (pictureCount == 0) throw new InvalidOperationException("The TLX save group has no pictures. Move the scanned roll to the save group before saving.");
                for (var index = 0; index < pictureCount; index++)
                {
                    int roll = 0, strip = 0, product = 0, specifier = 0, frameNumber = 0, aspect = 0, rotation = 0, selection = 0;
                    string frameName = string.Empty, oldFileName = string.Empty, oldDirectory = string.Empty;
                    instance.GetPictureInfo3(index, out roll, out strip, out product, out specifier, out frameName, out frameNumber, out aspect, out oldFileName, out oldDirectory, out rotation, out selection);
                    instance.PutPictureInfo(index, frameNumber, string.Format(CultureInfo.InvariantCulture, "frame-{0:D3}.jpg", index + 1), outputDirectory, rotation, selection);
                }
                // Original dimensions, rotation, color correction + scene balance + adjustments;
                // Bicubic scaling, JPEG, quality 90, 300 DPI, 24-bit output.
                const int saveControl = 0x74;
                state = "Saving";
                var values = new Dictionary<string, string>
                {
                    { "outputDirectory", outputDirectory }, { "pictureCount", pictureCount.ToString(CultureInfo.InvariantCulture) },
                    { "saveControl", "0x74" }, { "fileFormat", "JPEG" }, { "compression", "90" }
                };
                Trace("trace-jpeg-save-requested", values);
                Log("saving " + pictureCount.ToString(CultureInfo.InvariantCulture) + " trace JPEG(s) to " + outputDirectory);
                instance.SaveToDisk(-3, null, saveControl, 0, 0, 0, 2, 0, 90, 300, 24);
                return values;
            }
        }

        public IDictionary<string, string> MoveOldestRollToSaveGroup()
        {
            lock (sync)
            {
                RequireReady();
                Log("moving oldest roll to save group");
                Trace("move-oldest-roll-to-save-group-requested", null);
                instance.MoveOldestRollToSaveGroup();
                var result = GetStatusCore(); Trace("move-oldest-roll-to-save-group-completed", result); return result;
            }
        }

        public IDictionary<string, string> SaveFramesToDisk(IDictionary<string, string> arguments)
        {
            lock (sync)
            {
                RequireReady();
                state = "Saving";
                Log("starting save-to-disk");
                Trace("save-to-disk-requested", new Dictionary<string, string>(arguments));
                instance.SaveToDisk(
                    GetRequiredInt32(arguments, "index"), null, GetRequiredInt32(arguments, "saveControl"),
                    GetRequiredInt32(arguments, "width"), GetRequiredInt32(arguments, "height"), 0,
                    GetRequiredInt32(arguments, "scalingMethod"), GetRequiredInt32(arguments, "fileFormat"),
                    GetRequiredInt32(arguments, "compression"), GetRequiredInt32(arguments, "dpi"),
                    GetRequiredInt32(arguments, "colorBits"));
                var result = GetStatusCore(); Trace("save-to-disk-accepted", result); return result;
            }
        }

        public IDictionary<string, string> CancelScan()
        {
            lock (sync) { RequireOpen(); Log("cancelling scan"); instance.ScanCancel(); state = "CancellingScan"; var result = GetStatusCore(); Trace("scan-cancel-requested", result); return result; }
        }

        public IDictionary<string, string> CancelSave()
        {
            lock (sync) { RequireOpen(); Log("cancelling save"); instance.SaveCancel(); state = "CancellingSave"; var result = GetStatusCore(); Trace("save-cancel-requested", result); return result; }
        }

        public IDictionary<string, string> GetStatus()
        {
            // Initialization can block in native code. Status must remain available to the pipe
            // controller while that call owns the session lock.
            if (!Monitor.TryEnter(sync))
            {
                return new Dictionary<string, string>
                {
                    { "sessionOpen", (instance != null).ToString() }, { "state", state },
                    { "busy", "true" }
                };
            }
            try { var result = GetStatusCore(); Trace("session-status", result); return result; }
            finally { Monitor.Exit(sync); }
        }

        public IDictionary<string, string> Close()
        {
            lock (sync) { Trace("session-close-requested", GetStatusCore()); CloseCore(); var result = GetStatusCore(); Trace("session-closed", result); return result; }
        }

        private IDictionary<string, string> GetStatusCore()
        {
            var result = new Dictionary<string, string>
            {
                { "sessionOpen", (instance != null).ToString() }, { "state", state },
                { "callbackCookie", callbackCookie.ToString(CultureInfo.InvariantCulture) }
            };
            if (callback != null)
            {
                result["callbackCount"] = callback.Count.ToString(CultureInfo.InvariantCulture);
                result["lastOperation"] = callback.LastOperation.ToString(CultureInfo.InvariantCulture);
                result["lastStatus"] = callback.LastStatus.ToString(CultureInfo.InvariantCulture);
            }
            if (trace != null) result["traceDirectory"] = trace.DirectoryPath;
            return result;
        }

        private void OnCallback(int operation, int status)
        {
            lock (sync)
            {
                // The installed TLX 1.1 type library uses 3000 for WTP_ProgressComplete.
                if (IsErrorOperation(operation)) state = "Faulted";
                else if (status == 3000 && state != "Closed") state = "Ready";
                Log("callback operation=" + operation.ToString(CultureInfo.InvariantCulture) + "; status=" + status.ToString(CultureInfo.InvariantCulture) + "; state=" + state);
                Trace("callback", new Dictionary<string, string>
                {
                    { "operation", operation.ToString(CultureInfo.InvariantCulture) },
                    { "status", status.ToString(CultureInfo.InvariantCulture) }
                });
                if (IsErrorOperation(operation)) DrainLastErrors(operation, status);
                if (operation == 38 && status == 3000 && trace != null) trace.CaptureOutputEvidence();
            }
        }

        private static bool IsErrorOperation(int operation)
        {
            return operation == 1 || operation == 13 || operation == 15 || operation == 35 || operation == 39 || operation == 41;
        }

        private void DrainLastErrors(int callbackOperation, int callbackStatus)
        {
            if (instance == null) return;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    var message = string.Empty;
                    var number = string.Empty;
                    var detail = string.Empty;
                    var result = instance.GetAndClearLastError((int)INT_IID_000.INT_IID_ITLAMain, ref message, ref number);
                    var values = new Dictionary<string, string>
                    {
                        { "callbackOperation", callbackOperation.ToString(CultureInfo.InvariantCulture) },
                        { "callbackStatus", callbackStatus.ToString(CultureInfo.InvariantCulture) },
                        { "attempt", attempt.ToString(CultureInfo.InvariantCulture) },
                        { "returnCode", result.ToString(CultureInfo.InvariantCulture) },
                        { "message", message ?? string.Empty }, { "number", number ?? string.Empty }, { "detail", detail ?? string.Empty }
                    };
                    Log("native error: " + values["message"] + "; number=" + values["number"] + "; detail=" + values["detail"]);
                    Trace("native-error", values);
                    if (result != 25) break; // EC_PreviousError signals a queued error behind this one.
                }
                catch (Exception exception)
                {
                    Trace("native-error-drain-failed", new Dictionary<string, string> { { "exception", exception.GetType().FullName + ": " + exception.Message } });
                    break;
                }
            }
        }

        private IDictionary<string, string> SnapshotMetadata(string label)
        {
            var values = new Dictionary<string, string> { { "label", label }, { "state", state } };
            if (instance == null) return values;
            try
            {
                int scannerType = 0, scannerHardwareVersion = 0, darkPointMinutes = 0, colorPortraitMode = 0, packetTimeout = 0, noFilmTimeout = 0, lampSaverSeconds = 0, scannerSerial = 0;
                string rom = string.Empty, model = string.Empty, tlaVersion = string.Empty, tlxVersion = string.Empty;
                instance.GetScannerInfo000(ref scannerType, ref rom, ref model, ref scannerSerial, ref scannerHardwareVersion, ref tlaVersion, ref darkPointMinutes, ref colorPortraitMode, ref packetTimeout, ref noFilmTimeout, ref lampSaverSeconds, ref tlxVersion);
                values["scannerType"] = scannerType.ToString(CultureInfo.InvariantCulture); values["scannerModel"] = model ?? string.Empty; values["scannerSerial"] = scannerSerial.ToString(CultureInfo.InvariantCulture); values["romVersion"] = rom ?? string.Empty; values["tlxVersion"] = tlxVersion ?? string.Empty;
            }
            catch (Exception exception) { values["scannerInfo.error"] = exception.GetType().Name + ": " + exception.Message; }
            try
            {
                int scanStrips = 0, scanPictures = 0, scanWarnings = 0;
                instance.GetPictureCountScanGroup(0, ref scanStrips, ref scanPictures, ref scanWarnings);
                values["scanGroup.strips"] = scanStrips.ToString(CultureInfo.InvariantCulture); values["scanGroup.pictures"] = scanPictures.ToString(CultureInfo.InvariantCulture); values["scanGroup.warnings"] = scanWarnings.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception exception) { values["scanGroup.error"] = exception.GetType().Name + ": " + exception.Message; }
            try
            {
                int saveRolls = 0, saveStrips = 0, savePictures = 0, saveSelected = 0, saveHidden = 0;
                instance.GetPictureCountSaveGroup(ref saveRolls, ref saveStrips, ref savePictures, ref saveSelected, ref saveHidden);
                values["saveGroup.rolls"] = saveRolls.ToString(CultureInfo.InvariantCulture); values["saveGroup.strips"] = saveStrips.ToString(CultureInfo.InvariantCulture); values["saveGroup.pictures"] = savePictures.ToString(CultureInfo.InvariantCulture); values["saveGroup.selected"] = saveSelected.ToString(CultureInfo.InvariantCulture); values["saveGroup.hidden"] = saveHidden.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception exception) { values["saveGroup.error"] = exception.GetType().Name + ": " + exception.Message; }
            return values;
        }

        private void Trace(string eventName, IDictionary<string, string> values)
        {
            if (trace != null) trace.Write(eventName, state, values);
        }

        private static void Log(string message)
        {
            Console.WriteLine("[{0:O}] TLX {1}", DateTime.UtcNow, message);
        }

        private void CloseCore()
        {
            if (instance != null)
            {
                try { instance.ScanCancel(); } catch { }
                try { instance.SaveCancel(); } catch { }
            }
            if (instance != null && callbackCookie != 0) try { instance.CBUnadvise(callbackCookie); } catch (COMException) { }
            callbackCookie = 0; callback = null;
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            instance = null; state = "Closed";
            CleanupOwnedPfsBuffers();
        }

        private void CleanupOwnedPfsBuffers()
        {
            var before = pfsBuffersAtOpen; pfsBuffersAtOpen = null;
            if (before == null) return;
            foreach (var current in CapturePfsBuffers())
            {
                PfsBufferState original;
                if (before.TryGetValue(current.Key, out original) && original.Length == current.Value.Length && original.LastWriteUtcTicks == current.Value.LastWriteUtcTicks) continue;
                try
                {
                    File.Delete(current.Key);
                    Log("deleted bridge-owned PFS staging buffer: " + current.Key);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Could not delete bridge-owned PFS staging buffer '" + current.Key + "': " + exception.Message);
                }
            }
        }

        private static Dictionary<string, PfsBufferState> CapturePfsBuffers()
        {
            const string buffersDirectory = @"C:\Buffers";
            var result = new Dictionary<string, PfsBufferState>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(buffersDirectory)) return result;
            foreach (var path in Directory.GetFiles(buffersDirectory, "PFS*.bin", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path); result[path] = new PfsBufferState { Length = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
            }
            return result;
        }

        private sealed class PfsBufferState { public long Length; public long LastWriteUtcTicks; }

        private void RequireClosed() { if (instance != null) throw new InvalidOperationException("A TLX session is already open. Close it before initializing again."); }
        private void RequireOpen() { if (instance == null) throw new InvalidOperationException("No TLX session is open."); }
        private void RequireReady() { RequireOpen(); if (state != "Ready") throw new InvalidOperationException("TLX is not ready; query get-tlx-session-status until state is Ready."); }
        private static int GetRequiredInt32(IDictionary<string, string> arguments, string key) { if (arguments == null || !arguments.ContainsKey(key)) throw new ArgumentException("Bridge argument '" + key + "' is required."); return GetInt32(arguments, key, 0); }
        private static int GetInt32(IDictionary<string, string> arguments, string key, int fallback) { string raw; int value; if (arguments == null || !arguments.TryGetValue(key, out raw) || string.IsNullOrWhiteSpace(raw)) return fallback; if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new ArgumentException("Bridge argument '" + key + "' must be a signed 32-bit integer."); return value; }
        private static string GetString(IDictionary<string, string> arguments, string key) { string value; return arguments != null && arguments.TryGetValue(key, out value) ? value : string.Empty; }

        private static void PrepareNativeRuntime(string overrideDirectory)
        {
            var directory = string.IsNullOrWhiteSpace(overrideDirectory) ? ReadRegisteredTlxDirectory() : Path.GetFullPath(overrideDirectory);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) throw new DirectoryNotFoundException("Could not find the Pakon F-X35 COM SERVER directory. Supply comServerDirectory when initializing the TLX session.");
            if (!SetDllDirectory(directory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetDllDirectory failed for '" + directory + "'.");
            Environment.CurrentDirectory = directory;
        }

        private static string ReadRegisteredTlxDirectory()
        {
            const string path = @"CLSID\{EA82986B-E47C-4C0F-97EA-FB50ED216D2E}\InprocServer32";
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
            using (var key = baseKey.OpenSubKey(path))
            {
                var value = key == null ? null : key.GetValue(null) as string;
                return string.IsNullOrEmpty(value) ? null : Path.GetDirectoryName(value);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)] private sealed class TlxProgressCallback : StandardOleMarshalObject, ICallBackClient
        {
            private readonly TlxSession owner; private readonly object callbackSync = new object(); private int count; private int lastOperation; private int lastStatus;
            public TlxProgressCallback(TlxSession owner) { this.owner = owner; }
            public int Count { get { lock (callbackSync) return count; } } public int LastOperation { get { lock (callbackSync) return lastOperation; } } public int LastStatus { get { lock (callbackSync) return lastStatus; } }
            public void Awake(int operation, int status) { lock (callbackSync) { count++; lastOperation = operation; lastStatus = status; } owner.OnCallback(operation, status); }
        }
    }
}
