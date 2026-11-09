using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DLS.Description;
using DLS.Game;

namespace DLS.Simulation
{
	// Accelerator for gate-feedback custom chips.
	//
	// Unlike CombinationalJitCompiler this compiler deliberately accepts cycles. The chip
	// becomes one GateProgram whose sweeps read only the previous delta-cycle state and
	// write the next one, preserving the deterministic solver's simultaneous-update
	// semantics. The executor settles it by evaluating only the operations whose inputs
	// changed (GateProgram.Settle), on every platform: generated code that ran full sweeps
	// was about half as fast on the example projects, because a sweep evaluated every gate
	// although few change. The normal deterministic engine still performs power-on
	// settling; only after that fixed point is reached is the executor synchronized and
	// allowed to replace the expanded gate hierarchy.
	public static class FeedbackJitCompiler
	{
		const uint DisconnectedState = 0xFFFF0000u;
		const int MinPrimitiveNodeCount = 2;

		// Every sweep evaluates the whole program and a deep circuit needs many sweeps before it
		// settles, so a large program costs far more than simulating only the gates that change.
		// Larger chips are not compiled as a whole; their smaller feedback subchips (registers,
		// latches) still are. A 256-byte RAM built from registers ran 40x slower as one program.
		internal const int MaxPrimitiveNodeCount = 1024;

		sealed class ProgramHolder
		{
			public readonly CompiledFeedbackProgram Program;
			public ProgramHolder(CompiledFeedbackProgram program) => Program = program;
		}

		static readonly object programCacheLock = new();
		static ConditionalWeakTable<ChipDescription, ProgramHolder> programs = new();

		public static string LastFailureReason { get; private set; } = string.Empty;

		internal static void NotifyDescriptionsChanged()
		{
			lock (programCacheLock)
			{
				programs = new ConditionalWeakTable<ChipDescription, ProgramHolder>();
			}
		}

		internal static void Attach(SimChip simChip, ChipDescription description, ChipLibrary library)
		{
			if (simChip == null) return;
			simChip.FeedbackExecutor = null;

			if (simChip.CompiledExecutor != null ||
			    description == null ||
			    library == null ||
			    description.ChipType != ChipType.Custom ||
			    (description.Displays?.Length ?? 0) > 0)
			{
				return;
			}

			CompiledFeedbackProgram program = GetOrCompileProgram(simChip, description);
			if (program == null) return;

			CompiledFeedbackExecutor executor = program.CreateExecutor(simChip);
			if (executor != null) simChip.FeedbackExecutor = executor;
		}

		// Compiles a private program for one chip, outside the program cache. Null when the chip
		// is not supported. Used by the tests.
		internal static CompiledFeedbackProgram TryCompileStandalone(SimChip root, string chipName)
		{
			try
			{
				return CompileProgram(root, chipName);
			}
			catch (Exception)
			{
				return null;
			}
		}

		static CompiledFeedbackProgram GetOrCompileProgram(SimChip representative, ChipDescription description)
		{
			lock (programCacheLock)
			{
				if (programs.TryGetValue(description, out ProgramHolder holder))
				{
					return holder.Program;
				}
			}

			CompiledFeedbackProgram program;
			try
			{
				program = CompileProgram(representative, description.Name);
			}
			catch (Exception e)
			{
				LastFailureReason = e.GetType().Name + ": " + e.Message;
				return null;
			}

			if (program == null) return null;

			lock (programCacheLock)
			{
				if (programs.TryGetValue(description, out ProgramHolder existing)) return existing.Program;
				programs.Add(description, new ProgramHolder(program));
			}

			return program;
		}

