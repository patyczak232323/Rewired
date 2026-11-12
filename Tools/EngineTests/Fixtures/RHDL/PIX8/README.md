# PIX8

An 8-bit computer built chip by chip, the way it would be built by hand: every
part below is its own chip, made of smaller chips, down to NAND gates. The
basic parts (gates, adders, multiplexers, decoders, registers, RAM, the program
ROM) are the ones from CPU8; the files here are the parts that are new.

What it has that CPU8 does not:

- a 64 x 64 colour screen, eight keys, a random number generator and a buzzer;
- subroutines: CALL and RET with a return stack of 8 levels (in hardware);
- a data stack in RAM: PUSH and POP;
- an 8 x 8 bit multiplier (MUL gives the low byte, MULH the high byte);
- a negative flag N and the jumps JN / JNN.

Every instruction still takes one rising clock edge.

## Parts

| File | Chip | What it is |
|---|---|---|
| 00_AND_ROW8 | AndRow8 | 8 AND gates: A AND S, bit by bit (one row of the multiplier) |
| 01_MUL8 | Mul8 | 8 x 8 -> 16 bit multiplier: shift-and-add with seven Add8 |
| 02_ALU | Alu | 16 operations, flags Z, N, C and which flags the operation sets |
| 03_FLAGS | Flags | the Z, N and C flip-flops |
| 04_PROGRAM_COUNTER | ProgramCounter | 11-bit PC, also gives PC + 1 for CALL |
| 05_BYTE_CELL | ByteCell | one byte of memory: 8 D flip-flops on their own clock |
| 06_RAM16X8 | Ram16x8 | 16 bytes; the write clock only reaches the byte written |
| 07_RAM256X8 | Ram256x8 | 16 banks of 16 bytes, written on the falling clock edge |
| 08_RETURN_STACK | ReturnStack | 8 return addresses for CALL / RET |
| 09_STACK_POINTER | StackPointer | SP for PUSH / POP (the stack grows down from 0xFF) |
| 10_RGB332_EXPAND | Rgb332Expand | a colour byte RRRGGGBB to 4 bits per channel |
| 11_RGB332_PACK | Rgb332Pack | 4 bits per channel back to RRRGGGBB |
| 12_VIDEO_UNIT | VideoUnit | the pen (X, Y) and the colour register for the screen |
| 13_LFSR16 | Lfsr16 | 16-bit random number generator, one step per clock |
| 14_TONE_UNIT | ToneUnit | the pitch register for the buzzer |
| 15_CONTROL_UNIT | ControlUnit | turns an instruction into the control lines |
| 16_PIX8 | Pix8 | the processor |
| 17_PIX8_COMPUTER | Pix8Computer | processor, screen, keys, buzzer, clock and reset |

The memory gates its clocks, the way real chips do to save power: a byte only
sees a clock edge when it is written. Writing on the falling edge, in the
middle of an instruction, keeps that safe (the address and data have settled
and nothing else changes then). In the simulator it means 256 bytes of RAM cost
almost nothing on the clock ticks that do not write them; PIX8 runs about
twice as fast as it would with every byte on the main clock.

`cube3d.pix8` is the demo in the PIX8 example project: a wireframe cube that
turns in 3D (the multiplier and a sine table in RAM), drawn with a line
subroutine. The assembler is `Tools/EngineTests/Tests/Pix8Asm.cs`.

## Instructions

16-bit words, three formats:

    I  [ op:5 ][ Rd:3 ][ imm8         ]
    R  [ op:5 ][ Rd:3 ][ Rs:3 ][ - ][ fn:4 ]
    J  [ op:5 ][ addr11                ]

| op | Instruction | Does |
|---|---|---|
| 0 | NOP / HLT / RET / SHOW / CLS | system, chosen by the low byte: 0, 1, 2, 3, 4 |
| 1 | `<alu> Rd, Rs` | Rd <- Rd fn Rs (table below) |
| 2 | LDI Rd, n | Rd <- n |
| 3 | ADDI Rd, n | Rd <- Rd + n |
| 4 | SUBI Rd, n | Rd <- Rd - n |
| 5 | ANDI Rd, n | Rd <- Rd AND n |
| 6 | ORI Rd, n | Rd <- Rd OR n |
| 7 | XORI Rd, n | Rd <- Rd XOR n |
| 8 | CMPI Rd, n | flags of Rd - n |
| 9 | LD Rd, [a] | Rd <- RAM[a] |
| 10 | ST [a], Rd | RAM[a] <- Rd |
| 11 | LDR Rd, [Rs] | Rd <- RAM[Rs] |
| 12 | STR [Rd], Rs | RAM[Rd] <- Rs |
| 13 | PUSH Rd | SP <- SP - 1, RAM[SP] <- Rd |
| 14 | POP Rd | Rd <- RAM[SP], SP <- SP + 1 |
| 15 | PLOT Rd, Rs | pen <- (Rd, Rs), draw the colour register there |
| 16 | POS Rd, Rs | pen <- (Rd, Rs) |
| 17 | READ Rd | Rd <- colour shown at the pen (RRRGGGBB) |
| 18 | COL Rd | colour register <- Rd |
| 19 | COLI n | colour register <- n |
| 20 | KEYS Rd | Rd <- keys held: bit 0 D, 1 W, 2 A, 3 S, 4 J, 5 K |
| 21 | RND Rd | Rd <- random byte |
| 22 | TONE Rd | buzzer pitch <- Rd (0 is silent) |
| 23 | TONEI n | buzzer pitch <- n |
| 24 | JMP a | jump |
| 25 / 26 | JZ / JNZ a | jump if Z / not Z |
| 27 / 28 | JC / JNC a | jump if C / not C |
| 29 / 30 | JN / JNN a | jump if N / not N |
| 31 | CALL a | push PC + 1 on the return stack, jump |

ALU functions (`fn`, op 1):

| fn | Instruction | Rd <- | Flags |
|---|---|---|---|
| 0 | MOV | Rs | - |
| 1 | ADD | Rd + Rs | Z N C |
| 2 | ADC | Rd + Rs + C | Z N C |
| 3 | SUB | Rd - Rs | Z N C (C = borrow) |
| 4 | SBC | Rd - Rs - C | Z N C |
| 5 | AND | Rd AND Rs | Z N |
| 6 | OR | Rd OR Rs | Z N |
| 7 | XOR | Rd XOR Rs | Z N |
| 8 | CMP | (Rd - Rs, not written) | Z N C |
| 9 | NOT | NOT Rd | Z N |
| 10 | SHL | Rd << 1 | Z N C (bit 7) |
| 11 | SHR | Rd >> 1 | Z N C (bit 0) |
| 12 | INC | Rd + 1 | Z N C |
| 13 | DEC | Rd - 1 | Z N C |
| 14 | MUL | low byte of Rd x Rs | Z N |
| 15 | MULH | high byte of Rd x Rs | Z N |

The immediate instructions use the same functions with n in place of Rs:
LDI is MOV and leaves the flags alone, like MOV.

The screen: (0, 0) is the bottom-left pixel. PLOT draws into a hidden picture;
SHOW puts it on the screen and CLS clears the hidden one, so a frame can be
drawn over many instructions and appear at once. Colours are one byte,
RRRGGGBB: 0xE0 red, 0x1C green, 0x03 blue, 0xFF white.
