# TLX investigation notes

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
| TLX implementation modules | `TLA.dll`, `TLB.dll`, `TLC.dll` in the same directory | Three parallel native COM component implementations, each containing a full scanner/save pipeline. `tlx.dll` selects TLA for the observed F135 endpoint. |
| Image-processing library | `PakonImau.dll` in the same directory | Dynamically loaded by the implementation modules for image-processing and correction stages. |
| Other supporting libraries | `AIDToolkit.dll`, `DMLDICELib.dll` | Supporting image/metadata libraries. |
| FX35 driver | `C:\Code\FX35` | Source is available.  It loads scanner firmware and exposes the scanner transport to user-mode code. |

The installed COM-server folder contains `tlx.dll` (294,912 bytes), `TLA.dll`,
`TLB.dll`, `TLC.dll`, `PakonImau.dll`, and the other libraries above.

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

The COM callback is `ICallBackClient.Awake(int operation, int status)`.  The
callback mechanism and the full method surface are already observable from the
interop assembly and from the existing `PakonLib` source.

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
| `0x00200000` | `UseOrderAnalysisCallbacks` in the interop enum. Its callback behavior has not yet been traced. |

The descriptions other than aggressive framing identify the native state/path
selected, not yet a measured scanning result.

The existing friendly wrapper names some additional flags that are absent from
the installed interop enumeration.  It intentionally rejects these names at
runtime when the installed TLX type library does not define them.  Documentation
must therefore always identify the TLX version used for an observation.

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
three-plane image file/buffer.  A raw `PFSxx.bin` file alone is not sufficient:
it is only a sequential scanner-byte staging stream and still needs its packet
layout, dimensions, channel ordering, and frame crop decoded first.

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

### Direct-driver smoke test

The current machine successfully opened `\\.\PAKON135` and called
`IOCTL_EZUSB_GET_DRIVER_VERSION` directly.  This involved no TLX COM call and
did not send a scanner motion/scan command.  The installed driver returned a
success status with version bytes `0.0.0` and an information count of zero.
That result confirms the device path and IOCTL access; it should not yet be
treated as authoritative driver-version metadata because the installed driver
behavior differs from the current source's documented six-byte response.

## Incremental replacement strategy

1. **Direct transport probe.** Add a small managed `PakonDriverTransport`
   abstraction that opens the correct driver device and exposes only a safe
   metadata probe at first.  Keep `SendAndReceive` internal until packet
   semantics are known.
2. **Trace a harmless query.** Reverse `GetScannerInfo000` through `tlx.dll`
   and its delegated component(s), capture the packet, and replay it via the
   managed transport.  Compare all decoded values with TLX.
3. **Map scan setup.** Trace `ScanPictures`, including each scan-control bit.
   Establish the callback/state sequence and all packets.
4. **Acquire raw scan data.** Implement the packet/data-stream state machine
   and preserve raw data before attempting image processing.
5. **Recreate output processing.** Implement framing, color, scratch removal,
   and saving incrementally, using TLX output as a behavioral oracle.
6. **Remove dependencies deliberately.** Do not remove the COM server from a
   test environment until every required operation is covered by regression
   traces and real-film tests.

## Native-analysis tooling

Ghidra 12.1.2 is now available and has been used for the current `tlx.dll`
analysis.  It can decompile the 32-bit native DLLs, follow imports/exports and
COM vtables, label functions, and preserve an analysis project.  The next
static-analysis step is importing `TLA.dll`, `TLB.dll`, and `TLC.dll` into the
same project after resolving the CLSID/IID hand-off described above.

Useful companion tools are a debugger with process/module support (x64dbg is
adequate even for this 32-bit target) and a way to log `DeviceIoControl` calls.
Static analysis should identify candidates; dynamic logging against controlled,
safe operations validates the protocol.  Avoid exploratory commands that move
film, change lamp state, calibrate, write EEPROM, or reset the factory state.

## Open questions

- Which responsibilities belong to TLA, TLB, and TLC respectively?
- What is the complete packet structure and response/state model?
- How does TLX start and consume bulk scan-data transfers?
- Which exact `Config\ColorCorrection` assets are selected for each negative
  path, especially `_ClientColNegLut.txt` and `_ClientColNegMat.txt`?
- Which corrections are implemented in scanner firmware versus the TLA and
  `PakonImau.dll` host-side layers?
- Can the current 64-bit FX35 driver support every data-streaming behavior that
  TLX expects, without the old x86 shared-memory assumptions?