		static CompiledFeedbackProgram CompileProgram(SimChip root, string chipName)
		{
			if (root == null || root.ChipType != ChipType.Custom) return null;

			Stopwatch timer = Stopwatch.StartNew();

			List<SimPin> localPins = new();
			HashSet<SimPin> localPinSet = new();
			List<SimChip> primitiveChips = new();
			CollectLocalGraph(root, localPins, localPinSet, primitiveChips);

			// Reject side-effecting/clocked/source primitives before removing sink-only
			// terminators. A buzzer, key, clock, RAM or display must never disappear merely
			// because it has no output pin used by the compiled dataflow.
			for (int i = 0; i < primitiveChips.Count; i++)
			{
				if (!IsSupportedPrimitive(primitiveChips[i].ChipType)) return null;
			}

			primitiveChips.RemoveAll(c => c.OutputPins.Length == 0);
			if (primitiveChips.Count < MinPrimitiveNodeCount) return null;
			if (primitiveChips.Count > MaxPrimitiveNodeCount) return null;

			Dictionary<SimPin, SimPin> incomingSource = BuildIncomingSourceMap(localPins, localPinSet);
			Dictionary<SimPin, int> rootInputSlots = new();
			for (int i = 0; i < root.InputPins.Length; i++) rootInputSlots[root.InputPins[i]] = i;

			Dictionary<SimPin, SignalRef> primitiveOutputSignals = new();
			List<SimPin> orderedPrimitiveOutputs = new();
			int nextScratchSlot = root.InputPins.Length;

			for (int nodeIndex = 0; nodeIndex < primitiveChips.Count; nodeIndex++)
			{
				SimChip chip = primitiveChips[nodeIndex];
				for (int outputIndex = 0; outputIndex < chip.OutputPins.Length; outputIndex++)
				{
					SimPin output = chip.OutputPins[outputIndex];
					primitiveOutputSignals.Add(output, SignalRef.FromSlot(nextScratchSlot++, nodeIndex));
					orderedPrimitiveOutputs.Add(output);
				}
			}

			SignalResolver resolver = new(root, incomingSource, rootInputSlots, primitiveOutputSignals);
			CompiledNode[] nodes = new CompiledNode[primitiveChips.Count];
			List<int>[] outgoing = new List<int>[primitiveChips.Count];
			int[] indegree = new int[primitiveChips.Count];

			for (int nodeIndex = 0; nodeIndex < primitiveChips.Count; nodeIndex++)
			{
				SimChip chip = primitiveChips[nodeIndex];
				SignalRef[] inputs = new SignalRef[chip.InputPins.Length];

				for (int inputIndex = 0; inputIndex < chip.InputPins.Length; inputIndex++)
				{
					SignalRef signal = resolver.Resolve(chip.InputPins[inputIndex]);
					inputs[inputIndex] = signal;

					int producer = signal.ProducerNode;
					if (producer >= 0)
					{
						(outgoing[producer] ??= new List<int>()).Add(nodeIndex);
						indegree[nodeIndex]++;
					}
				}

				SignalRef[] outputs = new SignalRef[chip.OutputPins.Length];
				for (int outputIndex = 0; outputIndex < chip.OutputPins.Length; outputIndex++)
				{
					outputs[outputIndex] = primitiveOutputSignals[chip.OutputPins[outputIndex]];
				}

				nodes[nodeIndex] = new CompiledNode(chip.ChipType, inputs, outputs);
			}

			// This accelerator is intentionally reserved for graphs that really contain a
			// cycle. Acyclic custom chips are handled more efficiently by the existing JIT.
			if (!ContainsCycle(indegree, outgoing)) return null;

			SignalRef[] rootOutputs = new SignalRef[root.OutputPins.Length];
			for (int i = 0; i < root.OutputPins.Length; i++) rootOutputs[i] = resolver.Resolve(root.OutputPins[i]);

			GateProgram.Builder builder = new();
			for (int i = 0; i < nodes.Length; i++) AddNode(builder, nodes[i]);
			GateProgram program = builder.Build();

			int[] outputSlots = new int[rootOutputs.Length];
			uint[] outputConstants = new uint[rootOutputs.Length];
			for (int i = 0; i < rootOutputs.Length; i++)
			{
				outputSlots[i] = rootOutputs[i].IsConstant ? -1 : rootOutputs[i].Slot;
				outputConstants[i] = rootOutputs[i].IsConstant ? rootOutputs[i].Constant : 0;
			}

			timer.Stop();
			return new CompiledFeedbackProgram(
				program,
				nextScratchSlot,
				root.InputPins.Length,
				root.OutputPins.Length,
				primitiveChips.Count,
				outputSlots,
				outputConstants,
				orderedPrimitiveOutputs.Count,
				timer.Elapsed.TotalMilliseconds);
		}

