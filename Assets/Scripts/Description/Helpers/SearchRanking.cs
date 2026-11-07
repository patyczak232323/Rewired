using System;

namespace DLS.Description
{
	// How well a name matches what was typed into a search box: the higher, the better. Zero means
	// it does not match. Case, spaces and dashes are ignored where it helps ("4bit" finds "4-BIT").
	public static class SearchRanking
	{
		public const int NoMatch = 0;

		public static int Score(string text, string query)
		{
			if (string.IsNullOrWhiteSpace(query)) return 1;
			if (string.IsNullOrEmpty(text)) return NoMatch;

			query = query.Trim();
			const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;

			if (text.Equals(query, ignoreCase)) return 1000;
			if (text.StartsWith(query, ignoreCase)) return 900 - Math.Min(text.Length, 99);
			if (StartsAWord(text, query)) return 800 - Math.Min(text.Length, 99);

			string lenientText = Lenient(text);
			string lenientQuery = Lenient(query);
			if (lenientQuery.Length == 0) return NoMatch;
			if (lenientText.StartsWith(lenientQuery, ignoreCase)) return 700 - Math.Min(text.Length, 99);
			if (text.IndexOf(query, ignoreCase) >= 0) return 600 - Math.Min(text.Length, 99);
			if (lenientText.IndexOf(lenientQuery, ignoreCase) >= 0) return 500 - Math.Min(text.Length, 99);
			if (IsSubsequence(lenientText, lenientQuery)) return 200 - Math.Min(text.Length, 99);
			return NoMatch;
		}

		// "ram" matches "Read RAM" at the start of its second word.
		static bool StartsAWord(string text, string query)
		{
			for (int i = 1; i < text.Length; i++)
			{
				bool wordStart = !char.IsLetterOrDigit(text[i - 1]) || (char.IsLower(text[i - 1]) && char.IsUpper(text[i]));
				if (wordStart && i + query.Length <= text.Length && string.Compare(text, i, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0) return true;
			}

			return false;
		}

		// Every letter of the query, in order, though not next to each other: "rgbd" in "RGB DISPLAY".
		static bool IsSubsequence(string text, string query)
		{
			int q = 0;
			for (int i = 0; i < text.Length && q < query.Length; i++)
			{
				if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(query[q])) q++;
			}

			return q == query.Length;
		}

		static string Lenient(string s) => s.Replace("-", string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty);
	}
}
