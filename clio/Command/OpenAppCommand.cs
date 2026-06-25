using System;
using System.Runtime.InteropServices;
using Clio;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using Clio.Utilities;
using CommandLine;

namespace Clio.Command;

[Verb("open-web-app", Aliases = ["open"], HelpText = "Open application in web browser")]
public class OpenAppOptions : RemoteCommandOptions{
	[Value(0, MetaName = "EnvironmentName", Required = false, HelpText = "Environment name")]
	public string EnvironmentName {
		get => Environment;
		set => Environment = value;
	}

	[Option("authenticated", Required = false, HelpText =
		"Open the browser already signed in: clio obtains a session and injects it via Chrome DevTools " +
		"Protocol before navigating, so no login form is shown. Requires a Chromium-based browser and " +
		"forms-auth credentials in the environment config.")]
	public bool Authenticated { get; set; }
}

public class OpenAppCommand(
	IApplicationClient applicationClient,
	EnvironmentSettings environmentSettings,
	IWebBrowser webBrowser,
	IProcessExecutor processExecutor,
	ISettingsRepository settingsRepository,
	IBrowserSessionService browserSessionService,
	IAuthenticatedBrowserLauncher authenticatedBrowserLauncher
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

			if (options.Authenticated) {
				return OpenAuthenticated(env);
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
			Logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	#endregion

	#region Methods: Private

	// Mode A (--authenticated): obtain a session (cached or freshly authenticated) and inject it into a
	// freshly launched browser via CDP, so the user never sees the login form. On a missing browser or an
	// authentication failure it prints an actionable error and exits non-zero — it never silently falls
	// back to an unauthenticated launch.
	private int OpenAuthenticated(EnvironmentSettings env) {
		try {
			string sessionPath = browserSessionService.GetSessionPathAsync(env)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			authenticatedBrowserLauncher.LaunchAsync(env, sessionPath)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			return 0;
		}
		catch (CreatioAuthenticationException ex) {
			Logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
		catch (ChromiumNotFoundException ex) {
			Logger.WriteError(ex.Message);
			return 1;
		}
	}

	#endregion
}
