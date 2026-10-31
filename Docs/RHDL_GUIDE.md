# RHDL 2.0 — describe a chip in plain words

RHDL turns a few lines of text into an ordinary Rewired chip made of NAND gates and the chips you already have. You name the chip, list its pins, and write what each output is.

```text
chip FullAdder
    in A, B, CIN
    out SUM, COUT

    ab = A xor B
    SUM = ab xor CIN
    COUT = (A and B) or (CIN and ab)
```

That is the whole language in one example:

- `chip Name` starts a chip.
- `in` lists the input pins and `out` the output pins.
- Every other line gives something a value with `=`.
- `#` starts a comment.

Indentation is only for reading; RHDL ignores it. A chip ends where the next one starts or at the end of the file (an `end` line may close it too).

## Pins and widths

A pin is one bit. Write `: 8` after a name to give it 8 bits:

```text
chip Adder
    in A: 8, B: 8
    out Y: 8

    Y = A + B
```

Chip pins can be 1, 4 or 8 bits. Wires inside the chip can have any width from 1 to 32. Several `in` or `out` lines are fine; the pins keep the order they are written in.

## Wires

`name = ...` makes a wire. Its width comes from its value. To choose the width yourself, start the line with `wire`:

```text
chip Carry
    in A: 4, B: 4
    out SUM: 4, COUT

    wire total: 5 = A + B
    SUM = total[0..3]
    COUT = total[4]
```

Every line is hardware that exists at the same time as all the others, so the order of the lines does not matter: a wire can be used above the line that gives it its value. (When the same name gets a value several times, see *Repeating* below.)

## Logic

```text
chip Logic
    in A, B, C
    out both, either, different, inverted, majority, nand_ab

    both = A and B
    either = A or B
    different = A xor B
    inverted = not A
    majority = (A and B) or (A and C) or (B and C)
    nand_ab = A nand B
```

`and`, `or`, `xor`, `not`, `nand`, `nor` and `xnor` work on every bit of a value, so `A and B` with two 8-bit inputs gives 8 bits. `not` applies to the value right after it: `not A and B` is `(not A) and B`.

Programmers can use the short forms `&`, `|`, `^` and `~` instead of `and`, `or`, `xor` and `not`.

## Numbers and arithmetic

```text
chip Math
    in A: 8, B: 8
    out sum: 8, diff: 8, same, less, double: 8

    sum = A + B
    diff = A - B
    same = A == B
    less = A < B
    double = A << 1
```

- `5`, `0b0101` (binary) and `0x5` (hex) are numbers. Binary and hex also give a width: `0b0101` is 4 bits, `0x0F` is 8. `true` and `false` are 1 and 0.
- `+` and `-` wrap around at the width of the result: with 8 bits, `200 + 100` is `44`. Give the result one more bit to keep the carry, as in `wire total: 9 = A + B`.
- `==`, `!=`, `<`, `>`, `<=` and `>=` compare unsigned values and give one bit.
- `<<` and `>>` shift by a fixed number of bits; the new bits are 0.
- A value narrower than where it goes gets 0 bits at the top, so `Y = A` works when `A` has 4 bits and `Y` has 8.

Comparisons bind tighter than `and` and `or`, so `OP == 2 and EN` means what it says. Use brackets when in doubt.

## Choosing between values

`if ... then ... else` builds a multiplexer:

```text
chip Select
    in A: 4, B: 4, SEL
    out Y: 4

    Y = if SEL then A else B
```

The condition must be one bit. Choices can be chained, one per line:

```text
chip MiniAlu
    in A: 4, B: 4, OP: 4
    out Y: 4

    Y = if OP == 0 then A + B
        else if OP == 1 then A - B
        else if OP == 2 then A and B
        else A or B
```

A line continues on the next one when it ends with an operator or `=`, when the next line starts with `else`, `then` or an operator, and inside brackets.

## Bits and buses

```text
chip Bits
    in A: 8, B: 8
    out low: 4, high: 4, top, joined: 8

    low = A[0..3]
    high = A[4..7]
    top = A[7]
    joined = join(A[4..7], B[0..3])
```

