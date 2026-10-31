using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DLS.RHDL
{
	// ---- Tokens ----

	internal enum TokenKind
	{
		Name,
		Number,
		String,
		Op,
		Newline,
		Indent,
		Dedent,
		End
	}

	internal readonly struct Token
	{
		public readonly TokenKind Kind;
		public readonly string Text;
		public readonly long Value;
		public readonly int Line;
		public readonly int Column;

		public Token(TokenKind kind, string text, int line, int column, long value = 0)
		{
			Kind = kind;
			Text = text;
			Line = line;
			Column = column;
			Value = value;
		}

		public bool Is(string op) => (Kind == TokenKind.Op || Kind == TokenKind.Name) && Text == op;

		public override string ToString() => Kind switch
		{
			TokenKind.Newline => "end of line",
			TokenKind.Indent => "indented block",
			TokenKind.Dedent => "end of block",
			TokenKind.End => "end of file",
			TokenKind.String => "string",
			_ => $"'{Text}'"
		};
	}

	internal sealed class RhdlSyntaxException : Exception
	{
		public readonly int Line;
		public readonly int Column;

		public RhdlSyntaxException(int line, int column, string message) : base(message)
		{
			Line = line;
			Column = column;
		}
	}

	// ---- Syntax tree ----

	internal abstract class Node
	{
		public int Line;
		public int Column;
	}

	internal abstract class Expr : Node { }

	internal sealed class NumberExpr : Expr
	{
		public long Value;
		public string Raw;
		// Width implied by the literal's spelling: 0b0010 is 4 bits, 0x0F is 8 bits. 0 if none.
		public int SpelledWidth;
	}

	internal sealed class NameExpr : Expr
	{
		public string Name;
	}

	internal sealed class AttributeExpr : Expr
	{
		public Expr Target;
		public string Name;
	}

	internal sealed class IndexExpr : Expr
	{
		public Expr Target;
		public Expr Index;
	}

	internal sealed class SliceExpr : Expr
	{
		public Expr Target;
		public Expr Low;
		public Expr High;
		// RHDL 2.0 writes A[0..3]: both ends are bits of the slice, in either order. RHDL 1.0
		// wrote A[0:4] like Python, with High one past the last bit.
		public bool Inclusive;
	}

	internal sealed class CallArgument
	{
		public string Keyword;
		public Expr Value;
		public int Line;
		public int Column;
	}

	internal sealed class CallExpr : Expr
	{
		public Expr Function;
		public List<CallArgument> Arguments = new();
	}

	internal sealed class UnaryExpr : Expr
	{
		public string Op;
		public Expr Operand;
	}

	internal sealed class BinaryExpr : Expr
	{
		public string Op;
		public Expr Left;
		public Expr Right;
	}

	internal sealed class ConditionalExpr : Expr
	{
		public Expr Condition;
		public Expr WhenTrue;
		public Expr WhenFalse;
	}

	internal sealed class ListExpr : Expr
	{
		public List<Expr> Items = new();
	}

	internal sealed class PortSyntax : Node
	{
		public string Name;
		// 0 when no type was written (a single bit).
		public int Width;
	}

	internal abstract class Statement : Node { }

	internal sealed class AssignStatement : Statement
	{
		public List<Expr> Targets = new();
		public int AnnotatedWidth;
		public Expr Value;
	}

	internal sealed class DeclareStatement : Statement
	{
		public string Name;
		public int Width;
	}

	internal sealed class ExpressionStatement : Statement
	{
		public Expr Value;
	}

	internal sealed class ForStatement : Statement
	{
		public string Variable;
		public Expr Start;
		public Expr Stop;
		public Expr Step;
		// RHDL 2.0 writes 'for i in 0..3': Stop is the last value and the loop counts down when
		// Stop is below Start. RHDL 1.0 wrote range(0, 4), with Stop one past the last value.
		public bool Inclusive;
		public List<Statement> Body = new();
	}

	internal sealed class CircuitSyntax : Node
	{
		public string Name;
		public string Doc;
		// Written in the RHDL 1.0 syntax; messages then use that syntax too.
		public bool Legacy;
		public List<PortSyntax> Inputs = new();
		public List<PortSyntax> Outputs = new();
		public List<Statement> Body = new();
	}

	internal sealed class ModuleSyntax
	{
		public List<AssignStatement> Constants = new();
		public List<CircuitSyntax> Circuits = new();
		// True when the source used the RHDL 1.0 (Python-style) syntax.
		public bool Legacy;
	}


	// Splits RHDL 2.0 source into tokens. Indentation means nothing: every line is one
	// statement. A line also continues on the next one inside brackets, after an operator or
	// '=' at its end, and before a line that starts with 'else', 'then' or an operator.
	internal static class RhdlLexer
	{
		static readonly string[] Operators =
		{
			"..", "==", "!=", "<=", ">=", "<<", ">>", "->", "**", "//",
			"(", ")", "[", "]", ",", ":", ".", "=", "+", "-", "*", "/", "%", "&", "|", "^", "~", "!", "<", ">", "{", "}", ";", "@"
		};

		// A line ending in one of these goes on with the next line.
		static readonly HashSet<string> ContinuesAfter = new(StringComparer.Ordinal)
		{
			"=", ",", "+", "-", "&", "|", "^", "~", "!", "==", "!=", "<", ">", "<=", ">=", "<<", ">>", "..",
			"and", "or", "xor", "nand", "nor", "xnor", "not", "if", "then", "else"
		};

		// A line starting with one of these goes on with the line above.
		static readonly HashSet<string> ContinuesBefore = new(StringComparer.Ordinal)
		{
			"+", "-", "&", "|", "^", "==", "!=", "<<", ">>", "..",
			"and", "or", "xor", "nand", "nor", "xnor", "then", "else"
		};

		public static List<Token> Tokenize(string source)
		{
			List<Token> raw = new();
			string text = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
			int pos = 0;
			int line = 1;
			int lineStart = 0;
			int depth = 0;

			while (pos < text.Length)
			{
				char c = text[pos];
				int col = pos - lineStart + 1;

				if (c == '\n')
				{
					if (depth == 0) raw.Add(new Token(TokenKind.Newline, string.Empty, line, col));
					pos++;
					line++;
					lineStart = pos;
					continue;
				}

				if (c == ' ' || c == '\t')
				{
					pos++;
					continue;
				}

				if (c == '#')
				{
					while (pos < text.Length && text[pos] != '\n') pos++;
					continue;
				}

				if (c == '\\' && pos + 1 < text.Length && text[pos + 1] == '\n')
				{
					pos += 2;
					line++;
					lineStart = pos;
					continue;
				}

				if (char.IsLetter(c) || c == '_')
				{
					int start = pos;
					while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
					raw.Add(new Token(TokenKind.Name, text.Substring(start, pos - start), line, col));
					continue;
				}

				if (char.IsDigit(c))
				{
					int start = pos;
					while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
					string number = text.Substring(start, pos - start);
					if (!TryParseNumber(number, out long value))
					{
						throw new RhdlSyntaxException(line, col, $"'{number}' is not a valid number. Use decimal (12), binary (0b1100) or hex (0xC).");
					}

					raw.Add(new Token(TokenKind.Number, number, line, col, value));
					continue;
				}

				if (c == '"' || c == '\'')
				{
					throw new RhdlSyntaxException(line, col, "Text in quotes is not part of RHDL. Write notes as comments starting with #.");
				}

				string op = null;
				foreach (string candidate in Operators)
				{
					if (string.CompareOrdinal(text, pos, candidate, 0, candidate.Length) == 0)
					{
						op = candidate;
						break;
					}
				}

				if (op == null) throw new RhdlSyntaxException(line, col, $"Unexpected character '{c}'.");

				if (op is "(" or "[" or "{") depth++;
				else if (op is ")" or "]" or "}") depth = Math.Max(0, depth - 1);

				raw.Add(new Token(TokenKind.Op, op, line, col));
				pos += op.Length;
			}

			// Join continued lines and drop empty ones.
			List<Token> tokens = new(raw.Count + 2);
			for (int i = 0; i < raw.Count; i++)
			{
				Token token = raw[i];
				if (token.Kind == TokenKind.Newline)
				{
					if (tokens.Count == 0 || tokens[^1].Kind == TokenKind.Newline) continue;
					if (ContinuesAfter.Contains(tokens[^1].Text)) continue;
					int next = i + 1;
					while (next < raw.Count && raw[next].Kind == TokenKind.Newline) next++;
					if (next < raw.Count && ContinuesBefore.Contains(raw[next].Text)) continue;
				}

				tokens.Add(token);
			}

			int endLine = line;
			int endColumn = pos - lineStart + 1;
			if (tokens.Count > 0 && tokens[^1].Kind != TokenKind.Newline)
			{
				tokens.Add(new Token(TokenKind.Newline, string.Empty, endLine, endColumn));
			}

			tokens.Add(new Token(TokenKind.End, string.Empty, endLine, endColumn));
			return tokens;
		}

		internal static bool TryParseNumber(string raw, out long value)
		{
			value = 0;
			string digits = raw.Replace("_", string.Empty);
			int numberBase = 10;
			if (digits.Length > 2 && digits[0] == '0')
			{
				char prefix = char.ToLowerInvariant(digits[1]);
				if (prefix == 'x') numberBase = 16;
				else if (prefix == 'b') numberBase = 2;
				else if (prefix == 'o') numberBase = 8;
				if (numberBase != 10) digits = digits.Substring(2);
			}

			if (digits.Length == 0) return false;
			if (numberBase == 10) return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);

			try
			{
				value = Convert.ToInt64(digits, numberBase);
				return value >= 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		// Width implied by a literal's spelling: 0b0010 is 4 bits, 0x0F is 8 bits. 0 if none.
		internal static int SpelledWidth(string raw)
		{
			string digits = raw.Replace("_", string.Empty);
			if (digits.Length > 2 && digits[0] == '0')
			{
				char prefix = char.ToLowerInvariant(digits[1]);
				if (prefix == 'b') return digits.Length - 2;
				if (prefix == 'x') return (digits.Length - 2) * 4;
			}

			return 0;
		}
	}

	// RHDL 2.0 parser.
	//
	//   chip FullAdder
	//       in A, B, CIN
	//       out SUM, COUT
	//
	//       ab = A xor B
	//       SUM = ab xor CIN
	//       COUT = (A and B) or (CIN and ab)
	//
	// Operators, loosest first: 'if c then a else b', 'or'/'nor', 'xor'/'xnor', 'and'/'nand',
	// comparisons, '<<' '>>', '+' '-', then 'not' and '-' on the value right after them, and
	// finally calls, bits A[3] / A[0..3] and pins h.SUM. The symbols '|', '^', '&' and '~' are
	// accepted as short forms of 'or', 'xor', 'and' and 'not'.
	internal sealed class RhdlParser
	{
		public const int MaxWidth = 32;

		// Brackets, calls, 'not' and 'if ... then ... else' inside each other. Far more than any
		// circuit needs, and it keeps a pasted or generated expression from exhausting the stack.
		public const int MaxNesting = 100;

		static readonly Regex LegacyHeader = new(@"^[ \t]*circuit[ \t]+\w+[ \t]*\(", RegexOptions.Multiline);

		readonly List<Token> tokens;
		int position;
		int nesting;

		RhdlParser(List<Token> tokens) => this.tokens = tokens;

		// u8(x) and friends still widen a value, as in RHDL 1.0.
		public static bool TryParseTypeName(string text, out int width) => RhdlLegacyParser.TryParseTypeName(text, out width);

		// Sources written for RHDL 1.0 ('circuit Name(A, B) -> Y:') still build.
		public static bool IsLegacySource(string source) => source != null && LegacyHeader.IsMatch(source);

		public static ModuleSyntax Parse(string source)
		{
			if (IsLegacySource(source))
			{
				ModuleSyntax legacy = RhdlLegacyParser.Parse(source);
				legacy.Legacy = true;
				foreach (CircuitSyntax circuit in legacy.Circuits) circuit.Legacy = true;
				return legacy;
			}

			RhdlParser parser = new(RhdlLexer.Tokenize(source));
			return parser.ParseModule();
		}

		Token Current => tokens[position];
		Token Peek(int offset = 1) => tokens[Math.Min(position + offset, tokens.Count - 1)];

		Token Advance()
		{
			Token token = tokens[position];
			if (position < tokens.Count - 1) position++;
			return token;
		}

		bool Accept(string op)
		{
			if (!Current.Is(op)) return false;
			Advance();
			return true;
		}

		Token Expect(string op, string what = null)
		{
			if (Current.Is(op)) return Advance();
			throw Error(Current, $"Expected '{op}'{(what == null ? string.Empty : " " + what)} but found {Current}.");
		}

		Token ExpectName(string message)
		{
			if (Current.Kind == TokenKind.Name && !IsReserved(Current.Text)) return Advance();
			if (Current.Kind == TokenKind.Name) throw Error(Current, $"'{Current.Text}' is a keyword and cannot be used as a name.");
			throw Error(Current, message + $" Found {Current}.");
		}

		static RhdlSyntaxException Error(Token token, string message) => new(token.Line, token.Column, message);

		bool AtLineEnd => Current.Kind == TokenKind.Newline || Current.Kind == TokenKind.End;

		// ---- File and chip ----

		ModuleSyntax ParseModule()
		{
			ModuleSyntax module = new();
			while (Current.Kind != TokenKind.End)
			{
				if (Current.Kind == TokenKind.Newline)
				{
					Advance();
					continue;
				}

				if (Current.Is("chip"))
				{
					module.Circuits.Add(ParseChip(module));
					continue;
				}

				if (Current.Is("const"))
				{
					module.Constants.Add(ParseConstant());
					continue;
				}

				if (Current.Kind == TokenKind.Name && Peek().Is("="))
				{
					module.Constants.Add(ParseConstant());
					continue;
				}

				if (Current.Is("circuit") || Current.Is("def") || Current.Is("module") || Current.Is("entity"))
				{
					throw Error(Current, $"Start a chip with 'chip Name', not '{Current.Text}'.");
				}

				if (Current.Is("in") || Current.Is("out") || Current.Is("input") || Current.Is("output"))
				{
					throw Error(Current, "Pins belong to a chip. Write 'chip Name' on the line above.");
				}

				throw Error(Current, "Expected 'chip Name' or a constant such as 'const WIDTH = 4'.");
			}

			return module;
		}

		AssignStatement ParseConstant()
		{
			Accept("const");
			Token name = ExpectName("Expected the constant's name after 'const'.");
			Expect("=", "after the constant's name");
			AssignStatement constant = new() { Line = name.Line, Column = name.Column, Value = ParseExpression() };
			constant.Targets.Add(new NameExpr { Name = name.Text, Line = name.Line, Column = name.Column });
			EndStatement();
			return constant;
		}

		CircuitSyntax ParseChip(ModuleSyntax module)
		{
			Token keyword = Expect("chip");
			Token name = ExpectName("Expected the chip's name after 'chip', as in 'chip Adder'.");
			CircuitSyntax chip = new() { Name = name.Text, Line = keyword.Line, Column = keyword.Column };
			if (Current.Is("("))
			{
				throw Error(Current, $"Pins go on their own lines: 'chip {name.Text}', then 'in A, B' and 'out Y'.");
			}

			Accept(":");
			EndStatement();

			while (Current.Kind != TokenKind.End && !Current.Is("chip"))
			{
				if (Current.Kind == TokenKind.Newline)
				{
					Advance();
					continue;
				}

				if (Current.Is("end"))
				{
					// An optional 'end' (or 'end chip') closes the chip.
					Advance();
					if (Current.Is("chip") || Current.Is(chip.Name)) Advance();
					EndStatement();
					break;
				}

				if (Current.Is("in") || Current.Is("out"))
				{
					bool input = Advance().Text == "in";
					(input ? chip.Inputs : chip.Outputs).AddRange(ParsePins());
					EndStatement();
					continue;
				}

				if (Current.Is("input") || Current.Is("inputs") || Current.Is("output") || Current.Is("outputs"))
				{
					throw Error(Current, $"Write '{(Current.Text.StartsWith("in") ? "in" : "out")}' followed by the pin names, e.g. 'in A, B'.");
				}

				if (Current.Is("const"))
				{
					module.Constants.Add(ParseConstant());
					continue;
				}

				Statement statement = ParseStatement();
				if (statement != null) chip.Body.Add(statement);
			}

			return chip;
		}

		List<PortSyntax> ParsePins()
		{
			List<PortSyntax> pins = new();
			do
			{
				Token name = ExpectName("Expected a pin name.");
				PortSyntax pin = new() { Name = name.Text, Line = name.Line, Column = name.Column };
				if (Accept(":")) pin.Width = ParseWidth();
				pins.Add(pin);
			} while (Accept(",") && !AtLineEnd);

			return pins;
		}

		// The number of bits after ':', as in 'A: 8'.
		int ParseWidth()
		{
			Token token = Current;
			if (token.Kind == TokenKind.Number)
			{
				Advance();
				if (token.Value < 1 || token.Value > MaxWidth)
				{
					throw Error(token, $"A signal has 1 to {MaxWidth} bits, not {token.Text}.");
				}

				return (int)token.Value;
			}

			if (token.Kind == TokenKind.Name && (RhdlLegacyParser.TryParseTypeName(token.Text, out int width) || token.Text == "bits"))
			{
				throw Error(token, width > 0
					? $"Write the number of bits after ':', e.g. ': {width}'."
					: "Write the number of bits after ':', e.g. 'A: 8'.");
			}

			throw Error(token, "Expected the number of bits after ':', e.g. 'A: 8'.");
		}

		// ---- Statements ----

		Statement ParseStatement()
		{
			Token start = Current;

			if (start.Is("for")) return ParseFor();

			if (start.Is("wire")) return ParseWire();

			if (start.Is("if") || start.Is("while") || start.Is("elif"))
			{
				throw Error(start, $"'{start.Text}' cannot start a line: all lines are hardware that works at the same time. " +
				                   "Choose between values with 'Y = if select then A else B'.");
			}

			if (start.Is("else") || start.Is("then"))
			{
				throw Error(start, $"'{start.Text}' belongs to a choice such as 'Y = if select then A else B'.");
			}

			if (start.Is("return") || start.Is("def") || start.Is("import") || start.Is("class"))
			{
				throw Error(start, $"'{start.Text}' is not part of RHDL. Give each output a value by name, e.g. 'Y = A and B'.");
			}

			if (start.Kind == TokenKind.Name && Peek().Is(":"))
			{
				// 'name: 8' declares a wire, like 'wire name: 8'.
				Advance();
				return ParseDeclaration(start);
			}

			return ParseSimpleStatement();
		}

		Statement ParseWire()
		{
			Advance();
			Token name = ExpectName("Expected a name after 'wire', as in 'wire total: 8'.");
			if (Current.Is("="))
			{
				// 'wire t = A and B' is the same as 't = A and B'.
				Advance();
				AssignStatement plain = new() { Line = name.Line, Column = name.Column, Value = ParseExpression() };
				plain.Targets.Add(new NameExpr { Name = name.Text, Line = name.Line, Column = name.Column });
				EndStatement();
				return plain;
			}

			if (!Current.Is(":")) throw Error(Current, $"Give the wire a width or a value: 'wire {name.Text}: 8' or 'wire {name.Text} = ...'.");
			return ParseDeclaration(name);
		}

		Statement ParseDeclaration(Token name)
		{
			if (IsReserved(name.Text)) throw Error(name, $"'{name.Text}' is a keyword and cannot be used as a name.");
			Expect(":");
			int width = ParseWidth();
			if (Accept("="))
			{
				AssignStatement annotated = new() { Line = name.Line, Column = name.Column, AnnotatedWidth = width, Value = ParseExpression() };
				annotated.Targets.Add(new NameExpr { Name = name.Text, Line = name.Line, Column = name.Column });
				EndStatement();
				return annotated;
			}

			EndStatement();
			return new DeclareStatement { Name = name.Text, Width = width, Line = name.Line, Column = name.Column };
		}

		Statement ParseSimpleStatement()
		{
			Token start = Current;
			Expr first = ParseExpression();
			if (Current.Is(",") || Current.Is("="))
			{
				AssignStatement assign = new() { Line = start.Line, Column = start.Column };
				assign.Targets.Add(first);
				while (Accept(","))
				{
					if (Current.Is("=")) break;
					assign.Targets.Add(ParseExpression());
				}

				Expect("=", "after the signal names");
				assign.Value = ParseExpression();
				if (Current.Is("="))
				{
					throw Error(Current, "Write one '=' per line: 'a = b = c' is not supported.");
				}

				EndStatement();
				return assign;
			}

			if (Current.Kind == TokenKind.Op && Current.Text.Length == 1 && Peek().Is("=") && "&|^+-".Contains(Current.Text))
			{
				throw Error(Current, $"'{Current.Text}=' would change a signal after it is set. Write the whole expression in one line.");
			}

			if (first is BinaryExpr { Op: "==" } compare && compare.Left is NameExpr or IndexExpr or SliceExpr)
			{
				throw new RhdlSyntaxException(compare.Line, compare.Column, "Use a single '=' to give a signal its value; '==' compares two values.");
			}

			EndStatement();
			return new ExpressionStatement { Value = first, Line = start.Line, Column = start.Column };
		}

		void EndStatement()
		{
			if (Current.Kind == TokenKind.Newline)
			{
				Advance();
				return;
			}

			if (Current.Kind == TokenKind.End) return;
			if (Current.Is(";")) throw Error(Current, "Write one statement per line; ';' is not needed.");
			if (Current.Is("if")) throw Error(Current, "Write the choice the other way round: 'if select then A else B'.");
			throw Error(Current, $"Unexpected {Current} after the end of the statement.");
		}

		Statement ParseFor()
		{
			Token keyword = Expect("for");
			Token variable = ExpectName("Expected a loop variable after 'for', as in 'for i in 0..7'.");
			Expect("in", "in 'for i in 0..7'");
			if (Current.Is("range"))
			{
				throw Error(Current, "Write the first and the last number: 'for i in 0..3' repeats for 0, 1, 2 and 3.");
			}

			ForStatement loop = new() { Variable = variable.Text, Inclusive = true, Line = keyword.Line, Column = keyword.Column };
			loop.Start = ParseExpression();
			Expect("..", "between the first and the last number, as in 'for i in 0..7'");
			loop.Stop = ParseExpression();
			if (Accept("step")) loop.Step = ParseExpression();
			Accept(":");
			Accept("do");
			EndStatement();

			while (!Current.Is("end"))
			{
				if (Current.Kind == TokenKind.End || Current.Is("chip"))
				{
					throw Error(keyword, "This 'for' has no 'end'. Close the repeated lines with 'end'.");
				}

				if (Current.Kind == TokenKind.Newline)
				{
					Advance();
					continue;
				}

				Statement statement = ParseStatement();
				if (statement != null) loop.Body.Add(statement);
			}

			Advance();
			Accept("for");
			EndStatement();
			return loop;
		}

		// ---- Expressions ----

		void EnterNesting()
		{
			if (++nesting > MaxNesting)
			{
				throw Error(Current, $"This expression is nested more than {MaxNesting} levels deep. Split it into named wires.");
			}
		}

		Expr ParseExpression()
		{
			EnterNesting();
			Expr result = ParseExpressionInner();
			nesting--;
			return result;
		}

		Expr ParseExpressionInner()
		{
			if (Current.Is("if"))
			{
				Token keyword = Advance();
				Expr condition = ParseOr();
				Expect("then", "in 'if select then A else B'");
				Expr whenTrue = ParseExpression();
				if (!Current.Is("else"))
				{
					throw Error(Current, "A choice needs both values: 'if select then A else B'.");
				}

				Advance();
				Expr whenFalse = ParseExpression();
				return new ConditionalExpr
				{
					Condition = condition,
					WhenTrue = whenTrue,
					WhenFalse = whenFalse,
					Line = keyword.Line,
					Column = keyword.Column
				};
			}

			Expr value = ParseOr();
			if (Current.Is("if"))
			{
				throw Error(Current, "Write the choice the other way round: 'if select then A else B'.");
			}

			return value;
		}

		Expr ParseOr()
		{
			Expr left = ParseXor();
			while (Current.Is("or") || Current.Is("nor") || Current.Is("|"))
			{
				Token op = Advance();
				left = Logic(op, "|", left, ParseXor());
			}

			return left;
		}

		Expr ParseXor()
		{
			Expr left = ParseAnd();
			while (Current.Is("xor") || Current.Is("xnor") || Current.Is("^"))
			{
				Token op = Advance();
				left = Logic(op, "^", left, ParseAnd());
			}

			return left;
		}

		Expr ParseAnd()
		{
			Expr left = ParseComparison();
			while (Current.Is("and") || Current.Is("nand") || Current.Is("&"))
			{
				Token op = Advance();
				left = Logic(op, "&", left, ParseComparison());
			}

			return left;
		}

		// 'nand', 'nor' and 'xnor' are the inverted forms.
		static Expr Logic(Token op, string symbol, Expr left, Expr right)
		{
			BinaryExpr binary = new() { Op = symbol, Left = left, Right = right, Line = op.Line, Column = op.Column };
			bool inverted = op.Text is "nand" or "nor" or "xnor";
			return inverted ? new UnaryExpr { Op = "~", Operand = binary, Line = op.Line, Column = op.Column } : binary;
		}

		static readonly string[] ComparisonOps = { "==", "!=", "<", ">", "<=", ">=" };

		Expr ParseComparison()
		{
			Expr left = ParseShift();
			if (IsComparison(Current))
			{
				Token op = Advance();
				Expr right = ParseShift();
				if (IsComparison(Current))
				{
					throw Error(Current, "Compare two values at a time and join the results with 'and': (a < b) and (b < c).");
				}

				return Binary(op.Text, left, right, op);
			}

			return left;

			static bool IsComparison(Token token) => token.Kind == TokenKind.Op && Array.IndexOf(ComparisonOps, token.Text) >= 0;
		}

		Expr ParseShift()
		{
			Expr left = ParseArithmetic();
			while (Current.Is("<<") || Current.Is(">>"))
			{
				Token op = Advance();
				left = Binary(op.Text, left, ParseArithmetic(), op);
			}

			return left;
		}

		Expr ParseArithmetic()
		{
			Expr left = ParseUnary();
			while (true)
			{
				if (Current.Is("+") || Current.Is("-"))
				{
					Token op = Advance();
					left = Binary(op.Text, left, ParseUnary(), op);
					continue;
				}

				if (Current.Is("*") || Current.Is("/") || Current.Is("//") || Current.Is("%") || Current.Is("**"))
				{
					throw Error(Current, $"'{Current.Text}' is not supported: RHDL builds adders and subtractors only. " +
					                     "Multiply by 2, 4, 8 ... with '<<'.");
				}

				return left;
			}
		}

		Expr ParseUnary()
		{
			if (Current.Is("not") || Current.Is("~") || Current.Is("!") || Current.Is("-") || Current.Is("+"))
			{
				Token op = Advance();
				EnterNesting();
				Expr operand = ParseUnary();
				nesting--;
				if (op.Text == "+") return operand;
				return new UnaryExpr { Op = op.Text == "-" ? "-" : "~", Operand = operand, Line = op.Line, Column = op.Column };
			}

			return ParsePostfix();
		}

		Expr ParsePostfix()
		{
			Expr value = ParseAtom();
			while (true)
			{
				Token token = Current;
				if (token.Is("("))
				{
					value = ParseCall(value);
					continue;
				}

				if (token.Is("["))
				{
					Advance();
					Expr low = Current.Is("..") ? null : ParseExpression();
					if (Accept(".."))
					{
						Expr high = Current.Is("]") ? null : ParseExpression();
						Expect("]", "to close the bits");
						value = new SliceExpr { Target = value, Low = low, High = high, Inclusive = true, Line = token.Line, Column = token.Column };
					}
					else
					{
						if (Current.Is(":") || Current.Is(","))
						{
							throw Error(Current, "Select several bits with '..': A[0..3] is bits 0, 1, 2 and 3.");
						}

						Expect("]", "to close the bit number");
						value = new IndexExpr { Target = value, Index = low, Line = token.Line, Column = token.Column };
					}

					continue;
				}

				if (token.Is("."))
				{
					Advance();
					if (Current.Kind != TokenKind.Name) throw Error(Current, "Expected a pin name after '.'.");
					Token name = Advance();
					value = new AttributeExpr { Target = value, Name = name.Text, Line = name.Line, Column = name.Column };
					continue;
				}

				return value;
			}
		}

		Expr ParseCall(Expr function)
		{
			Token open = Expect("(");
			CallExpr call = new() { Function = function, Line = function.Line, Column = function.Column };
			bool sawKeyword = false;
			while (!Current.Is(")"))
			{
				if (Current.Kind == TokenKind.End) throw Error(open, "This '(' is never closed.");
				Token start = Current;
				CallArgument argument = new() { Line = start.Line, Column = start.Column };
				if (start.Kind == TokenKind.Name && Peek().Is("="))
				{
					Advance();
					Advance();
					argument.Keyword = start.Text;
					sawKeyword = true;
				}
				else if (sawKeyword)
				{
					throw Error(start, "Pins given by position must come before pins given by name.");
				}

				argument.Value = ParseExpression();
				call.Arguments.Add(argument);
				if (!Accept(",")) break;
			}

			if (Current.Kind == TokenKind.End) throw Error(open, "This '(' is never closed.");
			Expect(")", "to close the brackets");
			return call;
		}

		Expr ParseAtom()
		{
			Token token = Current;
			switch (token.Kind)
			{
				case TokenKind.Number:
					Advance();
					return new NumberExpr
					{
						Value = token.Value,
						Raw = token.Text,
						SpelledWidth = RhdlLexer.SpelledWidth(token.Text),
						Line = token.Line,
						Column = token.Column
					};

				case TokenKind.Name:
					if (token.Text is "true" or "false" or "True" or "False")
					{
						Advance();
						bool on = token.Text is "true" or "True";
						return new NumberExpr { Value = on ? 1 : 0, Raw = on ? "1" : "0", SpelledWidth = 1, Line = token.Line, Column = token.Column };
					}

					if (IsReserved(token.Text))
					{
						throw Error(token, $"Expected a value but found the keyword '{token.Text}'.");
					}

					Advance();
					return new NameExpr { Name = token.Text, Line = token.Line, Column = token.Column };

				case TokenKind.Op when token.Text == "(":
				{
					Advance();
					Expr inner = ParseExpression();
					if (Current.Is(","))
					{
						throw Error(Current, "Brackets hold one value. To join bits into a bus, write join(high, low).");
					}

					Expect(")", "to close the bracket");
					return inner;
				}

				case TokenKind.Op when token.Text == "{":
					throw Error(token, "Join bits into a bus with join(high, low).");

				case TokenKind.Newline:
				case TokenKind.End:
					throw Error(token, "Expected a value before the end of the line.");

				default:
					throw Error(token, $"Expected a value but found {token}.");
			}
		}

		internal static bool IsReserved(string name) => name is
			"chip" or "in" or "out" or "wire" or "const" or "for" or "end" or
			"if" or "then" or "else" or "and" or "or" or "xor" or "nand" or "nor" or "xnor" or "not" or
			"true" or "false";

		static BinaryExpr Binary(string op, Expr left, Expr right, Token token) =>
			new() { Op = op, Left = left, Right = right, Line = token.Line, Column = token.Column };
	}
}
