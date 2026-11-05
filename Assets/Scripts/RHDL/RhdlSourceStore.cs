using System;
using System.IO;
using DLS.Game;
using DLS.SaveSystem;

namespace DLS.RHDL
{
	public static class RhdlSourceStore
	{
		const string DraftFileName = "_draft.rhdl";

		public static string GetDirectory(string projectName) =>
			Path.Combine(SavePaths.GetProjectPath(projectName), "HDL");

		// Chip names come from shared JSON files: keep the source inside the project's HDL folder.
		public static string GetSourcePath(string projectName, string chipName) =>
			SavePaths.CombineContained(GetDirectory(projectName), chipName + ".rhdl");

		public static string LoadDraft(string projectName)
		{
			string path = Path.Combine(GetDirectory(projectName), DraftFileName);
			return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
		}

		public static void SaveDraft(string projectName, string source)
		{
			WriteAtomic(Path.Combine(GetDirectory(projectName), DraftFileName), source ?? string.Empty);
		}

		public static void SaveChipSource(string projectName, string chipName, string source)
		{
			if (string.IsNullOrWhiteSpace(chipName)) return;
			WriteAtomic(GetSourcePath(projectName, chipName), source ?? string.Empty);
		}

		public static string LoadChipSource(string projectName, string chipName)
		{
			string path = GetSourcePath(projectName, chipName);
			return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
		}

		public static bool HasChipSource(string projectName, string chipName)
		{
			try
			{
				return File.Exists(GetSourcePath(projectName, chipName));
			}
			catch (InvalidDataException)
			{
				return false;
			}
		}

		static void WriteAtomic(string path, string text)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string tmp = path + ".tmp";
			File.WriteAllText(tmp, text);
			// Replace swaps the files in one step, so a crash never leaves the source missing.
			if (File.Exists(path))
			{
				try
				{
					File.Replace(tmp, path, null);
					return;
				}
				catch (PlatformNotSupportedException)
				{
					File.Delete(path);
				}
			}

			File.Move(tmp, path);
		}
	}
}
