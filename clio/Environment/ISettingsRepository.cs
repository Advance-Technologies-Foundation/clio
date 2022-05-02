using System.IO;

namespace Clio.UserEnvironment
{
	public interface ISettingsRepository
	{
		bool IsEnvironmentExists(string name);
		string FindEnvironmentNameByUri(string uri);
		EnvironmentSettings GetEnvironment(string name = null);
		EnvironmentSettings GetEnvironment(EnvironmentOptions options);
		void SetActiveEnvironment(string name);
		void ConfigureEnvironment(string name, EnvironmentSettings environment);
		void RemoveEnvironment(string name);
		void ShowSettingsTo(TextWriter textWriter, string name);
	}
}
