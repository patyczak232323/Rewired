using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.RHDL;
using DLS.SaveSystem;
using DLS.Simulation;
using NUnit.Framework;

namespace DLS.Tests
{
	// PIX8 from Tools/EngineTests/Fixtures/RHDL/PIX8: an 8-bit computer built chip by chip, with a
	// screen, keys, sound, subroutines and a multiplier. The basic parts are CPU8's; the programs
	// below are assembled with Pix8Asm and run on the real engine.
	//
	// The PIX8 project that comes with Rewired (Assets/Resources/BundledProjects/PIX8) is built from
	// these sources with cube3d.pix8 in the ROM. After changing them, rewrite its files with:
	//   REWIRED_WRITE_BUNDLED=1 dotnet test Tools/EngineTests --filter Pix8ExampleTests
	public class Pix8ExampleTests
	{
		static string ExamplesRoot => TestPaths.Examples;

		public static readonly string[] Sources =
		{
			"Basics/04_DFF.rhdl",
			"CPU8/00_NOT.rhdl",
			"CPU8/01_AND.rhdl",
			"CPU8/02_OR.rhdl",
			"CPU8/03_XOR.rhdl",
			"CPU8/04_OR4.rhdl",
			"CPU8/05_MUX.rhdl",
			"CPU8/06_FULL_ADDER.rhdl",
			"CPU8/07_ADD8.rhdl",
			"CPU8/08_MUX2X8.rhdl",
			"CPU8/09_MUX4X8.rhdl",
			"CPU8/10_MUX8X8.rhdl",
			"CPU8/11_MUX16X8.rhdl",
			"CPU8/12_DEC2.rhdl",
			"CPU8/13_DEC3.rhdl",
			"CPU8/14_DEC4.rhdl",
			"CPU8/15_DEC5.rhdl",
			"CPU8/16_IS_ZERO8.rhdl",
			"CPU8/17_REGISTER8.rhdl",
			"CPU8/20_REGISTER_FILE.rhdl",
			"CPU8/24_PROGRAM_ROM.rhdl",
			"PIX8/00_AND_ROW8.rhdl",
			"PIX8/01_MUL8.rhdl",
			"PIX8/02_ALU.rhdl",
			"PIX8/03_FLAGS.rhdl",
			"PIX8/04_PROGRAM_COUNTER.rhdl",
			"PIX8/05_BYTE_CELL.rhdl",
			"PIX8/06_RAM16X8.rhdl",
			"PIX8/07_RAM256X8.rhdl",
			"PIX8/08_RETURN_STACK.rhdl",
			"PIX8/09_STACK_POINTER.rhdl",
			"PIX8/10_RGB332_EXPAND.rhdl",
			"PIX8/11_RGB332_PACK.rhdl",
			"PIX8/12_VIDEO_UNIT.rhdl",
			"PIX8/13_LFSR16.rhdl",
			"PIX8/14_TONE_UNIT.rhdl",
			"PIX8/15_CONTROL_UNIT.rhdl",
			"PIX8/16_PIX8.rhdl",
			"PIX8/17_PIX8_COMPUTER.rhdl"
		};

		// The processor wired to a screen, with the clock, reset and keys as inputs (for the tests).
		const string TestBench = @"
chip Pix8Bench
    in CLK, RESET, KEYS: 8
    out PC_LO: 8, PC_HI: 4, R0: 8, R1: 8, R2: 8, R3: 8, R4: 8, R5: 8, R6: 8, R7: 8, SP: 8, Z, N, C, HALTED, PITCH: 8, VOLUME: 4

