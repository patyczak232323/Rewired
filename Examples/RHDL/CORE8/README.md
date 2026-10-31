# Core8

Core8 is an 8-bit processor core and nothing else: no ROM, no RAM, no clock, no keys. It is built from scratch the way you would build it by hand in the editor, one small chip at a time, each made only from the chips before it: NAND gates become NOT, AND, OR and XOR, those become latches, flip-flops, registers, adders, multiplexers and decoders, and those become the ALU, the program counter, the step counter and the control unit. The registers meet on a shared bus made of 3-state buffers.

Rewired installs it as the **Core8** project. It opens on the `Core8` chip: drive `CLK`, `RESET` and `DATA_IN` from input pins and watch the registers, or place `Core8` in a chip of your own and connect a memory to it.

This is a different processor from CPU8 (`../CPU8`): CPU8 is a single-cycle machine with eight registers and its own ROM and RAM; Core8 is a multi-cycle accumulator machine with one memory bus, the way the classic 8-bit processors work.

## Architecture

- accumulator `A`, ALU operand register `B`, instruction register `IR`, memory address register `MAR`, 8-bit program counter `PC`
- flags Z (zero) and C (carry; after a subtraction C = 1 means no borrow)
- one 8-bit internal bus with four sources (PC, `DATA_IN`, A, ALU) on 3-state buffers
- one memory bus for program and data (256 bytes): `ADDR`, `DATA_IN`, `DATA_OUT`, `RD`, `WE`
- every instruction takes 3 to 6 clock cycles (steps T0..T5); everything changes on the rising edge of `CLK`
- `RESET` held over a rising edge clears every register and starts again at address 0

## Pins

| Pin | Dir | Meaning |
| --- | --- | --- |
| `CLK` | in | clock; each rising edge is one step |
| `RESET` | in | 1 over a rising edge: PC, step, registers and flags go to 0 |
| `DATA_IN` | in | the memory byte at `ADDR`; it must be there in the same step (no wait states) |
| `ADDR` | out | the address the core wants (MAR) |
| `DATA_OUT` | out | the byte to write (the internal bus) |
| `RD` | out | 1 while the core takes `DATA_IN` onto its bus |
| `WE` | out | 1 while memory should store `DATA_OUT` at `ADDR` on the next rising edge |
| `A`, `B`, `PC`, `IR`, `BUS`, `STEP`, `Z`, `C` | out | the inside of the core, to watch it work |
| `HALTED` | out | 1 after `HLT`, until `RESET` |

## Chips

Build them in file order:

| File | Chip | Built from |
| --- | --- | --- |
| `00_NOT` | `Not` | 1 NAND |
| `01_AND` .. `03_XOR` | `And`, `Or`, `Xor` | NAND, Not |
| `04_OR4`, `05_AND3` | `Or4`, `And3` | Or, And |
| `06_MUX` | `Mux` | NAND, Not |
| `07_D_LATCH` | `DLatch` | 4 NAND with feedback, Not |
| `08_DFF` | `Dff` | 2 DLatch (master and slave), Not |
| `09_BIT` | `Bit` | Dff, Mux (load), And (reset) |
| `10_REGISTER8` | `Register8` | 8 Bit |
| `11_HALF_ADDER`, `12_FULL_ADDER` | `HalfAdder`, `FullAdder` | Xor, And, Or |
| `13_ADD8`, `14_INC8` | `Add8`, `Inc8` | 8 FullAdder, 8 HalfAdder |
| `15_MUX2X8`, `16_MUX4X8` | `Mux2x8`, `Mux4x8` | Mux, Mux2x8 |
| `17_DEC2` .. `19_DEC4` | `Dec2`, `Dec3`, `Dec4` | And3, Not, then trees of Dec2 |
| `20_IS_ZERO8` | `IsZero8` | Or4, Or, Not |
| `21_BUS_BIT`, `22_BUS8` | `BusBit`, `Bus8` | 4 3-state buffers on one wire, then 8 of them |
| `23_ALU` | `Alu` | Add8, Xor, And, Or, Mux4x8, IsZero8 |
| `24_PROGRAM_COUNTER` | `ProgramCounter` | Register8, Inc8, Mux2x8 |
| `25_STEP_COUNTER` | `StepCounter` | 3 Bit, 3 HalfAdder |
| `26_CONTROL_UNIT` | `ControlUnit` | Dec4, Dec3, And, Or, Or4 |
| `27_CORE8` | `Core8` | everything above |

