using System.Collections.Generic;
using UnityEngine;

namespace MikanXR.Unity
{
	// Where the client's shaders come from. The package keeps them in a Resources folder, which a
	// player build of a project that includes the package carries whether or not a scene references
	// them. In a host that loads the client as a plugin they arrive in an AssetBundle the host hands
	// over, since that host's build never saw them.
	public static class MikanShaderLibrary
	{
		public const string PackDepthRGBA = "MikanXR/PackDepthRGBA";
		public const string StencilHoldout = "MikanXR/StencilHoldout";
		public const string StencilPreview = "MikanXR/StencilPreview";
		public const string StencilPreviewTextured = "MikanXR/StencilPreviewTextured";
		public const string UnlitColor = "MikanXR/UnlitColor";
		public const string ShadowCatcher = "MikanXR/ShadowCatcher";
		public const string ShadowDivide = "MikanXR/ShadowDivide";
		public const string SrgbRawBytes = "MikanXR/SrgbRawBytes";

		private const string k_resourcesFolder = "MikanXR/Shaders/";

		private static AssetBundle s_bundle;
		private static readonly Dictionary<string, Shader> s_bundleShaders = new Dictionary<string, Shader>();

		public static void SetBundle(AssetBundle bundle)
		{
			s_bundle = bundle;
			s_bundleShaders.Clear();

			if (bundle != null)
			{
				foreach (Shader shader in bundle.LoadAllAssets<Shader>())
				{
					s_bundleShaders[shader.name] = shader;
				}
			}
		}

		public static AssetBundle Bundle => s_bundle;

		// A non-shader asset of the package: from the bundle when one is loaded, else Resources
		public static T LoadAsset<T>(string resourcesPath) where T : Object
		{
			if (s_bundle != null)
			{
				string assetName = System.IO.Path.GetFileName(resourcesPath);
				T fromBundle = s_bundle.LoadAsset<T>(assetName);
				if (fromBundle != null)
				{
					return fromBundle;
				}
			}

			return Resources.Load<T>(resourcesPath);
		}

		public static Shader Find(string shaderName)
		{
			if (s_bundleShaders.TryGetValue(shaderName, out Shader bundled) && bundled != null)
			{
				return bundled;
			}

			Shader shader = Shader.Find(shaderName);
			if (shader != null)
			{
				return shader;
			}

			// The package's own shaders by their asset name, "MikanXR/Foo" being Resources/MikanXR/Shaders/MikanFoo
			if (shaderName.StartsWith("MikanXR/"))
			{
				shader = Resources.Load<Shader>(k_resourcesFolder + "Mikan" + shaderName.Substring("MikanXR/".Length));
			}

			return shader;
		}

		// Null, with one error per shader name, when nothing resolves it
		public static Material CreateMaterial(string shaderName)
		{
			Shader shader = Find(shaderName);
			if (shader == null)
			{
				if (s_reportedMissing.Add(shaderName))
				{
					Debug.LogError($"[MikanXR] Shader not found: {shaderName}");
				}
				return null;
			}

			return new Material(shader) { hideFlags = HideFlags.DontSave };
		}

		private static readonly HashSet<string> s_reportedMissing = new HashSet<string>();
	}
}
