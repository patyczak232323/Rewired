using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace DLS.Tests
{
	// IL2CPP (Android, WebGL) cannot generate code at run time, so there the JIT compilers run
	// their gate programs through GateProgram's interpreter instead. These tests compile every
	// chip of the bundled projects both ways and feed the native and the interpreted program
	// the same random signals, comparing every slot bit for bit. That covers every operation,
	// tri-state flags and constants included, independently of power-on randomness.
	public class InterpreterEquivalenceTests
	{
		const int RoundsPerProgram = 150;

		// Each comparison compiles the native program with the interpreter off, then the
		// interpreted one; afterwards the path the whole run uses is restored (EngineSetup).
		[SetUp]
		public void SetUp() => UseInterpreter(false);

		[TearDown]
		public void TearDown()
		{
			UseInterpreter(EngineSetup.InterpreterForced);
			RewiredEngine.Reset();
		}

		static void UseInterpreter(bool interpret)
		{
			CombinationalJitCompiler.ForceInterpreter = interpret;
			FeedbackJitCompiler.ForceInterpreter = interpret;
			CombinationalJitCompiler.NotifyDescriptionsChanged();
			FeedbackJitCompiler.NotifyDescriptionsChanged();
		}

		static IEnumerable<string> BundledProjects =>
			Directory.GetDirectories(Path.Combine(Application.dataPath, "Resources", "BundledProjects")).Select(Path.GetFileName).OrderBy(n => n);

		// Every custom chip of a bundled project, each built once as a SimChip tree. Building
		// starts background lookup-table builds, so each project is built once for all tests;
		// the tests only read the trees.
		static readonly Dictionary<string, (List<SimChip> chips, ChipLibrary library)> builtProjects = new();

		static List<SimChip> CustomChipsOf(string project, out ChipLibrary library)
		{
			if (!builtProjects.TryGetValue(project, out var built)) builtProjects[project] = built = BuildProject(project);
			library = built.library;
			return built.chips;
		}

		static (List<SimChip>, ChipLibrary) BuildProject(string project)
		{
			ChipLibrary library;
			string folder = Path.Combine(Application.dataPath, "Resources", "BundledProjects", project, "Chips");
			ChipDescription[] chips = Directory.GetFiles(folder, "*.json").OrderBy(f => f)
				.Select(f => Serializer.DeserializeChipDescription(File.ReadAllText(f))).ToArray();
			library = TestLibrary.Create(chips);

			List<SimChip> result = new();
			foreach (ChipDescription chip in chips)
			{
				if (chip.ChipType != ChipType.Custom || (chip.Displays?.Length ?? 0) > 0) continue;
				result.Add(RewiredEngine.BuildChip(chip, library));
			}

			return (result, library);
		}

		[TestCaseSource(nameof(BundledProjects))]
		public void CombinationalProgramsMatchNativeCode(string project)
		{
			Assume.That(CombinationalJitCompiler.DynamicCodeAvailable, "needs a platform that can generate code to compare against");
			Random random = new(1234);
			int compared = 0;

			foreach (SimChip root in AllCustomChips(CustomChipsOf(project, out _)))
			{
				CompiledCombinationalProgram native = CombinationalJitCompiler.TryCompileStandalone(root, root.Description.Name);
				if (native == null) continue;
				CombinationalJitCompiler.ForceInterpreter = true;
				CompiledCombinationalProgram interpreted = CombinationalJitCompiler.TryCompileStandalone(root, root.Description.Name);
				CombinationalJitCompiler.ForceInterpreter = false;
				Assert.IsNotNull(interpreted, root.Description.Name);
				Assert.AreEqual(native.ScratchCount, interpreted.ScratchCount, root.Description.Name);

				for (int round = 0; round < RoundsPerProgram; round++)
				{
					uint[] scratchA = RandomSignals(random, native.ScratchCount);
					uint[] scratchB = (uint[])scratchA.Clone();
					uint[] outputsA = new uint[native.OutputCount];
					uint[] outputsB = new uint[native.OutputCount];
					native.Run(scratchA, outputsA);
					interpreted.Run(scratchB, outputsB);
					CollectionAssert.AreEqual(scratchA, scratchB, $"{root.Description.Name}: scratch differs in round {round}");
					CollectionAssert.AreEqual(outputsA, outputsB, $"{root.Description.Name}: outputs differ in round {round}");
				}

				compared++;
			}

			TestContext.WriteLine($"{project}: {compared} combinational programs compared");
		}

		[TestCaseSource(nameof(BundledProjects))]
		public void FeedbackProgramsMatchNativeCode(string project)
		{
			Assume.That(FeedbackJitCompiler.DynamicCodeAvailable, "needs a platform that can generate code to compare against");
			Random random = new(5678);
			int compared = 0;

			foreach (SimChip root in AllCustomChips(CustomChipsOf(project, out _)))
			{
				CompiledFeedbackProgram native = FeedbackJitCompiler.TryCompileStandalone(root, root.Description.Name);
				if (native == null) continue;
				FeedbackJitCompiler.ForceInterpreter = true;
				CompiledFeedbackProgram interpreted = FeedbackJitCompiler.TryCompileStandalone(root, root.Description.Name);
				FeedbackJitCompiler.ForceInterpreter = false;
				Assert.IsNotNull(interpreted, root.Description.Name);
				Assert.AreEqual(native.ScratchCount, interpreted.ScratchCount, root.Description.Name);

				for (int round = 0; round < RoundsPerProgram; round++)
				{
					uint[] current = RandomSignals(random, native.ScratchCount);
					uint[] nextA = RandomSignals(random, native.ScratchCount);
					uint[] nextB = (uint[])nextA.Clone();
					native.RunSweep(current, nextA);
					interpreted.RunSweep((uint[])current.Clone(), nextB);
					CollectionAssert.AreEqual(nextA, nextB, $"{root.Description.Name}: sweep differs in round {round}");

					uint[] outputsA = new uint[native.OutputCount];
					uint[] outputsB = new uint[native.OutputCount];
					native.WriteOutputs(nextA, outputsA);
					interpreted.WriteOutputs(nextB, outputsB);
					CollectionAssert.AreEqual(outputsA, outputsB, $"{root.Description.Name}: outputs differ in round {round}");
				}

				compared++;
			}

			TestContext.WriteLine($"{project}: {compared} feedback programs compared");
		}

		// Chips that use every primitive the compilers support, including unconnected inputs
		// (constants), because the bundled projects do not use all of them.
		[TestCase(false)]
		[TestCase(true)]
		public void EveryPrimitiveMatchesNativeCode(bool withFeedback)
		{
			Assume.That(CombinationalJitCompiler.DynamicCodeAvailable && FeedbackJitCompiler.DynamicCodeAvailable, "needs a platform that can generate code to compare against");
			ChipLibrary library = TestLibrary.Create();
			ChipDescription description = EveryPrimitiveChip(library, withFeedback);
			library.NotifyChipSaved(description, false);
			SimChip root = RewiredEngine.BuildChip(description, library);
			Random random = new(91011);

			if (withFeedback)
			{
				CompiledFeedbackProgram native = FeedbackJitCompiler.TryCompileStandalone(root, description.Name);
				Assert.IsNotNull(native, "the chip should compile as a feedback program");
				FeedbackJitCompiler.ForceInterpreter = true;
				CompiledFeedbackProgram interpreted = FeedbackJitCompiler.TryCompileStandalone(root, description.Name);
				Assert.IsNotNull(interpreted);

				for (int round = 0; round < 5000; round++)
				{
					uint[] current = RandomSignals(random, native.ScratchCount);
					uint[] nextA = RandomSignals(random, native.ScratchCount);
					uint[] nextB = (uint[])nextA.Clone();
					native.RunSweep(current, nextA);
					interpreted.RunSweep(current, nextB);
					CollectionAssert.AreEqual(nextA, nextB, $"sweep differs in round {round}");
				}
			}
			else
			{
				CompiledCombinationalProgram native = CombinationalJitCompiler.TryCompileStandalone(root, description.Name);
				Assert.IsNotNull(native, "the chip should compile as a combinational program");
				CombinationalJitCompiler.ForceInterpreter = true;
				CompiledCombinationalProgram interpreted = CombinationalJitCompiler.TryCompileStandalone(root, description.Name);
				Assert.IsNotNull(interpreted);

				for (int round = 0; round < 5000; round++)
				{
					uint[] scratchA = RandomSignals(random, native.ScratchCount);
					uint[] scratchB = (uint[])scratchA.Clone();
					uint[] outputsA = new uint[native.OutputCount];
					uint[] outputsB = new uint[native.OutputCount];
					native.Run(scratchA, outputsA);
					interpreted.Run(scratchB, outputsB);
					CollectionAssert.AreEqual(scratchA, scratchB, $"scratch differs in round {round}");
					CollectionAssert.AreEqual(outputsA, outputsB, $"outputs differ in round {round}");
				}
			}
		}

		static ChipDescription EveryPrimitiveChip(ChipLibrary library, bool withFeedback)
		{
			ChipBuilder chip = new(withFeedback ? "EveryPrimitiveLatch" : "EveryPrimitive");
			PinAddress a = chip.Input("A", PinBitCount.Bit4);
			PinAddress b = chip.Input("B", PinBitCount.Bit4);
			PinAddress x = chip.Input("X", PinBitCount.Bit8);
			PinAddress enable = chip.Input("EN");
			PinAddress set = chip.Input("S");

			// Pin ids of a built-in chip, in the order the library declares them.
			int[] Ins(string name) => library.GetChipDescription(name).InputPins.Select(p => p.ID).ToArray();
			int[] Outs(string name) => library.GetChipDescription(name).OutputPins.Select(p => p.ID).ToArray();
			(int id, int[] ins, int[] outs) Part(ChipType type)
			{
				string name = ChipTypeHelper.GetName(type);
				return (chip.Chip(name), Ins(name), Outs(name));
			}

			var merge48 = Part(ChipType.Merge_4To8Bit);
			chip.Wire(a, ChipBuilder.Pin(merge48.id, merge48.ins[0]));
			chip.Wire(b, ChipBuilder.Pin(merge48.id, merge48.ins[1]));

			var split84 = Part(ChipType.Split_8To4Bit);
			chip.Wire(ChipBuilder.Pin(merge48.id, merge48.outs[0]), ChipBuilder.Pin(split84.id, split84.ins[0]));

			var split41 = Part(ChipType.Split_4To1Bit);
			chip.Wire(ChipBuilder.Pin(split84.id, split84.outs[0]), ChipBuilder.Pin(split41.id, split41.ins[0]));

			var split81 = Part(ChipType.Split_8To1Bit);
			chip.Wire(x, ChipBuilder.Pin(split81.id, split81.ins[0]));

			var tristate = Part(ChipType.TriStateBuffer);
			chip.Wire(ChipBuilder.Pin(split41.id, split41.outs[0]), ChipBuilder.Pin(tristate.id, tristate.ins[0]));
			chip.Wire(enable, ChipBuilder.Pin(tristate.id, tristate.ins[1]));

			// One NAND input left unconnected: a constant operand.
			int lone = chip.Chip(ChipTypeHelper.GetName(ChipType.Nand));
			chip.Wire(ChipBuilder.Pin(tristate.id, tristate.outs[0]), new PinAddress(lone, ChipBuilder.NandInA));

			int n1 = chip.Nand(ChipBuilder.Pin(split81.id, split81.outs[0]), ChipBuilder.Pin(split81.id, split81.outs[7]));

			// The last two bits of the 1-to-4 merge stay unconnected: constants again.
			var merge14 = Part(ChipType.Merge_1To4Bit);
			chip.Wire(ChipBuilder.NandOutput(lone), ChipBuilder.Pin(merge14.id, merge14.ins[0]));
			chip.Wire(ChipBuilder.NandOutput(n1), ChipBuilder.Pin(merge14.id, merge14.ins[1]));

			var merge18 = Part(ChipType.Merge_1To8Bit);
			for (int i = 0; i < 8; i++) chip.Wire(ChipBuilder.Pin(split81.id, split81.outs[7 - i]), ChipBuilder.Pin(merge18.id, merge18.ins[i]));

			var bus = Part(ChipType.Bus_8Bit);
			chip.Wire(ChipBuilder.Pin(merge18.id, merge18.outs[0]), ChipBuilder.Pin(bus.id, bus.ins[0]));

			// A bus with nothing driving it passes on a constant.
			var floating = Part(ChipType.Bus_4Bit);
			PinAddress undriven = chip.Output("U", PinBitCount.Bit4);
			chip.Wire(ChipBuilder.Pin(floating.id, floating.outs[0]), undriven);

			PinAddress nibbles = chip.Output("N", PinBitCount.Bit4);
			chip.Wire(ChipBuilder.Pin(merge14.id, merge14.outs[0]), nibbles);
			PinAddress low = chip.Output("L", PinBitCount.Bit4);
			chip.Wire(ChipBuilder.Pin(split84.id, split84.outs[1]), low);
			PinAddress wide = chip.Output("W", PinBitCount.Bit8);
			chip.Wire(ChipBuilder.Pin(bus.id, bus.outs[0]), wide);

			if (withFeedback)
			{
				// An SR latch whose set input passes through the tri-state buffer path.
				int q = chip.Chip(ChipTypeHelper.GetName(ChipType.Nand));
				int nq = chip.Nand(ChipBuilder.NandOutput(q), ChipBuilder.NandOutput(lone));
				chip.Wire(set, new PinAddress(q, ChipBuilder.NandInA));
				chip.Wire(ChipBuilder.NandOutput(nq), new PinAddress(q, ChipBuilder.NandInB));
				PinAddress latched = chip.Output("Q");
				chip.Wire(ChipBuilder.NandOutput(q), latched);
			}

			return chip.Build();
		}

		// The interpreted feedback executor settles by evaluating only what changed (GateProgram
		// .Settle) instead of full sweeps. Run both executors side by side on the same random
		// input sequence and compare outputs, convergence, sweep counts and every gate's state.
		[TestCaseSource(nameof(BundledProjects))]
		public void SettlingMatchesFullSweeps(string project)
		{
			Assume.That(FeedbackJitCompiler.DynamicCodeAvailable, "needs a platform that can generate code to compare against");
			int compared = 0;
			foreach (SimChip root in AllCustomChips(CustomChipsOf(project, out ChipLibrary library)))
			{
				if (CompareExecutors(root.Description, library, new Random(compared + 17))) compared++;
			}

			Assert.Greater(compared, 0);
			TestContext.WriteLine($"{project}: {compared} feedback chips compared");
		}

		[Test]
		public void SettlingMatchesFullSweepsWithoutConvergence()
		{
			// Three inverters in a ring oscillate while EN is high and never settle, so both
			// executors run into the sweep limit and must stop in the same state.
			ChipBuilder chip = new("RingOscillator");
			PinAddress enable = chip.Input("EN");
			int first = chip.Chip(ChipTypeHelper.GetName(ChipType.Nand));
			int second = chip.Nand(ChipBuilder.NandOutput(first), ChipBuilder.NandOutput(first));
			int third = chip.Nand(ChipBuilder.NandOutput(second), ChipBuilder.NandOutput(second));
			chip.Wire(enable, new PinAddress(first, ChipBuilder.NandInA));
			chip.Wire(ChipBuilder.NandOutput(third), new PinAddress(first, ChipBuilder.NandInB));
			PinAddress output = chip.Output("Y");
			chip.Wire(ChipBuilder.NandOutput(third), output);

			ChipDescription description = chip.Build();
			ChipLibrary library = TestLibrary.Create(description);
			Assert.IsTrue(CompareExecutors(description, library, new Random(3), expectNonConvergence: true));
		}

		[Test]
		public void SettlingMatchesFullSweepsOnEveryPrimitive()
		{
			ChipLibrary library = TestLibrary.Create();
			ChipDescription description = EveryPrimitiveChip(library, true);
			library.NotifyChipSaved(description, false);
			Assert.IsTrue(CompareExecutors(description, library, new Random(4)));
		}

		static bool CompareExecutors(ChipDescription description, ChipLibrary library, Random random, bool expectNonConvergence = false)
		{
			SimChip nativeRoot = RewiredEngine.BuildChip(description, library);
			CompiledFeedbackProgram nativeProgram = FeedbackJitCompiler.TryCompileStandalone(nativeRoot, description.Name);
			if (nativeProgram == null) return false;
			Assert.IsNull(nativeProgram.Interpreted);

			SimChip interpretedRoot = RewiredEngine.BuildChip(description, library);
			FeedbackJitCompiler.ForceInterpreter = true;
			CompiledFeedbackProgram interpretedProgram = FeedbackJitCompiler.TryCompileStandalone(interpretedRoot, description.Name);
			FeedbackJitCompiler.ForceInterpreter = false;
			Assert.IsNotNull(interpretedProgram?.Interpreted, description.Name);

			CompiledFeedbackExecutor native = nativeProgram.CreateExecutor(nativeRoot);
			CompiledFeedbackExecutor interpreted = interpretedProgram.CreateExecutor(interpretedRoot);
			native.SynchronizeFromChipTree();
			interpreted.SynchronizeFromChipTree();

			List<SimPin> nativePins = PrimitiveOutputs(nativeRoot);
			List<SimPin> interpretedPins = PrimitiveOutputs(interpretedRoot);
			bool sawNonConvergence = false;

			for (int step = 0; step < 200; step++)
			{
				for (int i = 0; i < nativeRoot.InputPins.Length; i++)
				{
					// Mostly small changes, like a clock or one data bit, sometimes a new word.
					uint value = nativeRoot.InputPins[i].State;
					int choice = random.Next(10);
					if (choice < 4) value ^= 1u;
					else if (choice < 6) value = (uint)random.Next(0, 256);
					else if (choice == 6) value = (uint)random.Next() ^ ((uint)random.Next() << 16);
					nativeRoot.InputPins[i].State = value;
					interpretedRoot.InputPins[i].State = value;
				}

				bool convergedA = native.Evaluate(nativeRoot, out uint[] outputsA, out int sweepsA);
				bool convergedB = interpreted.Evaluate(interpretedRoot, out uint[] outputsB, out int sweepsB);
				string at = $"{description.Name}, step {step}";
				Assert.AreEqual(convergedA, convergedB, at + ": convergence");
				Assert.AreEqual(sweepsA, sweepsB, at + ": sweeps");
				CollectionAssert.AreEqual(outputsA, outputsB, at + ": outputs");
				sawNonConvergence |= !convergedA;

				native.MaterializeState();
				interpreted.MaterializeState();
				for (int i = 0; i < nativePins.Count; i++)
				{
					Assert.AreEqual(nativePins[i].State, interpretedPins[i].State, $"{at}: gate output {i}");
				}
			}

			if (expectNonConvergence) Assert.IsTrue(sawNonConvergence, "the circuit should have hit the sweep limit");
			return true;
		}

		static List<SimPin> PrimitiveOutputs(SimChip chip)
		{
			List<SimPin> pins = new();
			void Collect(SimChip c)
			{
				if (c.ChipType == ChipType.Custom)
				{
					foreach (SimChip sub in c.SubChips) Collect(sub);
				}
				else pins.AddRange(c.OutputPins);
			}

			Collect(chip);
			return pins;
		}

		[Test]
		public void EveryKindOfProgramIsCovered()
		{
			// The comparisons above only mean something if the bundled projects contain
			// programs of both kinds.
			int combinational = 0, feedback = 0;
			foreach (string project in BundledProjects)
			{
				foreach (SimChip root in AllCustomChips(CustomChipsOf(project, out _)))
				{
					if (CombinationalJitCompiler.TryCompileStandalone(root, root.Description.Name) != null) combinational++;
					if (FeedbackJitCompiler.TryCompileStandalone(root, root.Description.Name) != null) feedback++;
				}
			}

			Assert.Greater(combinational, 5, "combinational programs");
			Assert.Greater(feedback, 2, "feedback programs");
		}

		// Each chip tree and every custom subchip in it, once per chip name.
		static IEnumerable<SimChip> AllCustomChips(List<SimChip> roots)
		{
			HashSet<string> seen = new();
			Stack<SimChip> stack = new(roots);
			while (stack.Count > 0)
			{
				SimChip chip = stack.Pop();
				if (chip.ChipType != ChipType.Custom) continue;
				if (seen.Add(chip.Description.Name)) yield return chip;
				foreach (SimChip sub in chip.SubChips) stack.Push(sub);
			}
		}

		// Signals as the engine stores them: bit states in the low half, tri-state flags in the
		// high half. Mixes fully random words with the common clean values.
		static uint[] RandomSignals(Random random, int count)
		{
			uint[] values = new uint[count];
			for (int i = 0; i < count; i++)
			{
				values[i] = random.Next(6) switch
				{
					0 => 0u,
					1 => 1u,
					2 => 0xFFFF0000u,
					3 => (uint)random.Next(0, 256),
					_ => (uint)random.Next() ^ ((uint)random.Next() << 16)
				};
			}

			return values;
		}
	}
}