    cpu = Pix8(CLK, RESET, KEYS, screen.R_OUT, screen.G_OUT, screen.B_OUT)
    screen = RGB_DISPLAY_64(cpu.X, cpu.Y, cpu.R, cpu.G, cpu.B, cpu.CLS, cpu.PLOT, cpu.SHOW, CLK)
    PC_LO = cpu.PC_LO
    PC_HI = cpu.PC_HI
    R0 = cpu.R0
    R1 = cpu.R1
    R2 = cpu.R2
    R3 = cpu.R3
    R4 = cpu.R4
    R5 = cpu.R5
    R6 = cpu.R6
    R7 = cpu.R7
    SP = cpu.SP
    Z = cpu.Z
    N = cpu.N
    C = cpu.C
    HALTED = cpu.HALTED
    PITCH = cpu.PITCH
    VOLUME = cpu.VOLUME
";

		[TearDown]
		public void TearDown() => RewiredEngine.Reset();

		public static (ChipLibrary library, List<ChipDescription> chips) BuildAll() =>
			CompiledSources.Build(Sources.Select(source => Path.Combine(ExamplesRoot, source)));

		static (ChipLibrary library, ChipDescription bench) BuildBench(uint[] program)
		{
			(ChipLibrary library, List<ChipDescription> _) = BuildAll();
			Cpu8ExampleTests.LoadProgram(library, program);
			RhdlCompileResult result = RhdlCompiler.Compile(TestBench, library);
			Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
			library.NotifyChipSaved(result.Description, false);
			return (library, result.Description);
		}

		static TestCircuit Start(string program, uint keys = 0)
		{
			(ChipLibrary library, ChipDescription bench) = BuildBench(Pix8Asm.Assemble(program));
			TestCircuit c = new(bench, library);
			c.Set("KEYS", keys);
			c.Set("RESET", 1);
			c.Tick();
			c.Set("RESET", 0);
			return c;
		}

		static TestCircuit RunToHalt(string program, int maxTicks = 5000, uint keys = 0)
		{
			TestCircuit c = Start(program, keys);
			for (int i = 0; i < maxTicks && c.Get("HALTED") == 0; i++) c.Tick();
			Assert.AreEqual(1u, c.Get("HALTED"), "the program should reach HLT");
			return c;
		}

		static uint Pc(TestCircuit c) => c.Get("PC_LO") | (c.Get("PC_HI") << 8);

		static uint[] ShownPicture(SimChip root)
		{
			SimChip screen = Find(root, ChipType.DisplayRGB64);
			Assert.IsNotNull(screen, "the bench should contain the screen");
			uint[] picture = new uint[DisplayRgb64.PixelCount];
			Array.Copy(screen.InternalState, DisplayRgb64.FrontOffset, picture, 0, picture.Length);
			return picture;
		}

		static SimChip Find(SimChip chip, ChipType type)
		{
			if (chip.ChipType == type) return chip;
			foreach (SimChip sub in chip.SubChips)
			{
				SimChip found = sub == null ? null : Find(sub, type);
				if (found != null) return found;
			}

			return null;
		}

		// A colour byte RRRGGGBB as the screen stores it (4 bits per channel).
		static uint Screen332(uint c)
		{
			uint r = (c >> 5) & 7, g = (c >> 2) & 7, b = c & 3;
			return DisplayRgb64.PackColour(r << 1 | r >> 2, g << 1 | g >> 2, b << 2 | b);
		}

