namespace MikanXR.Unity
{
	public partial class MikanClient
	{
		// The JSON file under the persistent data path, for a plain Unity project
		static partial void CreateDefaultSettingsProvider(ref IMikanSettingsProvider provider)
		{
			provider = new MikanJsonFileSettingsProvider();
		}
	}
}
