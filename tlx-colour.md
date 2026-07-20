# Pakon colour pipeline and Ansel

This is the implementation-facing reference for Pakon colour processing. It
collects the colour and LUT findings that were previously interleaved with
transport, COM, and save-worker notes. The complete function/offset evidence
remains in [tlx-lowlevel.md](tlx-lowlevel.md); the system-level migration
context remains in [tlx.md](tlx.md).

## Scope and safety boundary

Colour processing is host-side work. It is not an FX35 driver feature and does
not send LUTs, scene-balance data, or output adjustments to the scanner. The
pipeline begins only after raw acquisition data has been decoded into a framed,
planar image.

Do not call PakonImau exports directly yet. Their exported signatures are
known, but their long-lived TLA/TLB/TLC host contexts, ownership rules, and
initialization remain only partly decoded.

## The rendering pipeline

The Pakon look is a pipeline, not a single secret LUT:

```text
scanner samples
  -> raw ring/PFS staging
  -> decoded and framed planar image
  -> negative/reversal correction
  -> Ansel roll and scene balance
  -> colour adjustments/effects
  -> output profile, rotation, scale, encoding or memory delivery
```

For C-41, the first stage compensates for scanner response, orange mask, and
film-base density. Ansel then derives an adaptive scene transform in the
context of the whole roll. Finally, the adjustment stage applies contrast,
sharpening, saturation/monochrome effects, and an ICC-related output stage.

## Save-time controls and dependencies

The renderer applies these independent save-time choices:

| Flag | Native role | Dependency |
| --- | --- | --- |
| `SAV_UseColorCorrection` (`0x10`) | Negative/positive correction using PakonImau contexts and LUT/matrix inputs. | None. |
| `SAV_UseColorSceneBalance` (`0x20`) | Ansel out-of-place planar scene-balance transform. | Requires colour correction. |
| `SAV_UseColorAdjustments` (`0x40`) | Adjustment LUTs, contrast, sharpening, effects, and final profile work. | Requires scene balance and correction. |
| `SAV_UseScratchRemovalIfAvailable` (`0x80`) | Consume eligible scratch/IR information captured during scanning. | Requires scratch-capable acquisition. |

TLA rejects scene balance without correction and adjustments without scene
balance. All three output routes—disk, client memory, and shared memory—share
these processing stages before their final delivery step.

## PakonImau exports and known ABI boundary

TLA/TLB/TLC dynamically load `PakonImau.dll` and resolve these relevant
exports:

| Export | Known role |
| --- | --- |
| `PIColorCorrectColNegPlanarScan` | Scan-side colour-negative correction. |
| `PIColorCorrectColNegPlanarSave` | Renderer/save-side colour-negative correction. |
| `PIColorCorrectColRevPlanar` | Positive/reversal planar correction. |
| `PIAnselStartNewRoll`, `PIAnselAddScene`, `PIAnselEndRoll`, `PIAnselAnalyzeRoll`, `PIAnselColorSceneBalancePlanar` | Stateful roll/scene analysis and balance. |
| `PIColorAdjustPlanar` | Post-correction adjustment and effects chain. |

The save negative-correction call has this recovered shape:

```text
PIColorCorrectColNegPlanarSave(
    planarPixels, width, height, saveCorrectionContext, negativeLookupTable)
```

It operates on an already-decoded planar frame, not a PFS file. The last two
arguments are initialized TLA host state, not public COM values or raw text
assets. The scan-side and save-side negative entry points are distinct internal
recipes despite their similar public shape.

## Ansel: roll-level scene balance

Ansel is PakonImau's stateful roll/scene colour-balancing engine. It is neither
a scanner-firmware feature nor an individual LUT. Its recovered lifecycle is:

1. Start a roll with a processing path.
2. Add every scene with D-min and optional film product/specifier metadata.
3. End and analyze the roll.
4. Apply the selected scene transform to each planar image.

The recovered path values are:

| Value | Name |
| ---: | --- |
| `0` | `DC-Premium` |
| `1`, `2` | `CN-Enhanced` |
| `3` | `CN-Lockbeam` |
| `4` | `CP-Balance` |

For colour negatives Ansel receives three signed 16-bit D-min values plus an
optional product code and generation/specifier code. The descriptor has a
known minimum size of `0x68`; its image-source and ownership fields remain
unresolved. `PIAnselAnalyzeScene` is a stub in the installed binary, so the
working model is roll analysis followed by per-scene application.

### Observable Ansel behaviour

The six-frame diagnostic in `docs\ansel-diag-example.txt` proves that Ansel
uses a `250 x 375`, three-band, 12-bit `StandardAnalysisImage`; it is not
simply applying the final scan pixels to a fixed curve. In that C-41 example,
the selected generic film/FUGC LUTs and contrast stayed constant while Ansel's
per-scene RGB shifts varied. This is direct evidence of content-sensitive,
roll-aware balance.

The five-element classification result, decision-tree values, and shift scales
are observable regression targets, but their semantic units are not decoded.

### Diagnostic output

