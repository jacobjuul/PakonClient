# Pakon low-level reference

This document records the native interfaces beneath TLX: COM identities,
driver endpoints, packet envelopes, memory layouts, PFS staging, and the
PakonImau host boundary. It is intended for a developer who already
understands the software overview in [tlx.md](tlx.md) and needs the supporting
evidence for a specific implementation question.

It is deliberately precise. Native function labels, offsets, GUIDs, packet
bytes, and registry paths are evidence, not public API names. The colour
pipeline is explained in [tlx-colour.md](tlx-colour.md).

## Reading guide

| Question | Start with |
| --- | --- |
| Direct TLC ABI and initialization | **Direct TLC initialization**, **TLC `InitializeScanner` execution path**, **scanner-information dispatch boundary** |
| Driver endpoint and packet transport | **Driver endpoint opening**, **backend packet comparison**, **normal-startup packet family**, **PPB interrupt status** |
| Colour/Ansel ABI evidence | **PakonImau colour entry points** through **PakonImau dynamic-host ABI** |
| TLX backend selection and raw staging | **TLX facade and TLA/TLB/TLC implementation selection** and **PFS / driver pipeline addresses** |

This document does not attempt to narrate a scan from first principles; use
`tlx.md` for that context before relying on an isolated offset or packet.

## Backend scope

The observed F-135 facade path creates **TLA**. This document also contains
TLC and TLB evidence because those binaries preserve comparable interfaces and
often make a shared native contract easier to identify. Such evidence is
labelled with its source backend and is not automatically an F-135 fact.

For an F-135 implementation, rely on TLA evidence or on behaviour explicitly
confirmed as shared. Treat a packet or state transition found only in TLC or
TLB as an unverified comparison, not an instruction to send it to the scanner.

## Direct TLC initialization and callback ABI (type-library evidence)

TLC's registered `TLAMain` class has CLSID
`{6449DE65-60A9-4A45-A3A1-337F5E6B41E0}`. Its `ITLAMain` interface is
`{2E37F1D3-50D0-4345-8C0D-4481AFF29EB9}` and exposes dispatch member 1:

```text
InitializeScanner(int initializationFlags,
                  int memoryTimeoutMilliseconds,
                  int sharedMemoryBytes)
```

This differs from public TLX's two-argument facade. Callback registration is
instead `ILongOpsCB` (`{E4724BBF-4C27-4AB3-92A8-CD2D45B87682}`), an IUnknown
interface with `int CBAdvise(ICallBackClient)`, `CBUnadvise(int)`, and
`CBHelperNTS(int, int)`. `ICallBackClient`
(`{31E3E438-9AAD-408A-81FA-BBCA917907D2}`) receives
`Awake(int operation, int status)`. These GUIDs and signatures were recovered
from TLC.dll's own type library with TlbImp, not inferred from TLX.

**Important boundary, confirmed against the installed TLX interop assembly.**
TLC's callback interface is not interchangeable with TLX's despite the shared
`Awake(int operation, int status)` method shape. Public TLX uses
`TLXLib.ICallBackClient` with IID `{1A2F6DDF-AAD8-40FB-BAAB-4FEE015ADCD5}`.
Trying to pass TLC's `{31E3E438-9AAD-408A-81FA-BBCA917907D2}` callback
interface to `TLXMain.CBAdvise` is invalid. Conversely, TLC's `ILongOpsCB`
IID is not implemented by `TLXMain`. A bridge must bind each COM facade through
its own type-library declarations; shared method names do not establish COM
interface compatibility.

The new x86 bridge implements these minimal declarations locally, holds the
callback sink strongly for the TLC session lifetime, and releases it only after
`CBUnadvise`. The .NET 10 process sees only named-pipe messages and raw
operation/status values.

### Callback queue semantics (TLA static evidence)

TLA does not invoke client callbacks directly from scan/disk workers.
`TLA!FUN_10038b40` validates an operation number in the inclusive range
`0..42`, allocates a small `(operation, status)` work item, appends it under a
critical section, and signals `m_hEventCallBackClient`. A dedicated callback
worker drains that queue and ultimately invokes the registered client sink.

Callback ordering is therefore serialized by a native queue while producers
remain asynchronous. The dispatcher preserves `status` unchanged; its meaning
depends on the operation family. The managed bridge should preserve raw pairs,
serialize delivery, and only then project known operations into typed events.

## TLC `InitializeScanner` execution path (static-code evidence)

**Confirmed, TLC.dll build currently installed.** The actual implementation of
the three-argument TLC `ITLAMain.InitializeScanner` call is
`TLC!FUN_1004a7a0`. It is asynchronous; COM success means that TLC accepted
the request and started initialization, not that a scanner is ready.

