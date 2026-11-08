using System;
using DLS.Description;
using NUnit.Framework;

namespace DLS.Tests
{
	public class ProjectSummaryTests
	{
		static readonly DateTime Now = new(2026, 11, 5, 18, 30, 0);

		[Test]
		public void CountsChipsAndSaysWhenTheProjectWasSaved()
		{
			ProjectDescription project = new() { AllCustomChipNames = new[] { "AND", "OR", "XOR" }, LastSaveTime = new DateTime(2026, 11, 5, 9, 5, 0) };
			Assert.AreEqual("3 chips  \u2022  saved today 09:05", ProjectSummary.Describe(project, Now));
		}

		[Test]
		public void OneChipIsSingular()
		{
			ProjectDescription project = new() { AllCustomChipNames = new[] { "AND" } };
			Assert.AreEqual("1 chip", ProjectSummary.Describe(project, Now));
		}

		[Test]
		public void MissingChipListCountsAsNone()
		{
			Assert.AreEqual("0 chips", ProjectSummary.Describe(new ProjectDescription(), Now));
		}

		[Test]
		public void OlderSavesShowTheDate()
		{
			Assert.AreEqual("saved yesterday", ProjectSummary.DescribeSaveTime(new DateTime(2026, 11, 4, 23, 59, 0), Now));
			Assert.AreEqual("saved 31 Oct", ProjectSummary.DescribeSaveTime(new DateTime(2026, 10, 31, 12, 0, 0), Now));
			Assert.AreEqual("saved 3 Dec 2025", ProjectSummary.DescribeSaveTime(new DateTime(2025, 12, 3, 12, 0, 0), Now));
		}

		[Test]
		public void UnknownOrFutureSaveTimesAreLeftOut()
		{
			Assert.AreEqual(string.Empty, ProjectSummary.DescribeSaveTime(default, Now));
			Assert.AreEqual(string.Empty, ProjectSummary.DescribeSaveTime(Now.AddDays(2), Now));
		}
	}
}
