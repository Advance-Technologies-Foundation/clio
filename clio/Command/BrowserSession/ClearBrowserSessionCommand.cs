using System;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for <c>clear-browser-session</c>.</summary>
[Verb("clear-browser-session", Aliases = ["clear-session"],
	HelpText = "Delete the cached browser session for a Creatio environment")]
public class ClearBrowserSessionOptions : EnvironmentOptions {
}

/// <summary>
/// Deletes the cached Playwright storageState for an environment so the next
/// <c>get-browser-session</c> performs a fresh login. Idempotent — succeeds even when no
/// session is cached.
/// </summary>
public class ClearBrowserSessionCommand(
	IBrowserSessionService browserSessionService,
	ISettingsRepository settingsRepository,
	ILogger logger) : Command<ClearBrowserSessionOptions> {

	/// <inheritdoc />
	public override int Execute(ClearBrowserSessionOptions options) {
		try {
			EnvironmentSettings environment = settingsRepository.GetEnvironment(options);
			browserSessionService.ClearSessionAsync(environment).ConfigureAwait(false).GetAwaiter().GetResult();
			logger.WriteInfo($"Browser session for '{options.Environment ?? environment.Uri}' cleared.");
			return 0;
		} catch (Exception e) {
			logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}
}