The method records the supplied values in the embedded
`CN_CiThreadDataInitializeScanner` state and creates a suspended native worker
at `TLC!FUN_1003d930`, which it then resumes. The recovered field roles are:

| State offset | Source argument | Established role |
| --- | --- | --- |
| `+0x08` | `initializationFlags` | Forwarded to the scan-driver initializer. Its only recovered TLC read is `flags & 0x2` (firmware-update request). |
| `+0x0c` | `memoryTimeoutMilliseconds` | Required to be at least 1,000; passed to the client-memory/shared-memory setup helper. |
| `+0x10` | `sharedMemoryBytes` | Required to be non-negative; zero skips mapping creation, non-zero creates the mapping. |

Before it starts the worker, TLC refuses a concurrent/previously active
initialization state, validates the timeout and mapping size, marshals the
registered callback interface into a stream, and waits up to roughly three
seconds for the worker's startup result. This short wait is a startup handshake,
not the public memory timeout.

The worker:

1. calls `CoInitializeEx(..., COINIT_MULTITHREADED)`;
2. recovers the callback interface from the marshalled stream and reports its
   initial callback state;
3. when `sharedMemoryBytes != 0`, creates an event plus a pagefile-backed,
   GUID-named `CreateFileMapping`/`MapViewOfFile` region of that size;
4. creates the scanner/configuration and acquisition-side objects, including
   the scanner object that later owns packet events and the driver pipeline.

This directly explains the third argument that TLX hides: it is a request for a
TLC-managed shared-memory region, not an image-quality or scanner-control
setting. A new managed API should call it `sharedMemoryBytes` (or omit it until
managed capture uses it), rather than inherit an ambiguous legacy save name.

`INITIALIZE_CSharpClient` (`0x40000000`) is copied into the initialization
state and forwarded into the scan-driver initializer, but is not used there.
The only read of that initializer's recovered flag field is `flags & 0x2`,
which is the type-library's `INITIALIZE_FirmwareUpdate` bit. A full
instruction scan found no TLC test or forwarding of `0x40000000` itself.
Therefore this installed TLC build treats the C#-client bit as a no-op; it is a
legacy facade/compatibility label, not a direct-TLC scanner setting. Do not
carry it into the managed direct-TLC default.

`INITIALIZE_FirmwareUpdate` (`0x2`) is the sole recovered TLC consumer of this
control word. Firmware update is permanently out of scope for this project:
the bridge rejects that bit before making the COM call, and no managed probe or
future implementation may issue firmware-update commands. It may be examined
only through static analysis to ensure it remains excluded.

This file records native calling contracts, memory/layout facts, and analysis
addresses recovered from the Pakon binaries.  It deliberately keeps uncertain
names and hypotheses separate from the implementation guidance in `tlx.md`.

## Confidence convention

- **Confirmed:** directly observed in static code, exported signatures, or a
  local file/fixture.
- **Inferred:** strongly suggested by call order or data flow, but not yet
  validated with a scan trace.
- **Unknown:** do not implement from this note alone.

## Implementation boundary

The low-level evidence supports two separate implementation boundaries:

| Boundary | Established contract | Limit |
| --- | --- | --- |
| FX35 driver transport | `CreateFile`, `DeviceIoControl`, and overlapped bulk reads can be modelled once the relevant packet bytes and effects are confirmed. | Do not issue an active scanner command based only on a native label or offset. |
| PakonImau / Ansel processing | Export names, call sites, and portions of the host ABI are known. | The required host contexts, buffer ownership, and initialization are incomplete; direct managed calls are not supported. |

`tlx.dll`, TLA, TLB, and TLC remain useful behavioural references, but their
names and offsets must not become a new application's public API. Promote a
native concept only after its observable meaning and pipeline stage are clear.

## Driver endpoint opening (TLC + driver-source evidence)

**Confirmed.** Before TLC sends an initialization packet,
`TLC!FUN_10010390` calls `TLC!FUN_1000c730` to open the FX35 endpoint. That
function uses:

```text
CreateFileW("\\.\PakonX35",
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED)
```

It retains the handle and reports a TLC `CreateFile` error on failure. It does
not send an IOCTL or packet itself. The observed F135 backend is TLA and uses
`\\.\Pakon135`; endpoint names are case-insensitive on Windows.

The available FX35 driver source independently establishes the names at
`FX35USB/driver/FX35USB.cpp`: an F135 build publishes `PAKON135`, an F335
build publishes `PAKONX35`, and the F235 build publishes `LOOPBACK`. Its
`IRP_MJ_CREATE` handler only increments an internal open-handle counter and
completes successfully. Therefore opening a handle is safe for endpoint
discovery; scanner state first changes only at a later IOCTL/packet stage.