## Instructions

The opcode is the high nibble of the first byte (the low nibble is ignored). All instructions except `NOP` and `HLT` have a second byte: a number `n` or an address `a`.

| Byte | Instruction | Effect | Flags | Cycles |
| ---: | --- | --- | --- | ---: |
| `00` | `NOP` | nothing | - | 3 |
| `10 n` | `LDI n` | A <- n | - | 4 |
| `20 a` | `LDA a` | A <- [a] | - | 5 |
| `30 a` | `STA a` | [a] <- A | - | 5 |
| `40 a` | `ADD a` | A <- A + [a] | Z, C | 6 |
| `50 a` | `SUB a` | A <- A - [a] | Z, C | 6 |
| `60 a` | `AND a` | A <- A AND [a] | Z, C=0 | 6 |
| `70 a` | `OR a` | A <- A OR [a] | Z, C=0 | 6 |
| `80 a` | `XOR a` | A <- A XOR [a] | Z, C=0 | 6 |
| `90 n` | `ADI n` | A <- A + n | Z, C | 5 |
| `A0 n` | `CPI n` | flags of A - n, A unchanged | Z, C | 5 |
| `B0 a` | `JMP a` | PC <- a | - | 4 |
| `C0 a` | `JZ a` | jump if Z = 1 | - | 4 |
| `D0 a` | `JNZ a` | jump if Z = 0 | - | 4 |
| `E0 a` | `JC a` | jump if C = 1 (after `CPI n`: A >= n) | - | 4 |
| `F0` | `HLT` | stop until RESET | - | - |

## Steps

| Step | Every instruction | |
| --- | --- | --- |
| T0 | PC -> bus -> MAR | fetch |
| T1 | memory -> bus -> IR, PC + 1 | |
| T2 | PC -> bus -> MAR | operand address (`NOP` ends here, `HLT` stays here) |
| T3 | memory -> bus -> A (`LDI`), B (`ADI`, `CPI`), MAR (`LDA` .. `XOR`) or PC (taken jump); PC + 1 | operand |
| T4 | `LDA`: memory -> A; `STA`: A -> bus -> memory; `ADD` .. `XOR`: memory -> B; `ADI`: ALU -> A; `CPI`: flags | |
| T5 | `ADD` .. `XOR`: ALU -> A | |

The control unit (`26_CONTROL_UNIT.rhdl`) lists each control line as an OR of step-and-instruction terms.

## Connecting memory

`Core8` only asks for bytes; what answers is up to you:

- **ROM only.** Place a `ROM 256×16`, wire `ADDR` to its address and its low output byte to `DATA_IN`, and type the program bytes into the low bytes of its words. Without RAM, `STA` writes go nowhere, so keep variables in `A`.
- **RAM.** Build a RAM from `Register8` chips and a decoder (`../CPU8` shows a 256-byte one): `ADDR` selects the register, `DATA_OUT` is its data, `WE` its write enable, and its output goes to `DATA_IN`. Load the program into it, or put a ROM in the low addresses and RAM in the high ones and choose between them with a multiplexer on `ADDR[7]`.
- **Inputs and outputs.** Decode an address (for example `0xFF`) and use `WE` to latch `DATA_OUT` into a register that drives LEDs or a display: that is memory-mapped output.

Drive `CLK` from a `CLOCK`, and hold `RESET` for one rising edge when the simulation starts (a `PULSE` does that): flip-flops made of NAND gates start in no particular state.

## Writing programs

[`core8asm.py`](core8asm.py) turns assembly into bytes:

```bash
python3 Examples/RHDL/CORE8/core8asm.py Examples/RHDL/CORE8/sum.asm --list
```

Labels end with `:`, comments start with `;` or `#`, numbers can be decimal, `0x` hex or `0b` binary, and `DB` puts bytes into memory. [`sum.asm`](sum.asm) adds 10 + 9 + ... + 1 with its variables in memory and leaves 55 (`0x37`) at address `0x10`:

```
20 10 40 0F 30 10 20 0F 50 11 30 0F D0 00 F0 0A
00 01
```
