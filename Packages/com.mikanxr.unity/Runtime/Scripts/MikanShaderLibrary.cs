using System.Collections.Generic;
using UnityEngine;

namespace MikanXR.Unity
{
	// Where a host keeps the package's shaders and assets when they are not in the build's own
	// Resources. Paths are the package's Resources-relative ones ("MikanXR/MikanSpoutResources"),
	// so each host maps them to its own storage: an AssetBundle, a mod's asset loader.
	public interface IMikanAssetSource
	{
		// The shader with this shader name ("MikanXR/PackDepthRGBA"), or null
		Shader FindShader(string shaderName);

		// The asset at this Resources-relative path, or null
		T LoadAsset<T>(string resourcesPath) where T : Object;
	}

	// Shaders and assets from an AssetBundle, found by shader name and by asset file name
	public class MikanBundleAssetSource : IMikanAssetSource
	{
		private readonly AssetBundle _bundle;
		private readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();

		public MikanBundleAssetSource(AssetBundle bundle)
		{
			_bundle = bundle;

			foreach (Shader shader in bundle.LoadAllAssets<Shader>())
			{
				_shaders[shader.name] = shader;
			}
		}

		public AssetBundle Bundle => _bundle;

		public Shader FindShader(string shaderName)
		{
			return _shaders.TryGetValue(shaderName, out Shader shader) ? shader : null;
		}

		public T LoadAsset<T>(string resourcesPath) where T : Object
		{
			string assetName = resourcesPath.Substring(resourcesPath.LastIndexOf('/') + 1);
			return _bundle.LoadAsset<T>(assetName);
		}
	}

	// Where the client's shaders come from. The package keeps them in a Resources folder, which a
	// player build of a project that includes the package carries whether or not a scene references
	// them. A host that loads the client as a plugin never built them, so it installs a source that
	// reaches them: an AssetBundle through SetBundle, or its own source through SetAssetSource.
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

		private static IMikanAssetSource s_assetSource;

		public static void SetAssetSource(IMikanAssetSource assetSource)
		{
			s_assetSource = assetSource;
		}

		public static IMikanAssetSource AssetSource => s_assetSource;

		public static void SetBundle(AssetBundle bundle)
		{
			SetAssetSource(bundle != null ? new MikanBundleAssetSource(bundle) : null);
		}

		public static AssetBundle Bundle => (s_assetSource as MikanBundleAssetSource)?.Bundle;

		// A non-shader asset of the package: from the host's source when it has one, else Resources
		public static T LoadAsset<T>(string resourcesPath) where T : Object
		{
			if (s_assetSource != null)
			{
				T fromSource = s_assetSource.LoadAsset<T>(resourcesPath);
				if (fromSource != null)
				{
					return fromSource;
				}
			}

			return Resources.Load<T>(resourcesPath);
		}

		public static Shader Find(string shaderName)
		{
			Shader shader = s_assetSource?.FindShader(shaderName);
			if (shader != null)
			{
				return shader;
			}

			shader = Shader.Find(shaderName);
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
