# PakonClient

The active implementation is a new .NET 10, COM-free scanner project. Its
first component, `Pakon.Transport`, communicates directly with the installed
Pakon driver and intentionally exposes only documented, read-only metadata
access until scanner packets have been recovered and validated.

The previous TLX-based applications, COM interop wrapper, raw converter, and
initial x86 transport probe are preserved unchanged under `src\Legacy`. They
remain a valuable behavioural reference and test oracle; they are not the
foundation of the new application.

Build the active solution:

```powershell
dotnet build .\Pakon.sln
dotnet run --project .\src\Pakon.Transport.Cli
```

The CLI probes known driver endpoints using only
`IOCTL_EZUSB_GET_DRIVER_VERSION` (`0x222074`). It does not send scanner packet
commands or initialize/move hardware.

`Pakon.LegacyBridge` is the sole temporary exception: an x86 .NET 10 Windows
process that contains all TLC/TLX COM usage. The .NET 10 app communicates with
it using named pipes through `Pakon.LegacyBridge.Client`; it does not load COM
types itself. The bridge preserves the direct-TLC activation/initialization
probe for protocol research and also provides a persistent **TLX** session for
the currently proven scan/save facade. Its scan, save, move, cancel, and
close RPCs use behaviour-based names and scalar values, so TLX types never
become the new application's public API.

Start the bridge first, then initialize and poll the TLX session:

```powershell
dotnet run --project .\src\Pakon.LegacyBridge -- --pipe PakonLegacyBridge
dotnet run --project .\src\Pakon.Transport.Cli -- --initialize-tlx-session
dotnet run --project .\src\Pakon.Transport.Cli -- --tlx-session-status
```

The TLX session moves through `Initializing`, `Ready`, `Scanning`, `Saving`,
`CancellingScan`, `CancellingSave`, `Faulted`, and `Closed`. Callback operation
and status values are retained verbatim in session status for diagnosis.

To produce a controlled reference trace, start the bridge, then run one
controller command. It initializes, waits for TLX, prompts once before film
motion, scans the fixed baseline profile, promotes the roll, saves JPEGs, and
closes the session.

```powershell
dotnet run --project .\src\Pakon.Transport.Cli -- --run-tlx-trace C:\PakonTraces\roll-001
```

The trace contains a versioned `manifest.json`, append-only `events.jsonl`,
metadata snapshots, and runtime evidence (the relevant 32-bit registry values
and readable native logs). It records raw TLX callbacks and drains the native
TLX error queue after a callback error. PFS cleanup is limited to buffers
created or changed during the bridge's own session; existing unchanged buffers
are left alone. The recorder observes the known facade only: it neither hooks
the driver nor records/replays scanner packets or bulk pixels. The JPEG step
uses original dimensions, recorded rotation, colour correction, scene balance,
adjustments, bicubic scaling, quality 90, 300 DPI, and 24-bit output.

## Legacy background

_Intro_

The Kodak Pakon has a SDK/API called TLX installed on the machine. This is a COM-server which contains all the low level functions needed for using a Pakon through software. At some point, there probably was some documentation distributed for using this as a client, and part of that documentation an example application came along, known as the TLXClientDemo. The UI is very bare bones because the Pakon developers just shoved exactly everything the COM-server was capable of doing in there, with a checkbox or radio button for every setting.

Windows COM is old and forgotten but was pretty cool for its age. It is actually possible to do remote COM calls from another machine. And during the early 2000s the Pakon team probably thought that film scanners would have a long future yet, little did they know.. The SDK was probably not very popular among commercial users, just getting started was a bit too much and the payoff was that you could build your own software for scanning film. At some point the Pakon team saw that Microsoft .NET was gaining some serious traction, and being a programming language that is faster to get started with, they built their own interop layer between C# and the COM-server. A C# lib was probably also distributed at some point, luckily the internal Pakon Troubleshooting tool uses this lib and so it can be found as the Pakon.dll in the PTS directory.

