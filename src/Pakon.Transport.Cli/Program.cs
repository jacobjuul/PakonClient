using Pakon.Transport;
using Pakon.LegacyBridge.Client;

var endpoint = default(string);
var readInterruptStatus = false;
var probeLegacyBridge = false;
var initializeLegacyTlc = false;
var legacyTlcStatus = false;
var closeLegacyTlc = false;
var initializeTlxSession = false;
var tlxSessionStatus = false;
var closeTlxSession = false;
var beginTlxTraceDirectory = default(string);
var endTlxTrace = false;
var snapshotTlxTraceLabel = default(string);
var scanTlxTraceProfile = false;
var saveTlxTraceJpegs = false;
var runTlxTraceDirectory = default(string);
int[]? scanRollValues = null;
var moveOldestRollToSaveGroup = false;
var comServerDirectory = default(string);
var pipeName = "PakonLegacyBridge";
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--device" && index + 1 < args.Length)
    {
        endpoint = args[++index];
        continue;
    }

    if (args[index] == "--read-ppb-interrupt-status")
    {
        readInterruptStatus = true;
        continue;
    }

    if (args[index] == "--probe-legacy-bridge")
    {
        probeLegacyBridge = true;
        continue;
    }

    if (args[index] == "--initialize-legacy-tlc")
    {
        initializeLegacyTlc = true;
        continue;
    }

    if (args[index] == "--legacy-tlc-status")
    {
        legacyTlcStatus = true;
        continue;
    }

    if (args[index] == "--close-legacy-tlc")
    {
        closeLegacyTlc = true;
        continue;
    }

    if (args[index] == "--initialize-tlx-session")
    {
        initializeTlxSession = true;
        continue;
    }

    if (args[index] == "--tlx-session-status")
    {
        tlxSessionStatus = true;
        continue;
    }

    if (args[index] == "--close-tlx-session")
    {
        closeTlxSession = true;
        continue;
    }

    if (args[index] == "--begin-tlx-trace" && index + 1 < args.Length)
    {
        beginTlxTraceDirectory = args[++index];
        continue;
    }

    if (args[index] == "--end-tlx-trace")
    {
        endTlxTrace = true;
        continue;
    }

    if (args[index] == "--snapshot-tlx-trace" && index + 1 < args.Length)
    {
        snapshotTlxTraceLabel = args[++index];
        continue;
    }

    if (args[index] == "--scan-tlx-trace-profile")
    {
        scanTlxTraceProfile = true;
        continue;
    }

    if (args[index] == "--save-tlx-trace-jpegs")
    {
        saveTlxTraceJpegs = true;
        continue;
    }

    if (args[index] == "--run-tlx-trace" && index + 1 < args.Length)
    {
        runTlxTraceDirectory = args[++index];
        continue;
    }

    if (args[index] == "--scan-roll" && index + 5 < args.Length)
    {
        scanRollValues = [int.Parse(args[++index]), int.Parse(args[++index]), int.Parse(args[++index]), int.Parse(args[++index]), int.Parse(args[++index])];
        continue;
    }

    if (args[index] == "--move-oldest-roll-to-save-group")
    {
        moveOldestRollToSaveGroup = true;
        continue;
    }

    if (args[index] == "--com-server-dir" && index + 1 < args.Length)
    {
        comServerDirectory = args[++index];
        continue;
    }

    if (args[index] == "--pipe" && index + 1 < args.Length)
    {
        pipeName = args[++index];
        continue;
    }

    Console.Error.WriteLine("Usage: Pakon.Transport.Cli [--run-tlx-trace <directory>] [--device <path>] [--read-ppb-interrupt-status] [--probe-legacy-bridge] [--initialize-legacy-tlc] [--legacy-tlc-status] [--close-legacy-tlc] [--initialize-tlx-session] [--tlx-session-status] [--close-tlx-session] [--begin-tlx-trace <directory>] [--snapshot-tlx-trace <label>] [--scan-tlx-trace-profile] [--save-tlx-trace-jpegs] [--end-tlx-trace] [--scan-roll <resolution> <filmColor> <filmFormat> <stripMode> <scanControl>] [--move-oldest-roll-to-save-group] [--com-server-dir <path>] [--pipe <name>]");
    return 2;
}

if (runTlxTraceDirectory is not null)
{
    return await RunTlxTraceAsync(new LegacyBridgeClient(pipeName), runTlxTraceDirectory);
}

