using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	[Verb("restart-web-app", Aliases = new string[] { "restart" }, HelpText = "Restart a web application")]
	public class RestartOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	public class RestartCommand : BaseRemoteCommand
	{
		public RestartCommand(IApplicationClient applicationClient) 
			: base(applicationClient) {
		}

		private static string UnloadAppDomainUrl
		{
			get
			{
				if (_isNetCore) {
					return _appUrl + @"/ServiceModel/AppInstallerService.svc/RestartApp";
				} else {
					return _appUrl + @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
				}
			}
		}

		private void RestartInternal() {
			ApplicationClient.ExecutePostRequest(UnloadAppDomainUrl, @"{}");
		}

		public int Restart(RestartOptions options) {
			try {
				Configure(options);
				RestartInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		public int Restart(EnvironmentSettings settings) {
			try {
				Configure(settings);
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
