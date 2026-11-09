using System;
using System.Globalization;

namespace DLS.Description
{
	// The simulation speed as people set it: one clock speed in Hz, or MAX for no limit.
	//
	// Projects store the speed the way Digital Logic Sim does, as target steps per second plus
	// steps per clock tick (the clock changes level every that many steps, so one full clock
	// cycle takes twice that). Every step settles the whole circuit, so the extra steps
	// between clock edges do no work, and the clock speed is what decides how fast a circuit
	// runs. Steps per clock tick is kept as it is: PULSE chips count their length in steps.
	//
	// MAX is stored as the largest step rate, so older versions open such a project as
	// "as fast as possible" too.
	public static class SimulationSpeed
	{
		public const int Unlimited = int.MaxValue;
		public const string UnlimitedText = "MAX";

		public static bool IsUnlimited(int targetStepsPerSecond) => targetStepsPerSecond == Unlimited;

		// Full clock cycles per second for a step rate.
		public static double ClockHz(double stepsPerSecond, int stepsPerClockTick) =>
			stepsPerSecond / (2.0 * Math.Max(1, stepsPerClockTick));

		public static string FormatTarget(int targetStepsPerSecond, int stepsPerClockTick) =>
			IsUnlimited(targetStepsPerSecond) ? UnlimitedText : FormatHz(ClockHz(Math.Max(1, targetStepsPerSecond), stepsPerClockTick));

		// Whole numbers from 100 Hz up, otherwise up to two decimals ("2", "0.5", "12.5").
		public static string FormatHz(double hz)
		{
			if (double.IsNaN(hz) || hz <= 0) return "0";
			string format = hz >= 100 ? "0" : hz >= 10 ? "0.#" : "0.##";
			return hz.ToString(format, CultureInfo.InvariantCulture);
		}

		// Accepts what can still become a valid entry while it is typed: a number in Hz (with
		// a dot or a comma) or MAX, in any case.
		public static bool IsValidPartialInput(string text)
		{
			if (string.IsNullOrEmpty(text)) return true;
			if (text.Length > 12) return false;
			if (UnlimitedText.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;

			bool separator = false;
			foreach (char c in text)
			{
				if (c is >= '0' and <= '9') continue;
				if ((c == '.' || c == ',') && !separator)
				{
					separator = true;
					continue;
				}

				return false;
			}

			return true;
		}

		// The target step rate for an entry, or false when the entry is incomplete or zero.
		public static bool TryParse(string text, int stepsPerClockTick, out int targetStepsPerSecond)
		{
			targetStepsPerSecond = 0;
			if (!TryParseHz(text, out double hz, out bool max)) return false;
			targetStepsPerSecond = TargetFor(hz, max, stepsPerClockTick);
			return true;
		}

		// An entry as a clock speed in Hz, or MAX.
		public static bool TryParseHz(string text, out double hz, out bool max)
		{
			hz = 0;
			max = false;
			if (string.IsNullOrWhiteSpace(text)) return false;
			text = text.Trim();

			if (string.Equals(text, UnlimitedText, StringComparison.OrdinalIgnoreCase))
			{
				max = true;
				return true;
			}

			if (!double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out hz)) return false;
			return !double.IsNaN(hz) && !double.IsInfinity(hz) && hz > 0;
		}

		// The target step rate for a clock speed.
		public static int TargetFor(double hz, bool max, int stepsPerClockTick)
		{
			if (max) return Unlimited;
			double steps = Math.Round(hz * 2.0 * Math.Max(1, stepsPerClockTick));
			return (int)Math.Clamp(steps, 1, Unlimited - 1);
		}

		// ---- One speed for all projects ----
		//
		// The app settings hold one clock speed that every project runs at, unless the project
		// has a speed of its own (the bundled examples do: Snake is a game and needs 4000 Hz).
		// The project file still stores the step rate it runs at, so older versions open it at
		// the same speed.

		public const double DefaultSharedHz = 2; // what a new project used to start at

		public static double SharedHz(AppSettings settings) => settings.SimClockHz > 0 ? settings.SimClockHz : DefaultSharedHz;

		public static string FormatShared(AppSettings settings) => settings.SimClockMax ? UnlimitedText : FormatHz(SharedHz(settings));

		public static int SharedTarget(AppSettings settings, int stepsPerClockTick) =>
			TargetFor(SharedHz(settings), settings.SimClockMax, stepsPerClockTick);

		// Makes the project run at the shared speed unless it has its own. A project saved
		// before there was a shared speed keeps its own when it differs from the shared one,
		// so updating never changes how fast a project runs.
		public static ProjectDescription ApplyShared(ProjectDescription project, AppSettings settings)
		{
			int shared = SharedTarget(settings, project.Prefs_SimStepsPerClockTick);
			project.Prefs_SimOwnClockSpeed ??= project.Prefs_SimTargetStepsPerSecond != shared;
			if (project.Prefs_SimOwnClockSpeed == false) project.Prefs_SimTargetStepsPerSecond = shared;
			return project;
		}

		public static AppSettings WithShared(AppSettings settings, double hz, bool max)
		{
			settings.SimClockMax = max;
			if (!max) settings.SimClockHz = hz;
			return settings;
		}
	}
}
