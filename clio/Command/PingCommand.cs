using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	enum AppType
    {
		WebApp, 
		WebAppLoader
    }

	[Verb("ping-app", Aliases = new string[] { "ping" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : EnvironmentNameOptions
	{
		[Option('x', "Endpoint", Required = false, HelpText = "Relative path for checked endpoint (by default ise Ping service)")]
		public string Endpoint { get; set; } = "/api/HealthCheck/Ping";
	}

	public class PingAppCommand : RemoteCommand<PingAppOptions>
	{
		public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		/// <summary>
		/// Pings WebAppLoader and WebApp
		/// </summary>
		/// <param name="options"></param>
		/// <returns></returns>
		/// <remarks>
		/// See <see href="https://creatio.atlassian.net/wiki/spaces/TER/pages/2385216480/Endpoint+Creatio">new endpoint details</see>
		/// </remarks>
		public override int Execute(PingAppOptions options) {

			PingApp(options, AppType.WebAppLoader);
			PingApp(options, AppType.WebApp);
			return 0;
		}

		private void PingApp(PingAppOptions options, AppType appType)
        {
			// Core and framework have the same endpoint
			// WebAppLoader - 0/api/HealthCheck/Ping
			// WebApp       - api/HealthCheck/Ping
			// I use IsNetCore proprty to infulence ServiceUri 
			// See RemoteCommand.ServiceUri for details
			if(appType == AppType.WebAppLoader)
            {
				EnvironmentSettings.IsNetCore = true;
				ServicePath = options.Endpoint;
				Console.WriteLine($"Ping WebAppLoader {ServiceUri} ...");
            }
            else
            {
				EnvironmentSettings.IsNetCore = false;
				ServicePath = options.Endpoint;
				Console.WriteLine($"Ping WebApp       {ServiceUri} ...");
			}

			string result = ApplicationClient.ExecuteGetRequest(ServiceUri);
			var originalColor = Console.ForegroundColor;
						
			Console.Write($"Ping: ");

			if (string.IsNullOrEmpty(result))
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("Ok");
				Console.ForegroundColor = originalColor;
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("ERROR");
				Console.ForegroundColor = originalColor;
				Console.WriteLine("-------------");
				Console.WriteLine(result);
			}
		}


	}
}