- `A[3]` is bit 3. Bit 0 is the lowest.
- `A[0..3]` is bits 0 to 3, both included. `A[7..4]` is the same as `A[4..7]`. `A[4..]` goes from bit 4 to the top.
- `join(high, ..., low)` puts values side by side into one bus, the first one at the top, the way numbers are written. Numbers inside `join` need a width, so write them in binary: `join(A[0..6], 0b0)`.

A wire can also be built bit by bit. Give it a width first (outputs already have one):

```text
chip Reverse
    in A: 4
    out Y: 4

    for i in 0..3
        Y[i] = A[3 - i]
    end
```

## Repeating

`for i in 0..3` repeats the lines up to `end` for `i` = 0, 1, 2 and 3. `for i in 3..0` counts down, and `step 2` takes every second number. Repeating builds the hardware several times; it does not run in time.

When a name gets a value several times, each use reads the latest value above it. That is how chains such as a ripple-carry adder are written:

```text
chip Add4
    in A: 4, B: 4, CIN
    out S: 4, COUT

    carry = CIN
    for i in 0..3
        S[i] = A[i] xor B[i] xor carry
        carry = (A[i] and B[i]) or (carry and (A[i] xor B[i]))
    end
    COUT = carry
```

## Constants

`const` gives a number a name. Constants are worked out when the chip is built, so they can also choose what gets built: `Y = if INVERT then not A else A` builds only one of the two.

```text
const WIDTH = 4

chip Ones
    out Y: 4

    Y = (1 << WIDTH) - 1
```

## Using other chips

Every chip in the library, and every chip you have built, can be used like a function. Inputs are given in order, or by pin name:

```text
chip TwoNands
    in A, B, C
    out Y

    t = NAND(A, B)
    Y = NAND(t, C)
```

- A chip with one output gives its value directly.
- A chip with several outputs gives them in order: `s, c = HalfAdder(A, B)`. Write `_` for an output you do not need.
- Give a chip a name to reach its pins later: `h = HalfAdder(A, B)`, then `h.SUM` and `h.CARRY`. Its inputs can also be connected afterwards: `h.A = x`.
- Pin names are written without spaces: the NAND pins `IN A` and `IN B` are `IN_A` and `IN_B` (or just `A` and `B`).

Built-in chips are called `NAND`, `TRISTATE`, `CLOCK`, `PULSE`, `ROM`, `KEY`, `LED`, `SEVEN_SEGMENT`, `RGB_DISPLAY`, `RGB_DISPLAY_64`, `DOT_DISPLAY` and `BUZZER`.

`RGB_DISPLAY_64` is a 64 x 64 screen with 4 bits per colour. Its inputs are `X`, `Y` (8 bits, the low 6 are used; `(0, 0)` is the bottom-left pixel), `RED`, `GREEN`, `BLUE` (4 bits each), `RESET`, `WRITE`, `REFRESH` and `CLOCK`. On a rising clock edge `RESET` clears the hidden picture, otherwise `WRITE` draws the colour at `(X, Y)` into it; `REFRESH` then shows the hidden picture. Its outputs `R_OUT`, `G_OUT` and `B_OUT` give the colour shown at `(X, Y)`.

Several `TRISTATE` buffers may drive the same wire, which is how a shared bus is made:

```text
chip SharedBus
    in A, B, SEL
    out Y

    bus = TRISTATE(A, SEL)
    bus = TRISTATE(B, not SEL)
    Y = bus
```

## Memory

A latch is two NAND gates that feed each other. Because a wire can be used above the line that gives it its value, the chip is written the way it is drawn:

```text
chip SrLatch
    in S_N, R_N
    out Q, QN

    Q = NAND(S_N, QN)
    QN = NAND(R_N, Q)
```

Every `NAND(...)` written in the source is exactly one gate, kept as written. The `Examples/RHDL` folder builds a D flip-flop, a 4-bit register, a small RAM and CPUs this way.

## What gets built

Rewired's only logic gate is the NAND, and RHDL builds everything from it:

