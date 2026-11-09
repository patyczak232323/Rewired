using System;
using System.Collections.Generic;
using Unity.IL2CPP.CompilerServices;

namespace DLS.Simulation
{
	// The flattened gate programs of CombinationalJitCompiler and FeedbackJitCompiler, run by a
	// small interpreter instead of as generated machine code.
	//
	// Both compilers turn a custom chip into a list of primitive operations over slots of a
	// uint array. The combinational compiler emits that list as IL with DynamicMethod where it
	// can; IL2CPP (Android, WebGL) and other ahead-of-time platforms cannot create code at run
	// time, so there it builds a GateProgram from the same list instead. Every operation below
	// is a line-by-line copy of what the emitted IL computes, so both forms give bit-identical
	// results; the engine tests run every combinational program through both and compare them.
	// Feedback programs always run here, settled by Settle, which evaluates only what changes.
	//
	// A program reads its inputs from one array and writes its results to another. The
	// combinational compiler passes the same array twice and orders the operations
	// topologically; the feedback compiler reads the previous sweep and writes the next one.
	// Slots and code offsets come from the builder, which sized the arrays for them.
	[Il2CppSetOption(Option.NullChecks, false), Il2CppSetOption(Option.ArrayBoundsChecks, false)]
	internal sealed class GateProgram
	{
		const uint DisconnectedState = 0xFFFF0000u;
		const uint SingleBitMask = PinState.SingleBitMask;

		internal enum Op : byte
		{
			// write = 1 & ~(a & b)
			Nand,
			// write = (b & 1) != 0 ? a : disconnected
			TriState,
			// write = (a >> Arg) & SingleBitMask (one bit of a bus, with its tri-state flag)
			ShiftMask,
			// write = nibble of a starting at bit Arg, with its four tri-state flags
			Nibble,
			// write = two 4-bit buses (a = high, b = low) as one 8-bit bus
			Merge4To8,
			// write = OR of (input k & SingleBitMask) << k over Count inputs from Start
			MergeBits,
			// write = a
			Copy
		}

		// The operations as one stream of ints, in program order. Most operations are NANDs of
		// two slots, so consecutive ones are grouped into a run that a tight loop executes:
		//   NandRun:  [NandRun, n, a0, b0, d0, a1, b1, d1, ...]
		//   others:   [op, destination, operandA, operandB, argument, count]
		// An operand >= 0 is a slot of the read array; ~operand indexes the constants.
		const int NandRun = 255;
		readonly int[] code;
		readonly uint[] constants;
		readonly int operationCount;

		// For Settle: where each operation's fields start in code, whether it belongs to a NAND
		// run (fields a, b, destination) or not (the full header), and, per slot, the
		// operations that read it (compressed rows: readers[readerStart[s]..readerStart[s+1])).
		readonly int[] operationStart;
		readonly bool[] operationInRun;
		readonly int[] readerStart;
		readonly int[] readers;

