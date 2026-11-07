# Rewired v0.6.0

Rewired v0.6.0 brings RHDL 2.0, where chips are described in plain words, and two new example projects: CPU8, an 8-bit CPU, and Core8, an 8-bit processor core. On a phone, a touch now snaps to the nearest pin and the bundled Snake can be played with on-screen keys.

## RHDL 2.0

Chips are now described in words instead of Python-like syntax:

```
chip FullAdder
    in A, B, CIN
    out SUM, COUT

    SUM = A xor B xor CIN
    COUT = (A and B) or (CIN and (A xor B))
```

- Pins go on `in` and `out` lines, with widths as numbers (`A: 8`).
- Logic is written in words: `and`, `or`, `xor`, `not`, `nand`, `nor`, `xnor`. The symbols still work.
- `if S then A else B`. A long expression can continue on the next line.
- Bit ranges include both ends (`A[0..3]`), and `join(high, low)` puts values side by side.
- Loops: `for i in 0..7` ... `end`, counting down and with `step`.
- `wire t: 9 = ...` and `const N = 4`.
- A narrower value fills the top bits with 0, so casts are no longer needed.
- Indentation does not matter, and comparisons bind tighter than `and` and `or`.

**Sources in the 1.0 syntax still build.** RHDL Studio converts a saved 1.0 source to the new syntax, comments included, when it opens it. The template, example, quick reference, highlighting, auto-indent and formatter in RHDL Studio, and the [RHDL Guide](RHDL_GUIDE.md), all use the new syntax.

## New example projects

- **CPU8**: an 8-bit single-cycle CPU with 32 instructions, 8 registers, Z and C flags, 16-bit instructions, an 11-bit program counter over eight 256x16 ROMs, and 256 bytes of RAM. Every level is its own chip built from the level below, from NAND gates up to the control unit. A test program is in ROM 0. An assembler is included (`Examples/RHDL/CPU8/cpu8asm.py`).
- **Core8**: an 8-bit accumulator core built the way it would be built by hand: NAND, gates, D latch, flip-flop, registers, adder, ALU, a 3-state internal bus, a step counter and a microstep control unit. It has no ROM, RAM or clock inside, only memory bus pins (`ADDR`, `DATA_IN`, `DATA_OUT`, `RD`, `WE`), so you can connect your own memory.

Both appear in the project list the first time v0.6.0 starts. A project you already have with the same name is not replaced.

## Touch screens

- **Snap to pins**: a touch just outside a part, or on a wire, goes to the nearest pin within about 3.5 mm, and that pin is highlighted. A touch inside a part still moves it. The guide and the wiring hints now say that a wire can be dragged from pin to pin.
- **On-screen keys** for KEY chips: W A S D as a cross and other keys in a row, so Snake can be played without a keyboard.
- Every screen opened from the editor has the same header: *BACK*, the screen's name and, where it applies, a status on the right. Full-screen tools have a solid background, and small popups keep the dimmed board behind them.
- In *Save chip*, *SAVE* and *RENAME* are highlighted and availability is shown as two buttons.
- RHDL Studio has centred button labels and larger code when the phone is held upright. *HOW IT WORKS* wraps its text in portrait, and *Truth table* explains why a chip has no table.

## Desktop

- *Load project* shows each project's chip count and when it was last saved. Double-click a project, or press Enter, to open it.
- The bottom bar shows the project, the chip and *NOT SAVED*. *QUIT* in the editor menu is now *MAIN MENU*.
- The main menu logo and subtitle line up with the title, and the update status no longer runs into the author's name.
- The R in the logo is redrawn as pixel art, in the style of the REWIRED title.

## Security

- The in-app update checks the SHA-256 digest that GitHub publishes for each release file before unpacking or installing it.
- A chip name in a shared project file can no longer make RHDL Studio read or write a source outside the project's folder.
- Saving an RHDL source replaces the file in one step, so an interrupted save no longer loses the source.

## Fixes

- RHDL: in `a, b = Chip(...)` each name now gets the width of its own output. Before, all of them got the width of the first one.
- Chips with more than 1024 gates in a feedback loop, such as a 256-byte RAM, are no longer compiled as one program by the JIT. They ran up to 40 times slower than without the JIT.
- RHDL Studio: the buttons in the right column no longer cover *BUILD & OPEN* and *CLOSE*.
- *BACK* and Escape now close *CUSTOMIZE* on touch screens.

## Compatibility

- Projects from v0.5.2 open unchanged. RHDL sources in the 1.0 syntax still build and are converted when opened in RHDL Studio.
- **Android**: v0.5.2 installs this version from the app with *UPDATE*.

## Known issues

- The WebGL build shows an empty grey screen, because the editor draws with indirect instanced rendering, which WebGL does not support. Use a desktop or Android build for now.

## Downloads

- Windows x64: `DLSRewired-Windows-x64.zip`
- Linux x86_64: `DLSRewired-Linux-x86_64.zip`
- WebGL (browser): `DLSRewired-WebGL.zip` (see *Known issues*)
- Android ARM64: `DLSRewired-Android-arm64.apk` (experimental)

On Android, allow installing apps from your browser or file manager, then open the APK.
