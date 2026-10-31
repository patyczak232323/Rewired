using System;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.RHDL;
using DLS.Simulation;
using NUnit.Framework;

namespace DLS.Tests
{
	// The RHDL language itself: every construct is compiled and then simulated on the real
	// engine, and mistakes must produce a message that points at the right line.
	public class RhdlLanguageTests
	{
		ChipLibrary library;

		[SetUp]
		public void SetUp() => library = TestLibrary.Create();

		[TearDown]
		public void TearDown() => RewiredEngine.Reset();

		ChipDescription Build(string source)
		{
			RhdlCompileResult result = RhdlCompiler.Compile(source, library);
			Assert.IsTrue(result.Success, "RHDL failed to compile:\n" + string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
			library.NotifyChipSaved(result.Description, false);
			return result.Description;
		}

		RhdlDiagnostic[] Errors(string source)
		{
			RhdlCompileResult result = RhdlCompiler.Compile(source, library);
			Assert.IsFalse(result.Success, "expected the source to be rejected");
			Assert.IsNotEmpty(result.Diagnostics);
			return result.Diagnostics;
		}

		static void AssertMessage(RhdlDiagnostic[] diagnostics, int line, string fragment)
		{
			string all = string.Join("\n", diagnostics.Select(d => d.ToString()));
			Assert.IsTrue(diagnostics.Any(d => d.Line == line && d.Message.Contains(fragment)),
				$"expected a message on line {line} containing '{fragment}', got:\n{all}");
		}

		static int NandCount(ChipDescription chip) =>
			chip.SubChips.Count(s => ChipDescription.NameMatch(s.Name, ChipTypeHelper.GetName(ChipType.Nand)));

		// ---- Behaviour ----

		[Test]
		public void LoopVariableBuildsARippleCarryAdder()
		{
			// 'carry' is assigned in every repetition, so each use reads the value from the
			// line above it.
			ChipDescription chip = Build(@"
chip Add4
    in A: 4, B: 4, CIN
    out S: 4, COUT

    carry = CIN
    for i in 0..3
        S[i] = A[i] xor B[i] xor carry
        carry = (A[i] and B[i]) or (carry and (A[i] xor B[i]))
    end
    COUT = carry
");
			TestCircuit c = new(chip, library);
			for (uint a = 0; a < 16; a++)
			{
				for (uint b = 0; b < 16; b++)
				{
					for (uint cin = 0; cin < 2; cin++)
					{
						c.Set("A", a);
						c.Set("B", b);
						c.Set("CIN", cin);
						c.Step();
						uint sum = a + b + cin;
						Assert.AreEqual(sum & 15, c.Get("S"), $"S for {a}+{b}+{cin}");
						Assert.AreEqual(sum >> 4, c.Get("COUT"), $"COUT for {a}+{b}+{cin}");
					}
				}
			}
		}

		[Test]
		public void LoopsIncludeBothEndsCountDownAndStep()
		{
			ChipDescription chip = Build(@"
chip Loops
    in A: 8
    out REV: 8, EVEN: 4

    for i in 7..0
        REV[7 - i] = A[i]
    end
    for i in 0..6 step 2
        EVEN[i >> 1] = A[i]
    end
");
			TestCircuit c = new(chip, library);
			for (uint a = 0; a < 256; a += 7)
			{
				c.Set("A", a);
				c.Step();
				uint reversed = 0, even = 0;
				for (int i = 0; i < 8; i++) reversed |= ((a >> i) & 1) << (7 - i);
				for (int i = 0; i < 4; i++) even |= ((a >> (2 * i)) & 1) << i;
				Assert.AreEqual(reversed, c.Get("REV"), $"REV for {a}");
				Assert.AreEqual(even, c.Get("EVEN"), $"EVEN for {a}");
			}
		}

		[Test]
		public void NamesUsedAboveTheirAssignmentFormFeedback()
		{
			ChipDescription chip = Build(@"
chip SrLatch
    in S_N, R_N
    out Q, QN

    Q = NAND(S_N, QN)
    QN = NAND(R_N, Q)
");
			Assert.AreEqual(2, NandCount(chip), "each NAND(...) is exactly one gate");

			TestCircuit c = new(chip, library);
			c.Set("S_N", 0);
			c.Set("R_N", 1);
			c.Step();
			Assert.AreEqual(1u, c.Get("Q"), "set");
			c.Set("S_N", 1);
			c.Step(3);
			Assert.AreEqual(1u, c.Get("Q"), "hold after set");
			c.Set("R_N", 0);
			c.Step();
			Assert.AreEqual(0u, c.Get("Q"), "reset");
			Assert.AreEqual(1u, c.Get("QN"), "reset");
		}

		[Test]
		public void OperatorsBitsAndJoinWorkOnEveryBit()
		{
			ChipDescription chip = Build(@"
const WIDTH = 4

chip Ops
    in A: 4, B: 4, S
    out AND_: 4, OR_: 4, XOR_: 4, NOT_: 4, NAND_: 4, NOR_: 4, XNOR_: 4
    out ADD: 4, SUB: 4, SHL: 4, SHR: 4, MUX: 4, JOIN: 8, LOW: 4, HIGH: 4, TOP, WIDE: 8
    out EQ, NE, LT, GT, LE, GE, LOGIC

    AND_ = A and B
    OR_ = A or B
    XOR_ = A xor B
    NOT_ = not A
    NAND_ = A nand B
    NOR_ = A nor B
    XNOR_ = A xnor B
    ADD = A + B
    SUB = A - B
    SHL = A << 1
    SHR = A >> 2
    MUX = if S then A else B
    JOIN = join(A, B)
    LOW = join(B, A)[0..WIDTH - 1]
    HIGH = join(A, B)[7..4]
    TOP = A[3]
    WIDE = A + B
    EQ = A == B
    NE = A != B
    LT = A < B
    GT = A > B
    LE = A <= B
    GE = A >= B
    LOGIC = A[0] and not B[0] or S
");
			TestCircuit c = new(chip, library);
			for (uint a = 0; a < 16; a++)
			{
				for (uint b = 0; b < 16; b++)
				{
					uint s = (a + b) & 1;
					c.Set("A", a);
					c.Set("B", b);
					c.Set("S", s);
					c.Step();
					string at = $"A={a} B={b} S={s}";
					Assert.AreEqual(a & b, c.Get("AND_"), "and " + at);
					Assert.AreEqual(a | b, c.Get("OR_"), "or " + at);
					Assert.AreEqual(a ^ b, c.Get("XOR_"), "xor " + at);
					Assert.AreEqual(~a & 15, c.Get("NOT_"), "not " + at);
					Assert.AreEqual(~(a & b) & 15, c.Get("NAND_"), "nand " + at);
					Assert.AreEqual(~(a | b) & 15, c.Get("NOR_"), "nor " + at);
					Assert.AreEqual(~(a ^ b) & 15, c.Get("XNOR_"), "xnor " + at);
					Assert.AreEqual((a + b) & 15, c.Get("ADD"), "+ " + at);
					Assert.AreEqual((a - b) & 15, c.Get("SUB"), "- " + at);
					Assert.AreEqual((a << 1) & 15, c.Get("SHL"), "<< " + at);
					Assert.AreEqual(a >> 2, c.Get("SHR"), ">> " + at);
					Assert.AreEqual(s == 1 ? a : b, c.Get("MUX"), "if " + at);
					Assert.AreEqual((a << 4) | b, c.Get("JOIN"), "join " + at);
					Assert.AreEqual(a, c.Get("LOW"), "[0..3] " + at);
					Assert.AreEqual(a, c.Get("HIGH"), "[7..4] " + at);
					Assert.AreEqual(a >> 3, c.Get("TOP"), "[3] " + at);
					Assert.AreEqual(a + b, c.Get("WIDE"), "8-bit sum " + at);
					Assert.AreEqual(a == b ? 1u : 0u, c.Get("EQ"), "== " + at);
					Assert.AreEqual(a != b ? 1u : 0u, c.Get("NE"), "!= " + at);
					Assert.AreEqual(a < b ? 1u : 0u, c.Get("LT"), "< " + at);
					Assert.AreEqual(a > b ? 1u : 0u, c.Get("GT"), "> " + at);
					Assert.AreEqual(a <= b ? 1u : 0u, c.Get("LE"), "<= " + at);
					Assert.AreEqual(a >= b ? 1u : 0u, c.Get("GE"), ">= " + at);
					bool logic = ((a & 1) == 1 && (b & 1) == 0) || s == 1;
					Assert.AreEqual(logic ? 1u : 0u, c.Get("LOGIC"), "and/or/not " + at);
				}
			}
		}

		[Test]
		public void SymbolsAreShortFormsOfTheWords()
		{
			ChipDescription chip = Build(@"
chip Short
    in A: 4, B: 4
    out Y: 4, Z: 4

    Y = (A & B) | (~A ^ B)
    Z = (A and B) or (not A xor B)
");
			TestCircuit c = new(chip, library);
			for (uint a = 0; a < 16; a++)
			{
				c.Set("A", a);
				c.Set("B", 15 - a);
				c.Step();
				Assert.AreEqual(c.Get("Z"), c.Get("Y"), $"A={a}");
			}
		}

		[Test]
		public void ComparisonsBindTighterThanAndOr()
		{
			ChipDescription chip = Build(@"
chip Decode
    in OP: 4, EN
    out ADD, ANY

    ADD = OP == 2 and EN
    ANY = OP == 1 or OP == 2 or OP == 3
");
			TestCircuit c = new(chip, library);
			for (uint op = 0; op < 16; op++)
			{
				for (uint en = 0; en < 2; en++)
				{
					c.Set("OP", op);
					c.Set("EN", en);
					c.Step();
					Assert.AreEqual(op == 2 && en == 1 ? 1u : 0u, c.Get("ADD"), $"OP={op} EN={en}");
					Assert.AreEqual(op >= 1 && op <= 3 ? 1u : 0u, c.Get("ANY"), $"OP={op}");
				}
			}
		}

		[Test]
		public void LongChoicesContinueOnTheNextLines()
		{
			ChipDescription chip = Build(@"
chip Alu
    in A: 4, B: 4, OP: 4
    out Y: 4

    Y = if OP == 0 then A + B
        else if OP == 1 then A - B
        else if OP == 2 then
            A and B
        else A or
            B
");
			TestCircuit c = new(chip, library);
			for (uint op = 0; op < 4; op++)
			{
				c.Set("A", 12);
				c.Set("B", 10);
				c.Set("OP", op);
				c.Step();
				uint expected = op switch { 0 => 22u & 15, 1 => 2u, 2 => 8u, _ => 14u };
				Assert.AreEqual(expected, c.Get("Y"), $"OP={op}");
			}
		}

		[Test]
		public void NarrowerValuesFillTheTopBitsWithZero()
		{
			Build(@"
chip Pass8
    in D: 8
    out Q: 8

    Q = D
");
			ChipDescription chip = Build(@"
chip Widen
    in A: 4, B
    out Y: 8, Z: 8

    Y = A
    Z = Pass8(B)
");
			TestCircuit c = new(chip, library);
			c.Set("A", 9);
			c.Set("B", 1);
			c.Step();
			Assert.AreEqual(9u, c.Get("Y"));
			Assert.AreEqual(1u, c.Get("Z"));
		}

		[Test]
		public void DeclaredWidthKeepsTheCarry()
		{
			ChipDescription chip = Build(@"
chip Carry
    in A: 4, B: 4
    out SUM: 4, COUT

    wire wide: 5 = A + B
    SUM = wide[0..3]
    COUT = wide[4]
");
			TestCircuit c = new(chip, library);
			c.Set("A", 9);
			c.Set("B", 8);
			c.Step();
			Assert.AreEqual(1u, c.Get("SUM"));
			Assert.AreEqual(1u, c.Get("COUT"));
		}

		[Test]
		public void WiresCanBeBuiltBitByBit()
		{
			ChipDescription chip = Build(@"
chip Reverse
    in A: 4
    out Y: 4

    wire r: 4
    for i in 0..3
        r[i] = A[3 - i]
    end
    Y = r
end
");
			TestCircuit c = new(chip, library);
			c.Set("A", 0b0001);
			c.Step();
			Assert.AreEqual(0b1000u, c.Get("Y"));
		}

		[Test]
		public void ChipsAreCalledUnpackedAndWiredByPin()
		{
			Build(@"
chip HalfAdder
    in A, B
    out SUM, CARRY

    SUM = A xor B
    CARRY = A and B
");
			ChipDescription chip = Build(@"
chip TwoHalves
    in X, Y, Z
    out S, C1, C2

    s1, C1 = HalfAdder(X, Y)
    second = HalfAdder()
    second.A = s1
    second.B = Z
    S = second.SUM
    C2 = second.CARRY
");
			TestCircuit c = new(chip, library);
			for (uint i = 0; i < 8; i++)
			{
				uint x = i & 1, y = (i >> 1) & 1, z = i >> 2;
				c.Set("X", x);
				c.Set("Y", y);
				c.Set("Z", z);
				c.Step();
				Assert.AreEqual(x ^ y ^ z, c.Get("S"), $"S for {x}{y}{z}");
				Assert.AreEqual(x & y, c.Get("C1"), $"C1 for {x}{y}{z}");
				Assert.AreEqual((x ^ y) & z, c.Get("C2"), $"C2 for {x}{y}{z}");
			}
		}

		// Each unpacked name takes the width of its own output; all of them used to get the
		// width of the first output, so 's, c = Add4(...)' made c four bits wide.
		[Test]
		public void UnpackedOutputsKeepTheirOwnWidths()
		{
			Build(@"
chip Add4
    in A: 4, B: 4
    out S: 4, COUT

    wire wide: 5 = A + B
    S = wide[0..3]
    COUT = wide[4]
");
			ChipDescription chip = Build(@"
chip Add4Twice
    in A: 4, B: 4
    out S: 4, C1, C2

    s, carry = Add4(A, B)
    total = s
    total, C2 = Add4(total, B)
    S = total
    C1 = carry
");
			TestCircuit c = new(chip, library);
			c.Set("A", 9);
			c.Set("B", 4);
			c.Step();
			Assert.AreEqual(1u, c.Get("S"));
			Assert.AreEqual(0u, c.Get("C1"));
			Assert.AreEqual(1u, c.Get("C2"));
		}

		[Test]
		public void TriStateBuffersShareOneSignal()
		{
			ChipDescription chip = Build(@"
chip SharedBus
    in A, B, SEL
    out Y

    bus = TRISTATE(A, SEL)
    bus = TRISTATE(B, not SEL)
    Y = bus
");
			TestCircuit c = new(chip, library);
			for (uint i = 0; i < 8; i++)
			{
				uint a = i & 1, b = (i >> 1) & 1, sel = i >> 2;
				c.Set("A", a);
				c.Set("B", b);
				c.Set("SEL", sel);
				c.Step(2);
				Assert.AreEqual(sel == 1 ? a : b, c.Get("Y"), $"A={a} B={b} SEL={sel}");
			}
		}

		[Test]
		public void ConstantConditionsAreChosenWhenBuilding()
		{
			ChipDescription chip = Build(@"
const INVERT = 1

chip Maybe
    in A: 4
    out Y: 4

    Y = if INVERT then not A else A
");
			Assert.AreEqual(4, NandCount(chip), "only the chosen branch is built");
		}

		// ---- Gate economy ----

		[Test]
		public void LogicUsesFewNandGates()
		{
			Assert.AreEqual(5, NandCount(Build("chip H\n    in A, B\n    out S, C\n    S = A xor B\n    C = A and B\n")), "half adder");
			Assert.AreEqual(9, NandCount(Build("chip F\n    in A, B, CI\n    out S, CO\n    S = A xor B xor CI\n    CO = (A and B) or (CI and (A xor B))\n")), "full adder");
			Assert.LessOrEqual(NandCount(Build("chip Add4\n    in A: 4, B: 4\n    out Y: 4\n    Y = A + B\n")), 4 * 9, "4-bit adder");
			Assert.AreEqual(4, NandCount(Build("chip M\n    in A, B, S\n    out Y\n    Y = if S then A else B\n")), "2:1 multiplexer");
			Assert.AreEqual(1, NandCount(Build("chip N\n    in A, B\n    out Y\n    Y = A nand B\n")), "nand is one gate");
			Assert.AreEqual(0, NandCount(Build("chip W\n    in A: 4\n    out Y: 4\n    Y = A and 0b1111\n")), "AND with all ones is a wire");
		}

		// ---- Layout ----

		[TestCase("chip Add8\n    in A: 8, B: 8\n    out Y: 8\n    Y = A + B\n")]
		[TestCase("chip Latch\n    in S_N, R_N\n    out Q, QN\n    Q = NAND(S_N, QN)\n    QN = NAND(R_N, Q)\n")]
		[TestCase("chip Mix\n    in A: 4, B: 4, S\n    out Y: 4, EQ\n    Y = if S then A + B else A xor B\n    EQ = A == B\n")]
		public void LayoutHasNoOverlappingPartsAndOnlyStraightWires(string source)
		{
			ChipDescription chip = Build(source);

			for (int i = 0; i < chip.SubChips.Length; i++)
			{
				for (int j = i + 1; j < chip.SubChips.Length; j++)
				{
					SubChipDescription a = chip.SubChips[i];
					SubChipDescription b = chip.SubChips[j];
					UnityEngine.Vector2 sa = library.GetChipDescription(a.Name).Size;
					UnityEngine.Vector2 sb = library.GetChipDescription(b.Name).Size;
					bool overlap = Math.Abs(a.Position.x - b.Position.x) < (sa.x + sb.x) / 2 &&
					               Math.Abs(a.Position.y - b.Position.y) < (sa.y + sb.y) / 2;
					Assert.IsFalse(overlap, $"{a.Name}#{a.ID} overlaps {b.Name}#{b.ID}");
				}
			}

			foreach (WireDescription wire in chip.Wires)
			{
				// The first and last points are replaced by the pins; the bends in between
				// must form horizontal and vertical segments only.
				for (int p = 2; p < wire.Points.Length - 1; p++)
				{
					UnityEngine.Vector2 from = wire.Points[p - 1];
					UnityEngine.Vector2 to = wire.Points[p];
					bool straight = Math.Abs(from.x - to.x) < 0.001f || Math.Abs(from.y - to.y) < 0.001f;
					Assert.IsTrue(straight, $"diagonal wire segment {from} -> {to}");
				}
			}
		}

		// ---- Messages ----

		[Test]
		public void UnknownNamesSuggestTheClosestOne()
		{
			RhdlDiagnostic[] errors = Errors("chip C\n    in A, B\n    out Y\n    total = A and B\n    Y = totl\n");
			AssertMessage(errors, 5, "Did you mean 'total'");
		}

		[Test]
		public void PinsCannotBeAssignedTwice()
		{
			AssertMessage(Errors("chip C\n    in A, B\n    out Y\n    Y = A\n    Y = B\n"), 5, "assigned more than once");
		}

		[Test]
		public void VariablesMustBeAssignedBeforeUse()
		{
			RhdlDiagnostic[] errors = Errors("chip C\n    in A\n    out Y\n    Y = t\n    t = A\n    t = not t\n");
			AssertMessage(errors, 4, "used before it is assigned");
		}

		[Test]
		public void TooWideValuesExplainTheFix()
		{
			AssertMessage(Errors("chip C\n    in A: 8\n    out Y: 4\n    Y = A\n"), 4, "[0..3]");
			AssertMessage(Errors("chip C\n    in A: 4\n    out Y\n    Y = A[2..5]\n"), 4, "its bits are 0..3");
		}

		[Test]
		public void PythonHabitsGetAHint()
		{
			AssertMessage(Errors("chip C\n    in A, S\n    out Y\n    Y = A if S else 0\n"), 4, "if select then A else B");
			AssertMessage(Errors("chip C\n    in A, S\n    out Y\n    if S:\n        Y = A\n"), 4, "if select then A else B");
			AssertMessage(Errors("chip C\n    in A: 4\n    out Y\n    for i in range(4):\n        Y = A[i]\n"), 4, "for i in 0..3");
			AssertMessage(Errors("chip C\n    in A: 4\n    out Y: 2\n    Y = A[0:2]\n"), 4, "A[0..3]");
			AssertMessage(Errors("chip C\n    in A: u4\n    out Y\n    Y = A[0]\n"), 2, "': 4'");
			AssertMessage(Errors("chip C(A) -> Y:\n    Y = A\n"), 1, "Pins go on their own lines");
		}

		[Test]
		public void UnclosedLoopsAreReported()
		{
			AssertMessage(Errors("chip C\n    in A: 4\n    out Y: 4\n    for i in 0..3\n        Y[i] = A[i]\n"), 4, "has no 'end'");
		}

		[Test]
		public void KeywordsCannotBeNames()
		{
			AssertMessage(Errors("chip C\n    in A\n    out Y\n    out = A\n    Y = A\n"), 4, "");
			AssertMessage(Errors("chip C\n    in A, xor\n    out Y\n    Y = A\n"), 2, "keyword");
		}

		[Test]
		public void OldVersionSourcePointsToTheMigrationGuide()
		{
			RhdlDiagnostic[] errors = Errors("circuit Old\n    input A\n    output Y\n    Y = not A\nend\n");
			Assert.IsTrue(errors.Any(d => d.Message.Contains("Coming from RHDL 0.6")), string.Join("\n", errors.Select(d => d.ToString())));
		}

		[Test]
		public void MissingOutputsAndInputsAreReported()
		{
			AssertMessage(Errors("chip C\n    in A\n    out Y, Z\n    Y = A\n"), 3, "Output 'Z' is never assigned");
			AssertMessage(Errors("chip C\n    in A\n    out Y\n    Y = NAND(A)\n"), 4, "NAND needs both inputs");
			AssertMessage(Errors("chip C\n    in A\n    Y = A\n"), 1, "out Y");
		}

		[Test]
		public void PinWidthsMustExistOnChips()
		{
			AssertMessage(Errors("chip C\n    in A: 6\n    out Y\n    Y = A[0]\n"), 2, "1, 4 or 8 bits");
		}

		// ---- RHDL 1.0 sources ----

		const string Version1Source = @"
WIDTH = 4

circuit Mix(A: u4, B: u4, S) -> (Y: u4, LOW: u4, R: u4, W: u8):
    # a comment
    t = (A & B) | ~A    # trailing comment
    Y = A + B if S and not A[0] else t ^ B
    LOW = cat(A, B)[0:2]
    for i in range(WIDTH):
        R[i] = A[WIDTH - 1 - i]
    W = u8(A) + u8(B)
";

		[Test]
		public void Version1SourcesStillBuild()
		{
			TestCircuit c = new(Build(Version1Source), library);
			c.Set("A", 6);
			c.Set("B", 3);
			c.Set("S", 1);
			c.Step();
			Assert.AreEqual(9u, c.Get("Y"));
			Assert.AreEqual(3u, c.Get("LOW"));
			Assert.AreEqual(6u, c.Get("R"));
			Assert.AreEqual(9u, c.Get("W"));
		}

		[Test]
		public void Version1SourcesUpgradeToTheSameChip()
		{
			Assert.IsTrue(RhdlUpgrade.IsLegacySource(Version1Source));
			Assert.IsTrue(RhdlUpgrade.TryUpgrade(Version1Source, out string upgraded, out string error), error);
			Assert.IsFalse(RhdlUpgrade.IsLegacySource(upgraded), upgraded);
			StringAssert.Contains("chip Mix", upgraded);
			StringAssert.Contains("# a comment", upgraded);
			StringAssert.Contains("# trailing comment", upgraded);
			StringAssert.Contains("for i in 0..WIDTH - 1", upgraded);
			StringAssert.Contains("join(A, B)[0..1]", upgraded);

			string before = Serializer.SerializeChipDescription(RhdlCompiler.Compile(Version1Source, library).Description);
			RhdlCompileResult after = RhdlCompiler.Compile(upgraded, library);
			Assert.IsTrue(after.Success, upgraded + "\n" + string.Join("\n", after.Diagnostics.Select(d => d.ToString())));
			Assert.AreEqual(before, Serializer.SerializeChipDescription(after.Description), upgraded);
		}

		// ---- Sources that must not take the editor down ----

		[Test]
		public void DeeplyNestedExpressionsAreRejected()
		{
			int depth = RhdlCompiler.MaxExpressionNesting + 1;
			string brackets = new string('(', depth) + "A" + new string(')', depth);
			AssertMessage(Errors($"chip C\n    in A\n    out Y\n    Y = {brackets}\n"), 4, "nested more than");
			AssertMessage(Errors($"chip C\n    in A\n    out Y\n    Y = {new string('~', 50000)}A\n"), 4, "nested more than");
			AssertMessage(Errors($"chip C\n    in A\n    out Y\n    Y = {string.Concat(Enumerable.Repeat("not ", depth))}A\n"), 4, "nested more than");

			// Just inside the limit still builds.
			depth = RhdlCompiler.MaxExpressionNesting - 1;
			Build($"chip C\n    in A\n    out Y\n    Y = {new string('(', depth)}A{new string(')', depth)}\n");
		}

		[Test]
		public void LongChainsDoNotOverflowTheStack()
		{
			// Each of these used to recurse deeper than a 1 MB stack allows.
			Build($"chip LongAnd\n    in A\n    out Y\n    Y = {string.Join(" and ", Enumerable.Repeat("A", 3000))}\n");

			const int wires = 5000;
			string reversed = "chip Reversed\n    in A\n    out Y\n    Y = w0\n" +
			                  string.Concat(Enumerable.Range(0, wires).Select(i => $"    w{i} = w{i + 1}\n")) +
			                  $"    w{wires} = not A\n";
			TestCircuit c = new(Build(reversed), library);
			c.Set("A", 0);
			c.Step();
			Assert.AreEqual(1, c.Get("Y"));
		}

		[Test]
		public void LoopsWithEmptyBodiesStillHaveALimit()
		{
			System.Diagnostics.Stopwatch time = System.Diagnostics.Stopwatch.StartNew();
			AssertMessage(Errors("chip C\n    in A\n    out Y\n    for i in 0..1000000000000\n    end\n    Y = A\n"), 4, "Loops repeat more than");
			AssertMessage(Errors("chip C\n    in A\n    out Y\n    for i in 0..1000000000\n        for j in 0..0 step 1\n        end\n    end\n    Y = A\n"), 4, "Loops repeat more than");
			Assert.Less(time.ElapsedMilliseconds, 5000);
		}
	}
}
