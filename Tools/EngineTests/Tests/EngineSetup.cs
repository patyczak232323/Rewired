using System;
using DLS.Simulation;
using NUnit.Framework;

// Runs before every test in the assembly. With REWIRED_FORCE_INTERPRETER=1 every JIT program
// runs on GateProgram's interpreter, the way IL2CPP builds (Android, WebGL) run them, so the
// whole suite can be repeated on that path: dotnet test with the variable set.
[SetUpFixture]
public class EngineSetup
{
	public static bool InterpreterForced => Environment.GetEnvironmentVariable("REWIRED_FORCE_INTERPRETER") == "1";

	[OneTimeSetUp]
	public void ChooseExecutionPath()
	{
		CombinationalJitCompiler.ForceInterpreter = InterpreterForced;
		FeedbackJitCompiler.ForceInterpreter = InterpreterForced;
	}
}
