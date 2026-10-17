using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MikanXR.Unity
{
	[Serializable]
	public class MikanStagePlacement
	{
		public string stageName = "";
		public Vector3 position = Vector3.zero;
		public Vector3 eulerAngles = Vector3.zero;
		public Vector3 scale = Vector3.one;
	}

	// Everything the client persists between runs. The host decides where it lives through
	// IMikanSettingsProvider; the Unity project keeps these on the MikanClient component and a
	// host such as VNyan supplies its own store.
	[Serializable]
	public class MikanSettings
	{
		public string host = "";
		public string port = "";

		// Unity units per Mikan meter. The editor's scenes are metric and tabletop sized; a host
		// whose world is built to a different unit, or whose avatar is meant to tower over a desk,
		// scales the whole mirrored scene by this.
		public float worldScale = 1f;

		// Layers are indices rather than names, since a player build cannot add a layer
		public int stencilLayer = 30;
		public int visualizationLayer = 31;

		public bool showCameraFrustums = true;
		public bool renderStencils = true;
		// The shadow buffer needs stencils to catch on, so it is only published while both are on
		public bool renderShadows = true;
		// The packed depth buffer is off by default: the stencil knockout already cuts the character
		// out behind real geometry, so depth is only for a compositor that keys on it
		public bool renderDepth = false;
		public float shadowStrength = 0.7f;

		// The root's own placement in the host scene, for a host that creates the root itself
		public bool applyRootTransform = false;
		public Vector3 rootPosition = Vector3.zero;
		public Vector3 rootEulerAngles = Vector3.zero;
		public Vector3 rootScale = Vector3.one;

		// Stages the user has placed in the host scene, which then ignore the editor's transform
		public List<MikanStagePlacement> stagePlacements = new List<MikanStagePlacement>();

		public MikanStagePlacement FindStagePlacement(string stageName)
		{
			foreach (MikanStagePlacement placement in stagePlacements)
			{
				if (placement.stageName == stageName)
				{
					return placement;
				}
			}

			return null;
		}

		public MikanStagePlacement SetStagePlacement(string stageName, Vector3 position, Vector3 eulerAngles, Vector3 scale)
		{
			MikanStagePlacement placement = FindStagePlacement(stageName);
			if (placement == null)
			{
				placement = new MikanStagePlacement() { stageName = stageName };
				stagePlacements.Add(placement);
			}

			placement.position = position;
			placement.eulerAngles = eulerAngles;
			placement.scale = scale;

			return placement;
		}

		public void ClearStagePlacement(string stageName)
		{
			stagePlacements.RemoveAll(placement => placement.stageName == stageName);
		}
	}

	public interface IMikanSettingsProvider
	{
		// Returns false when nothing was stored yet, leaving the settings as they were
		bool Load(MikanSettings settings);
		void Save(MikanSettings settings);
	}

	// JSON under the application's persistent data path, the default for a plain Unity project
	public class MikanJsonFileSettingsProvider : IMikanSettingsProvider
	{
		private readonly string _path;

		public MikanJsonFileSettingsProvider()
			: this(Path.Combine(Application.persistentDataPath, "MikanXR", "settings.json"))
		{
		}

		public MikanJsonFileSettingsProvider(string path)
		{
			_path = path;
		}

		public string FilePath => _path;

		public bool Load(MikanSettings settings)
		{
			if (!File.Exists(_path))
			{
				return false;
			}

			JsonUtility.FromJsonOverwrite(File.ReadAllText(_path), settings);
			return true;
		}

		public void Save(MikanSettings settings)
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
			File.WriteAllText(_path, JsonUtility.ToJson(settings, true));
		}
	}
}
