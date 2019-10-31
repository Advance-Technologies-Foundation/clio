using System;
using CommandLine;

namespace clio.Command.RedisCommand
{

	[Verb("restart-web-app", Aliases = new string[] { "restart" }, HelpText = "Restart a web application")]
	internal class RestartOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	class RestartCommand : BaseRemoteCommand
	{
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

		private static void RestartInternal() {
			CreatioClient.ExecutePostRequest(UnloadAppDomainUrl, @"{}");
		}

		public static int Restart(RestartOptions options) {
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

		public static int Restart(EnvironmentSettings settings) {
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
