using System.IO;
using UnityEditor;
using UnityEngine;

namespace MikanXR.Unity.Editor
{
	// Builds the test scene as a standalone Windows player, which runs the client the way a host
	// application does: a real frame loop with end-of-frame callbacks, and KlakSpout's native
	// plugin loaded from the player's Plugins folder.
	public static class MikanTestPlayerBuild
	{
		private const string k_testScenePath = "Assets/Scenes/MikanTestScene.unity";
		private const string k_defaultOutputDir = "Build/MikanTestPlayer";

		[MenuItem("MikanXR/Build Test Player")]
		public static void BuildFromMenu()
		{
			Build(k_defaultOutputDir);
		}

		// Batch entry point. The output folder may be passed after -outputDir on the command line.
		public static void BuildTestPlayer()
		{
			string outputDir = k_defaultOutputDir;
			string[] args = System.Environment.GetCommandLineArgs();
			for (int index = 0; index + 1 < args.Length; ++index)
			{
				if (args[index] == "-outputDir")
				{
					outputDir = args[index + 1];
				}
			}

			Build(outputDir);
		}

		private static void Build(string outputDir)
		{
			Directory.CreateDirectory(outputDir);

			var options = new BuildPlayerOptions()
			{
				scenes = new string[] { k_testScenePath },
				locationPathName = Path.Combine(outputDir, "MikanTestPlayer.exe"),
				target = BuildTarget.StandaloneWindows64,
				options = BuildOptions.None
			};

			var report = BuildPipeline.BuildPlayer(options);
			Debug.Log($"[MikanXR] test player build: {report.summary.result}, {report.summary.totalErrors} errors, {options.locationPathName}");

			if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
			{
				EditorApplication.Exit(1);
			}
		}
	}
}