Setting non-zero `HKLM\SOFTWARE\WOW6432Node\Pakon\PakonIma\Exlax` enables
PakonImau's dormant Ansel diagnostic writer at `C:\test.txt` for its 32-bit
registry view. It is a diagnostic-only feature; normal Windows permissions may
require elevation or a pre-created writable file.

## Colour assets and host initialization

TLA initializes a colour host using `ProgramPath\ColorKodak`, configuration
under `Config\ColorCorrection`, and registry settings below
`HKLM\SOFTWARE\Pakon\PakonIma`. The relevant default assets include:

| Configuration key | Default |
| --- | --- |
| `CRInputProfileFile` | `romm.pf` |
| `InputProfileFile` | `rpd.pf` |
| `OutputProfileFile` | `srgb.pf` |
| `ClientColNegLutFile` | `ClientColNegLut.txt` |
| `ClientColNegMatFile` | `ClientColNegMat.txt` |
| `ColRevLut1File` / `ColRevLutS6` | Positive/reversal LUT assets |

The client negative LUT is a 16,384-entry 14-bit curve. The corresponding
matrix is a 3x4 RGB affine transform stored as twelve coefficients. TLA loads
or obtains calibrated matrix values, then derives native correction contexts
and performs additional LUT preprocessing. Therefore copying those text files
alone is insufficient for an exact managed port.

The installed files are underscore-prefixed (`_ClientColNegLut.txt` and
`_ClientColNegMat.txt`) whereas compiled defaults omit that prefix. The
configuration/fallback selection must be resolved before relying on them in a
standalone implementation.

`Defaults.ini` provides initial per-film adjustment values (RGB, brightness,
contrast, sharpness), selected by numeric colour-negative product code or the
`POSITIVE`, `BnW`, `IMPORTED`, or `NONE` section.

## DX, product metadata, and LUT selection

DX does affect rendering, but indirectly rather than by selecting a complete
spectral profile for each stock:

```text
DX product + generation/specifier
  -> common-ProdCodeTable.dpi -> ISO
  -> fugc-lutMap.map -> contrast class
  -> FUGC LUT and further product-specific Ansel rules
```

Exact product/generation rules override ISO-only rules before a default rule is
used. The FUGC LUTs are readable RGB tables; they select generic tonal/
contrast behaviour. Other modules, including SBA, can also have
product/generation-specific rules.

The scanner-side handoff that fills product/specifier/ISO for every scan is
still unresolved. Preserve that metadata in the new acquisition model, but do
not promise emulsion-specific output until it is traced end-to-end.

## The alternate “premium” path

The installed Ansel data set supports `CN-Premium`: its colour, contrast, and
tone-helper maps differ from `CN-Enhanced`, notably in tone-index and
decision-tree selection. However, static PakonImau code maps both ordinary
colour-negative mode codes to `CN-Enhanced`; we have not proven that
`SCAN_UsePremiumColorPath` reaches `CN-Premium` in this installation.

For the new API, call this an experimental alternate colour path, not a
quality switch or a default C-41 option.

## Offline processing and fixtures

PakonImau can process a decoded three-plane image without a live scanner, but
the native host contexts must first be reproduced or captured. A PFS file is
not sufficient by itself: TLA can deinterleave its 16-bit components into
planes, but the PFS span selection, component assignment, sample scale, and
frame/strip boundary rules are still incomplete.

`C:\Code\PakonImageConverter\raws\2.raw` is a useful Base16 fixture: a
16-byte little-endian header followed by `3000 x 2000` planar RGB pixels, each
channel 16-bit (`48` bits/pixel total). It is appropriate for a future
correction-only experiment after compatible native context setup.

The fixture directory was checked as a group: `2.raw`, `14.raw`, `17.raw`,
`22.raw`, and `34.raw` all have that exact `16 + 3000 * 2000 * 3 * 2` layout.
`different_width.raw` keeps the same 2,000-pixel height and 48-bit planar
layout with width 2,429. This confirms the converter fixture format is a
simple post-deinterleave three-plane container with no row padding; it must
not be confused with the raw PFS staging format or used to infer PFS packet
boundaries.

## Controlled modern-film extensions

Adding a modern film mapping is technically plausible only in a copied/test
Ansel data path. Start with an exact product/generation/ISO rule mapped to an
existing contrast class. A new FUGC LUT also appears structurally supported,
but requires an existing table shape, a new contrast mapping, and A/B scans.
This changes only part of the rendering model; SBA, PNR, defaults, and other
Ansel modules can still depend on product/ISO.

## What remains before an in-house colour port

1. Decode the PFS stream into planar frames.
2. Recover the colour-host context construction and ownership rules.
3. Trace scanner metadata through to Ansel scene descriptors.
4. Reproduce correction-only output against a known Base16 fixture.
5. Add roll-level Ansel regression tests using diagnostic output and legacy
   rendered images.
6. Replace adjustments and file encoding only after intermediate stages match.

For exact function addresses, descriptor offsets, and dynamic-host slots, see
[tlx-lowlevel.md](tlx-lowlevel.md). For scan transport and the migration plan,
see [tlx.md](tlx.md).