Short, read-only diagnostic probes may use a synchronous handle. Acquisition
uses `FILE_FLAG_OVERLAPPED`, matching the native bulk-read/ring path. Do not
interpret a successful `CreateFile` as scanner initialization or readiness.

## Backend packet comparison (static-code evidence)

TLC and TLB contain related packet wrappers and startup routines. They are
useful comparative evidence, but neither sequence identifies the observed
F135 facade path: that path creates TLA. The comparison proves:

| Item | TLC / X35 | TLB | Result |
| --- | --- | --- | --- |
| Device path string | `\\.\PakonX35` | `\\.\Pakon135` | Backend-specific endpoint evidence only. |
| Handle options | read/write, share read/write, `OPEN_EXISTING`, normal + overlapped | Same | Shared transport-open contract. |
| Packet transport | `IOCTL 0x222090`, 36-byte reply, overlapped completion, 2-second wait | Same | Shared packet envelope. |
| First post-open packet | `04 03 10 00 85` | `04 03 10 00 85` | Shared first setup command. |
| Subsequent startup | TLC follows with its type-`01` `10 02 03` query | TLB conditionally writes one byte to `10 8F`, with 100-ms waits and later status probes | Different native state machines; neither sequence is authorised for replay. |

TLB's extra sequence is now structurally classified, though its device effect
is still unknown. `TLB!FUN_10009ba0` builds a type-`02` one-byte write through
`FUN_10009ae0`, so its two observed forms are:

```text
02 04 10 01 8F 00
02 04 10 01 8F 01
```

The first form is issued when TLB state `+0xc4` is nonzero. The second is
conditionally issued when state `+0xbc` is negative; TLB then waits 100 ms,
issues a type-`04` query `04 03 28 00 00`, and may restore `10 8F` to zero.
It subsequently tries type-`04` status queries at addresses `44`, `46`, `24`,
and `26`; it accepts a type-`07` response as the affirmative result. These
are normal-startup state operations, **not** safe diagnostic commands.

Their state fields and device effects are not decoded. They are excluded from
diagnostic and direct-driver commands.

### First post-open initialization packet

**Confirmed bytes; unknown hardware effect.** Immediately after the successful
open, TLC's initialization routine calls its packet helper with the stack-built
request:

```text
04 03 10 00 85
```

The request builder establishes the framing: byte `0` is `04`, byte `1` is the
payload-derived length `03`, and the remaining bytes are `10 00 85`. It is
issued through the normal `IOCTL_PAKON_SEND_AND_RECEIVE_PACKET` wrapper with
the usual overlapped 36-byte response handling and retry/error machinery.

This is the first command after opening, but it is **not** classified as
read-only or safe. Its name and hardware effect have not been recovered; it
must not be sent by `Pakon.Transport` or a diagnostic probe.

### Recovered normal-startup packet family (TLC)

The initialization routine immediately follows `04 03 10 00 85` with a
type-`01` request `01 03 10 02 03`. Its helper returns a 16-bit result to the
caller and retries the request up to three times. If a response-status bit
`0x20` is set, TLC reissues `04 03 10 00 85`, waits 100 ms, and retries. The
meaning of selector `10 02 03` and the returned value remain unknown, so both
packets are prohibited from managed probes.

Later in the same TLC initialization routine, static calls establish these
additional packet *families* but not yet their safe effects:

| Call form | Constructed request family | Static role |
| --- | --- | --- |
| `FUN_1000ddd0(..., 0x34, 0x9a, ...)` | `04 03 34 00 9A` | Fixed setup command. |
| `FUN_1000df30(..., 0x34, 0x98, 0x0c, ...)` | Type `02`, address `34`, selector `98`, one-byte payload `0C` | Conditional setup write. |
| `FUN_1000e1d0(..., 0x38, 0x0f, ...)` | `02 04 38 01 0B 0F` before an associated read | Firmware/software information exchange. It is analysis-only and must never be confused with the permanently prohibited firmware-update path. |
| `FUN_1000d7c0(..., 0x38, 0x95, ..., 7, ...)` | Type `01`, address `38`, length `07`, followed by a seven-byte response copy | Reads an initialization information block. |

These are a static packet inventory, not authorization to transmit them.

## TLC scanner-information dispatch boundary

The legacy public `TLXLib.IScanPictures` interface is **not** implemented
directly by TLC. Its IID is `{AC3017C5-F047-4F45-BE66-3F7F5E4D3114}`; a direct
`QueryInterface` on `TLC.TLAMain.1` correctly returns `E_NOINTERFACE` for it.
This is further proof that `tlx.dll` is a COM facade/translator rather than a
pass-through object.

TLC instead exposes its own `IScanPictures` dispatch interface:

