using System;
using System.Diagnostics;
using Clio.Common;
using Clio.Utilities;
using CommandLine;

namespace Clio.Command
{
	[Verb("open-web-app", Aliases = new string[] { "open" }, HelpText = "Open application in web browser")]
	public class OpenAppOptions : EnvironmentNameOptions
	{
	}

	public class OpenAppCommand : RemoteCommand<OpenAppOptions>
	{
		public OpenAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
			: base(applicationClient, environmentSettings) {
		}

		public OpenAppCommand(EnvironmentSettings environmentSettings): base(environmentSettings) {
		}

		public override int Execute(OpenAppOptions options) {
			try {
				var settings = new SettingsRepository();
				var env = settings.GetEnvironment(options);
				WebBrowser.OpenUrl(env.SimpleloginUri);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
