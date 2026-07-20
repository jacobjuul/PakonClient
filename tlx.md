# TLX migration and implementation guide

This is the practical guide for replacing TLX/COM with a clear .NET 10
application. It records architecture decisions, supported semantics, safe
implementation boundaries, and the active migration plan. It deliberately
links to the evidence ledger rather than repeating function-address detail.

## Reading guide

| Need | Start here |
| --- | --- |
| Build the replacement architecture or choose the next implementation seam | This file: **Replacement inventory**, **scan controls**, **FX35 driver findings**, and **Target architecture**. |
| Work on colour, LUTs, Ansel, DX rules, or offline image processing | [tlx-colour.md](tlx-colour.md). |
| Verify a packet, COM ABI, native offset, registry path, or recovered function | [tlx-lowlevel.md](tlx-lowlevel.md). |
| Understand the original raw research narrative | The retained historical sections in this file; they are cross-referenced rather than deleted. |

## Documentation ownership

- **This file:** implementation choices and current conclusions.
- **`tlx-colour.md`:** colour pipeline, asset selection, Ansel, and future in-house colour port.
- **`tlx-lowlevel.md`:** static-analysis evidence, native ABI, packet bytes, and offsets.

The detailed colour sections below are retained as historical source material;
new colour work should be added to `tlx-colour.md` and low-level evidence to
`tlx-lowlevel.md`.

## Goal

Replace the legacy TLX COM server incrementally with code in PakonClient while
continuing to use the FX35 Windows driver.  The replacement needs to cover the
user-mode COM/server and scanner-protocol layers; it does **not** initially
need to replace the kernel driver.

## Components found

| Component | Location | Role currently understood |
| --- | --- | --- |
| .NET COM interop wrapper | `src/PakonLib/obj/Debug/Interop.TLXLib.dll` | Generated .NET declarations for the TLX COM API.  This contains signatures and enum values, not the scanner implementation. |
| TLX COM server | `C:\Program Files (x86)\Pakon\F-X35 COM SERVER\tlx.dll` | Native 32-bit COM entry point and orchestration layer. |
| TLX implementation modules | `TLA.dll`, `TLB.dll`, `TLC.dll` in the same directory | Three parallel native COM component implementations, each containing a full scanner/save pipeline. `tlx.dll` selects TLB for `\\.\Pakon135` and TLC for `\\.\PakonX35`; TLA is the loopback/simulator-oriented implementation. |
| Image-processing library | `PakonImau.dll` in the same directory | Dynamically loaded by the implementation modules for image-processing and correction stages. |
| Other supporting libraries | `AIDToolkit.dll`, `DMLDICELib.dll` | Supporting image/metadata libraries. |
| FX35 driver | `C:\Code\FX35` | Source is available.  It loads scanner firmware and exposes the scanner transport to user-mode code. |

The installed COM-server folder contains `tlx.dll` (294,912 bytes), `TLA.dll`,
`TLB.dll`, `TLC.dll`, `PakonImau.dll`, and the other libraries above.

## Replacement inventory and static-analysis plan

This is the authoritative work queue for the COM-removal project.  It prevents
us from extending the new application based on a method name alone.  A row is
ready to implement only when its **static-analysis exit** column is met; until
then, a bridge/probe may expose it only as a deliberately raw diagnostic.

| Capability | Current owner and call path | Driver / hardware involvement | What is established | Static-analysis exit | Replacement priority |
| --- | --- | --- | --- | --- | --- |
| Backend selection | `tlx.dll` probes a device then creates TLB/TLC | Opens then closes device only | `Pakon135 -> TLB`, `PakonX35 -> TLC`; TLA is loopback-oriented | Record any remaining selection/config fallback; do not reproduce COM selection logic | High — replace with explicit managed endpoint selection |
| COM API and callbacks | TLX facade or direct TLC `ITLAMain`/`ILongOpsCB` | None by itself | Direct TLC interfaces and callback ABI are recovered; direct TLC initialization has three arguments | Map public TLX methods to their direct backend interface/method for every capability below | High — bridge only during migration |
| Initialization | TLC `ITLAMain.InitializeScanner(flags, timeout, sharedMemoryBytes)` creates an async initialization worker | Opens/configures scanner and may load configuration/native modules | Signature, callback registration, and legacy defaults are known | Identify the worker-state layout; map every initialization flag and each configuration/driver stage | High — must be understood before replacing acquisition |
| Read-only status | TLC packet wrapper -> driver packet `03 01 10` | Yes, read-only `IOCTL 0x222090` | Exact request/response shape validated against `\\.\Pakon135` | Recover the complete status-bit meanings and polling-state transitions | High — first safe managed transport seam |
| Driver transport / packet codec | TLB/TLC packet wrapper over `IOCTL_PAKON_SEND_AND_RECEIVE_PACKET` | Yes | IOCTL number, 36-byte response convention, packet framing, timeout behavior, and one read-only command are known | Catalogue packet classes, checksum/trailer rule, retries, and error/status decoding from DLL + driver source | Highest — foundation for a COM-free scanner path |
| Raw acquisition and staging | Backend scanner workers -> overlapped reads -> PFS/CiBuffer staging | Yes — scan start, driver ring, motor/lamp/CCD control | Driver source exists; TLA/TLC have matching worker architecture; PFS is host-side staging | Recover scan-start packet sequence, ring ownership/lifetime, cancellation, and end-of-stream detection | Highest — enables in-house capture |
| Framing | Backend scan worker -> host framing coordinator | No direct driver flag for known aggressive framing choice | `AggressiveFraming` is confirmed as blind placement; normal path is content detection | Map remaining framing inputs, image-coordinate conventions, and strip/frame metadata output | High — replacement can be pure managed code after capture |
| Scan options | `ScanPictures` control mask reaches backend worker/config state | Mixed; most known bits are host state, sensor impact not yet proven | All public enum bits are listed; aggressive framing is understood; other paths partly identified | For each flag, locate its first read, all downstream branches, and any emitted packet/config field | High — no public managed option without this evidence |
| Save-group and metadata | Backend owns acquired-roll / save-group records | No, after acquisition | Count/move/delete and picture/strip metadata COM contracts are known | Recover record layout, lifecycle, and minimum metadata needed by rendering | Medium — straightforward managed domain model later |
| Rendering and destinations | Backend save worker -> disk, client memory, or shared memory | No, after acquisition | Destination meanings, worker split, and save-flag prerequisites are known | Map input planar format, per-picture state, and output layout/ownership | Medium — can first retain bridge/native implementation |
| Colour correction and LUTs | TLA/TLC dynamic PakonImau host -> PakonImau + `Config\\ColorCorrection` | No | Host-side only; LUT/config directories and major entry points are recovered | Reconstruct host context, config selection, LUT/matrix load order, and planar ABI | Medium — native bridge first, managed port later |
| Ansel scene balance | PakonImau roll/scene lifecycle after correction | No | Roll lifecycle, paths, diagnostics, partial descriptor, and renderer invocation are known | Recover complete descriptor/context ownership and validate deterministic offline fixtures | Medium — separate offline colour-science workstream |
| Encoding/output files | Backend save worker / legacy converter | No | Existing converter establishes usable planar fixture format | Map legacy encoder options only where compatibility matters | Low — modern .NET encoders can replace this once pixels are available |

### Static-analysis order

1. **TLC initialization worker:** recover its input-state layout and all reads
   of `initializationFlags`, including `0x40000000`
   (`INITIALIZE_CSharpClient`). This is the current task; its name is not a
   behavioural conclusion.
2. **TLC driver packet layer:** enumerate every call into the packet wrapper,
   cluster packets by command family, and correlate them with the available
   FX35 driver source. Start with status/open/close/error paths before motion.
3. **Scan acquisition state machine:** trace `ScanPictures` through setup,
   packet issuance, overlapped ring reads, PFS staging, completion, and cancel.
4. **Host-only post-acquisition path:** framing, save-group state, renderer,
   then PakonImau/Ansel. This sequence separates scanner safety/protocol work
   from image-processing research.

### Evidence rules

- A type-library enum name is a label, not proof of its effect.
- A diagnostic string is a clue, not proof of a reached code path.
- A driver command becomes a managed candidate only after its exact bytes,
  response validation, and purpose are recovered.
- Hardware tests validate a static conclusion; they do not substitute for a
  documented call chain.
- **Firmware update is permanently prohibited.** Never pass
  `INITIALIZE_FirmwareUpdate` (`0x2`) to TLC, issue a firmware packet, or add
  a firmware-update feature/probe. Static analysis may identify the guarded
  branch solely to exclude it from the managed implementation.

## TLX public contract

The project references the TLX type library with GUID
`{DEAE21C7-F1FF-407E-BC28-F907A2E2821A}`, version 1.1.  The generated interop
assembly reports `Interop.TLXLib, Version=1.1.0.0`.

The primary COM class is `TLXLib.TLXMainClass`/`ITLXMain`.  The important
methods include:

- `InitializeScanner`, `CBAdvise`, `CBUnadvise`, and `GetAndClearLastError`.
- `ScanPictures`, `ScanCancel`, `AdvanceFilm`, `StopFilmDrive`, and diagnostics.
- `GetScannerInfo000` and related scanner-information methods.
- Scan/save-group management plus `SaveToDisk` and `SaveToClientMemory`.
- Calibration, EEPROM, lamp, motor, and factory-reset methods.

The COM callback is `TLXLib.ICallBackClient.Awake(int operation, int status)`;
its IID is `{1A2F6DDF-AAD8-40FB-BAAB-4FEE015ADCD5}`. The callback mechanism
and the full method surface are observable from the installed interop assembly
and from the existing `PakonLib` source. This is a distinct COM interface from
TLC's identically shaped `ICallBackClient` (see `tlx-lowlevel.md`): use the
generated `Interop.TLXLib.dll` declarations for the TLX facade rather than
reusing TLC interface declarations.

## How PakonClient calls scan today

`ScannerScan.ScanPictures` calls:

```csharp
tlx.ScanPictures(resolution, filmColor, filmFormat, stripMode, scanControl, "1000");
```

The first four values select scan behavior; the fifth is a TLX scan-control
bitmask.  The final roll identifier is currently hard-coded to `"1000"`.

## Scan-control findings

The interop type library gives the following scan-control values for the
installed TLX version:

| Name | Value |
| --- | ---: |
| `SCAN_None` | `0x00000000` |
| `SCAN_AggressiveFraming` | `0x00000002` |
| `SCAN_RFT_SenseSplice` | `0x00000004` |
| `SCAN_UseScratchRemoval` | `0x00000008` |
| `SCAN_Use24mmAutoLoader` | `0x00000010` |
| `SCAN_HasFilmDrag` | `0x00001000` |
| `SCAN_PreScan` | `0x00010000` |
| `SCAN_UsePremiumColorPath` | `0x00100000` |
| `SCAN_UseOrderAnalysisCallbacks` | `0x00200000` |