| Expression | NAND gates |
| --- | --- |
| `not a` | 1 |
| `a nand b` | 1 |
| `a and b` | 2 |
| `a or b` | 3 |
| `a xor b` | 4 |
| `if s then a else b` | 3 per bit, plus 1 for `not s` |
| full adder (one bit of `+`) | 9 |

Constants are folded, identical gates are shared and gates nobody reads are removed, so `A and 0b1111` is just a wire. Multi-bit pins are split into bits only where single bits are needed.

The generated chip is drawn like a hand-made schematic: signals flow from left to right, each bit of a bus operation sits on its own row with the most significant bit at the top, wires run horizontally into pins and their vertical parts get separate tracks, and feedback loops back over the parts it skips.

## Messages

Mistakes are reported with the line and a hint, for example:

- `Unknown name 'totl'. Did you mean 'total'?`
- `This value is 8 bits but 'Y' is 4. Take the bits you need, e.g. [0..3].`
- `This 'for' has no 'end'. Close the repeated lines with 'end'.`
- `'if' cannot start a line: all lines are hardware that works at the same time. Choose between values with 'Y = if select then A else B'.`

## RHDL Studio

- Enter after `chip` or `for` indents the next line; `end` moves back.
- `Ctrl+/` toggles `#` comments.
- `Ctrl+Shift+F` tidies the indentation.
- `Ctrl+B` / `Ctrl+Enter` builds.
- `F5` builds and opens the chip.
- `F8` / `Shift+F8` jumps between errors.

Write one chip per source. Build it, then use it by name from the next one.

## Quick reference

| What | How |
| --- | --- |
| Start a chip | `chip Name` |
| Pins | `in A, B: 8` and `out Y: 8` |
| Wire | `t = A and B`, or `wire t: 9 = A + B` |
| Logic | `and or xor not nand nor xnor` |
| Arithmetic | `+ - << >>`, compare with `== != < > <= >=` |
| Choice | `if S then A else B` |
| Bits | `A[3]`, `A[0..3]`, `join(high, low)` |
| Repeat | `for i in 0..7` ... `end` |
| Constant | `const WIDTH = 8` |
| Other chips | `y = NAND(a, b)`, `s, c = HalfAdder(a, b)`, `h.SUM` |

## Coming from RHDL 1.0

RHDL 2.0 replaces the Python-style syntax of RHDL 1.0. Sources written for 1.0 still build, and RHDL Studio converts them to 2.0 when it opens them.

| 1.0 | 2.0 |
| --- | --- |
| `circuit Adder(A: u8, B: u8) -> Y: u8:` | `chip Adder`, `in A: 8, B: 8`, `out Y: 8` |
| `&`, `\|`, `^`, `~` | `and`, `or`, `xor`, `not` (the symbols still work) |
| `A if S else B` | `if S then A else B` |
| `A[0:4]` (bits 0 to 3) | `A[0..3]` |
| `cat(a, b)` | `join(a, b)` |
| `u8(x)` | not needed: narrower values get 0 bits at the top |
| `t: u9 = A + B` | `wire t: 9 = A + B` |
| `for i in range(4):` and an indented block | `for i in 0..3` ... `end` |
| `WIDTH = 4` at the top of the file | `const WIDTH = 4` |

## Coming from RHDL 0.6

RHDL 0.6 used `circuit X`, `input A: 4`, `output Y`, `signal t`, `component g : NAND(...)` and `connect a -> g.IN_A`. In RHDL 2.0 these are `chip X`, `in A: 4`, `out Y`, `wire t: 4`, `y = NAND(a, b)` and `g.IN_A = a`.

## Examples

The curated `Examples/RHDL/` set progresses through `And2 -> HalfAdder -> FullAdder -> Mux4 -> DffBit -> Reg4`, then adds a separate `Ram4x4` memory example, `CPU4`, a 4-bit accumulator processor, `PIX6`, a small computer that draws on the 64 x 64 screen, and `CPU8`, an 8-bit processor with eight registers and 256 bytes of RAM, built one small chip at a time.
