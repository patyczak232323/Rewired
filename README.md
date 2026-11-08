<p align="center"><img src="Docs/logo.png" width="128" alt="Rewired logo: a chip with a gold R"></p>

<h1 align="center">Rewired — Digital Logic Simulator</h1>

<p align="center">
<b>Build logic gates, adders, registers, RAM and whole CPUs from NAND gates — then play Snake on the computer you built.</b><br>
Free and open source · Windows · Linux · Android · MIT licence
</p>

<p align="center">
<a href="https://github.com/patyczak232323/Rewired/releases/latest"><b>⬇ Download the latest release</b></a>
</p>

<p align="center">
<a href="https://github.com/patyczak232323/Rewired/releases/latest"><img src="https://img.shields.io/github/v/release/patyczak232323/Rewired?label=release" alt="Latest release"></a>
<a href="https://github.com/patyczak232323/Rewired/releases"><img src="https://img.shields.io/github/downloads/patyczak232323/Rewired/total" alt="Downloads"></a>
<a href="https://github.com/patyczak232323/Rewired/actions/workflows/simulation-regression.yml"><img src="https://github.com/patyczak232323/Rewired/actions/workflows/simulation-regression.yml/badge.svg" alt="Regression"></a>
<a href="https://github.com/patyczak232323/Rewired/actions/workflows/unity-tests.yml"><img src="https://github.com/patyczak232323/Rewired/actions/workflows/unity-tests.yml/badge.svg" alt="Unity tests"></a>
<a href="LICENSE"><img src="https://img.shields.io/github/license/patyczak232323/Rewired" alt="License"></a>
<a href="https://github.com/patyczak232323/Rewired/stargazers"><img src="https://img.shields.io/github/stars/patyczak232323/Rewired?style=social" alt="Stars"></a>
</p>

<p align="center"><img src="Docs/images/snake-pix6.png" alt="Rewired editing the bundled Snake project: the parts panel on the left, the PIX6 processor driving a 64 by 64 colour screen in the middle, the details panel on the right and the top bar with undo, run, step, search and zoom" width="900"></p>
<p align="center"><i>The Snake project that ships with Rewired: a small processor made of logic gates, a 64×64 screen and four keys.</i></p>

**Rewired** is a free, open-source **digital logic simulator** and **logic gate circuit simulator** for learning **digital electronics**, **Boolean logic** and **computer architecture**. Start with a single NAND gate, build your own chips, and keep going until you have an ALU, registers, RAM and a working CPU. Rewired is a desktop and Android app built with Unity, with a deterministic, JIT-accelerated simulation engine and **RHDL**, a small **hardware description language** in plain words that turns source code into gate-level circuits.

Rewired is based on Sebastian Lague's [Digital Logic Sim](https://github.com/SebLague/Digital-Logic-Sim), but uses its own simulation engine and should be treated as an independent simulator rather than a drop-in compatible replacement.

## Screenshots

| Describe hardware in a few plain lines… | …and get a real gate-level circuit |
|---|---|
| <img src="Docs/images/rhdl-studio.png" alt="RHDL Studio: an 8-bit ALU written in RHDL, with the compiler status and a quick reference" width="440"> | <img src="Docs/images/rhdl-generated-alu.png" alt="The ALU from RHDL Studio built as a circuit of NAND gates in the editor" width="440"> |

<p align="center"><img src="Docs/images/main-menu.png" alt="Rewired start screen: recent projects and the bundled Snake, CPU8 and Core8 examples next to the main buttons" width="700"></p>

## Who it is for

- **Students** learning logic gates, truth tables, flip-flops and how a CPU works, for example alongside *Nand2Tetris*, *Digital Design and Computer Architecture* or *But How Do It Know?*
- **Teachers** who want a free logic simulator for digital electronics and computer science classes
- **Hobbyists** who want to design an 8-bit computer, build a CPU from scratch or make games that run on their own hardware
- **Players of Digital Logic Sim** who want bigger, faster circuits, a hardware description language and Android support

## What you can build

Rewired is intended for learning and experimenting with **digital electronics, Boolean logic, computer architecture and CPU design**.

You can build:

- logic gates and combinational circuits
- multiplexers, adders and ALUs
- latches, flip-flops and registers
- counters and state machines
- RAM and other gate-level memory structures
- custom reusable chips
- small CPUs and complete computer architectures
- source-generated circuits with RHDL

The repository includes a compact example progression ending with a **4-bit CPU** and a separate **4×4-bit RAM** example.

## Main features

- graphical digital circuit editor
- deterministic event-driven simulation
- compiled circuit/netlist topology
- dirty-gate scheduling
- fixed-point settling for signal propagation
- support for deeply nested Custom Chips
- global Custom Chips shared safely between projects
- combinational JIT acceleration
- feedback JIT for supported cyclic gate networks
- persistent FULL LUT cache for suitable combinational circuits
- simulation diagnostics and profiling
- waveform capture and deterministic replay
- non-convergence diagnostics
- **RHDL 2.0** hardware description language in plain words (`chip`, `in`, `out`, `and`, `or`, `if ... then ... else`)
- truth table for any combinational chip, recordable as a regression test
- breakpoints: pause when a pin changes, rises, falls or reaches a value
- export to structural Verilog, SVG schematics and PNG screenshots
- 64 x 64 colour screen (4 bits per channel, double-buffered) for games and graphics demos
- Snake, CPU8 and Core8 example projects, installed on first start and listed on the start screen
- desktop editor with a parts panel, a details panel for the selection, search for parts and commands (Ctrl+K) and a list of every shortcut (F1)
- updates from GitHub Releases inside the app on Windows, Linux and Android
- Windows x64, Linux x86_64, WebGL (browser) and Android ARM64 (experimental) release builds
- touch interface for Android tablets and phones, held sideways or upright (Settings → Screen): drag parts from a categorised panel, tap a part for its actions, one-finger panning, on-screen keys for KEY chips (play Snake without a keyboard) and a step-by-step guide

