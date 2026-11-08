using DLS.Description;
using NUnit.Framework;

namespace DLS.Tests
{
	public class SearchRankingTests
	{
		[Test]
		public void EmptyQueryMatchesEverything()
		{
			Assert.Greater(SearchRanking.Score("NAND", ""), SearchRanking.NoMatch);
			Assert.Greater(SearchRanking.Score("NAND", "   "), SearchRanking.NoMatch);
		}

		[Test]
		public void BetterMatchesScoreHigher()
		{
			int exact = SearchRanking.Score("AND", "and");
			int prefix = SearchRanking.Score("ANDGATE", "and");
			int word = SearchRanking.Score("FULL AND", "and");
			int inside = SearchRanking.Score("NAND", "and");
			int scattered = SearchRanking.Score("ADDER NODE", "and");

			Assert.Greater(exact, prefix);
			Assert.Greater(prefix, word);
			Assert.Greater(word, inside);
			Assert.Greater(inside, scattered);
			Assert.Greater(scattered, SearchRanking.NoMatch);
		}

		[Test]
		public void SpacesAndDashesAreIgnored()
		{
			Assert.Greater(SearchRanking.Score("1-4BIT", "14bit"), SearchRanking.NoMatch);
			Assert.Greater(SearchRanking.Score("Save chip", "savechip"), SearchRanking.NoMatch);
		}

		[Test]
		public void WordStartsCountInCamelCase()
		{
			Assert.Greater(SearchRanking.Score("RegisterFile", "file"), SearchRanking.Score("Profile", "file"));
		}

		[Test]
		public void UnrelatedTextDoesNotMatch()
		{
			Assert.AreEqual(SearchRanking.NoMatch, SearchRanking.Score("CLOCK", "ram"));
			Assert.AreEqual(SearchRanking.NoMatch, SearchRanking.Score("", "ram"));
		}
	}
}
