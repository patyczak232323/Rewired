using System.IO;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.SaveSystem;
using NUnit.Framework;

namespace DLS.Tests
{
	// Opening any chip of the example projects that come with Rewired must not show NOT SAVED:
	// the chip as the editor loads and describes it equals its saved description. Needs the
	// editor's chip and pin code, so it runs only in Unity (Tools/EngineHarness leaves it out).
	public class BundledProjectEditorTests
	{
		[TestCaseSource(nameof(ProjectNames))]
		public void ChipsOpenWithoutUnsavedChanges(string projectName)
		{
			string chipsFolder = Path.Combine(BundledProjectTests.FolderOf(projectName), "Chips");
			ChipDescription[] chips = Directory.GetFiles(chipsFolder, "*.json")
				.Select(path => Serializer.DeserializeChipDescription(File.ReadAllText(path)))
				.ToArray();
			ChipLibrary library = TestLibrary.Create(chips);

			foreach (ChipDescription chip in chips)
			{
				DevChipInstance devChip = DevChipInstance.LoadFromDescriptionTest(chip, library).devChip;
				if (!Project.HasUnsavedChanges(devChip, library)) continue;

				ChipDescription saved = DescriptionCreator.WithEditorSize(Saver.WithEditorPinColours(chip, library));
				Assert.Fail($"{projectName}/{chip.Name} opens with unsaved changes:\n" +
				            FirstDifference(Saver.CreateSerializedChipDescription(saved), Saver.CreateSerializedChipDescription(DescriptionCreator.CreateChipDescription(devChip))));
			}
		}

		static string[] ProjectNames() => BundledProjectInstaller.ProjectNames;

		// The first line that differs between two descriptions, with the lines before it.
		static string FirstDifference(string saved, string editor)
		{
			string[] a = saved.Split('\n');
			string[] b = editor.Split('\n');
			int n = System.Math.Min(a.Length, b.Length);
			int i = 0;
			while (i < n && a[i].Trim() == b[i].Trim()) i++;

			int from = System.Math.Max(0, i - 6);
			string context = string.Join("\n", a.Skip(from).Take(i - from));
			string savedLine = i < a.Length ? a[i] : "(end)";
			string editorLine = i < b.Length ? b[i] : "(end)";
			return $"{context}\nsaved:  {savedLine}\neditor: {editorLine}";
		}
	}
}
