using System.IO;
using UnityEditor;
using UnityEngine;

namespace MikanXR.Unity.Editor
{
	// Builds the package's shaders and Spout resources into one AssetBundle, which is how a host
	// that loads the client as a plugin (VNyan) gets shaders its own build never saw. The bundle
	// is loaded through MikanShaderLibrary.SetBundle.
	public static class MikanShaderBundleBuild
	{
		public const string BundleName = "mikanxr_shaders.bundle";

		private const string k_resourcesFolder = "Packages/com.mikanxr.unity/Runtime/Resources/MikanXR";
		private const string k_defaultOutputDir = "Build/ShaderBundle";

		[MenuItem("MikanXR/Build Shader Bundle")]
		public static void BuildFromMenu()
		{
			Build(k_defaultOutputDir);
		}

		// Batch entry point. The output folder may be passed after -outputDir on the command line.
		public static void BuildShaderBundle()
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

			if (!Build(outputDir) && Application.isBatchMode)
			{
				EditorApplication.Exit(1);
			}
		}

		private static bool Build(string outputDir)
		{
			string[] guids = AssetDatabase.FindAssets("t:Shader t:ScriptableObject", new string[] { k_resourcesFolder });
			var assetPaths = new string[guids.Length];
			for (int index = 0; index < guids.Length; ++index)
			{
				assetPaths[index] = AssetDatabase.GUIDToAssetPath(guids[index]);
			}

			if (assetPaths.Length == 0)
			{
				Debug.LogError($"[MikanXR] no shader assets found under {k_resourcesFolder}");
				return false;
			}

			Directory.CreateDirectory(outputDir);

			var builds = new AssetBundleBuild[]
			{
				new AssetBundleBuild() { assetBundleName = BundleName, assetNames = assetPaths }
			};

			AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
				outputDir, builds,
				BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
				BuildTarget.StandaloneWindows64);

			if (manifest == null)
			{
				Debug.LogError("[MikanXR] shader bundle build failed");
				return false;
			}

			Debug.Log($"[MikanXR] shader bundle: {Path.Combine(outputDir, BundleName)} with {assetPaths.Length} assets");
			return true;
		}
	}
}