		GateProgram(Builder builder)
		{
			List<int> stream = new();
			List<uint> constantTable = new();

			int Encode(int slot, uint constant)
			{
				if (slot >= 0) return slot;
				constantTable.Add(constant);
				return ~(constantTable.Count - 1);
			}

			int count = builder.Ops.Count;
			for (int i = 0; i < count;)
			{
				if (IsSlotNand(builder, i))
				{
					int header = stream.Count;
					stream.Add(NandRun);
					stream.Add(0);
					int run = 0;
					for (; i < count && IsSlotNand(builder, i); i++, run++)
					{
						stream.Add(builder.SlotA[i]);
						stream.Add(builder.SlotB[i]);
						stream.Add(builder.Destination[i]);
					}

					stream[header + 1] = run;
					continue;
				}

				stream.Add((int)builder.Ops[i]);
				stream.Add(builder.Destination[i]);
				stream.Add(Encode(builder.SlotA[i], builder.ConstantA[i]));
				stream.Add(Encode(builder.SlotB[i], builder.ConstantB[i]));
				if (builder.Ops[i] == Op.MergeBits)
				{
					// The merge inputs follow as operands of their own.
					int start = builder.Argument[i];
					stream.Add(stream.Count + 2);
					stream.Add(builder.Count[i]);
					for (int k = 0; k < builder.Count[i]; k++)
					{
						stream.Add(Encode(builder.MergeSlots[start + k], builder.MergeConstants[start + k]));
					}
				}
				else
				{
					stream.Add(builder.Argument[i]);
					stream.Add(builder.Count[i]);
				}

				i++;
			}

			code = stream.ToArray();
			constants = constantTable.ToArray();
			operationCount = count;

			// Index the stream by operation and list the readers of every slot.
			operationStart = new int[count];
			operationInRun = new bool[count];
			List<(int slot, int operation)> reads = new();
			int highestSlot = -1;
			for (int pc = 0, operation = 0; pc < code.Length;)
			{
				if (code[pc] == NandRun)
				{
					int end = pc + 2 + code[pc + 1] * 3;
					for (int k = pc + 2; k < end; k += 3, operation++)
					{
						operationStart[operation] = k;
						operationInRun[operation] = true;
						reads.Add((code[k], operation));
						reads.Add((code[k + 1], operation));
						highestSlot = Math.Max(highestSlot, Math.Max(code[k + 2], Math.Max(code[k], code[k + 1])));
					}

					pc = end;
					continue;
				}

				operationStart[operation] = pc;
				highestSlot = Math.Max(highestSlot, code[pc + 1]);
				foreach (int operand in OperandsOf(pc))
				{
					if (operand < 0) continue;
					reads.Add((operand, operation));
					highestSlot = Math.Max(highestSlot, operand);
				}

				pc = (Op)code[pc] == Op.MergeBits ? code[pc + 4] + code[pc + 5] : pc + 6;
				operation++;
			}

			readerStart = new int[highestSlot + 2];
			foreach ((int slot, _) in reads) readerStart[slot + 1]++;
			for (int i = 1; i < readerStart.Length; i++) readerStart[i] += readerStart[i - 1];
			readers = new int[reads.Count];
			int[] fill = (int[])readerStart.Clone();
			foreach ((int slot, int operation) in reads) readers[fill[slot]++] = operation;
		}

		// The operands a general (not run) operation at pc reads; constants are negative.
		IEnumerable<int> OperandsOf(int pc)
		{
			Op op = (Op)code[pc];
			yield return code[pc + 2];
			if (op is Op.Nand or Op.TriState or Op.Merge4To8) yield return code[pc + 3];
			if (op == Op.MergeBits)
			{
				for (int k = 0; k < code[pc + 5]; k++) yield return code[code[pc + 4] + k];
			}
		}

		static bool IsSlotNand(Builder builder, int i) =>
			builder.Ops[i] == Op.Nand && builder.SlotA[i] >= 0 && builder.SlotB[i] >= 0;

		public int OperationCount => operationCount;

		public void Run(uint[] read, uint[] write)
		{
			int[] code = this.code;
			uint[] constants = this.constants;
			int pc = 0;
			while (pc < code.Length)
			{
				int op = code[pc];
				if (op == NandRun)
				{
					int end = pc + 2 + code[pc + 1] * 3;
					for (int k = pc + 2; k < end; k += 3)
					{
						write[code[k + 2]] = ((read[code[k]] & read[code[k + 1]]) ^ 1u) & 1u;
					}

					pc = end;
					continue;
				}

				int destination = code[pc + 1];
				int a = code[pc + 2];
				uint valueA = a >= 0 ? read[a] : constants[~a];

				switch ((Op)op)
				{
					case Op.Nand:
					{
						int b = code[pc + 3];
						uint valueB = b >= 0 ? read[b] : constants[~b];
						write[destination] = ((valueA & valueB) ^ 1u) & 1u;
						break;
					}

					case Op.TriState:
					{
						int b = code[pc + 3];
						uint valueB = b >= 0 ? read[b] : constants[~b];
						write[destination] = (valueB & 1u) != 0 ? valueA : DisconnectedState;
						break;
					}

					case Op.ShiftMask:
						write[destination] = (valueA >> code[pc + 4]) & SingleBitMask;
						break;

					case Op.Nibble:
					{
						int shift = code[pc + 4];
						write[destination] = ((valueA >> shift) & 0xFu) | (((valueA >> (16 + shift)) & 0xFu) << 16);
						break;
					}

					case Op.Merge4To8:
					{
						// valueA is the high nibble source (input 0), valueB the low one (input 1).
						int b = code[pc + 3];
						uint valueB = b >= 0 ? read[b] : constants[~b];
						uint bits = ((valueB & 0xFFFFu) | ((valueA & 0xFFFFu) << 4)) & 0xFFFFu;
						uint flags = ((valueB >> 16) & 0xFu) | (((valueA >> 16) & 0xFu) << 4);
						write[destination] = bits | (flags << 16);
						break;
					}

					case Op.MergeBits:
					{
						int first = code[pc + 4];
						int inputs = code[pc + 5];
						uint result = 0;
						for (int bit = 0; bit < inputs; bit++)
						{
							int operand = code[first + bit];
							uint value = operand >= 0 ? read[operand] : constants[~operand];
							result |= (value & SingleBitMask) << bit;
						}

						write[destination] = result;
						pc = first + inputs;
						continue;
					}

					case Op.Copy:
						write[destination] = valueA;
						break;
				}

				pc += 6;
			}
		}