| Item | Value |
| --- | --- |
| TLC `IScanPictures` IID | `{176CC928-A815-42EB-A20F-C1656477CA03}` |
| `GetScannerInfo000` dispatch ID | `15` |
| Arguments | 12 by-reference outputs: scanner type, ROM version, model, serial number, hardware version, TLA version, dark-point interval, portrait-mode value, scan-packet timeout, no-film timeout, lamp-saver seconds, TLX version. |

The direct TLC query succeeds for this internal interface without invoking a
scanner method. Its `IDispatch::Invoke` entry is `TLC+0x50a10`; that routine
forwards to an internal generic ATL-style dispatch object/map rather than
containing the getter's logic. Therefore we have **not** established that
`GetScannerInfo000` sends a driver packet, nor that it is a current hardware
status query. It may simply expose state/configuration loaded during scanner
initialization.

Do not derive a direct-driver `ReadScannerIdentity` operation from this API.
The dispatch method's relationship to a hardware request is unconfirmed.

## Recovered direct-driver status query: PPB interrupt status

`TLC!FUN_1000c800` is the FX35 packet wrapper:
it calls `IOCTL_PAKON_SEND_AND_RECEIVE_PACKET` (`0x222090`) with an input size
of `packet[1] + 2`, a fixed 36-byte response buffer, and a two-second wait. It
accepts response types `1`, `3`, and `7`; types `1` and `3` must equal the
request type.

`TLC!FUN_1000d040`, called by TLC's hardware polling flow, sends this fixed
packet and consumes its response:

| Field | Value |
| --- | --- |
| Request | `03 01 10` |
| Meaning | PPB interrupt-status query; recovered from the caller's diagnostic text `bDrvGetPpbInterruptStatus`. |
| Expected response shape | `03 03 10 SS CC` (`SS` is the status byte; `CC` is an as-yet unnamed trailing protocol byte/check value). |
| Status location | Response byte `3` (`SS`). |
| Observed .NET 10 direct-driver response | `03 03 10 00 AA`, with `DeviceIoControl` reporting success but `bytesReturned = 0`. |

The query is read-only in TLC: the surrounding code only ORs bits from `SS`
into a host status accumulator and uses them to decide whether later polling
is required. It is now exposed only as the explicit
`--read-ppb-interrupt-status` diagnostic in `Pakon.Transport.Cli`; it is not
part of automatic endpoint detection. The managed result retains the full
36-byte buffer because the installed driver returns a zero byte count even
when the buffer contains the valid packet above.

### Boundary with TLX hardware callbacks

The one-byte PPB interrupt query above must not be confused with TLX callback
operation `12`/`13`'s 32-bit hardware-status word. Observed transport-sensor
values include `0x40000000`, `0xC0000000`, and `0x80000000`; TLX can report
`EC_HardwareFault (135)` from `FN_uiDriverPollPPB` with `0xE0000000`. This
proves the backend poller accumulates/derives higher-level state beyond the
single `SS` byte exposed by packet `03 01 10`. Do not infer a direct bit-for-bit
managed mapping from that read-only packet.

### Observed PFS I/O behaviour

Passive FileIO observation corroborates the static result that `PFS00.bin` is
a reusable staging store with later consumers, not an append-only raw capture
file. The ETW record has file operation sizes and timing only; it does not
expose PFS offsets, data bytes, driver packets, or per-frame ownership.

## Colour and Ansel evidence

### PakonImau colour entry points

Confirmed exported functions and TLA call order:

| Export | Native role recovered so far |
| --- | --- |
| `PIColorCorrectColNegPlanarScan` | Colour-negative correction used on the scan-side path. |
| `PIColorCorrectColNegPlanarSave` | Colour-negative correction used by the renderer/save path.  TLA supplies planar pixels, width, height, a matrix-derived save context, and a prepared native lookup-table context. |
| `PIColorCorrectColRevPlanar` | Colour-reversal/positive planar correction. |
| `PIColorAdjustPlanar` | Post-correction adjustment profile: adjustment LUTs, contrast, sharpening, saturation and B&W effect profiles, then an ICC-related final stage. |
| `PIAnselStartNewRoll`, `PIAnselAddScene`, `PIAnselEndRoll`, `PIAnselAnalyzeRoll`, `PIAnselAnalyzeScene`, `PIAnselColorSceneBalancePlanar` | Stateful roll/scene analysis and subsequent planar scene-balance operation. |

The exact C ABI types and context layouts are still **unknown**.  Do not call
these exports from managed code without reproducing the owning TLA host and its
initialized context objects.

The two negative entry points have the same five public arguments but dispatch
to different internal routines (`FUN_1001ca10` for save and `FUN_1001c470` for
scan). Both wrappers return zero regardless of the internal operation, so the
return value is not a usable success indicator. This confirms that scan-side
and renderer-side negative correction are distinct recipes even though they
share the same external shape.