`AggressiveFraming` is bit `0x2` in the fifth `ScanPictures` argument.

### `AggressiveFraming` behavior (confirmed by TLA static analysis)

This behavior is now known for the F135/TLA path.  It is host-side framing
behavior; it is not a packet flag passed directly through the FX35 driver.

TLA's scan setup function, currently labelled `FUN_10033dd0` by Ghidra,
copies the scan-control bit mask into its scan state and assigns:

```c
framingAggressive = scanControl & 0x2;
```

The subsequent framing coordinator (`FUN_10030a00`) passes that value to its
framing routine (`FUN_1000b1c0`).  The routine has two paths:

- When the value is zero, it runs content-based frame detection.  The detected
  candidate frames are refined by routines whose diagnostic strings are
  `FramingLookInBetweenEnds`, `LookAtEnd`, and `LookAtBeginning`.
- When the value is nonzero, it sets scan-warning bit `0x800` and calls
  `FramingBlindlyPlacePictures`.  That routine divides the scan length into
  expected frame-sized regions and creates framing rectangles from the
  expected spacing rather than detecting picture boundaries in the image.

Without this flag, the coordinator falls back to the same blind-placement
algorithm if normal content-based framing yields no frames.  With
`AggressiveFraming` enabled, it selects blind placement immediately.  This is
the concrete change made by the option.

The same setup function also confirms these direct state mappings:

| Scan-control mask | TLA state/use observed |
| --- | --- |
| `0x00000002` | Force blind frame placement as described above. |
| `0x00000004` | Splice-sensing option forwarded into native scan-worker setup. Its sensor-level result needs hardware validation. |
| `0x00000008` | Scratch-removal state. TLA logs either `Use Scratch Removal` or `NO Scratch Removal`; downstream processing decides the actual correction. |
| `0x00000010` | 24 mm auto-loader state, used when selecting scan/transport behavior. |
| `0x00001000` | Film-drag state; the auto-loader path can force this state on. |
| `0x00010000` | Pre-scan flow. TLA takes a distinct setup/early-return path and adjusts an internal timing value by 900 seconds; its user-visible result needs hardware validation. |
| `0x00100000` | `UsePremiumColorPath`: requests premium color-negative mode in host-side processing. PakonImau has distinct `ColorNegStandard` and `ColorNegPremium` configuration branches, but the recovered `CiColorCorrectionAnsel` entry point maps both corresponding negative mode codes to `CN-Enhanced`. The exact TLA-to-PakonImau hand-off and recipe change are not yet recovered; this bit does not appear to be an FX35 driver command. |
| `0x00200000` | `UseOrderAnalysisCallbacks` in the interop enum. The installed type library declares the associated `WTO_OrderAnalysisProgress` callback operation (`42`), but its status payload and native producer are not yet traced. |

The descriptions other than aggressive framing identify the native state/path
selected, not yet a measured scanning result.

### Scan request state and acquisition effects (TLA/F135)

`TLA!FUN_10033dd0` is the normal F135 scan-setup routine.  The request object
it consumes retains the public `ScanPictures` selections before the native
worker is created: resolution, film colour, film format, strip-mode setting,
and the scan-control word.  Resolution and format are used to select the
native acquisition/profile object and to size host buffers; colour is supplied
to the corresponding profile configuration step.  The exact sensor raster
values for Base4/Base8/Base16 are still not recovered, but these selections
are demonstrably acquisition inputs—not save-time rendering controls.

The same routine makes these control-bit boundaries concrete:

| Bit | Confirmed TLA behavior | Replacement implication |
| --- | --- | --- |
| `0x2` | Copied into framing state and consumed by the blind-placement branch. | Managed framing can replace this independently of the driver. |
| `0x4` | Passed directly into the native acquisition-worker constructor (`FUN_100393a0`). | Splice sensing belongs to the scan transport/acquisition boundary; preserve it as unsupported until its command/result contract is traced. |
| `0x8` | Stored as the scratch/IR acquisition state. TLA calculates its host ring-buffer line-unit count as `6` normally and `8` when this bit is set, then passes that value to the acquisition/ring-buffer setup. | This requests extra captured data, plausibly the IR channel used by later scratch removal. It must be decided before scanning; `SAV_UseScratchRemovalIfAvailable` can only consume an eligible result later. |
| `0x10` | Stored as auto-loader state and forces the film-drag state on. It also selects a different early hardware setup mode. | Transport-specific; do not surface in a generic 35 mm scan profile. |
| `0x1000` | Selects the film-drag data/transport branch. | Transport-specific, not image processing. |
| `0x10000` | Adds 900 seconds to the setup timing calculation and returns after pre-scan setup instead of entering normal acquisition. | A distinct workflow, not a normal-scan quality option. |

This confirms a key design rule: request scratch-capable capture at scan time,
then separately request scratch correction at render time.  It also confirms
that output resolution, colour correction, and file format must not be folded
back into the capture request model.

The existing friendly wrapper names some additional flags that are absent from
the installed interop enumeration.  It intentionally rejects these names at
runtime when the installed TLX type library does not define them.  Documentation
must therefore always identify the TLX version used for an observation.

### C-41 managed support boundary

For the first feature-complete migration, represent a C-41 scan as separate
**acquisition** and **output-processing** choices.  The old API places these
concepts close together, but they belong to different native layers.

| Managed choice | Legacy representation | What static analysis establishes | Initial replacement policy |
| --- | --- | --- | --- |
| Colour-negative source | `FilmColor.Negative` | A scan-time film-colour selection passed to `TLX.ScanPictures`; distinct from later PakonImau colour-negative processing. | Make it an explicit acquisition choice. Do not imply it selects a particular emulsion LUT. |
| 35 mm geometry | `FilmFormat.Film35mm` | A scan-time format selection; its packet-level encoding has not yet been recovered. | Expose it as acquisition geometry. |
| Base4 / Base8 / Base16 | `Resolution.Base4`, `Base8`, `Base16` | A scan-time resolution choice. Sensor raster dimensions and packet mapping remain to be traced. | Expose it without guessed DPI or byte dimensions. |
| Full-roll / strip workflow | `StripMode.FullRoll` | A scan-coordinator workflow choice, not a colour option. | Keep separate from colour and output options. |
| Content-detecting framing | no `AggressiveFraming` bit | TLA's default framing path detects image content. | Default for C-41. |
| Blind frame placement | `AggressiveFraming` (`0x2`) | Fully mapped: bypasses normal content detection and places frames from expected pitch/locations; warning `0x800` marks immediate blind placement. | Name by effect, for example `UseBlindFramePlacement`; retain legacy naming only for compatibility. |
| Scratch-removal request | `0x8` | Enables scan-side scratch-removal state, but does not guarantee correction in a saved image. Save processing separately has an "if available" scratch-removal option. | Model it as acquisition capability plus a separate output-processing request. |
| 24 mm autoloader / film drag | `0x10`, `0x1000` | Transport-specific controls; autoloader can force film-drag handling. | Exclude from the baseline 35 mm C-41 profile. |
| Pre-scan | `0x10000` | Selects a different scan flow and lengthens a related timer by 900 seconds. | Exclude pending a dedicated workflow trace. |
| Alternate colour path | `UsePremiumColorPath` (`0x100000`) | Host-side request for a different colour configuration/path, not a proven quality setting or direct driver flag. The exact TLX-to-PakonImau configuration decision is untraced. | Keep explicitly experimental; do not call it "premium" or make it the C-41 default. |
| Colour correction / scene balance / adjustments | save flags `0x10`, `0x20`, `0x40` | Save-time PakonImau processing. Scene balance requires colour correction; adjustments require both. | Represent as one output-processing profile, enabled by default for a Pakon-like C-41 rendering. |

No recovered public scan setting says "use LUT for film X." The Ansel
configuration maps and diagnostic trace do establish that DX-derived product,
specifier, and ISO values can select the FUGC/contrast recipe (see
"The actual DX/product-code LUT lookup" below). What remains untraced is the
complete scanner-side handoff that populates those values for every acquisition.
The replacement API should therefore preserve captured film metadata, but not
promise a particular emulsion-specific rendering until that handoff is mapped.

This gives the new application a stable, clear public model while the legacy
bridge remains responsible for unresolved native acquisition details.

### How to establish a setting's real behavior

For each flag, run a controlled A/B scan with the same physical film and all
other settings unchanged.  Record:

1. TLX callbacks and error/warning information.
2. Scan and save-group metadata, particularly frame count and framing values.
3. Raw client-memory output or saved files.
4. The packets sent to the FX35 driver.

Comparing the traces identifies whether the flag changes a scanner command,
changes only host-side processing, or both. This is still required for the
settings whose sensor-level or image-level consequences remain untested.

## Errors, warnings, and progress callbacks

TLX uses one public callback shape,
`TLXLib.ICallBackClient.Awake(operation, status)`. The first value identifies
the operation family; the second is either progress, an error code, or a
hardware-status bitfield depending on that family. TLC has a callback with the
same method shape but a different IID; this statement describes the public TLX
facade only. A new API should not expose this overloaded pair directly:
translate it into typed progress, fault, and hardware-state events.

### Callback operation contract

The installed type library assigns these stable groups:

| Operation values | Meaning of `status` | Replacement event |
| --- | --- | --- |
| `0` / `1` | Initialize progress / initialize error | `InitializationProgress` or `InitializationFailed` |
| `12` / `13` | General hardware status / hardware error bitfield | `HardwareStatusChanged` |
| `14` / `15` | APS hardware status / error bitfield | Model-specific APS status; not part of the F135 baseline |
| `34` / `35` | Scan progress / scan error | `ScanProgress` or `ScanFailed` |
| `38` / `39` | Save progress / save error | `RenderProgress` or `RenderFailed` |
| `40` / `41` | TLX/facade progress / error | Legacy-bridge diagnostic only |
| `42` | Order-analysis progress | Declared by the installed type library; detailed callback behavior remains untraced |

Other paired operation values represent diagnostics, calibration, film motion,
and legacy firmware-update activities. Firmware-update operation names are
present only because the type library is shared; they are never an allowed
operation in this project and must not cause any update path to be implemented
or invoked.

For ordinary progress operations, the defined markers are `0` (initial),
`1000` (start), `2000` (end), and `3000` (complete). With the initialization
percent-progress option enabled, intermediate values may instead be
percentage-style progress. The exact scale for scan and save intermediate
updates needs a controlled trace, so preserve the raw value alongside a
normalized progress estimate during migration.

### Error-code ownership

`ERROR_CODES_000` is a flat numeric namespace, but its numeric ranges identify
the failing layer:

