# Research notes and implementation inventory

This file preserves an historical implementation inventory and analysis order.
It is deliberately separate from the scanner reference documentation: it
records how the protocol work was organised, rather than explaining TLX to a
new reader. Its backend-selection row predates the resolved F135 TLA hand-off;
use `tlx.md` and `tlx-lowlevel.md` for the current hardware reference.

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

