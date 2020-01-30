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

		private string UnloadAppDomainUrl
		{
			get
			{
				if (EnvironmentSettings.IsNetCore) {
					return EnvironmentSettings.Uri + @"/ServiceModel/AppInstallerService.svc/RestartApp";
				} else {
					return EnvironmentSettings.Uri + @"/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
				}
			}
		}

		private void RestartInternal() {
			ApplicationClient.ExecutePostRequest(UnloadAppDomainUrl, @"{}");
		}

		public override int Execute(RestartOptions options) {
			try {
				RestartInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
