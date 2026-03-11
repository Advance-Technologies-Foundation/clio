using System;
using System.Runtime.InteropServices;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using CommandLine;

namespace Clio.Command;

[Verb("open-web-app", Aliases = ["open"], HelpText = "Open application in web browser")]
public class OpenAppOptions : RemoteCommandOptions{ }

public class OpenAppCommand(
	IApplicationClient applicationClient,
	EnvironmentSettings environmentSettings,
	IWebBrowser webBrowser,
	IProcessExecutor processExecutor,
	ISettingsRepository settingsRepository
	) : RemoteCommand<OpenAppOptions>(applicationClient, environmentSettings){
	#region Methods: Public

	public override int Execute(OpenAppOptions options) {
		try {
			EnvironmentSettings env = settingsRepository.GetEnvironment(options);

			if (string.IsNullOrEmpty(env.Uri)) {
				Logger.WriteError(
					$"Environment:{options.Environment ?? ""} has empty url. Use 'clio reg-web-app' command to configure it.");
				return 1;
			}

			if (!Uri.TryCreate(env.Uri, UriKind.Absolute, out Uri _)) {
				Logger.WriteError(
					$"Environment:{options.Environment ?? ""} has incorrect url format. Actual Url: '{env.Uri}' " +
					$"Use \r\n\r\n\tclio cfg -e {options.Environment} -u <correct-url-here>\r\n\r\n command to configure it.");
				return 1;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				ProcessExecutionOptions po = new("open", env.Uri);
				ProcessLaunchResult result = processExecutor.FireAndForgetAsync(po).ConfigureAwait(false).GetAwaiter()
															.GetResult();
				if (result.Started) {
					return 0;
				}

				Logger.WriteError(
					$"Failed to open url in browser: {env.SimpleloginUri}{Environment.NewLine}{result.ErrorMessage}");
				return 1;
			}
			else {
				webBrowser.OpenUrl(env.Uri);
			}

			return 0;
		}
		catch (Exception e) {
			Logger.WriteError(e.ToString());
			return 1;
		}
	}

	#endregion
}
