using UnityEngine;

namespace MikanXR.Unity
{
	// A mirrored transform component: scenes, stages, anchors, stencils, cameras and lights. The editor
	// sends every transform relative to its parent, so the object applies its relative values to
	// its local transform and the scene mirror parents it to match.
	public class MikanTransformObject : MonoBehaviour
	{
		private MikanClient _client;
		private string _systemName;
		private int _componentId = (int)MikanConstants.InvalidMikanID;
		private MikanTransformComponentValues _transformValues;

		public MikanClient Client => _client;
		public string SystemName => _systemName;
		public int ComponentId => _componentId;
		public MikanTransformComponentValues TransformValues => _transformValues;
		public bool IsBound => _client != null;

		// The component this object sits under in the editor's hierarchy
		public virtual int DesiredParentId => _transformValues != null
			? _transformValues.parent_transform_id
			: (int)MikanConstants.InvalidMikanID;

		public virtual bool IsParentField(string fieldName)
		{
			return fieldName == "parent_transform_id";
		}

		public virtual void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			_client = client;
			_systemName = systemName;
			_componentId = componentId;
			_transformValues = values as MikanTransformComponentValues;

			ApplyTransform();
		}

		public virtual void Unbind()
		{
			_client = null;
		}

		public virtual void OnValuesChanged(string fieldName)
		{
			switch (fieldName)
			{
			case "relative_position":
			case "relative_quaternion":
			case "relative_scale":
				ApplyTransform();
				break;
			case "component_name":
				if (_transformValues != null && !string.IsNullOrEmpty(_transformValues.component_name))
				{
					gameObject.name = _transformValues.component_name;
				}
				break;
			}
		}

		// Helpers only the host should see (frustums, light cones) leave the scene while a Mikan
		// camera renders, and come back after
		public virtual void HideHelpersForRender()
		{
		}

		public virtual void RestoreHelpers()
		{
		}

		public virtual void ApplyTransform()
		{
			if (_transformValues == null)
			{
				return;
			}

			if (_transformValues.relative_position != null)
			{
				transform.localPosition = MikanMath.ToUnityPosition(_transformValues.relative_position);
			}

			if (_transformValues.relative_quaternion != null)
			{
				transform.localRotation = MikanMath.ToUnityRotation(_transformValues.relative_quaternion);
			}

			if (_transformValues.relative_scale != null)
			{
				transform.localScale = MikanMath.ToUnityScale(_transformValues.relative_scale);
			}
		}
	}

	// A scene: only the editor's current scene is active, plus any scene flagged to render anyway
	public class MikanSceneObject : MikanTransformObject
	{
		public MikanSceneComponentValues SceneValues => TransformValues as MikanSceneComponentValues;

		public void UpdateVisibility(int currentSceneId)
		{
			bool visible = ComponentId == currentSceneId || (SceneValues != null && SceneValues.force_render);
			if (gameObject.activeSelf != visible)
			{
				gameObject.SetActive(visible);
			}
		}
	}

	// A stage: where a tracking space lands in the host scene. The user may pin it anywhere,
	// in which case the editor's own transform for it is ignored.
	public class MikanStageObject : MikanTransformObject
	{
		private MikanStagePlacement _placement;

		public MikanStageComponentValues StageValues => TransformValues as MikanStageComponentValues;
		public bool IsPinned => _placement != null;
		public string StageName => TransformValues != null ? TransformValues.component_name : "";

		public override void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			_placement = client.Settings.FindStagePlacement((values as MikanComponentValues)?.component_name ?? "");

			base.Bind(client, systemName, componentId, values);
		}

		public override void ApplyTransform()
		{
			if (_placement != null)
			{
				transform.localPosition = _placement.position;
				transform.localEulerAngles = _placement.eulerAngles;
				transform.localScale = _placement.scale;
				return;
			}

			base.ApplyTransform();
		}

		// Records the current local transform as the user's placement and saves it
		public void PinHere()
		{
			if (!IsBound)
			{
				return;
			}

			_placement = Client.Settings.SetStagePlacement(
				StageName, transform.localPosition, transform.localEulerAngles, transform.localScale);
			Client.SaveSettings();
		}

		public void Unpin()
		{
			if (!IsBound)
			{
				return;
			}

			Client.Settings.ClearStagePlacement(StageName);
			Client.SaveSettings();
			_placement = null;
			ApplyTransform();
		}
	}

	public class MikanAnchorObject : MikanTransformObject
	{
		public MikanAnchorComponentValues AnchorValues => TransformValues as MikanAnchorComponentValues;
	}
}