## Download

The latest prebuilt version is available in **GitHub Releases**:

**[Download the latest Rewired release](https://github.com/patyczak232323/Rewired/releases/latest)**

Release packages:

- Windows x64: `DLSRewired-Windows-x64.zip`
- Linux x86_64: `DLSRewired-Linux-x86_64.zip`
- WebGL (browser): `DLSRewired-WebGL.zip`. Unzip it and serve the folder from any static web host.
- Android ARM64 (experimental): `DLSRewired-Android-arm64.apk`

On Linux, after extracting the archive, the executable may need permission:

```bash
chmod +x DLSRewired
./DLSRewired
```

### Updating

Rewired checks GitHub for a newer release when it starts (turn this off in Settings). The bottom bar of the main menu, or the header on Android, then offers the update:

- **Windows and Linux**: *Update* downloads the release, quits Rewired, copies the new files over the installed ones and starts the new version. If the install folder is not writable, the release page opens instead.
- **Android**: *Update* downloads the new APK and installs it, then Rewired closes; open it again to use the new version. The first time, Android asks to allow installs from Rewired and to confirm the update. After that, Android 12 and newer install updates without asking. Projects are kept.
- **WebGL**: the site is updated by whoever hosts it.

## RHDL — hardware description language

Rewired includes **RHDL Studio**, a source-driven circuit generator. RHDL describes hardware in a few plain lines and builds an ordinary Rewired chip from NAND gates and your other chips; it does not use a separate simulation runtime.

Example:

```text
chip FullAdder
    in A, B, CIN
    out SUM, COUT

    ab = A xor B
    SUM = ab xor CIN
    COUT = (A and B) or (CIN and ab)
```

RHDL supports:

- logic in words: `and`, `or`, `xor`, `not`, `nand`, `nor`, `xnor`, plus `+ -`, comparisons and `<< >>`
- `if select then a else b` multiplexing
- bits and ranges (`A[3]`, `A[0..3]`) and `join(...)` to build buses
- `for i in 0..7` ... `end` loops and variables that build chains such as ripple-carry adders
- calling other chips like functions, with tuple unpacking (`q, nq = DffBit(d, clk)`)
- feedback and memory: a name can be used above the line that assigns it
- constant folding, shared gates and readable left-to-right schematics

## Bundled projects

Rewired installs three example computers, all built from NAND gates:

- **Snake** on the PIX6 computer and its 64 × 64 colour screen: open it and play with W A S D
- **CPU8**, an 8-bit single-cycle CPU with its own ROM and RAM
- **Core8**, an 8-bit processor core

## Simulation engine

Rewired's runtime is organized around a single integration boundary:

```text
Editor / Project / UI
        |
        v
   RewiredEngine
        |
        +-- deterministic runtime
        +-- simulation graph/backend
        +-- JIT / feedback JIT
        +-- FULL LUT cache
        +-- diagnostics / replay / waveform
```

The goal is predictable simulation semantics while still allowing acceleration of suitable circuit regions.

## Compatibility with Digital Logic Sim

Rewired keeps much of the original Digital Logic Sim editor workflow, project structure and visual language, so many existing projects can be opened.

However, exact simulation behaviour is **not guaranteed to match** Digital Logic Sim.

In particular, circuits that depend on:

- traversal order
- race conditions
- propagation timing quirks
- feedback initialization side effects

may behave differently under Rewired's deterministic simulation model.

Rewired is therefore best considered a separate **digital circuit simulator** derived from the original project.

## Development

Current version: **Rewired v0.7.0**

The Unity version is defined in `ProjectSettings/ProjectVersion.txt`.

Clone:

```bash
git clone https://github.com/patyczak232323/Rewired.git
cd Rewired
```

Builds for Windows, Linux, WebGL and Android run on GitHub Actions (`.github/workflows`). Unity runs on the self-hosted runners labelled `self-hosted, trusted`; `Tools/SelfHostedRunners` sets them up. The workflows need the secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`, and for Android the release key `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.

- Every change to `main` refreshes the `android-unofficial` pre-release with development builds for all platforms (`unofficial-build.yml`).
- To release, set the new version in `VERSION`, `Main.RewiredVersion` and `bundleVersion` in `ProjectSettings/ProjectSettings.asset`, merge to `main`, then run `release.yml` on `main` from the Actions tab (or push a tag such as `v0.8.0` that matches `VERSION`).

## Project status

Rewired is under active development. Complex stateful circuits and very large designs may still expose engine bugs or unsupported edge cases.

Bug reports and reproducible test circuits are useful, especially for:

- sequential logic
- feedback networks
- large nested chips
- CPU designs
- imported Digital Logic Sim projects

## Credits

Rewired is based on [Sebastian Lague's Digital Logic Sim](https://github.com/SebLague/Digital-Logic-Sim).

The original project provides the foundation for much of the editor, project format and surrounding application code. Rewired develops a separate simulation runtime, diagnostics tooling, acceleration paths and RHDL authoring workflow on top of that foundation.

## License

Licensed under the **MIT License**. See [LICENSE](LICENSE).
