using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Clio.Common;
using Clio.Utilities;
using CommandLine;

namespace Clio.Command
{
	[Verb("open-web-app", Aliases = new string[] { "open" }, HelpText = "Open application in web browser")]
	public class OpenAppOptions : RemoteCommandOptions
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
				
				if(string.IsNullOrEmpty(env.Uri)) {
					Logger.WriteError($"Environment:{options.Environment ?? ""} has empty url. Use 'clio reg-web-app' command to configure it.");
					return 1;
				}
				
				if(!Uri.TryCreate(env.Uri, UriKind.Absolute, out Uri _)) {
					Logger.WriteError($"Environment:{options.Environment ?? ""} has incorrect url format. Actual Url: '{env.Uri}' " +
						$"Use \r\n\r\n\tclio cfg -e {options.Environment} -u <correct-url-here>\r\n\r\n command to configure it.");
					return 1;
					}
				
				if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
					Process.Start("open", env.SimpleloginUri);
				} else {
					WebBrowser.OpenUrl(env.SimpleloginUri);
				}
				return 0;
			} catch (Exception e) {
				Logger.WriteError(e.ToString());
				return 1;
			}
		}
	}
}
