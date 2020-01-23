using System;
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
		public PingAppCommand(IApplicationClient applicationClient): base(applicationClient) {
		}

		public override int Execute(PingAppOptions options) {
			try {
				var settings = new SettingsRepository();
				var env = settings.GetEnvironment(options.Environment);
				Console.WriteLine($"Try login to {env.Uri} with {env.Login} credentials...");
				ApplicationClient.Login();
				Console.WriteLine("Login done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
