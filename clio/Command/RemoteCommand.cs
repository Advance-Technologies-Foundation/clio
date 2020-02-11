using Clio.Common;

namespace Clio.Command
{
	public abstract class RemoteCommand<TEnvironmentOptions>: Command<TEnvironmentOptions>
		where TEnvironmentOptions: EnvironmentOptions
	{
		protected IApplicationClient ApplicationClient { get; }
		protected EnvironmentSettings EnvironmentSettings { get; }

		protected RemoteCommand(IApplicationClient applicationClient, 
				EnvironmentSettings environmentSettings) {
			ApplicationClient = applicationClient;
			EnvironmentSettings = environmentSettings;
		}
	}
}
