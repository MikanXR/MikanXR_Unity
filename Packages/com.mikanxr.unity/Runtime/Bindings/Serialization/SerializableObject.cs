namespace MikanXR
{
	public class PolymorphicStruct 
	{
	}

	public class PolymorphicObject
	{
		private string _runtimeClassName = "";
		public string RuntimeClassName => _runtimeClassName;

		private PolymorphicStruct _instance;
		public PolymorphicStruct Instance => _instance;

		public PolymorphicObject()
		{
		}

		public PolymorphicObject(PolymorphicStruct instance)
		{
			setInstance(instance);
		}

		public void setInstance(PolymorphicStruct instance)
		{
			_runtimeClassName = instance.GetType().Name;
			_instance = instance;
		}
	}
}
