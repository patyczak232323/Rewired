using DLS.Description;
using NUnit.Framework;

namespace DLS.Tests
{
	public class SimulationSpeedTests
	{
		[TestCase(8000, 1, "4000")] // Snake: 8000 steps a second, a clock edge every step
		[TestCase(1000, 250, "2")] // a new project
		[TestCase(25, 5, "2.5")]
		[TestCase(1, 1, "0.5")]
		[TestCase(1001, 1, "501")] // 500.5 Hz, whole numbers from 100 Hz up
		[TestCase(SimulationSpeed.Unlimited, 1, "MAX")]
		[TestCase(SimulationSpeed.Unlimited, 250, "MAX")]
		public void ShowsTheClockSpeed(int stepsPerSecond, int stepsPerClockTick, string expected)
		{
			Assert.AreEqual(expected, SimulationSpeed.FormatTarget(stepsPerSecond, stepsPerClockTick));
		}

		[TestCase("4000", 1, 8000)]
		[TestCase("2", 250, 1000)]
		[TestCase("2.5", 5, 25)]
		[TestCase("2,5", 5, 25)]
		[TestCase("0.5", 1, 1)]
		[TestCase("0.1", 1, 1)] // never below one step a second
		[TestCase("max", 1, SimulationSpeed.Unlimited)]
		[TestCase(" MAX ", 250, SimulationSpeed.Unlimited)]
		[TestCase("99999999999", 250, SimulationSpeed.Unlimited - 1)] // a huge number is still a limit
		public void ReadsAClockSpeed(string text, int stepsPerClockTick, int expectedSteps)
		{
			Assert.IsTrue(SimulationSpeed.TryParse(text, stepsPerClockTick, out int steps));
			Assert.AreEqual(expectedSteps, steps);
		}

		[TestCase("")]
		[TestCase("0")]
		[TestCase("MA")]
		[TestCase(".")]
		public void IgnoresIncompleteOrZeroEntries(string text)
		{
			Assert.IsFalse(SimulationSpeed.TryParse(text, 1, out _));
		}

		[TestCase("", true)]
		[TestCase("m", true)]
		[TestCase("Ma", true)]
		[TestCase("MAX", true)]
		[TestCase("12.5", true)]
		[TestCase("12,5", true)]
		[TestCase("1.2.3", false)]
		[TestCase("-5", false)]
		[TestCase("MAXX", false)]
		[TestCase("1 000", false)]
		public void AcceptsOnlyWhatCanBecomeAnEntryWhileTyping(string text, bool valid)
		{
			Assert.AreEqual(valid, SimulationSpeed.IsValidPartialInput(text));
		}

		[Test]
		public void ShownSpeedReadsBackAsTheSameRate()
		{
			// What the menu shows for the bundled projects turns back into their step rates.
			foreach ((int steps, int perTick) in new[] { (8000, 1), (20, 1), (1000, 250), (SimulationSpeed.Unlimited, 1) })
			{
				string shown = SimulationSpeed.FormatTarget(steps, perTick);
				Assert.IsTrue(SimulationSpeed.TryParse(shown, perTick, out int back), shown);
				Assert.AreEqual(steps, back, shown);
			}
		}
	
		static ProjectDescription Project(int stepsPerSecond, int stepsPerClockTick, bool? own) =>
			new() { Prefs_SimTargetStepsPerSecond = stepsPerSecond, Prefs_SimStepsPerClockTick = stepsPerClockTick, Prefs_SimOwnClockSpeed = own };

		static AppSettings Shared(double hz, bool max = false) => new() { SimClockHz = hz, SimClockMax = max };

		[Test]
		public void TheSharedSpeedStartsAtTwoHertz()
		{
			AppSettings settings = AppSettings.Default();
			Assert.AreEqual("2", SimulationSpeed.FormatShared(settings));
			Assert.AreEqual(1000, SimulationSpeed.SharedTarget(settings, 250)); // what a new project used to get
		}

		[Test]
		public void AProjectRunsAtTheSharedSpeed()
		{
			ProjectDescription project = SimulationSpeed.ApplyShared(Project(1000, 250, false), Shared(50));
			Assert.AreEqual(25000, project.Prefs_SimTargetStepsPerSecond);
			Assert.AreEqual("50", SimulationSpeed.FormatTarget(project.Prefs_SimTargetStepsPerSecond, 250));

			project = SimulationSpeed.ApplyShared(Project(1000, 250, false), Shared(50, max: true));
			Assert.IsTrue(SimulationSpeed.IsUnlimited(project.Prefs_SimTargetStepsPerSecond));
		}

		[Test]
		public void SnakeKeepsItsOwnSpeed()
		{
			ProjectDescription snake = SimulationSpeed.ApplyShared(Project(8000, 1, true), Shared(2));
			Assert.AreEqual(8000, snake.Prefs_SimTargetStepsPerSecond);
			Assert.AreEqual(true, snake.Prefs_SimOwnClockSpeed);
		}

		// Projects saved before there was a shared speed keep running at the speed they had.
		[TestCase(1000, 250, 2, false)] // untouched new project: follows the shared speed
		[TestCase(8000, 1, 2, true)] // Snake installed by an older version
		[TestCase(20, 1, 2, true)] // CPU8 and Core8
		[TestCase(5000, 250, 2, true)] // a project set to 10 Hz
		[TestCase(5000, 250, 10, false)]
		public void AnOlderProjectKeepsItsSpeed(int stepsPerSecond, int stepsPerClockTick, double sharedHz, bool expectOwn)
		{
			ProjectDescription project = SimulationSpeed.ApplyShared(Project(stepsPerSecond, stepsPerClockTick, null), Shared(sharedHz));
			Assert.AreEqual(expectOwn, project.Prefs_SimOwnClockSpeed);
			Assert.AreEqual(stepsPerSecond, project.Prefs_SimTargetStepsPerSecond);
		}

		[Test]
		public void TheSharedSpeedIsSavedAsTyped()
		{
			Assert.IsTrue(SimulationSpeed.TryParseHz("12,5", out double hz, out bool max));
			AppSettings settings = SimulationSpeed.WithShared(AppSettings.Default(), hz, max);
			Assert.AreEqual("12.5", SimulationSpeed.FormatShared(settings));

			Assert.IsTrue(SimulationSpeed.TryParseHz("max", out hz, out max));
			settings = SimulationSpeed.WithShared(settings, hz, max);
			Assert.AreEqual("MAX", SimulationSpeed.FormatShared(settings));
			Assert.AreEqual(12.5, settings.SimClockHz); // turning MAX off again goes back to 12.5 Hz

			AppSettings back = Serializer.DeserializeAppSettings(Serializer.SerializeAppSettings(settings));
			Assert.AreEqual("MAX", SimulationSpeed.FormatShared(back));
		}

		[Test]
		public void TheProjectFileRemembersWhetherTheSpeedIsItsOwn()
		{
			foreach (bool? own in new bool?[] { true, false, null })
			{
				ProjectDescription project = Project(8000, 1, own);
				project.StarredList = new();
				project.ChipCollections = new();
				ProjectDescription back = Serializer.DeserializeProjectDescription(Serializer.SerializeProjectDescription(project));
				Assert.AreEqual(own, back.Prefs_SimOwnClockSpeed);
			}
		}
}
}
