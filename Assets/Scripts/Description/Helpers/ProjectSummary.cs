using System;
using System.Globalization;

namespace DLS.Description
{
	// One line about a saved project for the project lists: how many chips it has and when it
	// was last saved.
	public static class ProjectSummary
	{
		public static string Describe(ProjectDescription project, DateTime now)
		{
			int chips = project.AllCustomChipNames?.Length ?? 0;
			string chipText = chips == 1 ? "1 chip" : chips + " chips";
			string saved = DescribeSaveTime(project.LastSaveTime, now);
			return saved.Length == 0 ? chipText : chipText + "  \u2022  " + saved;
		}

		public static string DescribeSaveTime(DateTime saved, DateTime now)
		{
			if (saved == default || saved > now.AddMinutes(1)) return string.Empty;

			CultureInfo culture = CultureInfo.InvariantCulture;
			if (saved.Date == now.Date) return "saved today " + saved.ToString("HH:mm", culture);
			if (saved.Date == now.Date.AddDays(-1)) return "saved yesterday";
			return "saved " + saved.ToString(saved.Year == now.Year ? "d MMM" : "d MMM yyyy", culture);
		}
	}
}
