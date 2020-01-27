using System;
using System.Diagnostics;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("open-web-app", Aliases = new string[] { "open" }, HelpText = "Open application in web browser")]
	public class OpenAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Environment name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

	public class OpenAppCommand : RemoteCommand<OpenAppOptions>
	{
		public OpenAppCommand(IApplicationClient applicationClient) : base(applicationClient) {
		}

		public override int Execute(OpenAppOptions options) {
			try {
				var settings = new SettingsRepository();
				var env = settings.GetEnvironment(options);
				Console.WriteLine($"Open {env.Uri}");
				Process.Start(new ProcessStartInfo("cmd", $"/c start {env.Uri}") { CreateNoWindow = true });
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