| Range | Owner / examples | Managed treatment |
| --- | --- | --- |
| `1`–`30` | COM callback/thread/session and public API validation | Translate to managed lifecycle/argument errors. |
| `100`–`235` | Scanner, EEPROM, calibration, DX, transport, Win32, and hardware faults | Preserve native code and contextual Win32/detail values; classify only when the hardware meaning is known. |
| `1000`–`1022` | Driver/packet/ring failures, including ring overflow, lost sync, packet checksum/timeout, and transfer-in-progress | First-class transport faults; retain packet/ring diagnostics. |
| `2000`–`2022` | PakonImau memory/profile/LUT/colour-processing failures | First-class render/colour faults. |
| `3000`–`3019` | PFS staging store / capacity / logical-range failures | First-class acquisition-staging faults. |
| `4000`–`4009` | DICE worker/queue failures | Internal worker diagnostics. |
| `11000`–`11032` | TLX facade component selection and COM-interface failures | Migration/legacy-bridge diagnostics, not scanner faults. |

`EC_PreviousError` (`25`) is special: it denotes another queued native error,
not the root cause. The legacy client correctly calls `GetAndClearLastErrorTLX`
repeatedly (with a bounded loop) until it receives a different value. The
installed TLX facade exposes this as `GetAndClearLastError(interfaceId, ref
message, ref number)` returning the numeric error result; the current bridge
uses `INT_IID_ITLAMain` and records every bounded drain attempt after a callback
error.

### Controlled bridge trace profile

The temporary x86 bridge has an opt-in reference recorder, not a packet hook.
Its explicit `scan-tlx-trace-profile` command uses the installed type-library
values `Resolution.Base16=2`, `FilmColor.Negative=1`,
`FilmFormat.Film35mm=1`, `StripMode.FullRoll=0`, and `SCAN_None=0`. It records
the exact raw callback pairs, recognizes completion only at status `3000`, and
captures TLX's decoded error stack for error-operation families `1`, `13`,
`15`, `35`, `39`, and `41`. Before/after commands capture scanner identity and
scan/save-group counts; runtime evidence captures the relevant 32-bit scan
registry values and readable native logs. This is comparison evidence for the
managed replacement, not a claim that scanner packets or raw pixels are yet
understood.

After a trace roll is promoted, `save-tlx-trace-jpegs` sets deterministic
`frame-###.jpg` names inside the trace folder and invokes the ordinary TLX disk
renderer for every save-group picture. It uses original dimensions,
`SAV_UseCurrentRotation`, colour correction, scene balance, and adjustments
(`0x74` total), bicubic scaling, JPEG quality 90, 300 DPI, and 24-bit output.
The completed file list and byte counts are written to
`output-jpeg-manifest.json`; this records rendered comparison artifacts but
does not capture native raw pixels.

### Hardware status bits

For operation `12`/`13`, the callback status is a bitfield, not an
`ERROR_CODES_000` value. Confirmed bits include board faults (host, DX, lamp,
CCD, motor: bits `0`–`4`), lamp/temperature/fan/power warnings and errors,
stepper-indeterminate conditions, film-guide errors, cleaning-required, film
orientation, and entry/exit film sensing (bits `30` and `31`). APS statuses
have their own bitfield, including cartridge-present and film-jam states.
The legacy console's decoder is the authoritative currently recovered mapping;
copy its bit table into a future typed `HardwareStatus` value rather than
turning the full status word into a single Boolean failure.

Initialization warnings are independent of callback errors: `0` means none,
`1` EEPROM blank, and `2` EEPROM checksum bad. They should be surfaced as
non-fatal diagnostics unless later hardware behavior proves otherwise.

## Roll, strip, picture, and save-group model

The public TLX save interfaces establish the minimum domain model that exists
after acquisition and framing. Internal record layouts remain unrecovered, but
their observable ownership and fields are clear enough to design a replacement
model without inheriting the COM API.

```text
scan group: ordered rolls awaiting promotion
  roll: requested roll ID and one or more strips
    strip: capture geometry, film metadata, D-min, warnings, source frames
      picture: framing rectangle, frame number/name, selection, rotation,
               output name/path, rendering adjustments
save group: promoted rolls/pictures eligible for rendering
```

| Entity | Confirmed public fields | Replacement guidance |
| --- | --- | --- |
| Scan group / save group | Counts of rolls, strips, pictures, selected pictures, hidden pictures; oldest roll promotion; release/delete operations. | Use explicit `CapturedRoll` and `RenderQueue` collections instead of implicit global groups. |
| Strip | Parent roll index, strip marker, film colour/format, high/low-resolution height and length, scan warnings, product, specifier, 24 mm ID, RGB D-min, roll ID, and newer cartridge hand-of-load. | Keep film metadata and D-min with the strip: they are inputs to downstream colour/Ansel processing. |
| Picture | Parent roll/strip, product/specifier inherited from strip, frame name/number, print aspect ratio, output file/directory, rotation, selected/hidden state. | Make this a managed framed-image record; file naming must be output metadata, not acquisition state. |
| Framing | Native framing-risk value plus high-resolution and low-resolution user rectangles. | Store native-detected and user-edited rectangles separately. |
| Rendering adjustments | RGB, brightness, contrast, sharpness, lighting direction, red-eye rectangle/settings, saturation, B&W effect. | Preserve as explicit render options; do not merge with raw capture data. |

`MoveOldestRollToSaveGroup` is a lifecycle transition, not a file save. Actual
rendering occurs only when a `SaveTo…` route consumes eligible pictures from the
save group. `InsertPicture`/`DeletePicture` operate on the framed picture list;
they are host-side metadata operations and do not alter scanner acquisition.

The next static task is to locate the private record constructors at the end of
the PFS/framing pipeline. Until then, this public model is the safe contract;
do not treat COM indices as durable identifiers in the new application.

## Color-negative correction and LUT assets

The `Config\ColorCorrection` directory is used by the TLX stack's host-side
image-processing layer, not by the FX35 kernel driver or the scanner transport.
This is confirmed by static analysis of `PakonImau.dll`: its
`PIColorAdjustPlanar` routine constructs paths below
`%s\Config\ColorCorrection\`, and its planar-output routine explicitly opens
the `sRGB.pf`, `AdobeGamut.pf`, and `Romm.pf` assets. In the same DLL,
`CiColorCorrectionAnsel` selects named processing paths:

| Path selector | Processing path |
| --- | --- |
| Digital | `DC-Premium` |
| Color negative standard or premium | `CN-Enhanced` |
| Color negative lock-beam | `CN-Lockbeam` |
| Color positive | `CP-Balance` |

It also loads configuration from the corresponding registry branches under
`HKLM\SOFTWARE\Pakon\PakonIma`, including `ColorNegStandard`,
`ColorNegPremium`, and `ColorNegLockBeam`. Therefore color-negative correction
is part of the TLX/PakonImau scan-output pipeline. It is not a driver option
and is not sent unchanged to `\\.\PAKON135`.

Color correction is also explicitly controlled at **save** time. The installed
type library defines `SAV_UseColorCorrection` (`0x00000010`), alongside
`SAV_UseColorSceneBalance` (`0x20`) and `SAV_UseColorAdjustments` (`0x40`).
This places the correction decision after acquisition/framing, when TLX saves
to disk or client memory. Their documented native roles are:

| Save flag | Effect during output generation |
| --- | --- |
| `SAV_UseColorCorrection` | Applies the configured color transforms and LUTs. |
| `SAV_UseColorSceneBalance` | Applies automatic per-scene color balancing. It requires `SAV_UseColorCorrection`; TLA rejects the combination otherwise. |
| `SAV_UseColorAdjustments` | Applies configured user-style color adjustments after correction/balancing. It requires scene balance (and therefore color correction); TLA rejects it otherwise. |

The installed `TLXLib` also defines the complete save-control word below. The
low two bits form a size-selection field, rather than independent flags.

| Save-control value | Static/output role |
| --- | --- |
| `0x00000000` `SAV_SizeOriginal` | Original framed dimensions. |
| `0x00000001` `SAV_SizeLimitForDisplay` | Select display-size limiting. |
| `0x00000002` `SAV_SizeLimitForSave` | Select save-size limiting. |
| `0x00000003` `SAV_SizeBitMask` | Mask for the mutually exclusive size field; not a standalone choice. |
| `0x00000004` `SAV_UseCurrentRotation` | Applies recorded picture rotation during rendering. |
| `0x00000008` `SAV_UseLoResBuffer` | Renders from the low-resolution framed buffer. |
| `0x00000080` `SAV_UseScratchRemovalIfAvailable` | Requests scratch correction only when compatible scan-side data exists. |
| `0x00000100` `SAV_FastUpdate8BitDib` | Legacy 8-bit DIB delivery/update mode; not part of the baseline replacement contract. |
| `0x00000200` `SAV_TopDownDib` | Requests top-down orientation for DIB-formatted output. |
| `0x00000400` `SAV_FileHeader` | Includes the legacy header when delivering client/shared-memory output. |
| `0x00000800` `SAV_DoNotScaleUp` | Prevents enlargement while applying requested bounds. |
| `0x00001000`, `0x00002000` | Obsolete KCDFS/digital scene-balance flags; exclude from the replacement API. |

The destination is not encoded in this word. `SaveToDisk`,
`SaveToClientMemory`, and `SaveToSharedMemory` select it independently, then
share the same crop/rotation/scale/processing stages.

### Confirmed native save boundary

TLA dynamically loads `%ProgramPath%\PakonImau.dll` and resolves the native
processing entry points with `GetProcAddress`. The resolved stages include
`PIColorCorrectColNegPlanarScan`, `PIColorCorrectColNegPlanarSave`,
`PIColorCorrectColRevPlanar`, `PIAnselStartNewRoll`, `PIAnselAddScene`,
`PIAnselAnalyzeRoll`, `PIAnselAnalyzeScene`, `PIAnselColorSceneBalancePlanar`,
and `PIColorAdjustPlanar`, as well as the planar image open/save, rotation, and
scale routines. This is a confirmed TLA-to-PakonImau DLL boundary rather than
a driver call.

The Ansel export wrappers establish the stateful lifecycle arity, even though
their context structures are not yet named: `StartNewRoll(context, roll)`,
`AddScene(context, roll, scene)`, `EndRoll(context, roll)`,
`AnalyzeRoll(context)`, `AnalyzeScene(context, scene)`, and
`ColorSceneBalancePlanar(context, scene, planar)`. This confirms scene balance
is not a single stateless image transform.

`PIColorAdjustPlanar` has a separate, one-pointer configuration contract. Its
implementation constructs a profile/effect chain containing a base profile
transform, an optional saturation profile (`satPlus03` through `satPlus15`,
`satMinus03` through `satMinus15`, or `unity`), optional B&W effect profile
(`warm_bw`, `cold_bw`, `sepia`, or `unity`), 4,096-entry channel-adjustment
LUTs, contrast, unsharp mask, and a final combined ICC effect. This explains
why `SAV_UseColorAdjustments` is materially broader than a simple RGB offset:
it is host-side PakonImau post-correction rendering.

TLA validates the three save flags before processing: scene balance without
color correction produces `Image Correction without Color Correction`, and
color adjustments without scene balance produces `Color Adjustments without
Image Correction`. The exact branch-to-stage sequence still needs a deeper
decompilation pass, but the dependency chain is now proven.

### Can color processing be used offline?

**Yes, once the image has been decoded to PakonImau's planar input form.**
`PakonImau.dll` exports independent callable entry points for
`PIColorCorrectColNegPlanarScan`, `PIColorCorrectColNegPlanarSave`,
`PIColorCorrectColRevPlanar`, `PIAnselColorSceneBalancePlanar`, and
`PIColorAdjustPlanar`.  The negative-correction exports take five arguments
and call their internal implementation with the same image pointer as both
source and destination, which is direct evidence of in-place planar-image
processing rather than a dependency on a live scanner handle.

`PIFileSpecsPlanar_8` can open a planar file and returns its width, height, and
three color channels.  This supports an offline experiment using a decoded
three-plane image file/buffer. A raw `PFSxx.bin` file alone is not sufficient:
TLA's 16-bit component deinterleaver is now known, but PFS span selection,
component assignment, sample scale, dimensions, and frame crop still need
decoding.

#### Recovered negative-save call shape

TLA resolves `PIColorCorrectColNegPlanarSave` into its PakonImau function
table at offset `+0x48`.  Its common color wrapper calls that entry with this
five-argument shape:

```text
PIColorCorrectColNegPlanarSave(
    planarPixels,
    width,
    height,
    saveCorrectionContext,
    negativeLookupTable)