`PIColorCorrectColRevPlanar` constructs a generic in-memory three-channel
operation and copies `width * height * 3` 16-bit values back to the caller's
planar buffer. It is a host-side transform, not an FX35 driver command.

`PIColorAdjustPlanar` builds three 4,096-entry signed channel LUTs before it
creates its operations. Recovered operation names include
`ImaContrastLutOperation`, `ImaUnsharpMaskOperation`, and
`ImaICCEffectOperation_profileCombined`. This is direct evidence for the
post-correction order documented in `tlx.md`.

### Ansel: recovered contract and behaviour

`Ansel` is PakonImau's name for the stateful **roll/scene colour-balancing
engine**.  It is not an individual colour LUT and it is not a scanner-firmware
feature.

**Confirmed sequence:**

1. `PIAnselStartNewRoll(context, processingPath)` creates a roll session and
   chooses one of the processing-path names below.
2. `PIAnselAddScene(context, scene, metadata)` registers each scene.  The
   scene record includes source type and metric, and for colour-negative paths
   includes `dmin` (the film-base/minimum-density measurement).  When known,
   film `productCode` and `genCode` are also supplied.
3. `PIAnselEndRoll(context, roll)` finalizes the registered scene set.
4. `PIAnselAnalyzeRoll(context)` obtains/analyzes the roll transform state.
   `PIAnselAnalyzeScene` is exported but is a no-op in this installed binary.
5. `PIAnselColorSceneBalancePlanar(context, scene, planar)` constructs an
   in-memory transform operation from the selected scene's transform group and
   applies it to the planar image.

The operation creates an `ImaMemorySourceOperation`, obtains a
`transformGroupPtr`, and applies the resulting transform to the three-channel
planar source.  Therefore scene balance is a pixel transform, not merely a
metadata tag or a choice between the text LUT files.

**Recovered processing-path values in `PIAnselStartNewRoll`:**

| Numeric path value | PakonImau name |
| --- | --- |
| `0` | `DC-Premium` |
| `1`, `2` | `CN-Enhanced` |
| `3` | `CN-Lockbeam` |
| `4` | `CP-Balance` |

For the colour-negative cases (`1`, `2`, `3`), `AddScene` registers
`sourceType = 1`, `metric = 1`, then adds `dmin`, optional `productCode`, and
optional `genCode`.  `DC-Premium` uses source type 5 / metric 3; `CP-Balance`
uses source type 3 / metric 3.  The physical meaning of those numeric
source/metric enums remains **unknown**.

`PIAnselAnalyzeScene` currently forwards to a stub which returns zero.  The
supported workflow is consequently roll-based analysis followed by the
per-scene planar operation; a replacement should not rely on standalone
per-scene analysis being useful.

### Dormant Ansel diagnostic output

**Confirmed from static code:** `PIAnselAnalyzeRoll` checks the DWORD registry
value `HKLM\SOFTWARE\Pakon\PakonIma\Exlax`. PakonImau is a 32-bit DLL, so on
64-bit Windows this resolves to the **32-bit registry view**:
`HKLM\SOFTWARE\WOW6432Node\Pakon\PakonIma\Exlax`. A value set only in the
ordinary 64-bit `HKLM\SOFTWARE\Pakon\PakonIma` key will not be seen by the
DLL. When the 32-bit value is non-zero, it calls a diagnostic helper that
opens `C:\test.txt` and asks every registered scene to serialize itself to
that stream through a virtual method. The exact text schema is not yet
recovered, but this is the strongest available route to observe Ansel's
per-scene internal state/selected transform without changing the colour math.

The diagnostic helper does not check the result of opening `C:\test.txt`.
On current Windows installs, a normal non-elevated process commonly cannot
create files directly in the root of `C:`; in that case the diagnostic fails
silently even when `Exlax` is set correctly. Run the COM client elevated for
this one diagnostic scan, or pre-create `C:\test.txt` with write permission
for the client account.

### Observed Ansel diagnostic output

**Confirmed runtime observations from an attached six-scene C-41 diagnostic:**

- `CN-Enhanced`; `sourceType = 1`, `metric = 1`; `filmIso = 0` for all six
  scenes.
- Analysis input is `StandardAnalysisImage`, 250 by 375, three band,
  band-interleaved, single 16-bit packing with 12 bits per pixel.
- Each scene records a five-element `classification` result and RGB `shifts`.
  The shifts vary per scene even while D-min and the selected generic film LUT
  remain constant.  This is direct runtime evidence of content-sensitive
  scene balancing.
- The selected film LUT is
  `filmLut-scanner-prod-gen-default-default-default.lut`; the selected FUGC
  LUT is `NoShift_fugc-generic0225.lut`; contrast is `2.25` using
  `ansel-contrast-CNEnhanced` with a 4,096-entry curve and fixed index 1550.
