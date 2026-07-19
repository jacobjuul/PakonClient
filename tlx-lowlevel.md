# TLX native low-level notes

This file records native calling contracts, memory/layout facts, and analysis
addresses recovered from the Pakon binaries.  It deliberately keeps uncertain
names and hypotheses separate from the implementation guidance in `tlx.md`.

## Confidence convention

- **Confirmed:** directly observed in static code, exported signatures, or a
  local file/fixture.
- **Inferred:** strongly suggested by call order or data flow, but not yet
  validated with a scan trace.
- **Unknown:** do not implement from this note alone.

## How these findings support the .NET 10 migration

The intended end state is a managed .NET 10 scanner application with no
runtime COM requirement. This document is the evidence ledger for that work:
it records exactly what a managed/native compatibility boundary must preserve
while the old algorithms are still used.

The migration has two distinct compatibility layers:

| Layer | Near-term role | Long-term result |
| --- | --- | --- |
| FX35 driver transport | Managed `CreateFile` / `DeviceIoControl` / asynchronous bulk read, using only packets whose bytes and effects have been recovered. | Fully managed scanner acquisition; no TLX/TLC COM dependency. |
| PakonImau / Ansel image processing | Temporary native bridge that recreates the host contexts, buffer adapters, roll lifecycle, and config selection needed by the installed algorithms. | Managed colour, roll-scene balance, adjustment, and output components with regression-tested behaviour. |

`tlx.dll`, TLA, TLB, and TLC are valuable behavioural references and may be
used in isolated test harnesses. They must not define the public API of the
new app. In particular, a legacy name, bit mask, or native offset is not a
license to expose an unclear managed setting: promote it into the new API only
after this file and `tlx.md` establish its observable meaning and required
pipeline stage.

The current native PakonImau exports must **not** be called from C# yet. Their
owning TLC host constructs private contexts and adapters that are only partly
recovered. The documented function-pointer table, renderer call sites, and
Ansel descriptors are the route to removing that limitation safely.

## PakonImau colour entry points

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

## Ansel: recovered contract and behaviour

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

## Runtime Ansel trace: `docs/ansel-diag-example.txt`

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

## Partial Ansel scene-descriptor layout

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

## Renderer scene-balance invocation site (TLA and FX35 TLC)

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

Most importantly, the actual FX35 backend contains the same branch:
`TLC!FUN_1002f2a0` calls its PakonImau host at `+0x64` when the same renderer
state bit `0x20` is set. Its surrounding flow, temporary `0x60`-byte buffer,
source/destination field preparation, replacement-on-success behaviour, and
post-balance rotation path all match the TLA routine. Thus this conclusion is
now confirmed for the scanner in scope, rather than inferred from TLA alone.

### TLA temporary image-buffer object used by scene balance

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

This has not been enabled on the installed system. A future controlled test
may set the value for one short scan, preserve the generated `C:\test.txt`,
then restore/remove the value. It changes diagnostics only according to the
recovered code, but it is still an installed-machine registry modification and
must be treated as a reversible experiment rather than normal application
behaviour.

## TLA colour-host layout (partial)

**Confirmed:** TLA holds two 64 KiB native buffers at host offsets `+0x40` and
`+0x44`.  The colour-negative save call takes the pointer held at `+0x40` as
its fifth argument.  The fourth argument is the matrix-derived save context at
`+0x48`; the equivalent scan context is held at `+0xd8`.

The installed two-column `ClientColNegLut.txt` is parsed into an intermediate
16,384-entry integer curve, but this raw text curve is **not** the fifth
argument to `PIColorCorrectColNegPlanarSave`.  TLA performs subsequent native
lookup/log preparation before the call.

## PakonImau dynamic-host ABI (TLA and FX35 TLC)

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
| `\\.\Pakon135` | `{52B5538B-7926-40AD-9DBE-810228E147AD}` (`TLB.TLAMain.1`) | `TLB.dll` | `tlx!FUN_10005bcc` opens the device with read/write access and shared read/write, immediately closes it, then calls `CoCreateInstance` for this CLSID. |
| `\\.\PakonX35` | `{6449DE65-60A9-4A45-A3A1-337F5E6B41E0}` (`TLC.TLAMain.1`) | `TLC.dll` | `tlx!FUN_10007aa4` uses the same probe/close/create pattern. |

Both backend-creation functions query the same family of interfaces after
creation (main, scan, save, calibration and related interfaces) and install a
small wrapper object. They do not issue scanner commands as part of the probe.
This establishes that an FX35 client goes through **TLC**, not TLB, once the
public `TLXMainClass` has been constructed.

`TLA.dll`, `TLB.dll`, and `TLC.dll` each export only the standard four COM
entry points (`DllCanUnloadNow`, `DllGetClassObject`, `DllRegisterServer`, and
`DllUnregisterServer`).  The scanner implementation is internal to their COM
objects. All three contain the same broad diagnostic vocabulary—driver packet
operations, calibration, PFS buffers, scan/save worker threads, correction,
and PakonImau—so the earlier assumption that TLB was merely a configuration
module was wrong. They are closely related backend builds, not separate
configuration-only libraries.

Their registration and device strings currently support this working model:

| Module | COM ProgID | Device/backend clue | Status |
| --- | --- | --- | --- |
| `TLA.dll` | `TLA.TLAMain.1` | contains `\\.\Loopback` | likely simulation/loopback backend; direct selection path not yet recovered. |
| `TLB.dll` | `TLB.TLAMain.1` | contains `\\.\Pakon135`; selected by the recovered F135 probe | confirmed F135 backend. |
| `TLC.dll` | `TLC.TLAMain.1` | contains `\\.\Pakonx35`; selected by the recovered X35 probe | confirmed FX35 backend. |

The DLLs are not byte-identical (installed sizes: TLA 593,920 bytes, TLB
536,576 bytes, TLC 614,400 bytes), so implementation differences must still
be treated as meaningful. For future work, use TLC as the primary static
analysis target for this scanner, then compare individual routines against TLA
only where TLA already has clearer decompilation.

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
