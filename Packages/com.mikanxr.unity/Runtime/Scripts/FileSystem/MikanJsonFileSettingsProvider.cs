using System.IO;
using UnityEngine;

namespace MikanXR.Unity
{
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
