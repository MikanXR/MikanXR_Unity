namespace MikanXR.Unity
{
	// The object systems and component classes the Unity client mirrors, by the names the
	// editor's property requests address them with. The bindings generate the names onto the
	// values classes; these are the ones this client uses, gathered in one place.
	public static class MikanSystemNames
	{
		public const string SceneSystem = MikanSceneComponentValues.k_ownerSystemName;
		public const string SceneComponent = MikanSceneComponentValues.k_componentClassName;

		public const string StageSystem = MikanStageComponentValues.k_ownerSystemName;
		public const string StageComponent = MikanStageComponentValues.k_componentClassName;

		public const string AnchorSystem = MikanAnchorComponentValues.k_ownerSystemName;
		public const string AnchorComponent = MikanAnchorComponentValues.k_componentClassName;

		public const string QuadStencilSystem = MikanQuadStencilComponentValues.k_ownerSystemName;
		public const string QuadStencilComponent = MikanQuadStencilComponentValues.k_componentClassName;

		public const string BoxStencilSystem = MikanBoxStencilComponentValues.k_ownerSystemName;
		public const string BoxStencilComponent = MikanBoxStencilComponentValues.k_componentClassName;

		public const string ModelStencilSystem = MikanModelStencilComponentValues.k_ownerSystemName;
		public const string ModelStencilComponent = MikanModelStencilComponentValues.k_componentClassName;

		public const string CameraSystem = MikanCameraComponentValues.k_ownerSystemName;
		public const string CameraComponent = MikanCameraComponentValues.k_componentClassName;

		// A system's component list property, whose update with an invalid component id is the
		// create and destroy notification
		public static string ComponentIdListProperty(string componentClass)
		{
			return componentClass + "IdList";
		}
	}
}