```

The first three arguments are now confirmed by the caller: it creates a new
planar output object, obtains its pixel pointer, then passes that object's
width and height.  This proves the routine operates on a decoded/cropped
planar frame, not a PFS file.  Both remaining arguments belong to one
long-lived TLA color-host object: the renderer receives that object from TLA
global state, the save correction context is its embedded block at `+0x48`,
and the processing-options argument is its `+0x40` field.  TLA invokes the
same wrapper with a distinct embedded block for the scan-time negative path.
They are therefore native host state, not transient COM objects or values that
can be reconstructed by simply passing the public save flags.

#### Color-host initialization and asset sources

The color host is constructed during TLA startup and initialized against
`ProgramPath\ColorKodak`.  Its initializer builds default paths below
`Config\ColorCorrection`, then resolves configurable replacements for each of
these keys:

| Configuration key | Default asset |
| --- | --- |
| `CRInputProfileFile` | `romm.pf` |
| `InputProfileFile` | `rpd.pf` |
| `OutputProfileFile` | `srgb.pf` |
| `ColRevLut1File` | `ColRevLut1.pf` |
| `ColRevLutS6` | `ColRevLutS6.lut` |
| `ClientColNegLutFile` | `ClientColNegLut.txt` |
| `ClientColNegMatFile` | `ClientColNegMat.txt` |
| `ClientColRevLutFile` | `ClientColRevLut.txt` |
| `ClientColRevMatFile` | `ClientColRevMat.txt` |

At initialization TLA allocates two 64 KiB native work buffers at `+0x40` and
`+0x44`.  The first (`+0x40`) is directly passed as the fifth argument to
`PIColorCorrectColNegPlanarSave`; it is a 16,384-entry, 32-bit native lookup
table, not a generic options object.  The configured negative-LUT text file is
parsed into a 16,384-entry integer buffer and cached as a same-directory `.bin`
file.  TLA then performs a post-load generation pass over the native table
(using a logarithmic transform), so static evidence does **not** yet justify
treating the fifth argument as the raw text curve.  For each matrix direction,
TLA uses all twelve configured `NegMatrix0..11` or
`PosMatrix0..11` values when they are available; otherwise it parses the
corresponding configured matrix text file.  The text assets are therefore the
fallback/default matrix source, not proof of the active calibration.

The negative matrix is now structurally mapped: `NegMatrix0..11` are read as
12 double-precision values into the host at offsets `+0x200` through `+0x258`;
the positive equivalents occupy `+0x260` through `+0x2b8`.  TLA supplies
defaults/normalization when required, then generates two distinct quantized
correction tables from the negative values:

| Host block | Role |
| --- | --- |
| `+0x48` | Color-negative **save** correction context passed to `PIColorCorrectColNegPlanarSave`. |
| `+0xd8` | Corresponding scan-time color-negative correction context. |

The table generator writes 16-bit coefficients and expands them into repeated
entries for the native color routine.  This confirms the save context is a
derived matrix table, not an arbitrary blob.  The second 64 KiB buffer remains
associated with the other color direction/path; it is not passed directly to
the negative-save call.

The installed `_ClientColNegMat.txt` confirms the matrix file format: twelve
successive text lines of the form `coeff_<row>_<column>: <double>`, read with
`atof`, representing a 3x4 RGB affine transform.  `_ClientColNegLut.txt` is a
plain two-column 14-bit curve with 16,384 samples: input `0..16383` maps to an
output value.  The shipped negative curve begins `0 -> 16383`, `1 -> 14750`,
then decreases, so it is the inversion/tone component of the negative path.
The loader requires exactly 16,384 parsable sample pairs beginning at a line
whose first field is `0.0000`; it caches the resulting 65,536 raw bytes as
32-bit integers.  The subsequent native logarithmic post-processing still
needs dynamic comparison or a deeper PakonImau trace before it can be
reimplemented faithfully.
TLA's compiled defaults omit the leading underscores, whereas this installation
ships underscore-prefixed filenames; a configuration override or deployment
fallback must therefore select these files before a standalone replacement can
rely on them.

The remaining field-level layout inside the `+0x48` save-context block is not
yet decoded.  A safe shim should initially let TLA construct this host state
and capture it in-process; constructing the block independently requires the
next pass through the asset loader and context-population functions.

In the output renderer, this color call is guarded by
`SAV_UseColorCorrection` (`0x10`) and happens before the rendered planar frame
is copied to disk/client/shared memory.  Thus a shim can start with the
five-argument correction-only call; it does not need the entire TLX renderer,
but it does need compatible contexts and PakonImau initialization.

The remaining contract is stateful.  Correct color-negative output also needs
the chosen processing path, installed PakonIma registry/configuration, LUT and
matrix assets, and film/scene context.  In particular, scene balance uses the
Ansel roll/scene lifecycle rather than being a pure single-image LUT call.  A
realistic offline prototype should therefore begin with correction-only on a
known planar frame, using captured product/specifier/DX metadata; add Ansel
scene balance only after matching correction-only output.

#### Available offline Base16 fixtures

`C:\Code\PakonImageConverter\raws` contains suitable pixel fixtures produced
by TLXDemoClient's Base16 export.  For example, `2.raw` has a 16-byte
little-endian header (`headerSize = 16`, `width = 3000`, `height = 2000`,
`bitsPerPixel = 48`) followed by exactly `width * height * 6` bytes: three
contiguous 16-bit planar RGB channels in red, green, blue order.  This is not a
PFS scanner stream, and it can serve as the planar pixel argument for an
offline native-color experiment once compatible TLA/PakonImau contexts have
been captured or recreated.  The converter's current managed white/black
point, gamma, saturation, and contrast operations occur only after it reads
this fixture; they are not part of the fixture data.

#### 2026-07 runtime initialization attempt

Creating the 32-bit `Tlx.TLXMain` COM object succeeds without loading TLA or
PakonImau.  Calling only `InitializeScanner(INITIALIZE_CSharpClient, 20000)`
(no acquisition, feed, or save operation) currently fails during TLX's
`CiConfigMain` stage with `EC_PreviousError` (`25`); the detailed error stack is
`CiConfigMain -> CiTLXMain::bInitConfigMain -> InitializeScanner`.  TLA, TLB,
TLC, and PakonImau are consequently not loaded, so this installation cannot
yet supply a live native color context for the Base16 fixture.  The relevant
32-bit ProgramPath and component registrations are present; the specific
CiConfigMain prerequisite remains to be identified.  Do not infer a hardware
or scanning failure from this result: it occurs before those modules load.

### Why there are multiple "save" operations

`Save` means **render an acquired, framed image into an output destination**;
it does not necessarily mean writing a file. The image-processing stages and
save-control flags are shared conceptually, but the final destination differs:

| TLX operation | Destination | What it means in PakonClient |
| --- | --- | --- |
| `SaveToDisk` | Files created by the TLX component | The client first supplies per-picture directory/name metadata with `PutPictureInfo`; TLX renders and encodes JPEG/BMP/TIFF/etc. on its worker thread and writes the files. |
| `SaveToClientMemory` | Client-owned unmanaged buffers | The client allocates one or more buffers, registers their raw 32-bit addresses with `ClientMemoryBufferAdd`, then TLX fills a buffer asynchronously with a DIB or planar 8/16-bit image. It is not returned as a COM byte array. |
| `SaveToSharedMemory` | A named Windows file mapping | A separate legacy/internal delivery route. TLA creates a GUID-named event and `CreateFileMapping` mapping, then exposes the processed data through that mapping. |

The current `SaveToClientMemory` implementation in PakonClient allocates two
buffers and alternates them. Its pointer ABI is explicitly 32-bit, which is why
it requires an x86 process. This route is useful for custom/raw conversion
because PakonClient receives the pixels directly; `SaveToDisk` is useful when
the legacy component should perform final file encoding and file management.

PakonClient deliberately does not expose those legacy destination names in its
own API. `Scanner.Images` exposes `RenderToFile`, `RenderToBuffer`,
`RegisterRenderBuffer`, `ClearRenderBuffers`, and `CancelRender`; TLX-specific
`SaveTo…` calls are confined to the adapter implementation.

### TLA save-worker map (static, confirmed)

The three routes share save-control validation, source selection, framing crop,
rotation/scaling, and the PakonImau-backed correction path. They then split only
at their final destination:

```text
COM SaveTo… request
  -> validate requested dimensions, rotation, scaling, format, and save flags
  -> choose a picture or eligible pictures from the save group
  -> prepare/render the planar image and apply requested processing
  -> destination-specific worker
       SaveToDisk          -> encode/write named file
       SaveToClientMemory  -> copy formatted pixels to a registered client buffer
       SaveToSharedMemory  -> copy formatted pixels to a mapped shared-memory view
