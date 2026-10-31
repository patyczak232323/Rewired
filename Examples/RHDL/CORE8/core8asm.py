#!/usr/bin/env python3
"""Assembler for the Core8 processor core (see README.md).

    python3 core8asm.py program.asm          # bytes in hex, 16 per line
    python3 core8asm.py program.asm --list   # address, bytes and source line

Labels end with ':', comments start with ';' or '#', numbers can be decimal,
0x hex or 0b binary. 'DB 1, 2, 3' puts bytes into memory.
"""
import sys

MNEMONICS = ["NOP", "LDI", "LDA", "STA", "ADD", "SUB", "AND", "OR",
             "XOR", "ADI", "CPI", "JMP", "JZ", "JNZ", "JC", "HLT"]
ONE_BYTE = {"NOP", "HLT"}


def assemble(source):
    lines, labels, address = [], {}, 0
    for raw in source.splitlines():
        line = raw.split(";")[0].split("#")[0].strip()
        while ":" in line:
            label, line = line.split(":", 1)
            labels[label.strip().lower()] = address
            line = line.strip()
        if not line:
            continue
        parts = line.replace(",", " ").split()
        lines.append((address, parts, raw.strip()))
        mnemonic = parts[0].upper()
        address += len(parts) - 1 if mnemonic == "DB" else 1 if mnemonic in ONE_BYTE else 2

    def value(text):
        if text.lower() in labels:
            return labels[text.lower()]
        return int(text, 0) & 0xFF

    out, listing = [], []
    for address, parts, raw in lines:
        mnemonic = parts[0].upper()
        if mnemonic == "DB":
            code = [value(p) for p in parts[1:]]
        elif mnemonic in MNEMONICS:
            code = [MNEMONICS.index(mnemonic) << 4]
            if mnemonic not in ONE_BYTE:
                if len(parts) != 2:
                    raise SystemExit(f"{raw}: {mnemonic} needs one operand")
                code.append(value(parts[1]))
        else:
            raise SystemExit(f"{raw}: unknown instruction {parts[0]}")
        out += code
        listing.append((address, code, raw))
    if len(out) > 256:
        raise SystemExit(f"the program is {len(out)} bytes; Core8 addresses 256")
    return out, listing


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    with open(sys.argv[1]) as f:
        code, listing = assemble(f.read())
    if "--list" in sys.argv:
        for address, data, raw in listing:
            print(f"{address:02X}  {' '.join(f'{b:02X}' for b in data):<6}  {raw}")
    else:
        for i in range(0, len(code), 16):
            print(" ".join(f"{b:02X}" for b in code[i:i + 16]))


if __name__ == "__main__":
    main()