		static void CollectLocalGraph(
			SimChip chip,
			List<SimPin> pins,
			HashSet<SimPin> pinSet,
			List<SimChip> primitiveChips)
		{
			for (int i = 0; i < chip.InputPins.Length; i++)
			{
				pins.Add(chip.InputPins[i]);
				pinSet.Add(chip.InputPins[i]);
			}
			for (int i = 0; i < chip.OutputPins.Length; i++)
			{
				pins.Add(chip.OutputPins[i]);
				pinSet.Add(chip.OutputPins[i]);
			}

			if (chip.ChipType == ChipType.Custom)
			{
				for (int i = 0; i < chip.SubChips.Length; i++)
				{
					CollectLocalGraph(chip.SubChips[i], pins, pinSet, primitiveChips);
				}
			}
			else
			{
				primitiveChips.Add(chip);
			}
		}

		static Dictionary<SimPin, SimPin> BuildIncomingSourceMap(List<SimPin> pins, HashSet<SimPin> localPins)
		{
			Dictionary<SimPin, SimPin> incoming = new();

			for (int i = 0; i < pins.Count; i++)
			{
				SimPin source = pins[i];
				for (int j = 0; j < source.ConnectedTargetPins.Length; j++)
				{
					SimPin target = source.ConnectedTargetPins[j];
					if (!localPins.Contains(target)) continue;

					if (incoming.TryGetValue(target, out SimPin previous))
					{
						if (!ReferenceEquals(previous, source))
						{
							throw new InvalidOperationException("multiple drivers reached feedback JIT compiler");
						}
					}
					else
					{
						incoming.Add(target, source);
					}
				}
			}

			return incoming;
		}

		static bool ContainsCycle(int[] indegreeSource, List<int>[] outgoing)
		{
			int[] indegree = (int[])indegreeSource.Clone();
			Queue<int> ready = new();
			for (int i = 0; i < indegree.Length; i++)
			{
				if (indegree[i] == 0) ready.Enqueue(i);
			}

			int visited = 0;
			while (ready.Count > 0)
			{
				int node = ready.Dequeue();
				visited++;
				List<int> targets = outgoing[node];
				if (targets == null) continue;

				for (int i = 0; i < targets.Count; i++)
				{
					int target = targets[i];
					if (--indegree[target] == 0) ready.Enqueue(target);
				}
			}

			return visited != indegree.Length;
		}

		static bool IsSupportedPrimitive(ChipType type)
		{
			return type is
				ChipType.Nand or
				ChipType.TriStateBuffer or
				ChipType.Split_4To1Bit or
				ChipType.Split_8To1Bit or
				ChipType.Split_8To4Bit or
				ChipType.Merge_1To4Bit or
				ChipType.Merge_1To8Bit or
				ChipType.Merge_4To8Bit or
				ChipType.Bus_1Bit or
				ChipType.Bus_4Bit or
				ChipType.Bus_8Bit or
				ChipType.BusTerminus_1Bit or
				ChipType.BusTerminus_4Bit or
				ChipType.BusTerminus_8Bit;
		}

		// A primitive chip as GateProgram operations.
		static void AddNode(GateProgram.Builder program, CompiledNode node)
		{
			switch (node.Type)
			{
				case ChipType.Nand:
					program.Nand(node.Outputs[0].Slot, Operand(node.Inputs[0]), Operand(node.Inputs[1]));
					break;

				case ChipType.TriStateBuffer:
					program.TriState(node.Outputs[0].Slot, Operand(node.Inputs[0]), Operand(node.Inputs[1]));
					break;

				case ChipType.Split_4To1Bit:
					AddSplitBits(program, node, new[] { 3, 2, 1, 0 });
					break;

				case ChipType.Split_8To1Bit:
					AddSplitBits(program, node, new[] { 7, 6, 5, 4, 3, 2, 1, 0 });
					break;

				case ChipType.Merge_1To4Bit:
					AddMergeSingleBits(program, node, new[] { 3, 2, 1, 0 });
					break;

				case ChipType.Merge_1To8Bit:
					AddMergeSingleBits(program, node, new[] { 7, 6, 5, 4, 3, 2, 1, 0 });
					break;

				case ChipType.Merge_4To8Bit:
					program.Merge4To8(node.Outputs[0].Slot, Operand(node.Inputs[0]), Operand(node.Inputs[1]));
					break;

				case ChipType.Split_8To4Bit:
					program.Nibble(node.Outputs[0].Slot, Operand(node.Inputs[0]), 4);
					program.Nibble(node.Outputs[1].Slot, Operand(node.Inputs[0]), 0);
					break;

				case ChipType.Bus_1Bit:
				case ChipType.Bus_4Bit:
				case ChipType.Bus_8Bit:
					program.Copy(node.Outputs[0].Slot, Operand(node.Inputs[0]));
					break;

				case ChipType.BusTerminus_1Bit:
				case ChipType.BusTerminus_4Bit:
				case ChipType.BusTerminus_8Bit:
					break;

				default:
					throw new NotSupportedException("Unsupported primitive: " + node.Type);
			}
		}

