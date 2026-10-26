using System;
using UnityEngine;

namespace MikanXR.Unity
{
	// Announces the editor's current scene, by id and name, each time it changes and once when the
	// client connects and learns it, so a host can react per scene: load an avatar position, swap
	// props, fire its own graph events.
	//
	// The scene id arrives before the scene's components do (the data store lists each system
	// separately), so an announcement waits for the scene's values for a bounded time and goes out
	// without a name if they never come.
	public class MikanSceneAnnouncer : MonoBehaviour
	{
		private const int k_invalidId = (int)MikanConstants.InvalidMikanID;
		// How long an announcement waits for the scene's values before going out without a name
		private const float k_pendingTimeoutSeconds = 5f;

		private MikanClient _client;
		private bool _announcePending;
		private float _announceDeadline;

		// The scene id and name, once the scene's values have arrived or the wait ran out
		public event Action<int, string> SceneAnnounced;

		public int CurrentSceneId { get; private set; } = k_invalidId;
		public string CurrentSceneName { get; private set; } = "";

		public void Bind(MikanClient client)
		{
			_client = client;
			_client.Disconnected += HandleDisconnected;
			_client.DataStore.CurrentSceneChanged += HandleCurrentSceneChanged;
		}

		private void OnDestroy()
		{
			if (_client == null)
			{
				return;
			}

			_client.Disconnected -= HandleDisconnected;
			if (_client.DataStore != null)
			{
				_client.DataStore.CurrentSceneChanged -= HandleCurrentSceneChanged;
			}
		}

		private void HandleDisconnected()
		{
			_announcePending = false;
		}

		private void HandleCurrentSceneChanged(int sceneId)
		{
			_announcePending = true;
			_announceDeadline = Time.unscaledTime + k_pendingTimeoutSeconds;
		}

		private void Update()
		{
			if (!_announcePending || _client == null || !_client.IsClientInitialized)
			{
				return;
			}

			int sceneId = _client.DataStore.CurrentSceneId;
			string sceneName = "";

			if (sceneId != k_invalidId)
			{
				MikanSceneComponentValues sceneValues =
					_client.DataStore.GetValues<MikanSceneComponentValues>(MikanSystemNames.SceneSystem, sceneId);
				if (sceneValues == null && Time.unscaledTime < _announceDeadline)
				{
					return;
				}

				sceneName = sceneValues?.component_name ?? "";
			}

			_announcePending = false;
			CurrentSceneId = sceneId;
			CurrentSceneName = sceneName;

			SceneAnnounced?.Invoke(sceneId, sceneName);
		}
	}
}
