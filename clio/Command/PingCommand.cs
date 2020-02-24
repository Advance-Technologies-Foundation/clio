using System;
using System.Net;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("ping", Aliases = new string[] { "ping-app" }, HelpText = "Check current credentional for selected environments")]
	public class PingAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Environment name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

	public class PingAppCommand : RemoteCommand<PingAppOptions>
	{
		public PingAppCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		public override int Execute(PingAppOptions options) {
			EnvironmentSettings env = null;
			try {
				var settings = new SettingsRepository();
				env = settings.GetEnvironment(options);
				Console.WriteLine($"Try login to {env.Uri} with {env.Login} credentials...");
				ApplicationClient.Login();
				Console.WriteLine("Login done");
				return 0;
			} catch (WebException we) {
				HttpWebResponse errorResponse = we.Response as HttpWebResponse;
				if (errorResponse.StatusCode == HttpStatusCode.NotFound) {
					Console.WriteLine($"Application {env.Uri} not found");
				}
				return 1;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