```

The recovered TLA workers are:

| TLA worker | Confirmed behavior |
| --- | --- |
| `FUN_10040d00` | Disk worker. Calls the disk renderer `FUN_1002d980`, which derives the output extension (`.bmp`, `.tif`, `.jpg`, `.raw`, or `.rol`) and invokes the file-output stage. JPEG has additional scanner/file metadata handling. |
| `FUN_10041070` | Client-memory worker. Waits for a registered address/length pair, renders one picture with `FUN_1002e3e0`, marks the buffer consumed, and repeats for additional selected pictures. It reports an error if no buffer is supplied. |
| `FUN_10041480` | Shared-memory worker. Uses the same renderer as the client-memory worker, but writes to the mapped view created by the shared-memory setup and synchronizes each image with a Windows event. |

`FUN_1002e3e0` is the shared client/shared-memory renderer. Its final copy
routine (`FUN_10029fc0`) emits either a planar header plus pixels, or a DIB/BMP
header plus pixels, after checking the destination capacity. This confirms that
the client-memory route is a rendered-pixel transfer, not a raw scanner-data
transfer. The disk renderer has a parallel setup path but finishes by encoding
and writing its selected file type.

PakonClient now applies all three when its console `save` or `scan-save`
command uses the default save control for a C-41 color-negative scan. Passing
an explicit `--save-control` remains an override. Positive and black-and-white
film do not receive the color-negative defaults. This behavior should still be
confirmed with an A/B save, but it makes the intended processing policy clear
instead of silently returning raw-ish client-memory output.

The files themselves fall into three useful groups:

| Asset | What is known |
| --- | --- |
| `_ClientColNegLut.txt` | A 16,384-entry (14-bit input) one-dimensional tone curve. It maps input `0` to `16383`, then falls rapidly (`1` to about `14750`), consistent with an inverted negative-to-positive response curve. TLA, TLB, and TLC explicitly construct the path `Config\ColorCorrection\ClientColNegLut.txt` during color configuration. The installed filename has a leading underscore, so the deployment/selection fallback still needs resolving. |
| `_ClientColNegMat.txt` | Twelve coefficients forming a 3x4 RGB affine matrix: each output color is a weighted sum of RGB plus a constant offset. TLA, TLB, and TLC explicitly construct the corresponding no-underscore `ClientColNegMat.txt` path; the installed filename has a leading underscore, so activation versus fallback remains unresolved. |
| `*.pf` / `*.lut` | PakonImau opens named `.pf` files as planar color-profile/effect assets. `sRGB.pf`, `AdobeGamut.pf`, and `Romm.pf` are output-gamut profiles; `satplus*`, `satminus*`, `sepia*`, `warm_bw*`, and `cold_bw*` are named color effects; `ColRevLut1.pf` and `ColRevLutS6.lut` are color-reversal LUT assets. |
| `Defaults.ini` | Default per-film adjustment values (red, green, blue, brightness, contrast, sharpness). TLA reads it with `GetPrivateProfileIntW`, selecting the section from the film classification: numeric product number for color negative, `POSITIVE`, `BnW`, `IMPORTED`, or `NONE`. It then applies those six values as the initial color-adjustment settings. |

### Is it used when scanning through TLX?

Yes for the color-processing system as a whole: TLX calls into the
PakonImau-side pipeline, which resolves `Config\ColorCorrection` assets while
processing planar image output. The most defensible current boundary is:

```text
scanner/FX35 driver -> raw scan data -> TLA/TLX orchestration
    -> PakonImau negative/color/output processing -> saved or client image
```

That does **not** yet prove that every file in the directory is read in every
scan. The exact conditions that select the client negative LUT/matrix remain
open, as does the reason the installed files have leading underscores while the
native modules construct no-underscore paths. A safe future experiment is to
record file-open activity for identical normal and client-memory saves. Do not
edit these installed assets during that experiment; copy/restore them first,
since they can affect output color globally.

### The actual DX/product-code LUT lookup

The most important per-film data is not in `Config\ColorCorrection`; it is in
`anselinstalldir\dataPathItems`. These are readable text configuration files,
not an opaque DLL table.

1. The scanner reads DX markings and produces a product code and generation
   code (the diagnostic log calls them `Product` and `Specifier`).
2. `common\common-ProdCodeTable.dpi` maps the product/generation combination
   to ISO. Its comments identify the two fields as PIMA/DX Part 1 and Part 2;
   its table contains named manufacturers/films and was last updated through
   2006 additions such as Fuji Superia and Kodak Gold/Max variants.
3. `fugc\fugc-lutMap.map` matches `Dx1`, `Dx2`, and ISO. It chooses a
   contrast class (for example 2.25 or 2.50); rules with an exact product and
   generation override ISO-only rules, then a default is used.
4. The same map converts the chosen contrast class to a LUT key such as
   `fugc-generic0225.lut` or `fugc-generic025.lut`.
5. The selected `.lut` is a readable RGB table, not a binary blob. For example
   `fugc-generic0225.lut` declares `aTableDmin = 500 500 500` and then provides
   rows of input value plus three output-channel values.

This answers the DX-code question: **DX affects LUT choice indirectly.** It
first identifies a product/generation and ISO, then the FUGC map converts that
identity to a *generic contrast LUT*. It does not select a separate full
spectral film profile for every stock. Additional modules can also use product
and generation: `sba\SbaDPI\sba.map`, for example, has specific rules for
78/13, 79/15, 96/* and 43/* before falling back to `ansel-sba-CN-default`.

### What “premium” concretely changes

The Ansel data set supports a `CN-Premium` recipe, distinct from
`CN-Enhanced`. Its map files show the intended differences:

- `color\color.map` selects `color-CNPremium.dpi` for `CN-Premium` and the
  RPD color recipe for the normal negative metric. The installed CNPremium
  color-button defaults happen to equal the RPD defaults (25/25/25/75), so this
  file alone is not a material distinction.
- `contrast\contrast.map` selects `contrast-CNPremium.dpi` instead of
  `contrast-CNEnhanced.dpi`. Both use a 4,096-entry tone curve and the same
  slope maxima, but the fixed tone index is 1618 for premium versus 1550 for
  enhanced and their lower minimum-slope limits differ. This changes how the
  automatic tone curve protects/compresses shadows and highlights.
- `toneHelper\toneHelper.map` selects `toneHelper-CNPremium.dpi`; it uses
  `dTree1` for its tone decision tree, whereas `CN-Enhanced` falls through to
  `AllOnTree1`.

However, this establishes what the **supported CNPremium recipe** does, not
that `SCAN_UsePremiumColorPath` reaches it in this installed TLX build. The
recovered `PakonImau.dll` `CiColorCorrectionAnsel::bStartNewRoll` code chooses
`CN-Enhanced` for both of its normal color-negative mode codes. We need a
controlled A/B scan plus file-open tracing to determine whether the flag
reaches a different caller/path, changes configuration before that call, or is
currently unused.

### Can we add a modern-film LUT?

**Yes, with a controlled test installation, this is technically plausible.**
The least risky first extension is not a wholly new curve: add a new exact
`film = <Dx1> <Dx2> <ISO> <contrast>` rule to a copy of
`fugc-lutMap.map`, pointing at an existing contrast class. That lets a modern
film use the existing generic LUT whose contrast best matches it.

A genuine new LUT is also structurally supported: create a new text `.lut`
with the same header/table shape as an existing FUGC LUT, add a new
`contrast = <value> <new-file>.lut` line, then map the film code to that
contrast. But we have not yet established all validation rules or whether this
particular loader caches files, so do this only in a copied/test data path and
with A/B scans. Changing a map does not create a complete modern-film model:
the generic LUT handles tone/contrast, while SBA, PNR, and other modules still
apply their own product/ISO-dependent correction and defaults.

## Evidence from `tlx.dll`

Static strings in `tlx.dll` show that it creates and queries components named
TLA, TLB, and TLC.  Examples include:

- `EC_CoCreateInstanceTLA`, `EC_CoCreateInstanceTLB`, and
  `EC_CoCreateInstanceTLC`.
- `CTLXMain`, `CTLAWrapper`, `CTLBWrapper`, and `CTLCWrapper`.
- `IScanPicturesTLA`, `IScanPicturesTLB`, `IScanPicturesTLC`.
- `ISavePicturesTLA`, `ISavePicturesTLB`, `ISavePicturesTLC`.
- `EC_QueryInterfaceITLAMain`, `EC_QueryInterfaceITLBMain`, and
  `EC_QueryInterfaceITLCMain`.
- `EC_WIN_DeviceIoControl` and `EC_WIN_CreateFileMapping`.

This supports the following working model:

```text
PakonClient
  -> TLXMain COM API (tlx.dll)
    -> TLA/TLB/TLC wrappers and component interfaces
      -> DeviceIoControl / shared-memory / callbacks
        -> FX35 USB driver
          -> scanner firmware and hardware