		static void AddSplitBits(GateProgram.Builder program, CompiledNode node, int[] shifts)
		{
			for (int i = 0; i < shifts.Length; i++) program.ShiftMask(node.Outputs[i].Slot, Operand(node.Inputs[0]), shifts[i]);
		}

		static void AddMergeSingleBits(GateProgram.Builder program, CompiledNode node, int[] inputIndices)
		{
			GateProgram.Operand[] inputs = new GateProgram.Operand[inputIndices.Length];
			for (int bit = 0; bit < inputIndices.Length; bit++) inputs[bit] = Operand(node.Inputs[inputIndices[bit]]);
			program.MergeBits(node.Outputs[0].Slot, inputs);
		}

		static GateProgram.Operand Operand(SignalRef signal) =>
			signal.IsConstant ? GateProgram.Operand.FromConstant(signal.Constant) : GateProgram.Operand.FromSlot(signal.Slot);

		readonly struct SignalRef
		{
			public readonly bool IsConstant;
			public readonly uint Constant;
			public readonly int Slot;
			public readonly int ProducerNode;

			SignalRef(bool isConstant, uint constant, int slot, int producerNode)
			{
				IsConstant = isConstant;
				Constant = constant;
				Slot = slot;
				ProducerNode = producerNode;
			}

			public static SignalRef ConstantValue(uint value) => new(true, value, -1, -1);
			public static SignalRef FromSlot(int slot, int producerNode = -1) => new(false, 0, slot, producerNode);
		}

		readonly struct CompiledNode
		{
			public readonly ChipType Type;
			public readonly SignalRef[] Inputs;
			public readonly SignalRef[] Outputs;

			public CompiledNode(ChipType type, SignalRef[] inputs, SignalRef[] outputs)
			{
				Type = type;
				Inputs = inputs;
				Outputs = outputs;
			}
		}

		sealed class SignalResolver
		{
			readonly SimChip root;
			readonly Dictionary<SimPin, SimPin> incomingSource;
			readonly Dictionary<SimPin, int> rootInputSlots;
			readonly Dictionary<SimPin, SignalRef> primitiveOutputs;
			readonly Dictionary<SimPin, SignalRef> memo = new();
			readonly HashSet<SimPin> active = new();

			public SignalResolver(
				SimChip root,
				Dictionary<SimPin, SimPin> incomingSource,
				Dictionary<SimPin, int> rootInputSlots,
				Dictionary<SimPin, SignalRef> primitiveOutputs)
			{
				this.root = root;
				this.incomingSource = incomingSource;
				this.rootInputSlots = rootInputSlots;
				this.primitiveOutputs = primitiveOutputs;
			}

			public SignalRef Resolve(SimPin pin)
			{
				if (pin == null) return SignalRef.ConstantValue(DisconnectedState);
				if (memo.TryGetValue(pin, out SignalRef existing)) return existing;
				if (!active.Add(pin)) throw new InvalidOperationException("boundary signal cycle reached feedback JIT compiler");

				try
				{
					SignalRef result;
					if (ReferenceEquals(pin.parentChip, root) && pin.isInput && rootInputSlots.TryGetValue(pin, out int inputSlot))
					{
						result = SignalRef.FromSlot(inputSlot);
					}
					else if (incomingSource.TryGetValue(pin, out SimPin source))
					{
						result = Resolve(source);
					}
					else if (primitiveOutputs.TryGetValue(pin, out SignalRef primitive))
					{
						result = primitive;
					}
					else
					{
						result = SignalRef.ConstantValue(DisconnectedState);
					}

					memo[pin] = result;
					return result;
				}
				finally
				{
					active.Remove(pin);
				}
			}
		}
	}

