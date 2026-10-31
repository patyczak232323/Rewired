using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.RHDL;
using DLS.SaveSystem;
using DLS.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace DLS.Tests
{
	// Core8 from Examples/RHDL/CORE8: an 8-bit processor core built from scratch the way it would
	// be built by hand (NAND -> gates -> latches and flip-flops -> registers -> adder -> ALU, a
	// 3-state bus, a step counter and a control unit). It has no memory of its own; these tests
	// play the part of a 256-byte memory on its pins and run programs on the real engine.
	//
	// The Core8 project that comes with Rewired (Assets/Resources/BundledProjects/Core8) is built
	// from these sources. After changing them, rewrite its files with:
	//   REWIRED_WRITE_BUNDLED=1 dotnet test Tools/EngineHarness --filter Core8ExampleTests
	public class Core8ExampleTests
	{
		static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Examples", "RHDL", "CORE8"));

		// Every source, in file order.
		static string[] Sources => Directory.GetFiles(Folder, "*.rhdl").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();

		[TearDown]
		public void TearDown() => RewiredEngine.Reset();

		static (ChipLibrary library, List<ChipDescription> chips) BuildAll()
		{
			ChipLibrary library = TestLibrary.Create();
			List<ChipDescription> chips = new();
			foreach (string source in Sources)
			{
				RhdlCompileResult result = RhdlCompiler.Compile(File.ReadAllText(source), library);
				Assert.IsTrue(result.Success, $"{Path.GetFileName(source)} failed to compile:\n" + string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
				library.NotifyChipSaved(result.Description, false);
				chips.Add(result.Description);
			}

			return (library, chips);
		}

		// ---- Assembler (the same rules as Examples/RHDL/CORE8/core8asm.py) ----

		static readonly string[] Mnemonics = { "NOP", "LDI", "LDA", "STA", "ADD", "SUB", "AND", "OR", "XOR", "ADI", "CPI", "JMP", "JZ", "JNZ", "JC", "HLT" };

		public static byte[] Assemble(string source)
		{
			List<string[]> lines = new();
			Dictionary<string, int> labels = new(StringComparer.OrdinalIgnoreCase);
			int address = 0;
			foreach (string raw in source.Split('\n'))
			{
				string line = raw.Split(';', '#')[0].Trim();
				while (line.Contains(':'))
				{
					int colon = line.IndexOf(':');
					labels[line[..colon].Trim()] = address;
					line = line[(colon + 1)..].Trim();
				}

				if (line.Length == 0) continue;
				string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
				lines.Add(parts);
				address += parts[0].Equals("DB", StringComparison.OrdinalIgnoreCase) ? parts.Length - 1
					: parts[0].ToUpperInvariant() is "NOP" or "HLT" ? 1 : 2;
			}

			int Value(string text)
			{
				if (labels.TryGetValue(text, out int label)) return label;
				if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.Parse(text[2..], NumberStyles.HexNumber);
				if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt32(text[2..], 2);
				return int.Parse(text);
			}

			List<byte> bytes = new();
			foreach (string[] parts in lines)
			{
				string mnemonic = parts[0].ToUpperInvariant();
				if (mnemonic == "DB")
				{
					foreach (string b in parts.Skip(1)) bytes.Add((byte)Value(b));
					continue;
				}

				int op = Array.IndexOf(Mnemonics, mnemonic);
				if (op < 0) throw new FormatException("unknown instruction " + parts[0]);
				bytes.Add((byte)(op << 4));
				if (mnemonic is not ("NOP" or "HLT")) bytes.Add((byte)Value(parts[1]));
			}

			return bytes.ToArray();
		}

		// ---- The core with 256 bytes of memory on its pins ----

		sealed class Board
		{
			public readonly TestCircuit Core;
			public readonly byte[] Memory = new byte[256];
			public int Writes;

			public Board(string program)
			{
				(ChipLibrary library, List<ChipDescription> chips) = BuildAll();
				byte[] code = Assemble(program);
				Array.Copy(code, Memory, code.Length);
				Core = new TestCircuit(chips.Single(c => c.Name == "Core8"), library);
				Core.Set("RESET", 1);
				Tick();
				Core.Set("RESET", 0);
			}

			public uint Get(string name) => Core.Get(name);

			// One clock cycle (one step of an instruction): the memory answers ADDR and, on the
			// rising edge, stores DATA_OUT if WE is set.
			public void Tick()
			{
				Core.Set("CLK", 0);
				Core.Step();
				Core.Set("DATA_IN", Memory[Core.Get("ADDR")]);
				Core.Step();

				bool write = Core.Get("WE") == 1;
				uint address = Core.Get("ADDR");
				uint data = Core.Get("DATA_OUT");

				Core.Set("CLK", 1);
				Core.Step();
				if (write)
				{
					Memory[address] = (byte)data;
					Writes++;
				}
			}

			// Runs until HLT (or gives up), returns the number of clock cycles.
			public int RunUntilHalted(int maxTicks = 5000)
			{
				for (int i = 0; i < maxTicks; i++)
				{
					if (Get("HALTED") == 1) return i;
					Tick();
				}

				Assert.Fail("the program did not reach HLT");
				return maxTicks;
			}
		}

		[Test]
		public void AssemblerEncodesEveryInstruction()
		{
			CollectionAssert.AreEqual(new byte[]
			{
				0x00, 0x10, 0x05, 0x20, 0x80, 0x30, 0x81, 0x40, 0x80, 0x50, 0x80, 0x60, 0x0F, 0x70, 0xF0,
				0x80, 0xFF, 0x90, 0x01, 0xA0, 0x0A, 0xB0, 0x00, 0xC0, 0x00, 0xD0, 0x00, 0xE0, 0x00, 0xF0
			}, Assemble(@"
				start: NOP
				LDI 5
				LDA 0x80
				STA 0x81
				ADD 0x80
				SUB 128
				AND 0x0F
				OR 0xF0
				XOR 0b11111111
				ADI 1
				CPI 10
				JMP start
				JZ start
				JNZ start
				JC start
				HLT
			"));
		}

		[Test]
		public void CoreHasNoMemoryInside()
		{
			(ChipLibrary library, List<ChipDescription> chips) = BuildAll();
			HashSet<string> builtins = new();
			HashSet<string> seen = new();
			void Visit(ChipDescription chip)
			{
				foreach (SubChipDescription sub in chip.SubChips)
				{
					if (!seen.Add(sub.Name)) continue;
					ChipDescription desc = library.GetChipDescription(sub.Name);
					if (desc.ChipType == ChipType.Custom) Visit(desc);
					else builtins.Add(sub.Name);
				}
			}

			Visit(chips.Single(c => c.Name == "Core8"));
			// NAND gates, 3-state buffers and the split/merge chips that join bits into buses.
			string[] allowed = { "NAND", "3-STATE BUFFER", "1-4BIT", "1-8BIT", "4-8BIT", "4-1BIT", "8-4BIT", "8-1BIT" };
			CollectionAssert.IsSubsetOf(builtins, allowed, "no ROM, RAM, clock or keys inside the core");
			CollectionAssert.Contains(builtins, "3-STATE BUFFER");
		}

		[Test]
		public void FetchesOneStepAtATime()
		{
			Board board = new("LDI 0x42\nHLT");
			Assert.AreEqual(0u, board.Get("STEP"));
			Assert.AreEqual(0u, board.Get("RD"), "T0 puts the PC on the bus, not memory");
			board.Tick(); // T0: PC -> MAR
			Assert.AreEqual(1u, board.Get("STEP"));
			Assert.AreEqual(1u, board.Get("RD"), "T1 reads the opcode");
			board.Tick(); // T1: memory -> IR
			Assert.AreEqual(0x10u, board.Get("IR"));
			Assert.AreEqual(1u, board.Get("PC"));
			board.Tick(); // T2: PC -> MAR
			Assert.AreEqual(1u, board.Get("ADDR"));
			board.Tick(); // T3: memory -> A
			Assert.AreEqual(0x42u, board.Get("A"));
			Assert.AreEqual(0u, board.Get("STEP"), "LDI ends after T3");
			Assert.AreEqual(2u, board.Get("PC"));
			Assert.AreEqual(2, board.RunUntilHalted(), "HLT: T0, T1, then it stays in T2");
			Assert.AreEqual(2u, board.Get("STEP"));
			board.Tick();
			Assert.AreEqual(2u, board.Get("STEP"));
			Assert.AreEqual(3u, board.Get("PC"), "a halted core stays put");
		}

		[Test]
		public void ExecutesEveryInstruction()
		{
			Board board = new(@"
				LDI 200
				STA 0xF0        ; [F0] = 200
				ADI 100         ; 300: A = 44, C = 1
				STA 0xF1
				LDI 10
				ADD ten         ; 20
				STA 0xF2
				SUB ten         ; 10, no borrow: C = 1
				SUB 0xF2        ; 10 - 20: borrow, C = 0
				STA 0xF3        ; 246
				LDA ten
				AND mask        ; 0x0A & 0x0F
				OR high         ; | 0xF0 = 0xFA
				XOR high        ; ^ 0xF0 = 0x0A
				STA 0xF4
				CPI 10          ; Z = 1, A stays
				STA 0xF5
				HLT
			ten:  DB 10
			mask: DB 0x0F
			high: DB 0xF0
			");
			board.RunUntilHalted();

			Assert.AreEqual(200, board.Memory[0xF0]);
			Assert.AreEqual(44, board.Memory[0xF1]);
			Assert.AreEqual(20, board.Memory[0xF2]);
			Assert.AreEqual(246, board.Memory[0xF3]);
			Assert.AreEqual(0x0A, board.Memory[0xF4]);
			Assert.AreEqual(0x0A, board.Memory[0xF5], "CPI leaves A alone");
			Assert.AreEqual(1u, board.Get("Z"));
			Assert.AreEqual(1u, board.Get("C"), "10 - 10: no borrow");
			Assert.AreEqual(6, board.Writes);
		}

		[Test]
		public void FlagsAfterArithmeticAndLogic()
		{
			(string program, uint z, uint c)[] cases =
			{
				("LDI 255\nADI 1", 1, 1),
				("LDI 1\nADI 1", 0, 0),
				("LDI 5\nCPI 6", 0, 0), // borrow
				("LDI 6\nCPI 5", 0, 1),
				("LDI 0xFF\nADI 1\nAND zero", 1, 0), // logic clears C
				("LDI 0x0F\nXOR zero", 0, 0)
			};

			foreach ((string program, uint z, uint c) in cases)
			{
				Board board = new(program + "\nHLT\nzero: DB 0");
				board.RunUntilHalted();
				Assert.AreEqual(z, board.Get("Z"), program);
				Assert.AreEqual(c, board.Get("C"), program);
			}
		}

		[Test]
		public void ConditionalJumpsFollowTheFlags()
		{
			Board board = new(@"
				LDI 3
				CPI 3       ; Z = 1, C = 1
				JNZ fail
				JZ z_ok
				JMP fail
			z_ok:
				JC c_ok
				JMP fail
			c_ok:
				CPI 4       ; Z = 0, C = 0 (borrow)
				JZ fail
				JC fail
				JNZ done
			fail:
				LDI 0xEE
				STA 0xFF
				HLT
			done:
				LDI 1
				STA 0xFF
				HLT
			");
			board.RunUntilHalted();
			Assert.AreEqual(1, board.Memory[0xFF]);
		}

		// Counts 10 down to 0 and adds up 10 + 9 + ... + 1 = 55, keeping its variables in memory.
		[Test]
		public void RunsALoop()
		{
			Board board = new(@"
			loop:
				LDA sum
				ADD n
				STA sum
				LDA n
				SUB one
				STA n
				JNZ loop
				HLT
			n:   DB 10
			sum: DB 0
			one: DB 1
			");
			board.RunUntilHalted();
			byte[] code = Assemble("LDA 0\nADD 0\nSTA 0\nLDA 0\nSUB 0\nSTA 0\nJNZ 0\nHLT");
			Assert.AreEqual(55, board.Memory[code.Length + 1], "sum");
			Assert.AreEqual(0, board.Memory[code.Length], "n");
		}

		[Test]
		public void ResetStartsAgain()
		{
			Board board = new("LDI 7\nSTA 0x80\nHLT");
			board.RunUntilHalted();
			board.Core.Set("RESET", 1);
			board.Tick();
			board.Core.Set("RESET", 0);
			Assert.AreEqual(0u, board.Get("PC"));
			Assert.AreEqual(0u, board.Get("STEP"));
			Assert.AreEqual(0u, board.Get("HALTED"));
			Assert.AreEqual(0u, board.Get("A"));
			board.Memory[0x80] = 0;
			board.RunUntilHalted();
			Assert.AreEqual(7, board.Memory[0x80]);
		}

		[Test]
		public void PythonAssemblerMatches()
		{
			// core8asm.py and Assemble follow the same rules; the README lists this program's bytes.
			CollectionAssert.AreEqual(new byte[] { 0x20, 0x10, 0x40, 0x0F, 0x30, 0x10, 0x20, 0x0F, 0x50, 0x11, 0x30, 0x0F, 0xD0, 0x00, 0xF0, 0x0A, 0x00, 0x01 },
				Assemble(File.ReadAllText(Path.Combine(Folder, "sum.asm"))));
		}

		// ---- The bundled Core8 project ----

		const string ProjectName = "Core8";

		public static (ProjectDescription project, ChipDescription[] chips) CreateCore8Project()
		{
			(ChipLibrary _, List<ChipDescription> chips) = BuildAll();
			ProjectDescription project = new()
			{
				ProjectName = ProjectName,
				DLSVersion_LastSaved = Main.DLSVersion.ToString(),
				DLSVersion_EarliestCompatible = Main.DLSVersion_EarliestCompatible.ToString(),
				CreationTime = new DateTime(2026, 10, 31),
				LastSaveTime = new DateTime(2026, 10, 31),
				Prefs_MainPinNamesDisplayMode = 1,
				Prefs_ChipPinNamesDisplayMode = 1,
				Prefs_SimTargetStepsPerSecond = 20,
				Prefs_SimStepsPerClockTick = 1,
				AllCustomChipNames = chips.Select(c => c.Name).ToArray(),
				StarredList = BuiltinCollectionCreator.GetDefaultStarredList().ToList(),
				ChipCollections = BuiltinCollectionCreator.CreateDefaultChipCollections()
					.Append(new ChipCollection("CORE8", chips.Select(c => c.Name).ToArray()))
					.ToList(),
				StartupChipName = "Core8"
			};

			return (project, chips.ToArray());
		}

		[Test]
		public void BundledFilesMatchTheSources()
		{
			(ProjectDescription project, ChipDescription[] chips) = CreateCore8Project();
			BundledProjectTests.CheckBundledFiles(BundledProjectTests.FolderOf(ProjectName), project, chips);
		}

		[Test]
		public void BundledProjectIsInstalledWithRewired()
		{
			CollectionAssert.Contains(BundledProjectInstaller.ProjectNames, ProjectName);
		}
	}
}