- These keys resolve directly to readable installed configuration files:
  `contrast\contrast.map` maps `CN-Enhanced` to
  `contrast-CNEnhanced.dpi`; `fugc\fugc-rgb-lutMap.map` maps contrast `2.25`
  to `NoShift_fugc-generic0225.lut`. The runtime trace confirms those maps are
  actually used, rather than merely being dormant installation assets.
- SCP LUT diagnostics show `m_useSCPLut = 1`, but the resulting per-channel
  slope is 1 and offset is 0 in this example. The subsequent SBA stage emits
  the variable RGB shifts, so the useful dynamic balance in this trace occurs
  after the default SCP LUT stage.
- The active graph includes film LUT, contrast, scene balance (`sba` and
  `afterSCPLutSba`), flesh, DEI decision tree, FUGC, falloff, noise table,
  NRA, and SCP LUT capabilities.

The semantics/scales of the five `classification` numbers and RGB `shifts`
are still **unknown**. Treat them as observable regression targets, not as
direct RGB offsets in a replacement.

### Partial Ansel scene-descriptor layout

`PIAnselAddScene(context, scene, descriptor)` and
`PIAnselColorSceneBalancePlanar(context, scene, descriptor)` consume the same
descriptor object. The following offsets are **confirmed by both call paths**:

| Offset | Observed use |
| ---: | --- |
| `+0x00` | Image-source pointer consumed by the planar balance path. |
| `+0x04`, `+0x08`, `+0x0c` | Three additional image-source fields passed to the in-memory planar operation; their exact stride/layout/width/height assignment is still unknown. |
| `+0x48` | Ansel processing-path enum (`0` DC-Premium, `1`/`2` CN-Enhanced, `3` CN-Lockbeam, `4` CP-Balance). |
| `+0x54`, `+0x58`, `+0x5c` | Three signed 16-bit D-min values for colour-negative paths. |
| `+0x60` | Optional 32-bit film product code (`-1` means absent). |
| `+0x64` | Optional 32-bit film generation/specifier code (`-1` means absent). |

The descriptor is at least `0x68` bytes. Do not construct one yet: the fields
before `+0x48`, ownership rules, and `scene` handle lifetime still need a
runtime trace or deeper decompilation. It does, however, explain exactly how
the diagnostic's D-min and absent ISO/product metadata enter Ansel.

### Renderer scene-balance invocation site (TLA and FX35 TLC)

`TLA!FUN_1002caa0` is the recovered indirect caller of the PakonImau host's
`+0x64` Ansel slot. In the branch guarded by renderer-state bit `0x20`, it:

1. allocates a new native `0x60`-byte image-buffer object matching the source
   buffer's dimensions;
2. prepares source/destination image fields from the buffer object (including
   its `+0x0b`, `+0x0c`, and `+0x16` fields);
3. invokes the Ansel slot; and
4. releases the old buffer and continues downstream rendering using the new
   buffer on success.

This proves `UseColorSceneBalance` is a host-side **out-of-place planar image
transform** in the renderer branch. It is not a driver command and does not
alter PFS staging. The exact mapping of the temporary stack fields to the
PakonImau ABI remains unknown because this x86 indirect call is not recovered
cleanly by the decompiler; do not infer the image-buffer layout from these
offsets yet.

TLC contains the same branch:
`TLC!FUN_1002f2a0` calls its PakonImau host at `+0x64` when the same renderer
state bit `0x20` is set. Its surrounding flow, temporary `0x60`-byte buffer,
source/destination field preparation, replacement-on-success behaviour, and
post-balance rotation path all match the TLA routine. This corroborates the
host-side renderer architecture; F135-specific backend selection remains TLA.

#### TLA temporary image-buffer object used by scene balance

The renderer allocates a `0x60`-byte native buffer object before the Ansel
call. Its construction/initialization establishes these fields:

| Offset | Confirmed meaning |
| ---: | --- |
| `+0x2c` | Image width. |
| `+0x30` | Image height. |
| `+0x10` / `+0x14` | Right/bottom bounds, initialized to width/height minus one. |
| `+0x20` / `+0x24` | Duplicate full-image right/bottom bounds. |
| `+0x28` | Allocated backing byte count. |
| `+0x38` / `+0x3c` | Internal format/type selectors. |
| `+0x58` | Primary allocated pixel backing pointer for the normal renderer
  buffer type. |

The scene-balance branch obtains the source backing pointer from `+0x58`,
allocates an equal-sized destination object/backing store, then calls
PakonImau. This buffer object is **not necessarily identical** to the public
Ansel scene descriptor: TLA prepares temporary stack fields before the
indirect call, so it likely adapts the buffer plus the scene metadata into the
descriptor consumed by `PIAnselColorSceneBalancePlanar`.

