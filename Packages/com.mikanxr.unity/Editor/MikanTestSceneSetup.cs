using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MikanXR.Unity.Editor
{
	public static class MikanTestSceneSetup
	{
		private const string k_testScenePath = "Assets/Scenes/MikanTestScene.unity";
		private const string k_rootName = "MikanClient";

		[MenuItem("MikanXR/Add Client To Open Scene")]
		public static void AddClientToOpenScene()
		{
			AddClient(SceneManager.GetActiveScene());
		}

		// Batch entry point: opens the test scene, adds the client root if missing, saves
		public static void SetupTestScene()
		{
			Scene scene = EditorSceneManager.OpenScene(k_testScenePath, OpenSceneMode.Single);
			if (AddClient(scene))
			{
				EditorSceneManager.SaveScene(scene);
			}
		}

		private static bool AddClient(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.GetComponent<MikanClient>() != null)
				{
					Debug.Log($"[MikanXR] {scene.name} already has a MikanClient on {root.name}");
					return false;
				}
			}

			var client = new GameObject(k_rootName);
			client.AddComponent<MikanClient>();
			SceneManager.MoveGameObjectToScene(client, scene);
			Undo.RegisterCreatedObjectUndo(client, "Add MikanClient");
			EditorSceneManager.MarkSceneDirty(scene);

			Debug.Log($"[MikanXR] added a MikanClient root to {scene.name}");
			return true;
		}
	}
}