Through a disassembler it is possible to reverse engineer the Pakon.dll and get some well needed clues as to how to work with a Pakon through software. And through the reverse engineering software [Ghidra](https://ghidra-sre.org/) it is possible to gain some insights into TLX.dll.

_Legacy findings_

- TLA, TLB, TLC, TLX are COM DLL:s that TLXClientDemo interfaces with. Before TLXClientDemo there was a TLAClientDemo. There is a reference manual for TLA in the docs folder.
- TLXClientDemo is a demonstration app that you can run the Pakon without using the official software.
- Pakon.dll is a .NET library for using TLX that can be found in the PTS folder. This can be quite easily reverse engineered with for example [ILSpy](https://github.com/icsharpcode/ILSpy).
- Development for Windows XP is tricky today. The original advice was to run an old version of VS2010 on the same virtual machine as the Pakon is running on, but the current source has moved toward a newer Windows/.NET development setup while still talking to the old 32-bit TLX COM server.
- The TLX COM server expects its native DLL folder and working directory to be set up correctly. The current console client does that automatically by locating the registered 32-bit `tlx.dll`, or by using `--com-server-dir`.
- The application must run elevated. The console client checks for administrator privileges before opening a scanner session.

_What the legacy client can do_

`src\Legacy` contains:

- `PakonLib`: a C# wrapper around the TLX COM interfaces and related Pakon scanner operations.
- `ConsoleClient`: an x86 .NET Framework 4.8 command line client for operating the scanner.
- `RawImageConverterCli`: a .NET command line converter for Pakon client-memory raw buffers using ImageSharp.
- `docs`: the old TLA demo source and Pakon COM reference PDFs.

The console client can now:

- Initialize and close a TLX scanner session.
- Print scanner model, serial number, hardware capability values, initialization warnings, and TLX errors.
- Print diagnostics for the registered TLX path, Pakon COM server directory, expected native DLLs, config directory, and log directory.
- Run a basic full-roll scan with selectable resolution, film color, film format, strip mode, and scan-control flags.
- Run a one-shot `scan-save` workflow that initializes TLX, scans, moves the roll to the save group, saves files, and exits.
- Save scanned frames through TLX disk save as JPEG, BMP, TIFF, or EXIF.
- Save through TLX client memory as raw buffers, or convert those buffers to PNG, JPEG, TIFF, or BMP with the raw converter.
- Save 16-bit planar raw data as PNG/TIFF output through the converter, with gamma, contrast, saturation, and JPEG quality options.
- Handle black-and-white negative output by inverting saved disk images or raw-converted images.
- Move rolls from the scan group to the save group.
- Print scan-group and save-group counts.
- List and edit save-group picture metadata such as file name, directory, frame number, rotation, and selected/hidden state.
- Print picture framing rectangles.
- Run focus correction, light correction, film track test, film track result lookup, and simple film advance commands.
- Cancel scan and save operations.
- Run in either one-command mode or an interactive `pakon>` shell.

This is still experimental scanner-control software. It is not yet a polished replacement for the official Pakon software, but it is now more than a proof of concept: it can operate the scanner, save images, and exercise a useful chunk of the TLX API from the command line.

_Legacy-client usage_

Start interactive mode:

```powershell
ConsoleClient.exe
```

Basic interactive workflow:

```text
pakon> init
pakon> info
pakon> scan --scan-control scratch,dx
pakon> save --directory C:\Scans --prefix roll01 --format jpeg
pakon> quit
```

One-shot scan and save:

```powershell
ConsoleClient.exe scan-save --resolution base16 --film-color negative --scan-control scratch,dx --directory C:\Scans --prefix roll01 --format jpeg
```

Save through client memory and convert to PNG:

```powershell
ConsoleClient.exe save --directory C:\Scans --prefix roll01 --format png
```

Save client-memory raw buffers directly:

```powershell
ConsoleClient.exe save --directory C:\Raw --prefix roll01 --format raw
```

Print supported option values:

```powershell
ConsoleClient.exe values
```

Print detailed help for a command:

```powershell
ConsoleClient.exe help scan-save
ConsoleClient.exe help save
```

_Legacy-client commands_

- `init`: initialize TLX and keep the scanner session open in interactive mode.
- `info`: print scanner model, serial, and hardware values.
- `diagnostics`: print COM server path checks and file checks.
- `scan`: scan into the TLX scan group.
- `scan-save`: scan, move to save group, save, and exit.
- `save`: save the current save group.
- `pictures`, `picture-info`, `put-picture-info`, `select`, `framing`: inspect and edit save-group metadata.
- `focus-correction`, `light-correction`, `film-track-test`, `film-track-results`, `advance`: scanner maintenance/test operations.
- `values`: print friendly option names accepted by the CLI.
- `commands`: print the command list.

Run `ConsoleClient.exe help` for the complete list and examples.

_Supported values_

Friendly values are available for common TLX enum options, and raw TLX integer values are also accepted for enum-like settings.

- Resolution: `base4`, `base8`, `base16`.
- Film color: `negative`, `positive`, `bw`, `bw-c41` when the local TLX definitions expose them.
- Film format: `35mm`, `24mm`, and several 24mm cartridge/MOF values when available.
- Scan-control flags include values such as `scratch`, `dx`, `aggressive-framing`, `film-drag`, `splice`, `lamp-warmup`, and `prescan` when available.
- Save formats: `jpeg`, `bmp`, `tiff`, `exif`, `png`, `raw`.
- Client-memory formats: `planar16`, `planar8`, `dib8`.
- Raw converter output formats: `png`, `jpeg`, `tiff`, `bmp`, `raw`.

_Possibilites maybe?_

- If the drivers and COM-server can be installed on a newer machine, it would be possible to run a Pakon on something more modern. Maybe not Windows 11 but even 7 would be an upgrade.
- Remote COM-calls from another machine, have the pakon machine to run headless.
- Streamlined client for getting raw scans, together with [my raw converter](https://github.com/eatfrog/pakonrawconverter) you could reduce the number of clicks a lot to get a good nice 16 bit scans.

_Important links_

[Kai Kaufman](https://ktkaufman03.github.io/blog/2022/09/04/pakon-reverse-engineering/) has made important progress on reverse engineering the pakon driver and have gotten them to work on 64bit Windows.
Check the source for his driver [here](https://github.com/ktkaufman03/FX35).

_How to build_

Build `src\PakonClient.sln` on Windows. The scanner-facing projects target .NET Framework 4.8 and use the 32-bit TLX COM server, so build/run the console client as x86. `RawImageConverterCli` is a separate .NET SDK project and is copied beside the console client after it builds.

The console client uses the registered 32-bit TLX COM server path when it can. If needed, pass the COM server directory explicitly:

```powershell
ConsoleClient.exe diagnostics --com-server-dir "C:\Program Files (x86)\Pakon\F-X35 COM SERVER"
```

There is something weird going on with `msvcp71.dll` and `msvcr71.dll`. They need to be in the folder of the executable and also the F-X35 COM SERVER folder. Probably they are in `system32` on a winxp machine but missing on anything more modern? I am not sure what is going on here. If you are seeing a `PartitionAlreadySelected` error it is most likely due to this. You have logs in `C:\Program Files (x86)\Pakon\F-X35 COM SERVER` that you can check for more info.