### TLA colour-host layout (partial)

**Confirmed:** TLA holds two 64 KiB native buffers at host offsets `+0x40` and
`+0x44`.  The colour-negative save call takes the pointer held at `+0x40` as
its fifth argument.  The fourth argument is the matrix-derived save context at
`+0x48`; the equivalent scan context is held at `+0xd8`.

The installed two-column `ClientColNegLut.txt` is parsed into an intermediate
16,384-entry integer curve, but this raw text curve is **not** the fifth
argument to `PIColorCorrectColNegPlanarSave`.  TLA performs subsequent native
lookup/log preparation before the call.

### PakonImau dynamic-host ABI (TLA and FX35 TLC)

TLA loads `PakonImau.dll` dynamically and resolves every required export with
`GetProcAddress`; initialization fails if **any** expected export is missing.
The actual FX35 backend TLC has the same all-or-nothing resolver
(`TLC!FUN_1001a760`) with the same function-pointer offsets. The recovered
host layout is therefore confirmed for both modules:

| Host offset | Export |
| ---: | --- |
| `+0x24` / `+0x28` | `PIEnd` / `PIBegin` |
| `+0x2c`..`+0x40` | file save/spec/open, colour-adjust, rotate, scale/rotate |
| `+0x44` / `+0x48` | negative scan correction / negative save correction |
| `+0x4c` | reversal correction |
| `+0x50`..`+0x60` | Ansel start roll, add scene, end roll, analyze roll, analyze scene |
| `+0x64` | `PIAnselColorSceneBalancePlanar` |
| `+0x68` / `+0x6c` | Ansel delete roll / delete scene |

This is a useful compatibility boundary for a replacement shim or an
instrumented host: it establishes exactly which PakonImau features TLA
requires. It does **not** establish the parameter structs passed to each
function pointer.

## TLX facade and TLA/TLB/TLC implementation selection

The COM class used by this client, `TLXLib.TLXMainClass`, has CLSID
`{EA82986B-E47C-4C0F-97EA-FB50ED216D2E}` and is implemented by `tlx.dll`.
It is a **facade**, not the main scanning engine.  Its purpose is to select a
backend and proxy the public TLX interfaces to it.

The recovered backend probes are deliberately simple and safe to observe:

| Detected device | Backend COM class created by `tlx.dll` | Registered server | Evidence |
| --- | --- | --- | --- |
| `\\.\Pakon135` | `{52B5538B-7926-40AD-9DBE-810228E147AD}` (`TLAMain Class`) | `TLA.dll` | `tlx!FUN_10005bcc` opens the device with read/write access and shared read/write, immediately closes it, then calls `CoCreateInstance` for this CLSID. |
| `\\.\PakonX35` | `{6449DE65-60A9-4A45-A3A1-337F5E6B41E0}` (`TLC.TLAMain.1`) | `TLC.dll` | `tlx!FUN_10007aa4` uses the same probe/close/create pattern. |

Both backend-creation functions query the same family of interfaces after
creation (main, scan, save, calibration and related interfaces) and install a
small wrapper object. They do not issue scanner commands as part of the probe.
For the observed F135 endpoint, this establishes that a public
`TLXMainClass` call is handed to **TLA** after the endpoint probe. The X35
probe has a separate TLC hand-off.

`TLA.dll`, `TLB.dll`, and `TLC.dll` each export only the standard four COM
entry points (`DllCanUnloadNow`, `DllGetClassObject`, `DllRegisterServer`, and
`DllUnregisterServer`).  The scanner implementation is internal to their COM
objects. All three contain the same broad diagnostic vocabulary—driver packet
operations, calibration, PFS buffers, scan/save worker threads, correction,
and PakonImau—so the earlier assumption that TLB was merely a configuration
module was wrong. They are closely related backend builds, not separate
configuration-only libraries.

The recovered facade hand-off and native registration establish this model:

| Module | COM ProgID | Device/backend clue | Status |
| --- | --- | --- | --- |
| `TLA.dll` | `TLA.TLAMain.1` | Created by the recovered F135 facade hand-off; contains F135 acquisition and PFS code. | Confirmed backend for the observed F135 path. |
| `TLB.dll` | `TLB.TLAMain.1` | Contains `\\.\Pakon135` strings and a parallel packet implementation. | Related backend; selection condition not established. |
| `TLC.dll` | `TLC.TLAMain.1` | Contains `\\.\PakonX35` and a parallel packet implementation. | Related backend; selection condition not established for this hardware. |

The DLLs are not byte-identical (installed sizes: TLA 593,920 bytes, TLB
536,576 bytes, TLC 614,400 bytes), so implementation differences are
meaningful. F135 conclusions should be based on TLA or on an explicitly shared
contract, not inferred from a similarly named TLB/TLC routine.

