using System;
using System.Collections.Generic;

namespace MikanXR.Unity
{
	// The client's copy of the editor's component values for the systems it mirrors. Filled by
	// listing each system and fetching every component's values, then kept current by the
	// property update events: a field update patches the cached values object by field name,
	// and a system's component id list update (the create and destroy notification) re-lists
	// that system.
	public class MikanDataStore
	{
		public class SystemEntry
		{
			public string SystemName;
			public string ComponentClass;
			// The class the system's component values arrive as, by its wire name
			public string ValuesClassName;
		}

		public static readonly SystemEntry[] MirroredSystems = new SystemEntry[]
		{
			new SystemEntry() { SystemName = MikanSystemNames.SceneSystem, ComponentClass = MikanSystemNames.SceneComponent, ValuesClassName = nameof(MikanSceneComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.StageSystem, ComponentClass = MikanSystemNames.StageComponent, ValuesClassName = nameof(MikanStageComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.AnchorSystem, ComponentClass = MikanSystemNames.AnchorComponent, ValuesClassName = nameof(MikanAnchorComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.QuadStencilSystem, ComponentClass = MikanSystemNames.QuadStencilComponent, ValuesClassName = nameof(MikanQuadStencilComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.BoxStencilSystem, ComponentClass = MikanSystemNames.BoxStencilComponent, ValuesClassName = nameof(MikanBoxStencilComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.ModelStencilSystem, ComponentClass = MikanSystemNames.ModelStencilComponent, ValuesClassName = nameof(MikanModelStencilComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.CameraSystem, ComponentClass = MikanSystemNames.CameraComponent, ValuesClassName = nameof(MikanCameraComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.LightEnvironmentSystem, ComponentClass = MikanSystemNames.LightEnvironmentComponent, ValuesClassName = nameof(MikanLightEnvironmentComponentValues) },
			new SystemEntry() { SystemName = MikanSystemNames.RGBSpotLightSystem, ComponentClass = MikanSystemNames.RGBSpotLightComponent, ValuesClassName = nameof(MikanRGBSpotLightComponentValues) },
		};

		private readonly MikanClient _client;
		private readonly Dictionary<string, SystemEntry> _systemsByName = new Dictionary<string, SystemEntry>();
		private readonly Dictionary<string, Dictionary<int, MikanComponentValues>> _componentsBySystem =
			new Dictionary<string, Dictionary<int, MikanComponentValues>>();
		private int _currentSceneId = (int)MikanConstants.InvalidMikanID;

		public event Action<string, int, MikanComponentValues> ComponentAdded;
		public event Action<string, int> ComponentRemoved;
		public event Action<string, int, string, MikanComponentValues> ComponentChanged;
		public event Action<int> CurrentSceneChanged;

		public int CurrentSceneId => _currentSceneId;

		public MikanDataStore(MikanClient client)
		{
			_client = client;

			foreach (SystemEntry entry in MirroredSystems)
			{
				_systemsByName[entry.SystemName] = entry;
				_componentsBySystem[entry.SystemName] = new Dictionary<int, MikanComponentValues>();
			}
		}

		public bool IsMirroredSystem(string systemName)
		{
			return _systemsByName.ContainsKey(systemName);
		}

		public IReadOnlyDictionary<int, MikanComponentValues> GetComponents(string systemName)
		{
			return _componentsBySystem[systemName];
		}

		public T GetValues<T>(string systemName, int componentId) where T : MikanComponentValues
		{
			if (_componentsBySystem.TryGetValue(systemName, out var components) &&
				components.TryGetValue(componentId, out MikanComponentValues values))
			{
				return values as T;
			}

			return null;
		}

		public void RefreshAll()
		{
			RefreshSceneSystemValues();

			foreach (SystemEntry entry in MirroredSystems)
			{
				RefreshSystem(entry.SystemName);
			}
		}

		public void RefreshSceneSystemValues()
		{
			_client.SendRequest(new SystemGetValuesRequest() { ownerSystem = MikanSystemNames.SceneSystem }, response =>
			{
				if (response is SystemGetValuesResponse valuesResponse &&
					valuesResponse.valuesObject?.Instance is MikanSceneSystemValues sceneValues)
				{
					SetCurrentSceneId(sceneValues.current_scene_id);
				}
			});
		}

		public void RefreshSystem(string systemName)
		{
			if (!_systemsByName.TryGetValue(systemName, out SystemEntry entry))
			{
				return;
			}

			_client.SendRequest(
				new GetComponentListRequest() { ownerSystem = entry.SystemName, componentClassName = entry.ComponentClass },
				response =>
				{
					if (!(response is ComponentListResponse listResponse) || response.resultCode != MikanAPIResult.Success)
					{
						_client.Log(MikanLogLevel.Warning, $"MikanDataStore: listing {systemName} failed ({response.resultCode})");
						return;
					}

					ApplyComponentList(entry, listResponse.componentIdList ?? new List<int>());
				});
		}

		private void ApplyComponentList(SystemEntry entry, List<int> liveIds)
		{
			Dictionary<int, MikanComponentValues> components = _componentsBySystem[entry.SystemName];
			var liveSet = new HashSet<int>(liveIds);

			// Removed first, so a re-created id (same id, new object) is a remove then an add
			var removed = new List<int>();
			foreach (int componentId in components.Keys)
			{
				if (!liveSet.Contains(componentId))
				{
					removed.Add(componentId);
				}
			}

			foreach (int componentId in removed)
			{
				components.Remove(componentId);
				ComponentRemoved?.Invoke(entry.SystemName, componentId);
			}

			foreach (int componentId in liveIds)
			{
				if (!components.ContainsKey(componentId))
				{
					FetchComponentValues(entry, componentId);
				}
			}
		}

		private void FetchComponentValues(SystemEntry entry, int componentId)
		{
			_client.SendRequest(
				new ComponentGetValuesRequest() { ownerSystem = entry.SystemName, componentId = componentId },
				response =>
				{
					if (!(response is ComponentGetValuesResponse valuesResponse) || response.resultCode != MikanAPIResult.Success)
					{
						_client.Log(MikanLogLevel.Warning,
							$"MikanDataStore: fetching {entry.ComponentClass} {componentId} failed ({response.resultCode})");
						return;
					}

					if (!(valuesResponse.valuesObject?.Instance is MikanComponentValues values) ||
						values.RuntimeClassName != entry.ValuesClassName)
					{
						_client.Log(MikanLogLevel.Warning,
							$"MikanDataStore: {entry.ComponentClass} {componentId} answered {valuesResponse.componentClassName}, expected {entry.ValuesClassName}");
						return;
					}

					Dictionary<int, MikanComponentValues> components = _componentsBySystem[entry.SystemName];
					if (components.ContainsKey(componentId))
					{
						// A duplicate fetch raced an earlier one; the first answer stands
						return;
					}

					components[componentId] = values;
					ComponentAdded?.Invoke(entry.SystemName, componentId, values);
				});
		}

		public void HandlePropertyUpdate(MikanPropertyUpdateEvent propertyUpdate)
		{
			MikanPropertyValue propertyValue = propertyUpdate.propertyValue;
			if (propertyValue == null)
			{
				return;
			}

			string systemName = propertyValue.ownerSystem;
			string fieldName = propertyValue.fieldName;

			if (propertyValue.componentId == (int)MikanConstants.InvalidMikanID)
			{
				// A system property. The component id list is the create and destroy signal.
				if (fieldName != null && fieldName.EndsWith("IdList"))
				{
					RefreshSystem(systemName);
				}
				else if (systemName == MikanSystemNames.SceneSystem && fieldName == "current_scene_id")
				{
					if (TryGetVariantValue(propertyValue.fieldValue, out object value) && value is int sceneId)
					{
						SetCurrentSceneId(sceneId);
					}
				}

				return;
			}

			if (!_componentsBySystem.TryGetValue(systemName, out var components) ||
				!components.TryGetValue(propertyValue.componentId, out MikanComponentValues values))
			{
				return;
			}

			if (ApplyVariantToField(values, fieldName, propertyValue.fieldValue))
			{
				ComponentChanged?.Invoke(systemName, propertyValue.componentId, fieldName, values);
			}
		}

		public void Clear()
		{
			foreach (SystemEntry entry in MirroredSystems)
			{
				Dictionary<int, MikanComponentValues> components = _componentsBySystem[entry.SystemName];
				var ids = new List<int>(components.Keys);
				components.Clear();

				foreach (int componentId in ids)
				{
					ComponentRemoved?.Invoke(entry.SystemName, componentId);
				}
			}

			_currentSceneId = (int)MikanConstants.InvalidMikanID;
		}

		private void SetCurrentSceneId(int sceneId)
		{
			if (_currentSceneId != sceneId)
			{
				_currentSceneId = sceneId;
				CurrentSceneChanged?.Invoke(sceneId);
			}
		}

		// The generated values classes name their fields as the wire does, so a property update's
		// field name is the field to patch, and their TrySetField converts the variant's value to
		// the field's type
		private static bool ApplyVariantToField(MikanComponentValues values, string fieldName, MikanVariant variant)
		{
			if (string.IsNullOrEmpty(fieldName) || !TryGetVariantValue(variant, out object value))
			{
				return false;
			}

			return values.TrySetField(fieldName, value);
		}

		// The variant carries its payload as a typed wrapper whose single field is called value
		private static bool TryGetVariantValue(MikanVariant variant, out object value)
		{
			value = null;

			PolymorphicStruct wrapper = variant?.value_ptr?.Instance;
			return wrapper != null && wrapper.TryGetField("value", out value);
		}
	}
}
