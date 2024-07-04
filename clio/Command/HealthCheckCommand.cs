using System;
using System.Net;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("healthcheck", Aliases = new [] { "hc" }, HelpText = "Healthcheck monitoring")]
	public class HealthCheckOptions : RemoteCommandOptions
	{
		[Option('h', "WebHost", Required = false, HelpText = "Check web-host", Separator= ' ')]
		public string WebHost { get; set; }
		
		[Option('a', "WebApp", Required = false, HelpText = "Check web-app")]
		public string WebApp { get; set; }
	}

	public class HealthCheckCommand : RemoteCommand<HealthCheckOptions>
	{
		public HealthCheckCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
			settings.IsNetCore = true;
		}

		private int ExecuteGetRequest(string checkName) {
			try
			{
				string response = ApplicationClient.ExecuteGetRequest(ServiceUri);
				if (string.IsNullOrEmpty(response))
				{
					Logger.WriteInfo($"\t{checkName} - OK");
				}
			}
			catch (WebException ex)
			{
				Logger.WriteError($"\tError: {ex.Message}");
			}
			catch(Exception ex)
			{
				Logger.WriteError($"\tUnknown Error: {ex.Message}");
				throw;
			}
			return 0;
		}

		public override int Execute(HealthCheckOptions options) {
			int result = 0;
			if (options.WebApp == "true")
			{
				ServicePath = "/api/HealthCheck/Ping";
				Logger.WriteInfo($"Checking WebAppLoader {ServiceUri} ...");
				result = ExecuteGetRequest("WebAppLoader");
			}
			
			if (options.WebHost == "true")
			{
				ServicePath = "/0/api/HealthCheck/Ping";
				Logger.WriteInfo($"Checking WebHost {ServiceUri} ...");
				result += ExecuteGetRequest("WebHost");
			}
			return result;
		}
	}
}
