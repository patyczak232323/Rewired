# RHDL examples

This is the small curated RHDL example set, written in RHDL 2.0 (see [the RHDL guide](../../Docs/RHDL_GUIDE.md)).

Build them in this order:

1. `Basics/00_AND.rhdl`
2. `Basics/01_HALF_ADDER.rhdl`
3. `Basics/02_FULL_ADDER.rhdl`
4. `Basics/03_MUX4.rhdl`
5. `Basics/04_DFF.rhdl`
6. `Basics/05_REG4.rhdl`
7. `Memory/00_RAM4x4.rhdl`
8. `CPU4/00_CPU4.rhdl`
9. `PIX6/00_REG8.rhdl`, `PIX6/01_PIX6.rhdl`, `PIX6/02_PIX6_COMPUTER.rhdl`, `PIX6/03_SNAKE.rhdl`
10. `CPU8/` in file order, after `Basics/04_DFF.rhdl` (see `CPU8/README.md`)
11. `CORE8/` in file order; it needs nothing else (see `CORE8/README.md`)

The examples intentionally stay small. CPU4 is a 4-bit accumulator processor. PIX6 is a small computer that draws on the 64 x 64 screen; see `PIX6/README.md` for its instructions, two example programs and the Snake game. CPU8 is an 8-bit single-cycle processor with 32 instructions, eight registers and 256 bytes of RAM, built from small chips the way it would be built by hand; Rewired installs it as the **CPU8** project. CORE8 is a processor core alone, built from scratch: NAND gates up to an accumulator machine with a 3-state bus and a step-by-step control unit, with only memory pins outside (no ROM or RAM); Rewired installs it as the **Core8** project.

RAM4x4, CPU4 and PIX6 depend on `DffBit` and `Reg4`, so build the two stateful examples first.
