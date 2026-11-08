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
	}
}
