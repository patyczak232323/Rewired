using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DLS.Tests
{
	// Assembler for the PIX8 computer (Tools/EngineTests/Fixtures/RHDL/PIX8/README.md).
	//
	// One instruction per line; "label:" on its own line or before an instruction; ';' comments;
	// "NAME = value" defines a constant. Numbers in decimal, 0x hex or 0b binary; an immediate
	// byte may be negative (-1 is 0xFF).
	public static class Pix8Asm
	{
		enum Format
		{
			System, // NOP: the function in the low byte
			AluRegReg, // ADD Rd, Rs
			AluReg, // INC Rd
			Imm8, // ADDI Rd, n
			Load, // LD Rd, [a]
			Store, // ST [a], Rd
			LoadReg, // LDR Rd, [Rs]
			StoreReg, // STR [Rd], Rs
			Reg, // PUSH Rd
			RegReg, // PLOT Rd, Rs
			Imm, // COLI n
			Jump // JMP a
		}

		static readonly Dictionary<string, (uint opcode, uint fn, Format format)> Instructions = new(StringComparer.OrdinalIgnoreCase)
		{
			["NOP"] = (0, 0, Format.System), ["HLT"] = (0, 1, Format.System), ["RET"] = (0, 2, Format.System),
			["SHOW"] = (0, 3, Format.System), ["CLS"] = (0, 4, Format.System),

			["MOV"] = (1, 0, Format.AluRegReg), ["ADD"] = (1, 1, Format.AluRegReg), ["ADC"] = (1, 2, Format.AluRegReg),
			["SUB"] = (1, 3, Format.AluRegReg), ["SBC"] = (1, 4, Format.AluRegReg), ["AND"] = (1, 5, Format.AluRegReg),
			["OR"] = (1, 6, Format.AluRegReg), ["XOR"] = (1, 7, Format.AluRegReg), ["CMP"] = (1, 8, Format.AluRegReg),
			["NOT"] = (1, 9, Format.AluReg), ["SHL"] = (1, 10, Format.AluReg), ["SHR"] = (1, 11, Format.AluReg),
			["INC"] = (1, 12, Format.AluReg), ["DEC"] = (1, 13, Format.AluReg),
			["MUL"] = (1, 14, Format.AluRegReg), ["MULH"] = (1, 15, Format.AluRegReg),

			["LDI"] = (2, 0, Format.Imm8), ["ADDI"] = (3, 0, Format.Imm8), ["SUBI"] = (4, 0, Format.Imm8),
			["ANDI"] = (5, 0, Format.Imm8), ["ORI"] = (6, 0, Format.Imm8), ["XORI"] = (7, 0, Format.Imm8),
			["CMPI"] = (8, 0, Format.Imm8),

			["LD"] = (9, 0, Format.Load), ["ST"] = (10, 0, Format.Store),
			["LDR"] = (11, 0, Format.LoadReg), ["STR"] = (12, 0, Format.StoreReg),
			["PUSH"] = (13, 0, Format.Reg), ["POP"] = (14, 0, Format.Reg),

			["PLOT"] = (15, 0, Format.RegReg), ["POS"] = (16, 0, Format.RegReg), ["READ"] = (17, 0, Format.Reg),
			["COL"] = (18, 0, Format.Reg), ["COLI"] = (19, 0, Format.Imm), ["KEYS"] = (20, 0, Format.Reg),
			["RND"] = (21, 0, Format.Reg), ["TONE"] = (22, 0, Format.Reg), ["TONEI"] = (23, 0, Format.Imm),

			["JMP"] = (24, 0, Format.Jump), ["JZ"] = (25, 0, Format.Jump), ["JNZ"] = (26, 0, Format.Jump),
			["JC"] = (27, 0, Format.Jump), ["JNC"] = (28, 0, Format.Jump), ["JN"] = (29, 0, Format.Jump),
			["JNN"] = (30, 0, Format.Jump), ["CALL"] = (31, 0, Format.Jump)
		};

		public const int ProgramWords = 2048;

		public static uint[] Assemble(string source) => Assemble(source, out _);

		public static uint[] Assemble(string source, out Dictionary<string, int> labels)
		{
			labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, int> constants = new(StringComparer.OrdinalIgnoreCase);
			List<(int line, string op, string[] args)> instructions = new();

			string[] lines = source.Replace("\r", "").Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string text = lines[i];
				int comment = text.IndexOf(';');
				if (comment >= 0) text = text.Substring(0, comment);
				text = text.Trim();

				int equals = text.IndexOf('=');
				if (equals > 0)
				{
					string name = text.Substring(0, equals).Trim();
					if (!IsName(name)) throw Error(i, $"'{name}' is not a valid name");
					if (!constants.TryAdd(name, Value(i, text.Substring(equals + 1).Trim(), constants, null))) throw Error(i, $"'{name}' is defined twice");
					continue;
				}

				int colon = text.IndexOf(':');
				if (colon >= 0)
				{
					string label = text.Substring(0, colon).Trim();
					if (!IsName(label)) throw Error(i, $"'{label}' is not a valid label");
					if (!labels.TryAdd(label, instructions.Count)) throw Error(i, $"label '{label}' is defined twice");
					text = text.Substring(colon + 1).Trim();
				}

				if (text.Length == 0) continue;
				int space = text.IndexOfAny(new[] { ' ', '\t' });
				string op = space < 0 ? text : text.Substring(0, space);
				string[] args = space < 0
					? Array.Empty<string>()
					: text.Substring(space + 1).Split(',').Select(a => a.Trim()).ToArray();
				instructions.Add((i, op, args));
			}

			if (instructions.Count > ProgramWords) throw new FormatException($"the program has {instructions.Count} words; the ROM holds {ProgramWords}");

			uint[] words = new uint[instructions.Count];
			for (int n = 0; n < instructions.Count; n++)
			{
				(int line, string op, string[] args) = instructions[n];
				if (!Instructions.TryGetValue(op, out (uint opcode, uint fn, Format format) ins)) throw Error(line, $"unknown instruction '{op}'");

				uint word = ins.opcode << 11;
				switch (ins.format)
				{
					case Format.System:
						Expect(line, args, 0);
						word |= ins.fn;
						break;
					case Format.AluRegReg:
						Expect(line, args, 2);
						word |= Reg(line, args[0]) << 8 | Reg(line, args[1]) << 5 | ins.fn;
						break;
					case Format.AluReg:
						Expect(line, args, 1);
						word |= Reg(line, args[0]) << 8 | ins.fn;
						break;
					case Format.Imm8:
						Expect(line, args, 2);
						word |= Reg(line, args[0]) << 8 | Byte(line, args[1], constants, labels);
						break;
					case Format.Load:
						Expect(line, args, 2);
						word |= Reg(line, args[0]) << 8 | Byte(line, Bracket(line, args[1]), constants, labels);
						break;
					case Format.Store:
						Expect(line, args, 2);
						word |= Reg(line, args[1]) << 8 | Byte(line, Bracket(line, args[0]), constants, labels);
						break;
					case Format.LoadReg:
						Expect(line, args, 2);
						word |= Reg(line, args[0]) << 8 | Reg(line, Bracket(line, args[1])) << 5;
						break;
					case Format.StoreReg:
						Expect(line, args, 2);
						word |= Reg(line, Bracket(line, args[0])) << 8 | Reg(line, args[1]) << 5;
						break;
					case Format.Reg:
						Expect(line, args, 1);
						word |= Reg(line, args[0]) << 8;
						break;
					case Format.RegReg:
						Expect(line, args, 2);
						word |= Reg(line, args[0]) << 8 | Reg(line, args[1]) << 5;
						break;
					case Format.Imm:
						Expect(line, args, 1);
						word |= Byte(line, args[0], constants, labels);
						break;
					case Format.Jump:
						Expect(line, args, 1);
						int target = Value(line, args[0], constants, labels);
						if (target < 0 || target >= ProgramWords) throw Error(line, $"jump target {args[0]} is outside the program");
						word |= (uint)target;
						break;
				}

				words[n] = word;
			}

			return words;
		}

		static bool IsName(string name) => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');

		static void Expect(int line, string[] args, int count)
		{
			if (args.Length != count) throw Error(line, $"expected {count} operand(s), got {args.Length}");
		}

		static string Bracket(int line, string arg)
		{
			if (!arg.StartsWith("[") || !arg.EndsWith("]")) throw Error(line, $"expected [address], got '{arg}'");
			return arg.Substring(1, arg.Length - 2).Trim();
		}

		static uint Reg(int line, string arg)
		{
			if (arg.Length == 2 && (arg[0] == 'R' || arg[0] == 'r') && arg[1] >= '0' && arg[1] <= '7') return (uint)(arg[1] - '0');
			throw Error(line, $"expected a register R0..R7, got '{arg}'");
		}

		static uint Byte(int line, string arg, Dictionary<string, int> constants, Dictionary<string, int> labels)
		{
			int value = Value(line, arg, constants, labels);
			if (value < -128 || value > 255) throw Error(line, $"{arg} does not fit in a byte");
			return (uint)value & 0xFF;
		}

		static int Value(int line, string arg, Dictionary<string, int> constants, Dictionary<string, int> labels)
		{
			bool negative = arg.StartsWith("-");
			string text = negative ? arg.Substring(1).Trim() : arg;
			int value;
			if (constants.TryGetValue(text, out int constant)) value = constant;
			else if (labels != null && labels.TryGetValue(text, out int address)) value = address;
			else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) { }
			else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase) && TryParseBinary(text.Substring(2), out value)) { }
			else if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) { }
			else throw Error(line, $"expected a number, constant or label, got '{arg}'");

			return negative ? -value : value;
		}

		static bool TryParseBinary(string digits, out int value)
		{
			value = 0;
			if (digits.Length == 0 || digits.Length > 16) return false;
			foreach (char ch in digits)
			{
				if (ch != '0' && ch != '1') return false;
				value = value << 1 | (ch - '0');
			}

			return true;
		}

		static FormatException Error(int line, string message) => new($"line {line + 1}: {message}");
	}
}