		[Test]
		public void AssemblerEncodesEveryFormat()
		{
			CollectionAssert.AreEqual(new uint[]
			{
				0x0000, 0x0001, 0x0002, 0x0003, 0x0004, // NOP HLT RET SHOW CLS
				0x0B21, 0x090C, 0x0A0E, // ADD R3, R1; INC R1; MUL R2, R0
				0x12AA, 0x24FF, 0x4310, // LDI R2, 0xAA; SUBI R4, -1; CMPI R3, 16
				0x4920, 0x5220, 0x5940, 0x6360, // LD R1, [0x20]; ST [0x20], R2; LDR R1, [R2]; STR [R3], R3
				0x6D00, 0x7600, 0x7B40, 0x8A00, 0x9000, 0x98E3, 0xA100, 0xA800, 0xB300, 0xB840, // PUSH .. TONEI
				0xC003, 0xFFFF // JMP 3; CALL 0x7FF
			}, Pix8Asm.Assemble(@"
				NOP
				HLT
				RET
				SHOW
				CLS
				ADD R3, R1
				INC R1
				MUL R2, R0
				LDI R2, 0xAA
				SUBI R4, -1
				CMPI R3, 16
				LD R1, [0x20]
				ST [0x20], R2
				LDR R1, [R2]
				STR [R3], R3
				PUSH R5
				POP R6
				PLOT R3, R2
				READ R2
				COL R0
				COLI 0xE3
				KEYS R1
				RND R0
				TONE R3
				TONEI 0x40
				JMP 3
				CALL 0x7FF
			"));
		}

		// Every ALU function once, checked after each clock tick: (instruction, register, value, Z, N, C).
		[Test]
		public void ExecutesEveryAluFunction()
		{
			(string asm, string reg, uint value, uint z, uint n, uint c)[] steps =
			{
				("NOP", "R0", 0, 0, 0, 0),
				("LDI R1, 200", "R1", 200, 0, 0, 0), // LDI leaves the flags alone
				("LDI R2, 100", "R2", 100, 0, 0, 0),
				("MOV R3, R1", "R3", 200, 0, 0, 0),
				("ADD R3, R2", "R3", 44, 0, 0, 1), // 300: carry
				("ADC R3, R2", "R3", 145, 0, 1, 0), // 44 + 100 + 1
				("ADDI R3, 111", "R3", 0, 1, 0, 1), // 256
				("SUB R3, R2", "R3", 156, 0, 1, 1), // 0 - 100: borrow
				("SBC R3, R2", "R3", 55, 0, 0, 0), // 156 - 100 - 1
				("SUBI R3, 55", "R3", 0, 1, 0, 0),
				("DEC R3", "R3", 255, 0, 1, 1),
				("INC R3", "R3", 0, 1, 0, 1),
				("LDI R4, 0xCA", "R4", 0xCA, 1, 0, 1),
				("AND R4, R2", "R4", 0xCA & 100, 0, 0, 1), // logic leaves C alone
				("ANDI R4, 0x0F", "R4", 0, 1, 0, 1),
				("OR R4, R1", "R4", 200, 0, 1, 1),
				("ORI R4, 0x01", "R4", 201, 0, 1, 1),
				("XOR R4, R2", "R4", 201 ^ 100, 0, 1, 1),
				("XORI R4, 0xFF", "R4", ~(201u ^ 100) & 0xFF, 0, 0, 1),
				("NOT R4", "R4", 201 ^ 100, 0, 1, 1),
				("SHL R1", "R1", 144, 0, 1, 1), // bit 7 into C
				("SHR R1", "R1", 72, 0, 0, 0), // bit 0 into C
				("CMP R1, R2", "R1", 72, 0, 1, 1), // 72 - 100: borrow, negative, R1 unchanged
				("CMPI R2, 100", "R2", 100, 1, 0, 0),
				("LDI R5, 123", "R5", 123, 1, 0, 0),
				("LDI R6, 45", "R6", 45, 1, 0, 0),
				("MUL R5, R6", "R5", (123 * 45) & 0xFF, 0, 1, 0), // 5535 = 0x159F; MUL leaves C alone
				("LDI R5, 123", "R5", 123, 0, 1, 0),
				("MULH R5, R6", "R5", (123 * 45) >> 8, 0, 0, 0)
			};

			TestCircuit c = Start(string.Join("\n", steps.Select(s => s.asm)) + "\nHLT");
			for (int i = 0; i < steps.Length; i++)
			{
				c.Tick();
				(string asm, string reg, uint value, uint z, uint n, uint cf) = steps[i];
				Assert.AreEqual(value, c.Get(reg), $"{reg} after {asm}");
				Assert.AreEqual(z, c.Get("Z"), $"Z after {asm}");
				Assert.AreEqual(n, c.Get("N"), $"N after {asm}");
				Assert.AreEqual(cf, c.Get("C"), $"C after {asm}");
				Assert.AreEqual((uint)i + 1, Pc(c), $"PC after {asm}");
			}

			c.Tick();
			c.Tick();
			Assert.AreEqual(1u, c.Get("HALTED"));
			Assert.AreEqual((uint)steps.Length, Pc(c), "HLT keeps the PC on itself");
		}

		// The multiplier on its own, for edge cases and a spread of other values.
		[Test]
		public void MultiplierGivesTheFullProduct()
		{
			(ChipLibrary library, List<ChipDescription> chips) = BuildAll();
			TestCircuit c = new(chips.Single(ch => ch.Name == "Mul8"), library);
			Random rng = new(8);
			List<(uint a, uint b)> cases = new() { (0, 0), (255, 255), (1, 255), (255, 1), (128, 2), (16, 16), (15, 17) };
			for (int i = 0; i < 400; i++) cases.Add(((uint)rng.Next(256), (uint)rng.Next(256)));

			foreach ((uint a, uint b) in cases)
			{
				c.Set("A", a);
				c.Set("B", b);
				c.Step();
				Assert.AreEqual(a * b, c.Get("LO") | (c.Get("HI") << 8), $"{a} x {b}");
			}
		}

		[Test]
		public void ConditionalJumpsFollowTheFlags()
		{
			TestCircuit c = RunToHalt(@"
				LDI R0, 0
				LDI R1, 0
				CMPI R1, 0      ; Z=1 N=0 C=0
				JNZ fail
				JC fail
				JN fail
				JZ z_ok
				HLT
			z_ok:
				JNC c_ok
				HLT
			c_ok:
				JNN n_ok
				HLT
			n_ok:
				CMPI R1, 1      ; 0 - 1 = 0xFF: Z=0 N=1 C=1
				JZ fail
				JNC fail
				JNN fail
				JNZ nz_ok
				HLT
			nz_ok:
				JC c2_ok
				HLT
			c2_ok:
				JN done
				HLT
			done:
				LDI R0, 1
				HLT
			fail:
				LDI R0, 0xEE
				HLT
			");

			Assert.AreEqual(1u, c.Get("R0"));
		}

		// Subroutines call each other three deep; each one saves the register it uses on the stack.
		[Test]
		public void CallsNestAndTheStackKeepsRegisters()
		{
			TestCircuit c = RunToHalt(@"
				LDI R1, 7
				CALL outer
				ADDI R0, 100    ; R0 = 1 + 10 + 20 + 100
				HLT
			outer:
				PUSH R1
				LDI R1, 99
				LDI R0, 1
				CALL middle
				POP R1
				RET
			middle:
				ADDI R0, 10
				CALL inner
				RET
			inner:
				ADDI R0, 20
				RET
			");

			Assert.AreEqual(131u, c.Get("R0"));
			Assert.AreEqual(7u, c.Get("R1"), "PUSH and POP kept R1");
			Assert.AreEqual(0u, c.Get("SP"), "the stack is empty again");
		}

		[Test]
		public void StackIsLastInFirstOutAtTheTopOfRam()
		{
			TestCircuit c = RunToHalt(@"
				LDI R1, 11
				LDI R2, 22
				LDI R3, 33
				PUSH R1
				PUSH R2
				PUSH R3
				LD R7, [0xFF]   ; the first byte pushed
				LD R6, [0xFD]   ; the last one
				POP R4          ; 33
				POP R5          ; 22
				POP R0          ; 11
				HLT
			");

			Assert.AreEqual(11u, c.Get("R7"));
			Assert.AreEqual(33u, c.Get("R6"));
			Assert.AreEqual(33u, c.Get("R4"));
			Assert.AreEqual(22u, c.Get("R5"));
			Assert.AreEqual(11u, c.Get("R0"));
			Assert.AreEqual(0u, c.Get("SP"));
		}

		[Test]
		public void MemoryByAddressAndThroughRegisters()
		{
			TestCircuit c = RunToHalt(@"
				LDI R1, 0x40    ; pointer
				LDI R2, 1       ; value
			fill:
				STR [R1], R2
				INC R1
				INC R2
				CMPI R2, 11
				JNZ fill
				LDI R0, 0       ; sum of RAM[0x40 .. 0x49]
				LDI R1, 0x40
			sum:
				LDR R3, [R1]
				ADD R0, R3
				INC R1
				CMPI R1, 0x4A
				JNZ sum
				ST [0x80], R0
				LD R4, [0x80]
				HLT
			");

			Assert.AreEqual(55u, c.Get("R0"));
			Assert.AreEqual(55u, c.Get("R4"));
		}

		// PLOT draws in the hidden picture, SHOW shows it, READ reads the shown colour back and CLS
		// clears the hidden picture.
		[Test]
		public void DrawsOnTheScreenAndReadsItBack()
		{
			TestCircuit c = RunToHalt(@"
				COLI 0xE0       ; red
				LDI R0, 5
				LDI R1, 9
				PLOT R0, R1
				READ R2         ; not shown yet: black
				SHOW
				READ R3         ; red, at the pen
				LDI R4, 0x1F
				COL R4
				LDI R0, 63
				LDI R1, 0
				PLOT R0, R1
				CLS
				LDI R0, 1
				PLOT R0, R0     ; the only pixel after CLS
				SHOW
				LDI R5, 5
				LDI R6, 9
				POS R5, R6
				READ R7         ; (5, 9) was cleared
				HLT
			");

			Assert.AreEqual(0u, c.Get("R2"));
			Assert.AreEqual(0xE0u, c.Get("R3"));
			Assert.AreEqual(0u, c.Get("R7"));

			uint[] picture = ShownPicture(c.Root);
			Assert.AreEqual(Screen332(0x1F), picture[1 * 64 + 1]);
			Assert.AreEqual(1, picture.Count(p => p != 0), "only (1, 1) after CLS");
		}

		[Test]
		public void ReadsKeysRandomNumbersAndSetsTheTone()
		{
			TestCircuit c = Start(@"
				KEYS R0
				RND R1
				RND R2
				RND R3
				TONEI 0x50
				HLT
			", keys: 0b100101);
			for (int i = 0; i < 4; i++) c.Tick();
			Assert.AreEqual(0b100101u, c.Get("R0"));
			Assert.AreEqual(0u, c.Get("PITCH"));
			c.Tick();
			Assert.AreEqual(0x50u, c.Get("PITCH"));
			Assert.AreEqual(8u, c.Get("VOLUME"));

			uint[] random = { c.Get("R1"), c.Get("R2"), c.Get("R3") };
			Assert.AreEqual(3, random.Distinct().Count(), "a new random byte every tick: " + string.Join(", ", random));
		}

		// 255 values in a row from RND: a 16-bit LFSR gives every byte value in its low bits.
		[Test]
		public void RandomBytesCoverManyValues()
		{
			TestCircuit c = Start(@"
				LDI R1, 0
			loop:
				RND R0
				STR [R1], R0
				INC R1
				JNZ loop
				HLT
			");

			HashSet<uint> seen = new();
			for (int i = 0; i < 256 * 4 + 4 && c.Get("HALTED") == 0; i++)
			{
				c.Tick();
				if (Pc(c) == 2) seen.Add(c.Get("R0"));
			}

			Assert.Greater(seen.Count, 140, "random bytes should not repeat a short cycle");
		}

		[Test]
		public void JumpsReachEveryRomAndCallsReturnAcrossThem()
		{
			uint[] program = new uint[2048];
			Array.Copy(Pix8Asm.Assemble("CALL 0x6F0\nLDI R2, 0x22\nHLT"), program, 3);
			Array.Copy(Pix8Asm.Assemble("LDI R1, 0x11\nRET"), 0, program, 0x6F0, 2);

			(ChipLibrary library, ChipDescription bench) = BuildBench(program);
			TestCircuit c = new(bench, library);
			c.Set("RESET", 1);
			c.Tick();
			c.Set("RESET", 0);
			c.Tick();
			Assert.AreEqual(0x6F0u, Pc(c));
			c.Tick();
			Assert.AreEqual(0x11u, c.Get("R1"));
			c.Tick();
			Assert.AreEqual(1u, Pc(c), "RET goes back to the word after CALL");
			c.Tick();
			c.Tick();
			Assert.AreEqual(0x22u, c.Get("R2"));
			Assert.AreEqual(1u, c.Get("HALTED"));
		}
			// ---- The cube demo (Tools/EngineTests/Fixtures/RHDL/PIX8/cube3d.pix8) ----

		public static string CubeProgram => File.ReadAllText(Path.Combine(ExamplesRoot, "PIX8", "cube3d.pix8"));

		// Runs the demo until it has shown `frames` frames; returns the circuit at the start of the next one.
		static TestCircuit RunCube(int frames, uint keys = 0)
		{
			Pix8Asm.Assemble(CubeProgram, out Dictionary<string, int> labels);
			TestCircuit c = Start(CubeProgram, keys);
			int shown = 0;
			for (int i = 0; i < 20000 * (frames + 1) && shown <= frames; i++)
			{
				c.Tick();
				if (Pc(c) == labels["frame"]) shown++;
			}

			Assert.AreEqual(frames + 1, shown, "the demo should keep drawing frames");
			return c;
		}

		[Test]
		public void CubeDemoDrawsTwelveEdgesThatTurn()
		{
			TestCircuit c = RunCube(2);
			uint[] first = ShownPicture(c.Root);
			uint cyan = Screen332(0x1F);
			Assert.IsTrue(first.All(p => p == 0 || p == cyan), "only the cube is drawn, in cyan");
			int lit = first.Count(p => p == cyan);
			Assert.That(lit, Is.InRange(150, 600), "twelve edges of a cube about 34 pixels wide");

			// The cube stays in the middle of the screen.
			for (int i = 0; i < first.Length; i++)
			{
				if (first[i] == 0) continue;
				int x = i % 64, y = i / 64;
				Assert.That(x, Is.InRange(2, 61), $"pixel ({x}, {y})");
				Assert.That(y, Is.InRange(2, 61), $"pixel ({x}, {y})");
			}

			RewiredEngine.Reset();
			uint[] later = ShownPicture(RunCube(4).Root);
			Assert.AreNotEqual(first, later, "the cube turns from frame to frame");
		}

		[Test]
		public void CubeDemoChangesColourWithJ()
		{
			TestCircuit c = RunCube(2, keys: 0b10000);
			uint[] picture = ShownPicture(c.Root);
			uint colour = picture.First(p => p != 0);
			Assert.AreNotEqual(Screen332(0x1F), colour, "J picks the next colour");
			Assert.IsTrue(picture.All(p => p == 0 || p == colour), "one colour for the whole cube");
		}

		// ---- The bundled PIX8 project ----

		const string ProjectName = "PIX8";
		const uint PowerOnPulseSteps = 4;

		// Letters of the keys by the input of the KEYS merge they are wired to (input 0 is bit 7),
		// and the key that resets.
		static readonly Dictionary<int, char> KeyByMergeInput = new() { [2] = 'K', [3] = 'J', [4] = 'S', [5] = 'A', [6] = 'W', [7] = 'D' };
		const char ResetKey = 'R';

		public static (ProjectDescription project, ChipDescription[] chips) CreatePix8Project()
		{
			(ChipLibrary library, List<ChipDescription> chips) = BuildAll();
			Cpu8ExampleTests.LoadProgram(library, Pix8Asm.Assemble(CubeProgram));

			ChipDescription computer = chips.Single(c => c.Name == "Pix8Computer");
			string keyName = ChipTypeHelper.GetName(ChipType.Key);
			int mergeId = computer.SubChips.Single(s => s.Name == ChipTypeHelper.GetName(ChipType.Merge_1To8Bit)).ID;
			for (int i = 0; i < computer.SubChips.Length; i++)
			{
				string name = computer.SubChips[i].Name;
				if (name == ChipTypeHelper.GetName(ChipType.Pulse)) computer.SubChips[i].InternalData = new uint[] { PowerOnPulseSteps, 0, 0 };
				if (name != keyName) continue;

				int id = computer.SubChips[i].ID;
				WireDescription[] wires = computer.Wires.Where(w => w.SourcePinAddress.PinOwnerID == id).ToArray();
				bool toMerge = wires.Length == 1 && wires[0].TargetPinAddress.PinOwnerID == mergeId;
				char key = toMerge ? KeyByMergeInput[wires[0].TargetPinAddress.PinID] : ResetKey;
				computer.SubChips[i].InternalData = new uint[] { key };
			}

			CollectionAssert.AreEquivalent("WASDJKR", computer.SubChips.Where(s => s.Name == keyName).Select(s => (char)s.InternalData[0]));

			ProjectDescription project = new()
			{
				ProjectName = ProjectName,
				DLSVersion_LastSaved = Main.DLSVersion.ToString(),
				DLSVersion_EarliestCompatible = Main.DLSVersion_EarliestCompatible.ToString(),
				CreationTime = new DateTime(2026, 11, 12),
				LastSaveTime = new DateTime(2026, 11, 12),
				Prefs_MainPinNamesDisplayMode = 1,
				Prefs_ChipPinNamesDisplayMode = 1,
				// As fast as the machine can run it: a frame of the demo is about 2500 instructions.
				Prefs_SimTargetStepsPerSecond = SimulationSpeed.Unlimited,
				Prefs_SimStepsPerClockTick = 1,
				Prefs_SimOwnClockSpeed = true, // an example runs at its own speed, not the one set for all projects
				AllCustomChipNames = chips.Select(c => c.Name).ToArray(),
				StarredList = BuiltinCollectionCreator.GetDefaultStarredList().ToList(),
				ChipCollections = BuiltinCollectionCreator.CreateDefaultChipCollections()
					.Append(new ChipCollection("PIX8", chips.Select(c => c.Name).ToArray()))
					.ToList(),
				StartupChipName = computer.Name
			};

			return (project, chips.ToArray());
		}

		[Test]
		public void BundledFilesMatchTheSources()
		{
			(ProjectDescription project, ChipDescription[] chips) = CreatePix8Project();
			BundledProjectTests.CheckBundledFiles(BundledProjectTests.FolderOf(ProjectName), project, chips);
		}

		[Test]
		public void BundledProjectIsInstalledWithRewired()
		{
			CollectionAssert.Contains(BundledProjectInstaller.ProjectNames, ProjectName);
		}

		// The project runs the demo by itself: the power-on pulse resets the processor and the
		// clock runs it until the first cube is on the screen.
		[Test]
		public void ComputerShowsTheCubeWithoutPressingReset()
		{
			(ProjectDescription _, ChipDescription[] chips) = CreatePix8Project();
			ChipLibrary library = TestLibrary.Create(chips);
			ChipDescription computer = chips.Single(c => c.Name == "Pix8Computer");

			RewiredEngine.Reset();
			RewiredEngine.StepsPerClockTransition = 1;
			SimChip root = RewiredEngine.BuildChip(computer, library);
			RewiredEngine.EnsureInitialized(root, Array.Empty<DevPinInstance>(), null);

			SimChip screen = Find(root, ChipType.DisplayRGB64);
			bool Drawn() => Enumerable.Range(0, DisplayRgb64.PixelCount).Any(i => screen.InternalState[DisplayRgb64.FrontOffset + i] != 0);
			for (int i = 0; i < 12000 && !Drawn(); i++) RewiredEngine.RunStep(root, Array.Empty<DevPinInstance>(), null);

			Assert.IsTrue(Drawn(), "the first frame of the cube should be shown");
		}
	}
}
