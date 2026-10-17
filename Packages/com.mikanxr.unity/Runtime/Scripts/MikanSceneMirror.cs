using System.Collections.Generic;
using UnityEngine;

namespace MikanXR.Unity
{
	// Turns the data store's components into GameObjects under the client root and keeps the
	// hierarchy following the editor's parent ids. Component ids are unique across systems, so
	// one registry serves every kind.
	public class MikanSceneMirror
	{
		private readonly MikanClient _client;
		private readonly Dictionary<int, MikanTransformObject> _objectsById = new Dictionary<int, MikanTransformObject>();

		public MikanSceneMirror(MikanClient client)
		{
			_client = client;

			MikanDataStore dataStore = client.DataStore;
			dataStore.ComponentAdded += HandleComponentAdded;
			dataStore.ComponentRemoved += HandleComponentRemoved;
			dataStore.ComponentChanged += HandleComponentChanged;
			dataStore.CurrentSceneChanged += HandleCurrentSceneChanged;
		}

		public IReadOnlyDictionary<int, MikanTransformObject> Objects => _objectsById;

		public MikanTransformObject Find(int componentId)
		{
			_objectsById.TryGetValue(componentId, out MikanTransformObject mikanObject);
			return mikanObject;
		}

		public IEnumerable<T> FindAll<T>() where T : MikanTransformObject
		{
			foreach (MikanTransformObject mikanObject in _objectsById.Values)
			{
				if (mikanObject is T typed)
				{
					yield return typed;
				}
			}
		}

		public void RouteCameraEvent(int cameraId, MikanEvent mikanEvent)
		{
			if (_objectsById.TryGetValue(cameraId, out MikanTransformObject mikanObject) &&
				mikanObject is MikanCameraObject cameraObject)
			{
				cameraObject.HandleCameraEvent(mikanEvent);
			}
		}

		public void Clear()
		{
			var objects = new List<MikanTransformObject>(_objectsById.Values);
			_objectsById.Clear();

			foreach (MikanTransformObject mikanObject in objects)
			{
				if (mikanObject != null)
				{
					mikanObject.Unbind();
					Object.Destroy(mikanObject.gameObject);
				}
			}
		}

		private void HandleComponentAdded(string systemName, int componentId, MikanComponentValues values)
		{
			if (_objectsById.ContainsKey(componentId))
			{
				return;
			}

			MikanTransformObject mikanObject = CreateObject(systemName, values);
			if (mikanObject == null)
			{
				return;
			}

			_objectsById[componentId] = mikanObject;
			mikanObject.transform.SetParent(_client.WorldRoot, false);
			mikanObject.Bind(_client, systemName, componentId, values);
			ResolveParent(mikanObject);

			// Children that arrived before this parent are waiting under the root
			foreach (MikanTransformObject other in _objectsById.Values)
			{
				if (other != mikanObject && other.DesiredParentId == componentId)
				{
					ResolveParent(other);
				}
			}

			if (mikanObject is MikanSceneObject sceneObject)
			{
				sceneObject.UpdateVisibility(_client.DataStore.CurrentSceneId);
			}
		}

		private void HandleComponentRemoved(string systemName, int componentId)
		{
			if (!_objectsById.TryGetValue(componentId, out MikanTransformObject mikanObject))
			{
				return;
			}

			_objectsById.Remove(componentId);

			if (mikanObject == null)
			{
				return;
			}

			// Destroying a parent destroys its children in Unity, and the children's components
			// still exist in the editor, so they go back under the root first
			var children = new List<Transform>();
			foreach (Transform child in mikanObject.transform)
			{
				if (child.GetComponent<MikanTransformObject>() != null)
				{
					children.Add(child);
				}
			}

			foreach (Transform child in children)
			{
				child.SetParent(_client.WorldRoot, false);
			}

			mikanObject.Unbind();
			Object.Destroy(mikanObject.gameObject);
		}

		private void HandleComponentChanged(string systemName, int componentId, string fieldName, MikanComponentValues values)
		{
			if (!_objectsById.TryGetValue(componentId, out MikanTransformObject mikanObject) || mikanObject == null)
			{
				return;
			}

			mikanObject.OnValuesChanged(fieldName);

			if (mikanObject.IsParentField(fieldName))
			{
				ResolveParent(mikanObject);
			}

			if (mikanObject is MikanSceneObject sceneObject && fieldName == "force_render")
			{
				sceneObject.UpdateVisibility(_client.DataStore.CurrentSceneId);
			}
		}

		private void HandleCurrentSceneChanged(int currentSceneId)
		{
			foreach (MikanSceneObject sceneObject in FindAll<MikanSceneObject>())
			{
				sceneObject.UpdateVisibility(currentSceneId);
			}
		}

		private void ResolveParent(MikanTransformObject mikanObject)
		{
			int parentId = mikanObject.DesiredParentId;
			Transform parent = _client.WorldRoot;

			if (parentId != (int)MikanConstants.InvalidMikanID &&
				_objectsById.TryGetValue(parentId, out MikanTransformObject parentObject) &&
				parentObject != null && parentObject != mikanObject)
			{
				parent = parentObject.transform;
			}

			if (mikanObject.transform.parent != parent)
			{
				mikanObject.transform.SetParent(parent, false);
				mikanObject.ApplyTransform();
			}
		}

		private MikanTransformObject CreateObject(string systemName, MikanComponentValues values)
		{
			string objectName = string.IsNullOrEmpty(values.component_name)
				? $"{systemName} {values.component_id}"
				: values.component_name;
			var gameObject = new GameObject(objectName);

			switch (systemName)
			{
			case MikanSystemNames.SceneSystem:
				return gameObject.AddComponent<MikanSceneObject>();
			case MikanSystemNames.StageSystem:
				return gameObject.AddComponent<MikanStageObject>();
			case MikanSystemNames.AnchorSystem:
				return gameObject.AddComponent<MikanAnchorObject>();
			case MikanSystemNames.QuadStencilSystem:
				return gameObject.AddComponent<MikanQuadStencilObject>();
			case MikanSystemNames.BoxStencilSystem:
				return gameObject.AddComponent<MikanBoxStencilObject>();
			case MikanSystemNames.ModelStencilSystem:
				return gameObject.AddComponent<MikanModelStencilObject>();
			case MikanSystemNames.CameraSystem:
				return gameObject.AddComponent<MikanCameraObject>();
			case MikanSystemNames.LightEnvironmentSystem:
				return gameObject.AddComponent<MikanLightEnvironmentObject>();
			case MikanSystemNames.RGBSpotLightSystem:
				return gameObject.AddComponent<MikanRGBSpotLightObject>();
			default:
				Object.Destroy(gameObject);
				return null;
			}
		}
	}
}
