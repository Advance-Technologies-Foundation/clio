using System;
using System.Net;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("healthcheck", Aliases = new [] { "hc" }, HelpText = "Healthcheck monitoring")]
	public class HealthCheckOptions : EnvironmentNameOptions
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
					var originalColor = Console.ForegroundColor;
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine("\t{0} - OK",checkName);
					Console.ForegroundColor = originalColor;
				}
			}
			catch (WebException ex)
			{
				var originalColor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("\tError: {0}",ex.Message);
				Console.ForegroundColor = originalColor;
			}
			catch(Exception ex)
			{
				var originalColor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("\tUnknown Error: {0}", ex.Message);
				Console.ForegroundColor = originalColor;
				throw;
			}
			return 0;
		}

		public override int Execute(HealthCheckOptions options) {
			int result = 0;
			if (options.WebApp == "true")
			{
				ServicePath = "/api/HealthCheck/Ping";
				Console.WriteLine($"Checking WebAppLoader {ServiceUri} ...");
				result = ExecuteGetRequest("WebAppLoader");
			}
			
			if (options.WebHost == "true")
			{
				ServicePath = "/0/api/HealthCheck/Ping";
				Console.WriteLine($"Checking WebHost {ServiceUri} ...");
				result += ExecuteGetRequest("WebHost");
			}
			return result;
		}
	}
}