		// What one operation writes, computed from state, exactly as Run computes it.
		uint Evaluate(int operation, uint[] state, out int destination)
		{
			int pc = operationStart[operation];
			if (operationInRun[operation])
			{
				destination = code[pc + 2];
				return ((state[code[pc]] & state[code[pc + 1]]) ^ 1u) & 1u;
			}

			destination = code[pc + 1];
			int a = code[pc + 2];
			uint valueA = a >= 0 ? state[a] : constants[~a];

			switch ((Op)code[pc])
			{
				case Op.Nand:
				{
					int b = code[pc + 3];
					uint valueB = b >= 0 ? state[b] : constants[~b];
					return ((valueA & valueB) ^ 1u) & 1u;
				}

				case Op.TriState:
				{
					int b = code[pc + 3];
					uint valueB = b >= 0 ? state[b] : constants[~b];
					return (valueB & 1u) != 0 ? valueA : DisconnectedState;
				}

				case Op.ShiftMask:
					return (valueA >> code[pc + 4]) & SingleBitMask;

				case Op.Nibble:
				{
					int shift = code[pc + 4];
					return ((valueA >> shift) & 0xFu) | (((valueA >> (16 + shift)) & 0xFu) << 16);
				}

				case Op.Merge4To8:
				{
					int b = code[pc + 3];
					uint valueB = b >= 0 ? state[b] : constants[~b];
					uint bits = ((valueB & 0xFFFFu) | ((valueA & 0xFFFFu) << 4)) & 0xFFFFu;
					uint flags = ((valueB >> 16) & 0xFu) | (((valueA >> 16) & 0xFu) << 4);
					return bits | (flags << 16);
				}

				case Op.MergeBits:
				{
					int first = code[pc + 4];
					int inputs = code[pc + 5];
					uint result = 0;
					for (int bit = 0; bit < inputs; bit++)
					{
						int operand = code[first + bit];
						uint value = operand >= 0 ? state[operand] : constants[~operand];
						result |= (value & SingleBitMask) << bit;
					}

					return result;
				}

				default:
					return valueA;
			}
		}

		// Per-executor bookkeeping for Settle (a program is shared by every chip that uses it).
		internal sealed class SettleState
		{
			internal readonly bool[] Dirty;
			internal int[] DirtyList;
			internal int DirtyCount;
			internal int[] Batch;
			internal readonly int[] StagedSlot;
			internal readonly uint[] StagedValue;

			public SettleState(GateProgram program)
			{
				Dirty = new bool[program.operationCount];
				DirtyList = new int[program.operationCount];
				Batch = new int[program.operationCount];
				StagedSlot = new int[program.operationCount];
				StagedValue = new uint[program.operationCount];
			}
		}

		// Marks every operation that reads slot for the next Settle.
		public void MarkReaders(SettleState work, int slot)
		{
			if (slot + 1 >= readerStart.Length) return;
			for (int i = readerStart[slot]; i < readerStart[slot + 1]; i++) MarkDirty(work, readers[i]);
		}

		public void MarkAll(SettleState work)
		{
			for (int i = 0; i < operationCount; i++) MarkDirty(work, i);
		}