	internal sealed class CompiledFeedbackProgram
	{
		readonly int[] outputSlots;
		readonly uint[] outputConstants;

		public readonly int ScratchCount;
		public readonly int InputCount;
		public readonly int OutputCount;
		public readonly int PrimitiveNodeCount;
		public readonly int PrimitiveOutputCount;
		public readonly double CompileMilliseconds;

		public readonly GateProgram Program;

		public CompiledFeedbackProgram(
			GateProgram program,
			int scratchCount,
			int inputCount,
			int outputCount,
			int primitiveNodeCount,
			int[] outputSlots,
			uint[] outputConstants,
			int primitiveOutputCount,
			double compileMilliseconds)
		{
			Program = program;
			this.outputSlots = outputSlots;
			this.outputConstants = outputConstants;
			ScratchCount = scratchCount;
			InputCount = inputCount;
			OutputCount = outputCount;
			PrimitiveNodeCount = primitiveNodeCount;
			PrimitiveOutputCount = primitiveOutputCount;
			CompileMilliseconds = compileMilliseconds;
		}

		public void RunSweep(uint[] current, uint[] next)
		{
			Program.Run(current, next);
		}

		public void WriteOutputs(uint[] current, uint[] outputs)
		{
			for (int i = 0; i < outputSlots.Length; i++)
			{
				int slot = outputSlots[i];
				outputs[i] = slot < 0 ? outputConstants[i] : current[slot];
			}
		}

		// fullSweeps: evaluate every operation in every sweep instead of settling only what
		// changes. Same results, slower; the tests compare Settle against it.
		public CompiledFeedbackExecutor CreateExecutor(SimChip root, bool fullSweeps = false)
		{
			List<SimPin> primitiveOutputs = new(PrimitiveOutputCount);
			CollectPrimitiveOutputs(root, primitiveOutputs);
			if (primitiveOutputs.Count != PrimitiveOutputCount) return null;
			return new CompiledFeedbackExecutor(this, primitiveOutputs.ToArray(), fullSweeps);
		}

		static void CollectPrimitiveOutputs(SimChip chip, List<SimPin> outputs)
		{
			if (chip.ChipType == ChipType.Custom)
			{
				for (int i = 0; i < chip.SubChips.Length; i++) CollectPrimitiveOutputs(chip.SubChips[i], outputs);
				return;
			}

			if (!FeedbackJitCompilerIsSupported(chip.ChipType) || chip.OutputPins.Length == 0) return;
			for (int i = 0; i < chip.OutputPins.Length; i++) outputs.Add(chip.OutputPins[i]);
		}

		// Keep the executor construction independent from compiler-private helpers.
		static bool FeedbackJitCompilerIsSupported(ChipType type)
		{
			return type is
				ChipType.Nand or
				ChipType.TriStateBuffer or
				ChipType.Split_4To1Bit or
				ChipType.Split_8To1Bit or
				ChipType.Split_8To4Bit or
				ChipType.Merge_1To4Bit or
				ChipType.Merge_1To8Bit or
				ChipType.Merge_4To8Bit or
				ChipType.Bus_1Bit or
				ChipType.Bus_4Bit or
				ChipType.Bus_8Bit or
				ChipType.BusTerminus_1Bit or
				ChipType.BusTerminus_4Bit or
				ChipType.BusTerminus_8Bit;
		}
	}

	internal sealed class CompiledFeedbackExecutor
	{
		readonly CompiledFeedbackProgram program;
		readonly SimPin[] primitiveOutputPins;
		uint[] current;
		uint[] next;
		readonly uint[] outputs;
		readonly uint[] lastStableInputs;
		bool hasStableInputSnapshot;
		bool disabled;
		// The bookkeeping of GateProgram.Settle (null for full sweeps), and whether current is a
		// settled state that Settle may start from (else every operation is evaluated once).
		readonly GateProgram.SettleState settleState;
		bool settled;

		public bool Ready { get; private set; }
		public bool Disabled => disabled;
		public bool RuntimeActive { get; private set; }
		public int PrimitiveNodeCount => program.PrimitiveNodeCount;
		public double CompileMilliseconds => program.CompileMilliseconds;
		public int LastSweepCount { get; private set; }