```

`tlx.dll` is therefore the best map of the public API to the internal modules,
but may not contain all scanner commands or image algorithms.  A trace from
`TLXMain.ScanPictures` into TLA/TLB/TLC is the highest-value first native
analysis target.

### TLB and TLC are full parallel implementations

Both `TLB.dll` and `TLC.dll` were imported into the same Ghidra project. They
are not isolated configuration or LUT libraries: each contains its own
`UIScanPictures` and `UISavePictures` COM classes, save-to-disk and
save-to-client-memory workers, DX/product logging, the color/save configuration
objects, and the same dynamically resolved PakonImau processing exports as TLA.
TLC directly probes `\\.\Pakonx35` and stores settings below
`HKLM\Software\Pakon\TLC`; TLB has the equivalent full scanner pipeline and
its own `HKLM\Software\Pakon\TLB` branch. Some copied diagnostic strings still
say `TLA`, so those strings are not evidence that the running module is TLA.

For the observed F135 path, `tlx.dll` still chooses TLA. The exact model or
installer condition selecting TLB versus TLC remains open, but they should be
treated as alternative complete implementations rather than layers required by
the F135 save path.

### First Ghidra pass (2026-07-18)

Ghidra 10.1.3 completed the first import.  The current project was then
re-created with Ghidra 12.1.2, which imported and analyzed `tlx.dll` as
`x86:LE:32:default` with the Windows calling convention.  The current analysis
project is intentionally outside this repository at
`C:\Temp\PakonGhidra12\TLXAnalysis`.  The earlier 10.1 project remains at
`C:\Temp\PakonGhidra\TLXAnalysis` and is preserved as a baseline.

Ghidra 12.1.2's batch launcher mishandles the parentheses in the original
`Program Files (x86)` path when importing.  It was therefore given a temporary
copy at `C:\Temp\PakonGhidra12\tlx.dll`; the source and copy have identical
SHA-256 hashes:

```text
F837B62A640AC05EC9F319462CEAF4D2BBCE3CE49BCFEF4BA4E35FA8D93E0DD8
```

The executable is stripped: Ghidra recovered generic function names rather
than retaining names such as `ScanPictures`.  Its embedded type-library strings
do preserve public/interface names including `FN_InitializeScanner`,
`FN_ScanPictures`, `IScanPictures`, `IScanPictures1`, and `IScanPictures2`.
This means the COM ABI remains usable as a map, but the implementation methods
must be located via COM vtables, call sites, and dynamic traces.

Decompilation of two non-library `CreateFileW` callers produced a useful
result:

- `FUN_10005bcc` opens `\\.\Pakon135` with read/write access and shared
  read/write, then closes the test handle.  On success it calls
  `CoCreateInstance` and uses `QueryInterface` repeatedly to obtain a group of
  internal interfaces.
- `FUN_10007aa4` implements the same pattern for `\\.\PakonX35`.
- `FUN_10003db6` implements the equivalent loopback path for
  `\\.\Loopback` when its mode argument permits it.

Each successful path queries interfaces stored at successive offsets in the
wrapper object (`+0x10` through `+0x2c`) before attaching its callback object.
This is direct evidence that TLX first selects/probes a driver endpoint and
then instantiates a model-specific internal COM component rather than talking
to the driver from every public method itself.

The recovered code has not yet associated the `CoCreateInstance` CLSIDs and
IIDs with the readable TLA/TLB/TLC names.  Resolving those GUIDs and importing
the corresponding module next will make the hand-off explicit.

### F135 COM hand-off resolved

The F135 endpoint path in `tlx.dll` (`FUN_10005bcc`) constructs the following
GUIDs and passes them to `CoCreateInstance`/`QueryInterface`:

| GUID | Registered name | Role in the observed F135 path |
| --- | --- | --- |
| `{52B5538B-7926-40AD-9DBE-810228E147AD}` | `TLAMain Class` | CLSID passed to `CoCreateInstance`; this is the F135 implementation component. |
| `{20A28C18-FB46-4923-8269-E51CC0A9DE8B}` | `ITLAMain` | Queried from the created component. |
| `{F13200EF-B62D-40C8-8662-92096481FCD8}` | `IScanPictures` | Queried scan interface. |
| `{DF4B1020-FFC5-482E-ABA9-735B49ED04B9}` | `ISavePictures` | Queried save interface. |
| `{FFE972CC-8B45-4669-9AE5-8A546944E49F}` | `IScanPictures1` | Additional queried scan interface. |
| `{4B6E6AB1-3271-47F8-8E71-539BFBA8416B}` | `ISavePictures1` | Additional queried save interface. |
| `{22E93DD8-9C78-4668-931B-9111857B136B}` | `IScanPictures2` | Additional queried scan interface. |
| `{0425F410-1638-4817-A99F-6B791808FC40}` | `ISavePictures2` | Additional queried save interface. |
| `{75246116-FD6F-4D43-BF32-B5E4B5AA47E3}` | `ICalibrationWizard` | Queried calibration interface. |

`TLA.dll` has now been imported into the Ghidra 12.1.2 project.  This confirms
that the TLA component is not just a thin model selector: it contains scanner
state setup, driver command/data-flow coordination, frame detection, scratch
removal selection, and calibration-related code.  `tlx.dll` itself is a
dispatcher and endpoint/component selector for this F135 path.

## FX35 driver findings

The FX35 repository provides source for a modern 64-bit driver:

- `FX35Loader` deploys firmware on connection/power-on.
- `FX35USB` is the scanner data/transport driver.
- `FX35Package` supplies firmware and installation resources.

For an F135 build, `FX35USB.cpp` creates the user-mode device name
`\\.\PAKON135`.  The Ghidra result confirms that TLX itself probes the same
endpoint (case-insensitively as `\\.\Pakon135`); it also has distinct
`PakonX35` and `Loopback` paths.  The connected device is currently visible as:

```text
Pakon F135 USB 2.0 Scanner - Version 2
USB\VID_0F05&PID_F135\010-203-04
```

The driver exposes these relevant IOCTLs in `FX35USB/driver/ezusb.h`:

| IOCTL | Value | Meaning |
| --- | ---: | --- |
| `IOCTL_EZUSB_GET_DRIVER_VERSION` | `0x222074` | Driver metadata query. |
| `IOCTL_PAKON_READ_DIRECT` | `0x222088` | Read directly from the configured scanner endpoint. |
| `IOCTL_PAKON_WRITE_DIRECT` | `0x22208C` | Write directly to the configured scanner endpoint. |
| `IOCTL_PAKON_SEND_AND_RECEIVE_PACKET` | `0x222090` | Write a packet, then read a response. |

The direct packet buffers are capped at 512 bytes by the driver.
`IOCTL_PAKON_SEND_AND_RECEIVE_PACKET` itself does not define the Pakon
protocol; it simply executes the write then the read.  Packet format,
checksums, sequence, and meaning must be learned from TLX/TLA/TLB/TLC or from
controlled traces.

### TLA direct packet envelope (confirmed)

TLA contains the user-mode driver wrapper, currently labelled `FUN_1000c5d0`
by Ghidra.  It gives us the first exact details needed for a managed transport:

- It calls `DeviceIoControl` with `IOCTL_PAKON_SEND_AND_RECEIVE_PACKET`
  (`0x222090`).
- The input packet length is `packet[1] + 2`.  This strongly indicates that
  byte 0 is a packet type/command field and byte 1 is a payload-length field;
  the exact field meanings still need traces.
- It supplies a fixed 36-byte response buffer.
- It uses overlapped I/O and waits up to 2,000 ms for completion.
- A response whose first byte is `1`, `3`, or `7` is accepted.  For response
  types `1` and `3`, TLA additionally requires that response byte 0 equal the
  request's byte 0.  Type `7` is accepted without that equality test.

This is a practical small replacement seam: a managed `PakonDriverTransport`
can reproduce this envelope without COM, while leaving construction of actual
scanner commands disabled until each command's bytes are understood.

TLA also makes vendor/class requests through
`IOCTL_EZUSB_VENDOR_OR_CLASS_REQUEST` (`0x222059`) with a 10-byte control
structure and a maximum response length of `0x5000`.  That path is separate
from the normal Pakon packet exchange above.

### Bulk scan acquisition is a driver-managed ring (confirmed)

The small `SEND_AND_RECEIVE_PACKET` IOCTL is **not** the mechanism by which
scan pixels are transferred.  The F135 implementation in `TLA.dll` opens the
same `\\.\Pakon135` device and starts an overlapped `ReadFile` into a
caller-owned ring buffer.  The FX35 driver source confirms the exact buffer
header required by that read: `RING_TAIL` in
`FX35USB/driver/ringtail.h`.

This is an x86 structure (size `0x38`), with these important fields:

| Offset | Field | Responsibility |
| ---: | --- | --- |
| `0x00` | `m_iHeaderSize` | Must be `sizeof(RING_TAIL)` (`0x38`). |
| `0x04` | `m_iTotalSize` | Size of the allocated ring region. |
| `0x0c` | `m_iNumPackets` | Number of packet slots in the ring. |
| `0x10` | `m_iNumSimultaneousPackets` | Number of USB reads the driver keeps in flight. |
| `0x14` / `0x18` | `m_iReading` / `m_iToRead` | Consumer-side ring positions. |
| `0x1c` / `0x20` | `m_iWriting` / `m_iNumFinished` | Driver producer/completion positions. |
| `0x24` | `m_iPacketSize` | Byte size of each USB packet slot. |
| `0x28` | `m_iMinimumPacketsForReady` | Completed-packet threshold before notification. |
| `0x2c` | `HANDLE_EventScanPacketReady` | Event signalled when data is ready. |
| `0x30`–`0x32` | transfer/overflow flags | Stop, in-progress, and overflow state. |
| `0x34` | `m_pRingData` | 32-bit pointer to the packet data area. |

`FX35USB` receives USB bulk data on its `RingReadPipe`, writes each completed
packet at `m_pRingData + m_iWriting * m_iPacketSize`, advances the shared
producer counters, and signals `EventScanPacketReady` when the configured
threshold is reached.  The driver validates a non-zero packet size no larger
than `0x5000`; this is independent of the 512-byte limit for direct control
packets.

TLA's scan worker waits for this event, consumes the completed ring slots, and
hands raw packets to its `CiBufferHiRes`/PFS staging layer.  Its setup code
creates files named `ProgramPath\\Buffers\\PFS%02d.bin`; the recovered write
path writes raw chunks into this staging system before later processing.  The
pixel packing and the subsequent decoder have not yet been identified, so the
PFS files should currently be considered raw scan-stream staging rather than a
documented image format.

The packet-consumer helper has now been traced far enough to rule out an
important alternative: it does not decode pixels before staging.  It validates
that each consumed chunk is an exact multiple of its configured unit size,
waits for the selected PFS buffer to become writable, calls `WriteFile` with
the raw bytes, and advances a 64-bit sequential position.  Pixel unpacking,
framing, and color work therefore occur after this initial disk-backed stage.

The PFS initializer (`FUN_10005740`) is also mapped.  It creates the
`ProgramPath\Buffers` directory, rounds the configured partition and per-roll
capacities to the volume's cluster size, derives how many per-roll partitions
fit, caps the result at **16**, and preallocates `PFS00.bin` through the
selected final `PFSnn.bin` with `SetEndOfFile`.  Each PFS file is therefore a
fixed-capacity backing partition in a small strip-oriented file system, not a
file corresponding to a single image or scanner packet.  This explains the
PFS-specific errors for partition selection, strip completion, and reading
beyond a file's logical end.

TLA's scan-initialization worker (`FUN_1003ee40`) ties this storage to the
acquisition pipeline. It reads `HiResPath`, `HiResMegabytesTotal`, and
`HiResMegabytesRoll` from the TLA configuration, initializes the PFS object,
constructs the large scan/processing state object, then starts its worker.
The PFS layer is consequently configured before the driver scan worker begins;
it is owned by the high-resolution scan pipeline rather than by a save route.

TLA's ring allocator (`FUN_1002e9b0`) is also recovered.  It allocates and
zeroes a page-aligned virtual-memory block, writes the `0x38` header at its
start, and deliberately places `m_pRingData` at `base + 0x1000` rather than
immediately after the header.  It fills the header from its arguments as
follows: packet-slot count at `0x0c`, minimum-ready threshold at `0x28`,
packet size at `0x24`, simultaneous USB transfers at `0x10`, and the event
handle at `0x2c`.

For the normal scan path, TLA calculates the slot count as an internal scan
buffer size divided by packet size, and takes the packet size, ready threshold,
and simultaneous-transfer count from scanner configuration.  A separate setup
path passes `3` simultaneous transfers, but that must not be treated as the
universal value.  None of these scan-specific dimensions are safe to hard-code
until their configuration source and hardware behavior have been mapped.

### Scan setup uses validated parameter registers

TLA does not begin a normal scan by immediately issuing the bulk `ReadFile`.
Before allocation and acquisition it programs scanner-side setup values through
a control family headed by command `0x84`.  The recovered helper only accepts
subcommands `2` through `7` in this family and uses a read-back transaction to
verify each written value:

| Subcommands | Input domain enforced by TLA | Observed behavior |
| --- | --- | --- |
| `2`, `3`, `4` | Unsigned, clamped to `0..63` | Three small scan/setup parameters. |
| `5`, `6`, `7` | Signed magnitude, clamped to `-255..255` | Three signed scan/setup parameters. |

The subcommand meanings are not named in the binary, so these must remain
protocol labels rather than user-facing settings.  The normal scan setup also
selects a mode from a restricted set (`1`, `2`, `4`, `8`, `0x1000`, or
`0x2000`) and conditionally sends another control pair in the `0xf6` family.
Those values appear to be scan geometry/throughput configuration, but that is
an inference from call order and validation only—not a decoded protocol
specification.

This tells us how to capture the remaining contract safely: log these
read-back-verified transactions during a known scan profile, correlate each
parameter with the public COM request fields, then replay only the captured
sequence into a disposable test session.

### Acquisition and image processing are distinct phases

The scan-completion routine confirms that the raw driver ring is not retained
for later rendering.  It first requests stop through the ring header, waits for
the driver to clear `m_bTransferInProgress`, frees the ring and its associated
control block, and only then iterates the staged scan buffers.  Each completed
buffer is passed into the subsequent native image-processing chain before it is
made available as a framed picture.  This establishes the architecture:

`scanner USB bulk data -> driver ring -> PFS raw staging -> native frame/image processing -> Images renderer`

The next unresolved seam is inside that native PFS-to-picture chain.  It is
separate from both the driver transport and the `ScannerUnsafe` callback
buffers, making it a good later target for an independently testable decoder.

This identifies the next direct-driver boundary precisely: an acquisition
component must allocate an **x86-compatible unmanaged**, page-aligned
`RING_TAIL` plus data region, create the readiness event, issue overlapped
`ReadFile`, and consume the shared counters.  It must also reproduce the
scanner-start control sequence that chooses the ring dimensions and data
format.  Those parameters and packets remain to be mapped; no active scan
implementation has been added.

### Scan control loop versus pixel path (additional static trace)

The TLA object created after PFS setup (`FUN_10032440`, approximately
`0x1465c` bytes) is a long-lived scan state container.  Starting it creates a
manual-reset event specifically named `m_DriverOverlappedReadFile.hEvent`,
then allocates a separate `0x1e8`-byte control coordinator.  The coordinator
starts a dedicated polling thread and creates two further events labelled
`m_DriverOverlappedPPB.hEvent` and `m_hDriverEventPollPPB`.

That polling thread is **not the pixel decoder**.  It waits on the driver
event, queries a four-entry interrupt/status sequence, reports changes to the
outer scan state, and writes small scanner command packets when a state change
requires it.  The command helper accepts status selectors `0xf4` and `0xfe`;
the state helper in turn emits `0x98`-family commands.  This is a scanner
control/firmware-status loop, distinct from the overlapped bulk-read/PFS data
path.

During the same startup sequence TLA dynamically loads `PakonImau.dll`,
constructs its image-processing host, invokes a configuration entry with the
configured colour/profile paths, sends a scanner command in the `0xf6/0x80`
family using a resulting 16-bit value, and only then publishes the initial
scanner state.  This reinforces the ordering: native colour resources and
scanner firmware setup are initialized before scan acquisition, but the
thread above does not itself transform PFS data to pixels.

The currently recovered PFS helper (`FUN_100061a0`) merely validates that the
PFS object has an active backing allocation and returns its current handle.
It is not a PFS read/decompression routine.  Therefore the outstanding
decoder trace should follow the completed-buffer processing calls, rather
than the PPB event-poll functions or this PFS allocation helper.

### PFS is a bounded striped file store, not an image codec

The PFS read and write primitives are now identified:

| Native routine | Confirmed behavior |
| --- | --- |
| `FUN_10006320` | Validates a selected stripe/file and block-aligned request, waits for the PFS file-access lock, then uses `WriteFile`.  The caller breaks writes into chunks no larger than `0x100000` (1 MiB). |
| `FUN_100065e0` | Computes the selected stripe offset, seeks with `SetFilePointerEx`, and reads the requested span in chunks no larger than `0x100000` via `ReadFile`. |
| `FUN_10007010` | Allocates one contiguous client buffer, reads a requested PFS byte span into it, then returns pointers to the data and the trailing portion. |
| `FUN_10037140` | The `EventScanWriteToDisk` worker consumes completed driver-ring regions and persists them through a `CiBufferHiRes` object into PFS. |

There is no compression, pixel conversion, or colour operation in these
routines.  PFS is a bounded, block-aligned, striped temporary byte store over
the preallocated `PFSnn.bin` files.  It protects concurrent file access with
events/critical sections and validates range/stripe completion, but otherwise
preserves the bytes supplied by the scan writer.  This is useful for a future
replacement: a modern implementation can initially retain the same raw byte
stream in a simple append-only capture file, without reimplementing PFS's
partition management.

The next decoder target is now narrower: the consumers of the `CiBufferHiRes`
read buffer after scan completion, not PFS itself.  Those consumers must
interpret the preserved scanner strip bytes and produce framed pixel planes.

### Overlapped acquisition lifecycle

`FUN_1002fca0` ties the driver ring and PFS writer together.  It resets two
events, starts a scan-packet worker and the `EventScanWriteToDisk` worker,
then issues one overlapped `ReadFile` against the FX35 driver with the ring
data region and a dedicated `m_DriverOverlappedReadFile.hEvent` event.  A
successful asynchronous start must return `ERROR_IO_PENDING`; a synchronous
completion is deliberately treated as an error by this legacy code.

The scan-packet worker consumes `EventScanPacketReady`; the disk worker
consumes `EventScanWriteToDisk`.  At completion, TLA cancels the outstanding
driver I/O, waits for both workers, then sends a final scanner state command.
The disk worker explicitly reports `ProcessedRingTailOverflow` if its
processed position falls behind the driver ring, so the ring is a producer /
consumer queue with back-pressure rather than a one-shot buffer.

This gives a practical initial contract for a direct managed acquisition
experiment: allocate the existing ring layout, begin a single overlapped read,
advance the driver and processed tails without allowing either to overrun the
other, persist each completed region verbatim, and only then stop/cancel the
read.  The exact interpretation of a completed region is still unresolved;
the packet-worker entry is an analysis gap in the current Ghidra function map
and needs to be rediscovered from its call target or by a safe runtime trace.

### Direct-driver smoke test

The current machine successfully opened `\\.\PAKON135` and called
`IOCTL_EZUSB_GET_DRIVER_VERSION` directly.  This involved no TLX COM call and
did not send a scanner motion/scan command.  The installed driver returned a
success status with version bytes `0.0.0` and an information count of zero.
That result confirms the device path and IOCTL access; it should not yet be
treated as authoritative driver-version metadata because the installed driver
behavior differs from the current source's documented six-byte response.

### Managed transport probe

`src\Legacy\PakonTransportProbe` is the original small .NET Framework 4.8
x86 console project that established the first two replacement seams without
invoking `tlx.dll`:

1. It opens a selected driver endpoint and sends only
   `IOCTL_EZUSB_GET_DRIVER_VERSION` (`0x222074`), logging the exact empty
   request and returned bytes. It never invokes the packet IOCTL (`0x222090`),
   scan initialization, scanner firmware commands, movement, lamps,
   calibration, or EEPROM operations.
2. It directly instantiates the registered FX35 backend CLSID
   `{6449DE65-60A9-4A45-A3A1-337F5E6B41E0}` (`TLC.TLAMain.1`) and immediately
   releases it. No TLX/TLC interface is queried and `InitializeScanner` is not
   called. This proves that C# can bypass `tlx.dll` while still retaining TLC
   as the backend.

Example invocations:

```powershell
PakonTransportProbe.exe --device \\.\PakonX35 --log C:\Temp\pakon-transport.log
PakonTransportProbe.exe --device \\.\Pakon135
```

On the current machine, the default `\\.\PakonX35` open correctly reported
`ERROR_FILE_NOT_FOUND`, while direct TLC COM activation succeeded. Re-running
with the installed `\\.\Pakon135` endpoint successfully opened the driver,
sent `0x222074`, and logged a successful zero-byte reply; TLC activation also
succeeded. The zero-byte response matches the previously observed installed
driver behaviour and is recorded verbatim rather than decoded as a version.

The active replacement is now `src\Pakon.Transport` plus
`src\Pakon.Transport.Cli` in the root `Pakon.sln`. It is a .NET 10 x64,
COM-free port of the safe driver portion. Its default CLI probes both known
endpoints, successfully opened `\\.\Pakon135` on this machine, and reproduced
the same successful zero-byte metadata response. The legacy probe remains as
evidence of the original experiment; new transport work belongs only in the
.NET 10 projects.

## Target architecture and migration plan

The target is a clear, x64 **.NET 10** application. Its normal scan path must
not require a registered COM server, `Interop.TLXLib.dll`, `tlx.dll`, or the
old TLX public naming. It will use names that describe observable behaviour
(for example `ApplyRollSceneBalance`, `CaptureRawScanStream`, and
`WriteRenderedImage`) rather than preserving ambiguous legacy method names.

During the first migration stage we will deliberately retain the legacy native
image-processing algorithms where they provide value—especially the installed
PakonImau/Ansel implementation—but call them through an explicitly documented
managed/native boundary. The old DLLs are an implementation dependency during
that stage, **not** the application's public API or architecture. TLC/TLX may
run beside the new code as a regression oracle, but must not be required for a
new scan.

```text
Target first production path
.NET 10 app -> managed driver transport -> Pakon driver -> scanner
            -> documented native image bridge -> PakonImau / Ansel

