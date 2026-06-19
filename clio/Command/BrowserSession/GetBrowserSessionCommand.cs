using System;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for <c>get-browser-session</c>.</summary>
[Verb("get-browser-session", Aliases = ["get-session"],
	HelpText = "Obtain a Playwright-compatible browser session (storageState) for a Creatio environment")]
public class GetBrowserSessionOptions : EnvironmentOptions {
	/// <summary>Optional explicit file path to write the storageState JSON to (CLI-only; validated).</summary>
	[Option("output-path", Required = false,
		HelpText = "File path to write the storageState JSON. When omitted, a cached path under ~/.clio/sessions is used")]
	public string OutputPath { get; set; }

	/// <summary>When set, bypasses the cache and performs a fresh login.</summary>
	[Option("force-refresh", Required = false,
		HelpText = "Bypass the cached session and authenticate again")]
	public bool ForceRefresh { get; set; }
}

/// <summary>
/// Authenticates against a Creatio environment and writes a Playwright-compatible storageState file,
/// printing its absolute path. Cookie values are never printed — only the file path.
/// </summary>
public class GetBrowserSessionCommand(
	IBrowserSessionService browserSessionService,
	ISettingsRepository settingsRepository,
	ILogger logger) : Command<GetBrowserSessionOptions> {

	/// <inheritdoc />
	public override int Execute(GetBrowserSessionOptions options) {
		try {
			EnvironmentSettings environment = settingsRepository.GetEnvironment(options);
			string sessionFilePath = browserSessionService
				.GetSessionPathAsync(environment, options.OutputPath, options.ForceRefresh)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			// Print ONLY the file path — never the cookie values it contains.
			logger.WriteInfo(sessionFilePath);
			return 0;
		} catch (CreatioAuthenticationException e) {
			// Message is already sanitized (no secret material).
			logger.WriteError($"Error: {e.Message}");
			return 1;
		} catch (Exception e) {
			logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}
}