		public CompiledFeedbackExecutor(CompiledFeedbackProgram program, SimPin[] primitiveOutputPins, bool fullSweeps = false)
		{
			this.program = program;
			this.primitiveOutputPins = primitiveOutputPins;
			current = new uint[program.ScratchCount];
			next = new uint[program.ScratchCount];
			outputs = new uint[program.OutputCount];
			lastStableInputs = new uint[program.InputCount];
			if (!fullSweeps) settleState = new GateProgram.SettleState(program.Program);
		}

		public void SynchronizeFromChipTree()
		{
			if (disabled) return;

			int slot = program.InputCount;
			for (int i = 0; i < primitiveOutputPins.Length && slot < current.Length; i++, slot++)
			{
				uint state = primitiveOutputPins[i].State;
				current[slot] = state;
				next[slot] = state;
			}

			hasStableInputSnapshot = false;
			settled = false;
			Ready = true;
		}

		public void SetRuntimeActive(bool active)
		{
			RuntimeActive = active && Ready && !disabled;
		}

		public void MaterializeState()
		{
			if (!Ready) return;

			int slot = program.InputCount;
			for (int i = 0; i < primitiveOutputPins.Length && slot < current.Length; i++, slot++)
			{
				primitiveOutputPins[i].State = current[slot];
			}
		}

		public void Disable()
		{
			MaterializeState();
			disabled = true;
			hasStableInputSnapshot = false;
			RuntimeActive = false;
			Ready = false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Evaluate(SimChip chip, out uint[] result, out int sweepCount)
		{
			result = outputs;
			sweepCount = 0;
			LastSweepCount = 0;
			if (!Ready || disabled) return false;

			int inputCount = Math.Min(program.InputCount, chip.InputPins.Length);

			if (hasStableInputSnapshot && inputCount == lastStableInputs.Length)
			{
				bool unchanged = true;
				for (int i = 0; i < inputCount; i++)
				{
					if (chip.InputPins[i].State == lastStableInputs[i]) continue;
					unchanged = false;
					break;
				}

				// Feedback-JIT programs reject clocks, keys, pulse generators, RAM,
				// displays and every other spontaneous/stateful primitive. Therefore a
				// previously converged region cannot change while all boundary inputs
				// remain identical.
				if (unchanged) return true;
			}

			int maxSweeps = Math.Max(256, program.PrimitiveNodeCount + 64);
			bool converged = false;

			if (settleState != null)
			{
				// Same result as the full sweeps below; see GateProgram.Settle.
				GateProgram gates = program.Program;
				if (!settled) gates.MarkAll(settleState);
				for (int i = 0; i < inputCount; i++)
				{
					uint state = chip.InputPins[i].State;
					if (current[i] != state) gates.MarkReaders(settleState, i);
					current[i] = state;
				}

				// Settle works in current alone; next is only used by the full-sweep loop.
				sweepCount = gates.Settle(current, settleState, maxSweeps, out converged);
				settled = converged;
			}
			else
			{
				for (int i = 0; i < inputCount; i++)
				{
					uint state = chip.InputPins[i].State;
					current[i] = state;
					next[i] = state;
				}
			}

			for (int sweep = 0; settleState == null && sweep < maxSweeps; sweep++)
			{
				for (int i = 0; i < inputCount; i++) next[i] = current[i];
				program.RunSweep(current, next);

				bool changed = false;
				for (int slot = program.InputCount; slot < program.ScratchCount; slot++)
				{
					if (current[slot] != next[slot])
					{
						changed = true;
						break;
					}
				}

				sweepCount = sweep + 1;
				if (!changed)
				{
					converged = true;
					break;
				}

				uint[] swap = current;
				current = next;
				next = swap;
			}

			LastSweepCount = sweepCount;
			program.WriteOutputs(current, outputs);

			if (converged && inputCount == lastStableInputs.Length)
			{
				for (int i = 0; i < inputCount; i++) lastStableInputs[i] = chip.InputPins[i].State;
				hasStableInputSnapshot = true;
			}
			else
			{
				hasStableInputSnapshot = false;
			}

			return converged;
		}
	}
}