Temporary validation path only
.NET 10 test harness -> legacy TLX/TLC COM -> same scanner and algorithms
```

This separates two goals that should not be conflated: remove the COM/runtime
requirement first, then replace each native algorithm only after it has a
measurable compatible managed implementation.

### Temporary feature-completeness bridge

The active solution now contains `Pakon.LegacyBridge`, an x86 .NET 10 Windows
host, and `Pakon.LegacyBridge.Client`, its .NET 10 named-pipe client. This is
the intentional short-term exception to the COM-free process rule: only the
bridge may construct TLC/PakonImau COM objects. The .NET 10 application uses
our request/response contract and never references those COM interfaces.

The first `probe-tlc` RPC has been validated end-to-end: a .NET 10 client
started an x86 bridge, the bridge directly activated and released
`TLC.TLAMain.1`, and the client received the CLSID/runtime confirmation. It
does not call `InitializeScanner` or a scanner method.

The next bridge seam is now implemented but deliberately not invoked by the
default CLI: `initialize-tlc`. It is a direct TLC operation, not a TLX call.
The TLC type library establishes the exact signature:

```text
ITLAMain.InitializeScanner(
    int initializationFlags,
    int memoryTimeoutMilliseconds,
    int sharedMemoryBytes)
```

The old public TLX facade exposed only two parameters; the third TLC parameter
is the requested shared-memory size. The bridge supplies `0` unless a future
managed capture path explicitly requests shared memory. Its direct-TLC default
is `0x1` (`INITIALIZE_ProgressUpdatesAsPercent`) with a 200,000-ms timeout.
The legacy client additionally supplied `INITIALIZE_CSharpClient`
(`0x40000000`), but static TLC analysis shows that this installed TLC build
does not use that bit: the forwarded control word is only read as `flags & 2`,
the documented firmware-update request. The C# bit is therefore a no-op in the
direct TLC path and is intentionally omitted from the new default.

Static TLC analysis now establishes the initialization contract behind that
call. It is asynchronous: TLC validates the request, records the three values
in `CN_CiThreadDataInitializeScanner`, launches a worker, and waits only for a
short startup handshake. The worker enters a multithreaded COM apartment,
recovers the callback registration, and builds the scanner/configuration and
acquisition objects. `memoryTimeoutMilliseconds` must be at least 1,000;
`sharedMemoryBytes` must be non-negative. A non-zero shared-memory value makes
TLC allocate and map a GUID-named, pagefile-backed shared-memory region; zero
skips that allocation. This confirms that the third parameter is a transport
buffer request, not a colour, framing, or scan-quality control. See
[`tlx-lowlevel.md`](tlx-lowlevel.md) for addresses and field-level evidence.

The C#-client flag is forwarded into the scan-driver initialization object but
never consumed by this TLC build. It is not a managed application setting.

TLC's `CBAdvise` / `CBUnadvise` are **not** methods on `ITLAMain`; they belong
to `ILongOpsCB` (`{E4724BBF-4C27-4AB3-92A8-CD2D45B87682}`) and accept an
`ICallBackClient` (`{31E3E438-9AAD-408A-81FA-BBCA917907D2}`) with
`Awake(operation, status)`. The bridge keeps the COM object, callback sink,
and advise cookie alive after initialization and reports callback count plus
the last raw `(operation, status)` through `get-tlc-session-status`. This makes
the next scan/control bridge operations possible without retaining TLX or
leaking COM callbacks into the .NET 10 process. `close-tlc-session` unadvises
and releases the TLC object.

This direct-TLC callback contract must not be reused for the public TLX facade.
Runtime validation on the installed scanner computer showed that `TLXMain`
does not implement TLC's `ILongOpsCB` IID and that its dispatch surface cannot
be safely substituted for generated TLX interop. The temporary x86 scan/save
bridge must therefore reference the installed `Interop.TLXLib.dll` for TLX
operations, while retaining the minimal TLC declarations only for direct-TLC
research operations.

Future bridge RPCs must be named by behaviour (`ScanRoll`, `SaveFrames`,
`RunLightCalibration`) and paired with an eventual managed replacement; do not
expose the raw legacy API or its ambiguous flags across the pipe.

1. **Remove the public COM dependency.** Build a .NET 10 transport layer that
   discovers/opens the installed driver endpoint, sends only recovered packet
   types, handles the bulk read/ring lifecycle, and exposes well-named managed
   scanner state. The existing x86 probe proves direct driver access; the
   .NET 10 implementation will be x64 and will not use TLX COM client-memory
   pointers.
2. **Recover and isolate the native image boundary.** Reproduce the TLC-owned
   PakonImau host setup, contexts, buffer adapters, and Ansel roll lifecycle
   sufficiently to use the installed PakonImau/Ansel algorithms without
   constructing a TLX/TLC COM object. This is a research milestone, not yet a
   justified implementation: the exact native contexts and ownership rules
   remain incomplete.
3. **Give every setting a managed meaning.** For each legacy flag, document
   its stage, prerequisite, hardware effect versus host-side effect, and
   observable output change. The new API will expose an explicit option only
   when that meaning is known; unknown values remain internal/raw diagnostics,
   not misleading public booleans.
4. **Replace acquisition and framing in house.** Capture the raw stream with
   the managed transport, define the raw-buffer and frame boundaries, and use
   TLX output only as a regression reference. Replace PFS with straightforward
   managed capture/storage rather than emulating its striped temporary files.
5. **Replace rendering incrementally.** Implement negative correction, LUT
   selection, scene balance, adjustments, scratch removal, and output encoding
   as separately testable .NET components. Keep a calibrated intermediate at
   each stage so that differences against the legacy pipeline are explainable.
6. **Retire each legacy dependency deliberately.** PakonImau/Ansel is the
   final major image-processing dependency to replace, after its inputs,
   outputs, metadata decisions, and roll-level behaviour have regression
   coverage. Only then can the Kodak/Pakon COM server and processing DLLs be
   absent from a production installation.

The next immediate research target is still a harmless scanner status query:
trace it through TLC, replay it with the managed transport, and give it a
clear managed name. That expands direct-driver capability without guessing at
scan, movement, lamp, calibration, or EEPROM packets.

`GetScannerInfo000` is no longer assumed to be that query. Direct TLC analysis
shows that it is an internal-dispatch getter distinct from the public TLX
interface and may return initialized configuration/cache values rather than
talk to the driver. The exact dispatch boundary and reason it is not yet a
managed transport API are documented in `tlx-lowlevel.md`.

The first actual managed status operation is instead the explicit PPB
interrupt-status diagnostic: request `03 01 10`, observed response
`03 03 10 00 AA`. It is intentionally opt-in while the meaning of every status
bit and trailing protocol byte is still being recovered.

## Colour pipeline and the Pakon look

The distinctive Pakon result is a **pipeline**, rather than one secret LUT.
The recovered native code supports this implementation model for a C-41 scan:

`scanner samples -> negative correction -> scene/roll balance -> optional creative adjustments -> output transform/encoding`

1. **Negative correction** uses a prepared native lookup-table context plus a
   matrix-derived context.  The installed client negative assets include a
   16,384-entry tone curve and a 3x4 RGB affine matrix, but TLA preprocesses
   those assets into native contexts before calling PakonImau.  The text LUT is
   therefore an input calibration asset, not a complete replacement transform.
2. **Scene balance** is the PakonImau subsystem called **Ansel**.  It carries
   state across a roll: each scene contributes its film-base/D-min measurement
   and, when available, film product and generation codes.  Ansel analyzes the
   assembled roll and applies a selected scene transform to planar pixels.
   This is why `UseColorSceneBalance` is meaningful only after colour
   correction and why enabling it can make frames on the same roll look more
   consistent than independently corrected frames.
3. **Adjustments** happen afterward.  PakonImau's post-correction operation
   can use adjustment LUTs, contrast, sharpening, saturation or monochrome
   effect profiles, and an ICC-related final stage.  These are separate from
   film inversion and roll balance.

In colour-science terms, the first stage compensates for the orange mask,
film-base density, scanner response, and film-specific dye behaviour; the
second estimates a pleasing neutral/tonal reference using information from the
roll; the final stage applies rendering choices.  The warm, contrasty,
consistent “Pakon look” comes from the interaction of all three.  It should
not be described as a universal “premium LUT.”

The current evidence does **not** identify the exact mathematical form or
coefficients of Ansel's roll analysis.  A future replacement should initially
make this stage explicit and optional: preserve a calibrated negative-corrected
intermediate, implement a transparent per-frame balance first, then add
roll-level statistics and compare them against TLX output on controlled rolls.
The full low-level contract is maintained in `tlx-lowlevel.md`.

### Runtime Ansel diagnostic: six-frame colour-negative example

`docs\ansel-diag-example.txt` is an actual diagnostic from a six-frame C-41
scan with the `CN-Enhanced` path. It turns the previously inferred pipeline
into an observable contract:

- Each scene is reduced to a `250 x 375`, three-band, 12-bit,
  band-interleaved `StandardAnalysisImage` for analysis. The final scan image
  is not used directly at this stage.
- The frames all reported the same scanner D-min: `354, 829, 1047`, no film
  ISO (`0`), and the negative source/metric enum (`1` / `1`). Thus this
  particular strip had no usable product/generation/DX choice at the Ansel
  level.
- Every frame selected the generic default film LUT
  `filmLut-scanner-prod-gen-default-default-default.lut`, the FUGC LUT
  `NoShift_fugc-generic0225.lut`, and the `CN-Enhanced` 4,096-entry contrast
  recipe with contrast `2.25`. It therefore demonstrates that the prominent
  per-frame differences below occur even when the film LUT and contrast class
  stay constant.
- Ansel produced different RGB scene-balance shifts for the six scenes:
  `596/210/-52`, `514/128/-136`, `372/-8/-275`, `426/63/-193`,
  `491/88/-169`, and `332/-30/-244`. Its derived balanced D-min values also
  vary by scene (for example `872/960/917` for the first scene and
  `595/709/712` for the sixth). This is direct evidence that scene balance is
  adaptive image analysis rather than a fixed film transform.
- The active capabilities were `afterSCPLutSba`, `contrast`, `dei`, `falloff`,
  `filmLut`, `flesh`, `fugc`, `noiseTable`, `nra`, `sba`, and `scpLut`. The
  diagnostic records a five-value scene `classification`, a 17-node DEI
  decision tree result, flesh-protection shifts, noise/falloff settings, and
  the chosen LUT/configuration keys.

For a compatible implementation, this gives a sensible staged target: first
replicate the analysis proxy image and record D-min, scene statistics and RGB
balance shifts; next reproduce the default LUT/contrast choice; only then
attempt the proprietary classification, flesh, and decision-tree behaviour.

## Native-analysis tooling

Ghidra 12.1.2 has been used for the current `tlx.dll`, `TLA.dll`, `TLB.dll`,
and `TLC.dll` analysis. It can decompile the 32-bit native DLLs, follow
imports/exports and COM vtables, label functions, and preserve an analysis
project. The important CLSID hand-off is now resolved: `tlx.dll` is the public
facade and selects `TLC.dll` for `\\.\PakonX35` (FX35), while it selects
`TLB.dll` for `\\.\Pakon135` (F135). Therefore TLC is the primary static
analysis target for this scanner. The low-level module map and recovered
addresses are maintained in `tlx-lowlevel.md`.

Useful companion tools are a debugger with process/module support (x64dbg is
adequate even for this 32-bit target) and a way to log `DeviceIoControl` calls.
Static analysis should identify candidates; dynamic logging against controlled,
safe operations validates the protocol.  Avoid exploratory commands that move
film, change lamp state, calibrate, write EEPROM, or reset the factory state.

## Open questions

The remaining questions are now intentionally narrow. They are not safe to
answer by packet replay or by treating inferred names as protocol facts:

- Which raw component maps to which sensor channel, where the meaningful
  12-bit value sits in the recovered 16-bit plane sample, and how PFS spans
  become strip/frame boundaries.
- Which stateful scanner packets select a real F135 scan profile and start the
  physical scan; unknown packets remain permanently excluded from transport.
- Which acquisition-stage producer populates DX/product/specifier/D-min and
  creates the private strip/picture records consumed by Ansel and the renderer.
- The final allocation/ownership details of the PakonImau correction and Ansel
  host contexts, needed to call it without TLA/TLB/TLC.
- The exact intermediate progress scales and the native producer/payload of
  `OrderAnalysisProgress`.

Everything else needed for an initial architecture is now statically bounded:
endpoint selection, driver ring lifetime, raw PFS staging, 16-bit planar
deinterleaving, save-group semantics, output routes, colour-stage ordering,
Ansel inputs, asset/configuration selection, error taxonomy, and callback
queue semantics. The remaining answers require either deeper data-structure
recovery from the native code or controlled runtime observation of an existing
scan; no exploratory hardware commands are justified.
