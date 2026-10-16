namespace MikanXR.Unity
{
	// The object systems and component classes the Unity client mirrors, by the names the
	// editor's property requests address them with. The generated bindings carry no name
	// constants yet; once the codegen emits them these become aliases of the generated ones.
	public static class MikanSystemNames
	{
		public const string SceneSystem = "SceneObjectSystem";
		public const string SceneComponent = "SceneComponent";

		public const string StageSystem = "StageObjectSystem";
		public const string StageComponent = "StageComponent";

		public const string AnchorSystem = "AnchorObjectSystem";
		public const string AnchorComponent = "AnchorComponent";

		public const string QuadStencilSystem = "QuadStencilSystem";
		public const string QuadStencilComponent = "QuadStencilComponent";

		public const string BoxStencilSystem = "BoxStencilSystem";
		public const string BoxStencilComponent = "BoxStencilComponent";

		public const string ModelStencilSystem = "ModelStencilSystem";
		public const string ModelStencilComponent = "ModelStencilComponent";

		public const string CameraSystem = "CameraObjectSystem";
		public const string CameraComponent = "CameraComponent";

		// A system's component list property, whose update with an invalid component id is the
		// create and destroy notification
		public static string ComponentIdListProperty(string componentClass)
		{
			return componentClass + "IdList";
		}
	}
}
