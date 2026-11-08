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
			if (string.IsNullOrWhiteSpace(text)) return false;
			text = text.Trim();

			if (string.Equals(text, UnlimitedText, StringComparison.OrdinalIgnoreCase))
			{
				targetStepsPerSecond = Unlimited;
				return true;
			}

			if (!double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double hz)) return false;
			if (double.IsNaN(hz) || double.IsInfinity(hz) || hz <= 0) return false;

			double steps = Math.Round(hz * 2.0 * Math.Max(1, stepsPerClockTick));
			targetStepsPerSecond = (int)Math.Clamp(steps, 1, Unlimited - 1);
			return true;
		}
	}
}
