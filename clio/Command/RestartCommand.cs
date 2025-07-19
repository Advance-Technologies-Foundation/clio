using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("restart-web-app", Aliases = ["restart"], HelpText = "Restart a web application")]
public class RestartOptions : RemoteCommandOptions { }

public class RestartCommand : RemoteCommand<RestartOptions> {

	#region Constructors: Public

	public RestartCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
		: base(applicationClient, settings) { }

	#endregion

	#region Properties: Protected

	protected override string ServicePath =>
		EnvironmentSettings.IsNetCore ? "/ServiceModel/AppInstallerService.svc/RestartApp"
			: @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";

	#endregion

}
