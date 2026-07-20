using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace Pakon.LegacyBridge
{
    /// <summary>Append-only diagnostic trace. It observes bridge activity and never hooks the driver.</summary>
    internal sealed class TlxTraceRecorder : IDisposable
    {
        private readonly object sync = new object();
        private readonly StreamWriter writer;

        public TlxTraceRecorder(string directory)
        {
            Directory.CreateDirectory(directory);
            DirectoryPath = Path.GetFullPath(directory);
            File.WriteAllText(Path.Combine(DirectoryPath, "manifest.json"),
                "{\"format\":\"pakon-tlx-trace-v2\",\"startedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"capture\":\"bridge operations, raw TLX callbacks, decoded native errors, metadata snapshots, and native runtime evidence\",\"baselineProfile\":\"base16-negative-35mm-full-roll-normal-framing\"}");
            writer = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, "events.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            Write("trace-started", "Closed", new Dictionary<string, string> { { "directory", DirectoryPath } });
        }

        public string DirectoryPath { get; private set; }

        public void Write(string eventName, string state, IDictionary<string, string> values)
        {
            var record = new TraceRecord
            {
                TimestampUtc = DateTime.UtcNow.ToString("o"), EventName = eventName, State = state,
                Values = values == null ? new Dictionary<string, string>() : new Dictionary<string, string>(values)
            };
            lock (sync)
            {
                var serializer = new DataContractJsonSerializer(typeof(TraceRecord));
                using (var memory = new MemoryStream())
                {
                    serializer.WriteObject(memory, record);
                    writer.WriteLine(Encoding.UTF8.GetString(memory.ToArray()));
                    writer.Flush();
                }
            }
        }

        public void Dispose()
        {
            lock (sync) writer.Dispose();
        }

        public void WriteMetadataSnapshot(string label, IDictionary<string, string> values)
        {
            var snapshots = Path.Combine(DirectoryPath, "metadata");
            Directory.CreateDirectory(snapshots);
            var safeLabel = string.Concat(label.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeLabel)) safeLabel = "snapshot";
            File.WriteAllText(Path.Combine(snapshots, safeLabel + ".json"), Serialize(values));
        }

        /// <summary>Copies small, observable runtime evidence; failures are recorded rather than hidden.</summary>
        public void CaptureRuntimeEvidence()
        {
            var evidence = Path.Combine(DirectoryPath, "runtime-evidence");
            Directory.CreateDirectory(evidence);
            var details = new Dictionary<string, string>
            {
                { "capturedUtc", DateTime.UtcNow.ToString("o") },
                { "processArchitecture", IntPtr.Size == 4 ? "x86" : "x64" },
                { "osVersion", Environment.OSVersion.VersionString },
                { "currentDirectory", Environment.CurrentDirectory }
            };
            CaptureRegistry(details);
            CaptureNativeLogs(details, evidence);
            File.WriteAllText(Path.Combine(evidence, "runtime.json"), Serialize(details));
            Write("runtime-evidence", "Tracing", details);
        }

        public void CaptureOutputEvidence()
        {
            var outputDirectory = Path.Combine(DirectoryPath, "output-jpeg");
            var values = new Dictionary<string, string>();
            if (!Directory.Exists(outputDirectory))
            {
                values["outputDirectory"] = "missing";
            }
            else
            {
                var files = Directory.GetFiles(outputDirectory, "*.jpg", SearchOption.TopDirectoryOnly);
                values["outputDirectory"] = outputDirectory;
                values["fileCount"] = files.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    values["file." + Path.GetFileName(file)] = info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            File.WriteAllText(Path.Combine(DirectoryPath, "output-jpeg-manifest.json"), Serialize(values));
            Write("output-evidence", "Ready", values);
        }

        private static string Serialize(IDictionary<string, string> values)
        {
            var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>));
            using (var memory = new MemoryStream())
            {
                serializer.WriteObject(memory, new Dictionary<string, string>(values));
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        private static void CaptureRegistry(IDictionary<string, string> values)
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Pakon\TLB\Scan"))
                {
                    if (key == null) { values["registry.TLB.Scan"] = "missing"; return; }
                    foreach (var name in key.GetValueNames()) values["registry.TLB.Scan." + name] = Convert.ToString(key.GetValue(name), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }
            catch (Exception exception) { values["registry.captureError"] = exception.GetType().Name + ": " + exception.Message; }
        }

        private static void CaptureNativeLogs(IDictionary<string, string> values, string evidenceDirectory)
        {
            var candidates = new[]
            {
                Path.Combine(Environment.CurrentDirectory, "PakonPpbDebugDx.txt"),
                Path.Combine(Environment.CurrentDirectory, "PakonPpbDebugDx1.txt"),
                Path.Combine(Environment.CurrentDirectory, "TLX.log")
            };
            foreach (var source in candidates)
            {
                try
                {
                    if (!File.Exists(source)) continue;
                    var target = Path.Combine(evidenceDirectory, Path.GetFileName(source));
                    File.Copy(source, target, true);
                    values["nativeLog." + Path.GetFileName(source)] = target;
                }
                catch (Exception exception) { values["nativeLogError." + Path.GetFileName(source)] = exception.GetType().Name + ": " + exception.Message; }
            }
        }

        [DataContract]
        private sealed class TraceRecord
        {
            [DataMember(Order = 1)] public string TimestampUtc { get; set; }
            [DataMember(Order = 2)] public string EventName { get; set; }
            [DataMember(Order = 3)] public string State { get; set; }
            [DataMember(Order = 4)] public Dictionary<string, string> Values { get; set; }
        }
    }
}
