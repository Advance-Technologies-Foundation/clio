using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	[Verb("restart-web-app", Aliases = new string[] { "restart" }, HelpText = "Restart a web application")]
	public class RestartOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

	public class RestartCommand : RemoteCommand<RestartOptions>
	{
		public RestartCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/RestartApp";

	}
}
