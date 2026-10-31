using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DLS.RHDL
{
	// Rewrites a source in the RHDL 1.0 syntax ('circuit Name(A, B) -> Y:') in the RHDL 2.0
	// syntax, keeping its comments. The result builds the same chip.
	public static class RhdlUpgrade
	{
		const string Indent = "    ";
		const int LineLimit = 96;

		public static bool IsLegacySource(string source) => RhdlParser.IsLegacySource(source);

		public static bool TryUpgrade(string source, out string upgraded, out string error)
		{
			upgraded = null;
			error = null;
			ModuleSyntax module;
			try
			{
				module = RhdlLegacyParser.Parse(source);
			}
			catch (RhdlSyntaxException e)
			{
				error = $"Line {e.Line}: {e.Message}";
				return false;
			}

			upgraded = new Writer(source).Write(module);
			return true;
		}

		sealed class Writer
		{
			readonly StringBuilder output = new();
			readonly string[] lines;
			// Comment text per source line, and whether the line holds nothing but the comment.
			readonly Dictionary<int, (string text, bool alone)> comments = new();
			readonly HashSet<int> blankLines = new();
			readonly HashSet<string> renamed = new(StringComparer.Ordinal);
			int nextSourceLine = 1;
			bool lastWasBlank = true;

			public Writer(string source)
			{
				lines = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i];
					int hash = FindComment(line);
					if (hash >= 0)
					{
						comments[i + 1] = (line.Substring(hash).TrimEnd(), line.Substring(0, hash).Trim().Length == 0);
					}
					else if (line.Trim().Length == 0)
					{
						blankLines.Add(i + 1);
					}
				}
			}

			static int FindComment(string line)
			{
				char quote = '\0';
				for (int i = 0; i < line.Length; i++)
				{
					char c = line[i];
					if (quote != '\0')
					{
						if (c == quote) quote = '\0';
						continue;
					}

					if (c == '"' || c == '\'') quote = c;
					else if (c == '#') return i;
				}

				return -1;
			}

			public string Write(ModuleSyntax module)
			{
				foreach (AssignStatement constant in module.Constants.Where(c => module.Circuits.Count == 0 || c.Line < module.Circuits[0].Line))
				{
					WriteConstant(constant, string.Empty);
				}

				foreach (CircuitSyntax circuit in module.Circuits)
				{
					foreach (AssignStatement constant in module.Constants.Where(c => c.Line > circuit.Line && module.Circuits.All(o => o == circuit || o.Line < circuit.Line || o.Line > c.Line)))
					{
						WriteConstant(constant, string.Empty);
					}

					WriteCircuit(circuit);
				}

				FlushComments(int.MaxValue, string.Empty);
				string text = output.ToString().TrimEnd('\n', ' ');
				return text + "\n";
			}

			void WriteConstant(AssignStatement constant, string indent)
			{
				FlushComments(constant.Line, indent);
				Line(indent + $"const {Name(((NameExpr)constant.Targets[0]).Name)} = {Expression(constant.Value, 0)}", constant.Line);
			}

			void WriteCircuit(CircuitSyntax circuit)
			{
				FlushComments(circuit.Line, string.Empty);
				Line($"chip {circuit.Name}", circuit.Line);
				if (!string.IsNullOrWhiteSpace(circuit.Doc))
				{
					foreach (string docLine in circuit.Doc.Split('\n')) Line(Indent + ("# " + docLine.Trim()).TrimEnd(), 0);
				}

				// Comments inside the header (between the pins) go above the pins.
				int bodyLine = circuit.Body.Count > 0 ? circuit.Body[0].Line : int.MaxValue;
				int headerEnd = Math.Max(circuit.Line, circuit.Inputs.Concat(circuit.Outputs).Select(p => p.Line).DefaultIfEmpty(circuit.Line).Max());
				FlushComments(headerEnd + 1, Indent);
				WritePins("in", circuit.Inputs);
				WritePins("out", circuit.Outputs);
				nextSourceLine = Math.Max(nextSourceLine, headerEnd + 1);
				Blank();
				if (bodyLine != int.MaxValue) SkipBlankLinesBefore(bodyLine);

				WriteBody(circuit.Body, Indent);
			}

			void WritePins(string keyword, List<PortSyntax> pins)
			{
				if (pins.Count == 0) return;
				StringBuilder line = new(Indent + keyword + " ");
				bool first = true;
				foreach (PortSyntax pin in pins)
				{
					string text = Name(pin.Name) + (pin.Width > 1 ? $": {pin.Width}" : string.Empty);
					if (!first && line.Length + text.Length + 2 > LineLimit)
					{
						Line(line.ToString(), 0);
						line.Clear().Append(Indent + keyword + " ");
						first = true;
					}

					if (!first) line.Append(", ");
					line.Append(text);
					first = false;
				}

				Line(line.ToString(), 0);
			}

			void WriteBody(List<Statement> body, string indent)
			{
				foreach (Statement statement in body)
				{
					FlushComments(statement.Line, indent);
					switch (statement)
					{
						case ForStatement loop:
							WriteFor(loop, indent);
							break;
						case DeclareStatement declare:
							Line(indent + $"wire {Name(declare.Name)}: {declare.Width}", declare.Line);
							break;
						case AssignStatement assign:
							WriteAssign(assign, indent);
							break;
						case ExpressionStatement expression:
							Line(indent + Expression(expression.Value, 0), expression.Line);
							break;
					}
				}
			}

			void WriteFor(ForStatement loop, string indent)
			{
				long step = 1;
				bool stepKnown = loop.Step == null || TryNumber(loop.Step, out step);
				if (TryNumber(loop.Start, out long start) && TryNumber(loop.Stop, out long stop) && stepKnown && step != 0 &&
				    (step > 0 ? start >= stop : start <= stop))
				{
					// An empty range: nothing is built.
					Line(indent + "# (a loop that repeated zero times was removed)", loop.Line);
					return;
				}

				string first = Expression(loop.Start, 0);
				string last = Offset(loop.Stop, step < 0 ? 1 : -1);
				string stepText = loop.Step == null ? string.Empty
					: stepKnown ? (Math.Abs(step) == 1 ? string.Empty : $" step {Math.Abs(step)}")
					: $" step {Expression(loop.Step, 0)}";
				Line(indent + $"for {Name(loop.Variable)} in {first}..{last}{stepText}", loop.Line);
				WriteBody(loop.Body, indent + Indent);
				Line(indent + "end", 0);
			}

			void WriteAssign(AssignStatement assign, string indent)
			{
				string targets = string.Join(", ", assign.Targets.Select(t => Expression(t, 0)));
				Expr value = assign.Value;
				int width = assign.AnnotatedWidth;

				// 'x = u4(y)' gives x four bits: 'wire x: 4 = y' says the same.
				if (width == 0 && assign.Targets.Count == 1 && assign.Targets[0] is NameExpr && IsCast(value, out int castWidth, out Expr inner))
				{
					width = castWidth;
					value = inner;
				}

				string prefix = width > 0 ? $"wire {targets}: {width} = " : $"{targets} = ";
				Line(indent + prefix + Value(value, indent + Indent, (indent + prefix).Length), assign.Line);
			}

			// The value of an assignment. Long choices and calls are spread over several lines.
			string Value(Expr value, string continuation, int used)
			{
				string flat = Expression(value, 0);
				if (used + flat.Length <= LineLimit) return flat;

				if (value is ConditionalExpr conditional)
				{
					StringBuilder text = new();
					Expr current = conditional;
					bool first = true;
					while (current is ConditionalExpr choice)
					{
						if (!first) text.Append('\n').Append(continuation).Append("else ");
						text.Append($"if {Expression(choice.Condition, 1)} then {Expression(choice.WhenTrue, 0, nested: true)}");
						current = choice.WhenFalse;
						first = false;
					}

					text.Append('\n').Append(continuation).Append("else ").Append(Expression(current, 0));
					return text.ToString();
				}

				if (value is CallExpr call)
				{
					StringBuilder text = new(Expression(call.Function, 8) + "(\n");
					StringBuilder line = new(continuation);
					foreach (string argument in call.Arguments.Select(Argument))
					{
						if (line.Length > continuation.Length && line.Length + argument.Length + 2 > LineLimit)
						{
							text.Append(line.ToString().TrimEnd()).Append('\n');
							line.Clear().Append(continuation);
						}

						line.Append(argument).Append(", ");
					}

					text.Append(line.ToString().TrimEnd()).Append('\n');
					text.Append(continuation.Substring(Math.Min(Indent.Length, continuation.Length))).Append(')');
					return text.ToString();
				}

				return flat;
			}

			// ---- Expressions ----

			// Precedence, loosest first: 0 if-then-else, 1 or, 2 xor, 3 and, 4 comparisons,
			// 5 shifts, 6 + -, 7 not and unary -, 8 calls, bits and names.
			string Expression(Expr expr, int needed, bool nested = false)
			{
				(string text, int precedence) = Print(expr, nested);
				return precedence < needed || (nested && expr is ConditionalExpr) ? $"({text})" : text;
			}

			(string, int) Print(Expr expr, bool nested)
			{
				switch (expr)
				{
					case NumberExpr number:
						return (number.Raw is "True" ? "true" : number.Raw is "False" ? "false" : number.Raw, 8);

					case NameExpr name:
						return (Name(name.Name), 8);

					case AttributeExpr attribute:
						return ($"{Expression(attribute.Target, 8)}.{attribute.Name}", 8);

					case IndexExpr index:
						return ($"{Expression(index.Target, 8)}[{Expression(index.Index, 0)}]", 8);

					case SliceExpr slice:
					{
						string low = slice.Low == null ? "0" : Expression(slice.Low, 0);
						string high = slice.High == null ? string.Empty : Offset(slice.High, -1);
						return ($"{Expression(slice.Target, 8)}[{low}..{high}]", 8);
					}

					case CallExpr call:
					{
						string function = call.Function is NameExpr { Name: "cat" } ? "join" : Expression(call.Function, 8);
						return ($"{function}({string.Join(", ", call.Arguments.Select(Argument))})", 8);
					}

					case UnaryExpr unary when unary.Op is "~" or "not":
					{
						if (unary.Operand is BinaryExpr { Op: "&" or "|" or "^" or "and" or "or" } inner)
						{
							string op = inner.Op switch { "&" or "and" => "nand", "|" or "or" => "nor", _ => "xnor" };
							int level = Level(inner.Op);
							return ($"{Expression(inner.Left, level)} {op} {Expression(inner.Right, level + 1)}", level);
						}

						return ($"not {Expression(unary.Operand, 7)}", 7);
					}

					case UnaryExpr unary:
						return ($"-{Expression(unary.Operand, 7)}", 7);

					case BinaryExpr binary:
					{
						int level = Level(binary.Op);
						string op = binary.Op switch { "&" or "and" => "and", "|" or "or" => "or", "^" => "xor", _ => binary.Op };
						// Comparisons do not chain, so both sides bind tighter.
						int left = level == 4 ? level + 1 : level;
						return ($"{Expression(binary.Left, left)} {op} {Expression(binary.Right, level + 1)}", level);
					}

					case ConditionalExpr conditional:
						return ($"if {Expression(conditional.Condition, 1)} then {Expression(conditional.WhenTrue, 0, nested: true)} else {Expression(conditional.WhenFalse, 0)}", 0);

					case ListExpr list:
						return ($"[{string.Join(", ", list.Items.Select(i => Expression(i, 0)))}]", 8);

					default:
						throw new InvalidOperationException("Unknown expression " + expr.GetType().Name);
				}
			}

			static int Level(string op) => op switch
			{
				"|" or "or" => 1,
				"^" => 2,
				"&" or "and" => 3,
				"==" or "!=" or "<" or ">" or "<=" or ">=" => 4,
				"<<" or ">>" => 5,
				_ => 6
			};

			string Argument(CallArgument argument)
			{
				// A pin takes a narrower value as it is, so 'u4(x)' is just 'x' there.
				Expr value = IsCast(argument.Value, out _, out Expr inner) ? inner : argument.Value;
				string text = Expression(value, 0);
				return argument.Keyword == null ? text : $"{argument.Keyword}={text}";
			}

			static bool IsCast(Expr expr, out int width, out Expr inner)
			{
				width = 0;
				inner = null;
				if (expr is not CallExpr { Function: NameExpr function } call || call.Arguments.Count != 1 || call.Arguments[0].Keyword != null) return false;
				if (!RhdlLegacyParser.TryParseTypeName(function.Name, out width)) return false;
				inner = call.Arguments[0].Value;
				return true;
			}

			// 'expr + delta', folding numbers: the last bit of a 1.0 slice or range is one below its end.
			string Offset(Expr expr, long delta)
			{
				if (TryNumber(expr, out long value)) return (value + delta).ToString();
				if (expr is BinaryExpr { Op: "+" or "-" } binary && TryNumber(binary.Right, out long k))
				{
					long total = (binary.Op == "+" ? k : -k) + delta;
					string left = Expression(binary.Left, 6);
					return total == 0 ? left : total > 0 ? $"{left} + {total}" : $"{left} - {-total}";
				}

				return delta >= 0 ? $"{Expression(expr, 6)} + {delta}" : $"{Expression(expr, 6)} - {-delta}";
			}

			static bool TryNumber(Expr expr, out long value)
			{
				switch (expr)
				{
					case NumberExpr number:
						value = number.Value;
						return true;
					case UnaryExpr { Op: "-" } negative when TryNumber(negative.Operand, out long inner):
						value = -inner;
						return true;
					default:
						value = 0;
						return false;
				}
			}

			// RHDL 2.0 keywords that were ordinary names in 1.0 get a '_' at the end.
			string Name(string name)
			{
				if (!RhdlParser.IsReserved(name)) return name;
				renamed.Add(name);
				return name + "_";
			}

			// ---- Lines and comments ----

			void Line(string text, int sourceLine)
			{
				if (sourceLine > 0 && comments.TryGetValue(sourceLine, out (string text, bool alone) comment) && !comment.alone)
				{
					int newline = text.IndexOf('\n');
					text = newline < 0 ? $"{text}  {comment.text}" : $"{text.Substring(0, newline)}  {comment.text}{text.Substring(newline)}";
					comments.Remove(sourceLine);
				}

				output.Append(text).Append('\n');
				lastWasBlank = false;
				if (sourceLine >= nextSourceLine) nextSourceLine = sourceLine + 1;
			}

			void Blank()
			{
				if (lastWasBlank) return;
				output.Append('\n');
				lastWasBlank = true;
			}

			void SkipBlankLinesBefore(int line)
			{
				while (nextSourceLine < line && blankLines.Contains(nextSourceLine)) nextSourceLine++;
			}

			// Writes the comments and blank lines of the source above 'line' that are not written yet.
			void FlushComments(int line, string indent, bool onlyAlone = false)
			{
				int stop = line;
				for (int i = nextSourceLine; i < stop && i <= lines.Length; i++)
				{
					if (comments.TryGetValue(i, out (string text, bool alone) comment))
					{
						if (!comment.alone && onlyAlone) continue;
						output.Append(indent).Append(comment.text).Append('\n');
						lastWasBlank = false;
						comments.Remove(i);
					}
					else if (blankLines.Contains(i) && !onlyAlone)
					{
						Blank();
					}
				}

				if (stop != int.MaxValue) nextSourceLine = Math.Max(nextSourceLine, stop);
			}

		}
	}
}
