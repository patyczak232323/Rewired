using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.RHDL;
using NUnit.Framework;

namespace DLS.Tests
{
	// Compiles a list of RHDL sources in order into a fresh library, like the example tests do,
	// but only once per list: later calls get new copies of the same chips, read back from
	// their saved JSON, so no test shares an object with another. Compiling is deterministic
	// (same sources, same empty library, same chips), so the copies are exactly what compiling
	// again would give. Saves most of the compile time of the example tests.
	public static class CompiledSources
	{
		static readonly Dictionary<string, string[]> compiled = new();

		public static (ChipLibrary library, List<ChipDescription> chips) Build(IEnumerable<string> paths)
		{
			string[] files = paths.ToArray();
			string key = string.Join("\n", files);
			ChipLibrary library = TestLibrary.Create();
			List<ChipDescription> chips = new();

			if (compiled.TryGetValue(key, out string[] json))
			{
				foreach (string chipJson in json)
				{
					ChipDescription chip = Serializer.DeserializeChipDescription(chipJson);
					library.NotifyChipSaved(chip, false);
					chips.Add(chip);
				}

				return (library, chips);
			}

			foreach (string file in files)
			{
				RhdlCompileResult result = RhdlCompiler.Compile(File.ReadAllText(file), library);
				Assert.IsTrue(result.Success, $"{Path.GetFileName(file)} failed to compile:\n" + string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
				library.NotifyChipSaved(result.Description, false);
				chips.Add(result.Description);
			}

			compiled[key] = chips.Select(Serializer.SerializeChipDescription).ToArray();
			// This run's chips are returned too, so the first caller sees the same as the rest.
			return Build(files);
		}
	}
}