## PFS / driver pipeline addresses (TLA.dll)

| Address | Finding |
| --- | --- |
| `FUN_10006320` | PFS stripe write primitive; validates block alignment and calls `WriteFile`. |
| `FUN_100065e0` | PFS stripe read primitive; seeks and calls `ReadFile`, in at most `0x100000` byte chunks. |
| `FUN_10007010` | Allocates a contiguous buffer then reads a requested PFS span into it. |
| `FUN_10037140` | `EventScanWriteToDisk` worker.  Drains completed driver-ring regions to a `CiBufferHiRes`/PFS backing store. |
| `FUN_1002fca0` | Coordinates overlapped driver read, scan-packet worker, and disk worker. |

The PFS path retains raw staged bytes; it contains no image decode, colour
conversion, or compression.

### Raw line staging shape

`TLA!FUN_100393a0` configures the scan-to-PFS worker and makes one important
fact explicit: it uses **three** 16-bit components per scan-line unit in the
normal path and **four** when its final boolean argument is set. The normal
scan setup supplies the scratch/IR state (`scanControl & 0x8`) as that final
argument; it supplies splice sensing (`scanControl & 0x4`) separately. The
resulting worker stores `componentCount * lineWidth` 16-bit values per raw line
and derives all byte counts as that value times two.

`TLA!FUN_10037140` (`EventScanWriteToDisk`) waits for ring readiness, obtains
the active `CiBufferHiRes` PFS backing object, and calls `FUN_10007160` to copy
whole configured raw-line chunks. It handles wrap-around by copying the tail
and head as separate chunks, advances its PFS logical position in 16-bit units,
and asks the PFS object to complete/rotate a strip when necessary. It does not
inspect component values, identify packet headers, unpack 12-bit samples, or
produce planar RGB.

```text
driver ring packets -> 16-bit raw-line chunks -> PFS strip storage
                                      ^
                         decoder/framing begins after this point (unrecovered)
```

The `3` versus `4` component count is a capture/storage shape, not proof that
all four components are conventional RGB+IR pixels at this point. The packet
framing, channel order, 12-bit packing, and frame/strip boundary rules remain
unrecovered and must not be guessed from this layout alone.

### PFS-to-planar deinterleaver

`FUN_100071d0` reads a selected raw span through
`FUN_10007010`/`FUN_100065e0`, then calls `FUN_10004d60` when the read
completes. `FUN_10004d60` is a line-oriented deinterleaver:

- It treats the PFS input as 16-bit sample units and copies every component
  sample into a distinct contiguous plane.
- The destination object contains three equal `width * height` planes at its
  base, `+planeBytes`, and `+2 * planeBytes`.
- When its component-count field is `4`, it creates/uses a fourth equal plane
  at `+3 * planeBytes`.
- Its final direction argument selects forward or reverse line placement, so
  vertical orientation is a decoder operation rather than an output-only DIB
  choice.

This recovers the first usable decoded representation:

```text
PFS span: [component 0, component 1, component 2 (, component 3)] per sample
  -> contiguous 16-bit plane 0, plane 1, plane 2 (, plane 3)
```

The code does not perform bit shifts, masking, or packed-sample reconstruction;
at this stage the data is already addressed as 16-bit units. It is therefore
accurate to call this a **component deinterleaver**, not yet a complete
sensor-to-linear-image decoder. The component colour assignment, numeric scale
(including whether only 12 significant bits are used), raw packet
synchronization, and frame/strip boundaries are still unresolved.

### Acquisition lifecycle and failure boundary

`TLA!FUN_1002fca0` coordinates the normal acquisition lifetime. It resets the
packet-ready and disk-writer events, starts two workers (the ring-packet worker
and `EventScanWriteToDisk`), starts one overlapped `ReadFile` over the ring
region, and waits for both workers to reach their startup handshakes. A normal
stop sequence is explicit and ordered:

```text
issue/await overlapped ring read
  -> cancel outstanding ReadFile
  -> set ring stop-transfer state and signal packet worker
  -> wait (up to 500 ms) for driver transfer-in-progress to clear
  -> signal disk writer to flush/complete PFS staging
  -> wait for both workers (up to 6 s each)
```

The routine treats ring overflow, `CancelIo`, event failures, unexpected
overlapped completion, worker-start failure, and worker-shutdown timeout as
acquisition failures. The recovered error values include driver ring overflow
(`1002`), wait timeout (`159`), and the expected Win32/ring specific errors.
This is sufficient to define a managed acquisition lifecycle with explicit
start, stop, flush, and timeout states; it is not yet sufficient to decode the
contents that were flushed.
