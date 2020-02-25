using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	[Verb("restart-web-app", Aliases = new string[] { "restart" }, HelpText = "Restart a web application")]
	public class RestartOptions : EnvironmentNameOptions
	{
	}

	public class RestartCommand : RemoteCommand<RestartOptions>
	{
		public RestartCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => EnvironmentSettings.IsNetCore ? @"/ServiceModel/AppInstallerService.svc/RestartApp" : @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";

	}
}