if (probeLegacyBridge)
{
    var response = await new LegacyBridgeClient(pipeName).ProbeTlcAsync();
    Console.WriteLine("Legacy bridge probe: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (initializeLegacyTlc)
{
    Console.WriteLine("Initializing a direct TLC session through the legacy bridge. This may access scanner hardware.");
    var response = await new LegacyBridgeClient(pipeName).InitializeTlcAsync();
    Console.WriteLine("Legacy TLC initialization accepted: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (legacyTlcStatus || closeLegacyTlc)
{
    var client = new LegacyBridgeClient(pipeName);
    var response = closeLegacyTlc
        ? await client.CloseTlcSessionAsync()
        : await client.GetTlcSessionStatusAsync();
    Console.WriteLine("Legacy TLC session: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (initializeTlxSession)
{
    Console.WriteLine("Initializing the proven TLX scan/save facade through the x86 bridge. This accesses scanner hardware.");
    var response = await new LegacyBridgeClient(pipeName).InitializeTlxSessionAsync(comServerDirectory);
    Console.WriteLine("TLX initialization accepted: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (beginTlxTraceDirectory is not null || endTlxTrace || snapshotTlxTraceLabel is not null)
{
    var client = new LegacyBridgeClient(pipeName);
    var response = endTlxTrace ? await client.EndTlxTraceAsync()
        : snapshotTlxTraceLabel is not null ? await client.SnapshotTlxTraceAsync(snapshotTlxTraceLabel)
        : await client.BeginTlxTraceAsync(beginTlxTraceDirectory!);
    Console.WriteLine("TLX trace: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (scanRollValues is not null || moveOldestRollToSaveGroup || scanTlxTraceProfile || saveTlxTraceJpegs)
{
    var client = new LegacyBridgeClient(pipeName);
    var response = saveTlxTraceJpegs ? await client.SaveTlxTraceJpegsAsync()
        : scanTlxTraceProfile ? await client.ScanTlxTraceProfileAsync()
        : scanRollValues is not null
        ? await client.ScanRollAsync(scanRollValues[0], scanRollValues[1], scanRollValues[2], scanRollValues[3], scanRollValues[4])
        : await client.MoveOldestRollToSaveGroupAsync();
    Console.WriteLine("TLX workflow operation: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

if (tlxSessionStatus || closeTlxSession)
{
    var client = new LegacyBridgeClient(pipeName);
    var response = closeTlxSession
        ? await client.CloseTlxSessionAsync()
        : await client.GetTlxSessionStatusAsync();
    Console.WriteLine("TLX session: succeeded={0}; error={1}", response.Succeeded, response.Error ?? "");
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    return response.Succeeded ? 0 : 1;
}

var endpoints = endpoint is null ? KnownPakonEndpoints.ProbeOrder : [endpoint];
Console.WriteLine("Read-only Pakon driver metadata probe (.NET {0}, {1}-bit process)", Environment.Version, IntPtr.Size * 8);
Console.WriteLine("Safety: only IOCTL_EZUSB_GET_DRIVER_VERSION (0x{0:X8}) is sent.", PakonDriverMetadataProbe.IoctlGetDriverVersion);
if (readInterruptStatus)
{
    Console.WriteLine("Explicit diagnostic: TLC's read-only PPB interrupt-status packet (03 01 10) will be sent.");
}

var anySuccess = false;
foreach (var candidate in endpoints)
{
    var result = PakonDriverMetadataProbe.Probe(candidate);
    if (!result.Opened)
    {
        Console.WriteLine("{0}: open failed: Win32 {1} ({2})", candidate, result.Win32Error, PakonDriverMetadataProbe.DescribeWin32Error(result.Win32Error!.Value));
        continue;
    }

    if (!result.IoctlSucceeded)
    {
        Console.WriteLine("{0}: metadata IOCTL failed: Win32 {1} ({2})", candidate, result.Win32Error, PakonDriverMetadataProbe.DescribeWin32Error(result.Win32Error!.Value));
        continue;
    }

    anySuccess = true;
    Console.WriteLine("{0}: metadata IOCTL succeeded; bytesReturned={1}; response=[{2}]", candidate, result.BytesReturned, Convert.ToHexString(result.ResponseBytes));

    if (readInterruptStatus)
    {
        var interrupt = PakonPpbInterruptStatusQuery.Read(candidate);
        if (!interrupt.IoctlSucceeded)
        {
            Console.WriteLine("{0}: PPB interrupt-status query failed: Win32 {1} ({2})", candidate, interrupt.Win32Error, PakonPpbInterruptStatusQuery.DescribeWin32Error(interrupt.Win32Error!.Value));
        }
        else
        {
            var decoded = interrupt.InterruptStatus is null ? "unavailable" : $"0x{interrupt.InterruptStatus:X2}";
            Console.WriteLine("{0}: PPB interrupt-status response; bytesReturned={1}; response=[{2}]; expectedShape={3}; decodedStatus={4}", candidate, interrupt.BytesReturned, Convert.ToHexString(interrupt.ResponseBytes), interrupt.HasExpectedPacketShape, decoded);
        }
    }
}

return anySuccess ? 0 : 1;

static async Task<int> RunTlxTraceAsync(LegacyBridgeClient client, string directory)
{
    var traceOpen = false;
    var sessionOpen = false;
    try
    {
        Console.WriteLine("Starting one-command TLX reference trace: {0}", Path.GetFullPath(directory));
        PrintResponse("begin trace", await client.BeginTlxTraceAsync(directory));
        traceOpen = true;

        PrintResponse("initialize", await client.InitializeTlxSessionAsync());
        sessionOpen = true;
        await WaitForReadyAsync(client, "initialization");
        PrintResponse("before-scan snapshot", await client.SnapshotTlxTraceAsync("before-scan"));

        Console.WriteLine();
        Console.WriteLine("Load the film strip now. Press Enter here only when it is ready to scan.");
        Console.ReadLine();

        PrintResponse("start scan", await client.ScanTlxTraceProfileAsync());
        await WaitForReadyAsync(client, "scan");
        PrintResponse("after-scan snapshot", await client.SnapshotTlxTraceAsync("after-scan"));
        PrintResponse("promote roll", await client.MoveOldestRollToSaveGroupAsync());
        PrintResponse("after-promotion snapshot", await client.SnapshotTlxTraceAsync("after-promotion"));
        PrintResponse("save JPEGs", await client.SaveTlxTraceJpegsAsync());
        await WaitForReadyAsync(client, "JPEG save");
        PrintResponse("after-JPEG snapshot", await client.SnapshotTlxTraceAsync("after-jpeg-save"));
        PrintResponse("close session", await client.CloseTlxSessionAsync());
        sessionOpen = false;
        PrintResponse("finish trace", await client.EndTlxTraceAsync());
        traceOpen = false;
        Console.WriteLine("Trace complete. JPEGs and evidence are in: {0}", Path.GetFullPath(directory));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("TLX trace failed: {0}: {1}", exception.GetType().Name, exception.Message);
        return 1;
    }
    finally
    {
        if (sessionOpen)
        {
            try { PrintResponse("close session", await client.CloseTlxSessionAsync()); } catch (Exception exception) { Console.Error.WriteLine("Could not close TLX session: " + exception.Message); }
        }
        if (traceOpen)
        {
            try { PrintResponse("finish trace", await client.EndTlxTraceAsync()); } catch (Exception exception) { Console.Error.WriteLine("Could not finish trace: " + exception.Message); }
        }
    }
}

static async Task WaitForReadyAsync(LegacyBridgeClient client, string phase)
{
    Console.WriteLine("Waiting for {0} to complete...", phase);
    while (true)
    {
        var response = await client.GetTlxSessionStatusAsync();
        if (!response.Succeeded) throw new InvalidOperationException(response.Error ?? "TLX status request failed.");
        string? state;
        response.Values.TryGetValue("state", out state);
        Console.WriteLine("  {0}: state={1}; callbacks={2}; last=({3}, {4})", phase, state ?? "unknown",
            GetValue(response, "callbackCount"), GetValue(response, "lastOperation"), GetValue(response, "lastStatus"));
        if (string.Equals(state, "Ready", StringComparison.Ordinal)) return;
        if (string.Equals(state, "Faulted", StringComparison.Ordinal) || string.Equals(state, "Closed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TLX entered state '" + state + "' during " + phase + ". Check the bridge console and trace events.");
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

static void PrintResponse(string action, Pakon.LegacyBridge.Protocol.BridgeResponse response)
{
    Console.WriteLine("{0}: succeeded={1}; error={2}", action, response.Succeeded, response.Error ?? string.Empty);
    foreach (var value in response.Values) Console.WriteLine("  {0}={1}", value.Key, value.Value);
    if (!response.Succeeded) throw new InvalidOperationException(response.Error ?? action + " failed.");
}

static string GetValue(Pakon.LegacyBridge.Protocol.BridgeResponse response, string key)
{
    string? value;
    return response.Values.TryGetValue(key, out value) ? value : "?";
}