		static void MarkDirty(SettleState work, int operation)
		{
			if (work.Dirty[operation]) return;
			work.Dirty[operation] = true;
			work.DirtyList[work.DirtyCount++] = operation;
		}

		// The same fixed-point iteration as running full sweeps (each reads the state of the
		// previous sweep and all results are applied together), but each sweep evaluates only
		// the operations whose inputs changed in the sweep before. An operation whose inputs
		// did not change would compute the value it already holds, so after every sweep the
		// state is exactly what full sweeps give, provided the operations not marked already
		// hold their values: true after a full sweep (MarkAll) and after a converged Settle.
		// Returns the number of sweeps a full-sweep loop would report (the last one finds no
		// change), or maxSweeps without convergence, leaving the state of that last sweep.
		public int Settle(uint[] state, SettleState work, int maxSweeps, out bool converged)
		{
			for (int sweep = 0; sweep < maxSweeps; sweep++)
			{
				// This sweep's operations; the list collects those of the next one.
				int batchCount = work.DirtyCount;
				(work.Batch, work.DirtyList) = (work.DirtyList, work.Batch);
				work.DirtyCount = 0;

				int staged = 0;
				for (int i = 0; i < batchCount; i++)
				{
					int operation = work.Batch[i];
					work.Dirty[operation] = false;
					uint value = Evaluate(operation, state, out int destination);
					if (value == state[destination]) continue;
					work.StagedSlot[staged] = destination;
					work.StagedValue[staged] = value;
					staged++;
				}

				if (staged == 0)
				{
					converged = true;
					return sweep + 1;
				}

				for (int i = 0; i < staged; i++)
				{
					int slot = work.StagedSlot[i];
					state[slot] = work.StagedValue[i];
					MarkReaders(work, slot);
				}
			}

			converged = false;
			return maxSweeps;
		}

		// A signal read by an operation: a slot of the read array, or a constant.
		internal readonly struct Operand
		{
			public readonly int Slot;
			public readonly uint Constant;

			Operand(int slot, uint constant)
			{
				Slot = slot;
				Constant = constant;
			}

			public static Operand FromSlot(int slot) => new(slot, 0);
			public static Operand FromConstant(uint value) => new(-1, value);
		}

		internal sealed class Builder
		{
			internal readonly List<Op> Ops = new();
			internal readonly List<int> Destination = new();
			internal readonly List<int> SlotA = new();
			internal readonly List<uint> ConstantA = new();
			internal readonly List<int> SlotB = new();
			internal readonly List<uint> ConstantB = new();
			internal readonly List<int> Argument = new();
			internal readonly List<int> Count = new();
			internal readonly List<int> MergeSlots = new();
			internal readonly List<uint> MergeConstants = new();

			void Add(Op op, int destination, Operand a, Operand b, int argument = 0, int count = 0)
			{
				Ops.Add(op);
				Destination.Add(destination);
				SlotA.Add(a.Slot);
				ConstantA.Add(a.Constant);
				SlotB.Add(b.Slot);
				ConstantB.Add(b.Constant);
				Argument.Add(argument);
				Count.Add(count);
			}

			static readonly Operand None = Operand.FromConstant(0);

			public void Nand(int destination, Operand a, Operand b) => Add(Op.Nand, destination, a, b);
			public void TriState(int destination, Operand data, Operand enable) => Add(Op.TriState, destination, data, enable);
			public void ShiftMask(int destination, Operand a, int shift) => Add(Op.ShiftMask, destination, a, None, shift);
			public void Nibble(int destination, Operand a, int shift) => Add(Op.Nibble, destination, a, None, shift);
			public void Merge4To8(int destination, Operand high, Operand low) => Add(Op.Merge4To8, destination, high, low);
			public void Copy(int destination, Operand a) => Add(Op.Copy, destination, a, None);

			// inputs[k] goes to bit k of the result.
			public void MergeBits(int destination, Operand[] inputs)
			{
				int start = MergeSlots.Count;
				foreach (Operand input in inputs)
				{
					MergeSlots.Add(input.Slot);
					MergeConstants.Add(input.Constant);
				}

				Add(Op.MergeBits, destination, None, None, start, inputs.Length);
			}

			public GateProgram Build() => new(this);
		}
	}
}
