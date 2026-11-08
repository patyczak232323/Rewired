<p align="center"><img src="Docs/logo.png" width="128" alt="Rewired logo"></p>

<h1 align="center">Rewired</h1>

<p align="center">
A digital logic simulator. Start with NAND gates, end up with a CPU, then play Snake on it.<br>
Windows · Linux · Android · MIT
</p>

<p align="center">
<a href="https://github.com/patyczak232323/Rewired/releases/latest"><img src="https://img.shields.io/github/v/release/patyczak232323/Rewired?label=release" alt="Latest release"></a>
<a href="https://github.com/patyczak232323/Rewired/releases"><img src="https://img.shields.io/github/downloads/patyczak232323/Rewired/total" alt="Downloads"></a>
<a href="LICENSE"><img src="https://img.shields.io/github/license/patyczak232323/Rewired" alt="License"></a>
</p>

<p align="center"><a href="https://patyczak232323.github.io/Rewired/">Website</a> · <a href="https://patyczak232323.github.io/Rewired/play/">Play in the browser</a> · <a href="https://patyczak232323.github.io/Rewired/pl/">Po polsku</a></p>

<p align="center"><img src="Docs/images/snake-pix6.png" alt="Rewired with the Snake project open" width="900"></p>

Rewired is a fork of Sebastian Lague's [Digital Logic Sim](https://github.com/SebLague/Digital-Logic-Sim). The editor and the project format are still mostly his. The simulator underneath is new: it gives the same result every time and stays fast on big circuits like CPUs. On top of that there's a small hardware description language, a colour screen and an Android version.

## Download

**[Try it in your browser](https://patyczak232323.github.io/Rewired/play/)**, nothing to install. Or grab the latest build from [Releases](https://github.com/patyczak232323/Rewired/releases/latest):

- Windows: `DLSRewired-Windows-x64.zip`
- Linux: `DLSRewired-Linux-x86_64.zip`
- Browser: `DLSRewired-WebGL.zip` (unzip and put it on any static host)
- Android: `DLSRewired-Android-arm64.apk` (still experimental)

On Linux you may need to make it executable first:

```bash
chmod +x DLSRewired
./DLSRewired
```

The app checks for new releases on startup and can update itself on Windows, Linux and Android. You can turn that off in Settings. If the install folder isn't writable it just opens the release page. On Android the first update asks for permission to install apps; after that it's one tap.

## What's in it

- The usual editor: place gates, wire them up, pack them into your own chips, nest those as deep as you like.
- A rewritten simulator. It's deterministic, so the same circuit does the same thing every time. Combinational parts get compiled (JIT) and small ones get cached as lookup tables, which makes big designs a lot faster.
- RHDL, a small language for describing chips in text (more below).
- Truth tables for combinational chips. You can save one as a test.
- Breakpoints on pins: stop when a pin changes, rises, falls or hits a value.
- Waveforms, replay and some diagnostics for when a circuit won't settle.
- A 64×64 colour screen (4 bits per channel, double-buffered).
- Export to Verilog, SVG and PNG.
- Ctrl+K searches parts and commands, F1 lists every shortcut.
- A touch UI for phones and tablets, with on-screen keys so you can play Snake without a keyboard.

<p align="center"><img src="Docs/images/main-menu.png" alt="Start screen with recent projects and the example projects" width="700"></p>

## Example projects

Three projects get installed the first time you start Rewired. All of them are NAND gates all the way down.

- **Snake**: a small processor (PIX6) driving the 64×64 screen. Open it, press run, use W A S D.
- **CPU8**: an 8-bit single-cycle CPU with its own ROM and RAM.
- **Core8**: an 8-bit processor core.

## RHDL

Wiring up a 16-bit adder by hand gets old fast. With RHDL you describe a chip in a few lines and RHDL Studio turns it into a normal Rewired chip made of NANDs and your other chips. There's no separate runtime, the result is just a circuit you can open and poke at.

```text
chip FullAdder
    in A, B, CIN
    out SUM, COUT

    ab = A xor B
    SUM = ab xor CIN
    COUT = (A and B) or (CIN and ab)
```

| RHDL Studio | What it builds |
|---|---|
| <img src="Docs/images/rhdl-studio.png" alt="An 8-bit ALU written in RHDL" width="440"> | <img src="Docs/images/rhdl-generated-alu.png" alt="The same ALU as a circuit in the editor" width="440"> |

What it can do:

- `and`, `or`, `xor`, `not`, `nand`, `nor`, `xnor`, plus `+ -`, comparisons and `<< >>`
- `if s then a else b` for muxes
- bit indexing and ranges (`A[3]`, `A[0..3]`), `join(...)` to build buses
- `for i in 0..7 ... end` loops, handy for ripple-carry adders and the like
- calling other chips like functions: `q, nq = DffBit(d, clk)`
- feedback: you can use a name before the line that assigns it, which is how latches and registers are written

## Compatibility with Digital Logic Sim

Most DLS projects open fine, but don't expect them to behave exactly the same. If a circuit relied on evaluation order, races or odd startup states in feedback loops, it may act differently here. That's on purpose: one predictable answer beats whatever order the original happened to visit gates in.

If you find a circuit that clearly behaves wrong, please open an issue and attach the project. Sequential stuff, feedback loops and big nested chips are the most useful to see.

## Building

You need the Unity version from `ProjectSettings/ProjectVersion.txt`.

```bash
git clone https://github.com/patyczak232323/Rewired.git
```

Then open the folder in Unity Hub.

Release builds run on GitHub Actions on self-hosted runners (`Tools/SelfHostedRunners` has the setup script). The workflows need `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`, and for Android `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.

- Every push to `main` updates the `android-unofficial` pre-release with fresh builds (`unofficial-build.yml`).
- `pages.yml` publishes the website in `site/` to GitHub Pages, with the browser build from the latest release under `play/`. It runs after every release and when `site/` changes.
- For a release: bump the version in `VERSION`, `Main.RewiredVersion` and `bundleVersion` in `ProjectSettings/ProjectSettings.asset`, merge to `main` and run `release.yml` (or push a `v*` tag matching `VERSION`).

Current version: 0.7.0.

## Status

Work in progress. The example projects run fine, but big stateful circuits can still hit bugs nobody has found yet.

## Credits and license

Huge thanks to [Sebastian Lague](https://github.com/SebLague) for Digital Logic Sim, which this is built on.

MIT, see [LICENSE](LICENSE).
