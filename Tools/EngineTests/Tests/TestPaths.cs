using System.IO;
using UnityEngine;

namespace DLS.Tests
{
	// Where the tests find their input files. The RHDL sources the example projects were
	// built from live next to the tests, outside the Unity project, so Unity never imports them.
	public static class TestPaths
	{
		public static string Examples => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "EngineTests", "Fixtures", "RHDL"));
	}
}
