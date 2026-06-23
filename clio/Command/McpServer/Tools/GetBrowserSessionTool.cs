using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.BrowserSession;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for <c>get-browser-session</c>. Returns the path to an authenticated Playwright
/// storageState file so an agent can open Creatio already logged in. Cookie values are never part
/// of the response. <c>--output-path</c> is intentionally not exposed here (CLI-only) so an agent
/// cannot redirect a bearer-token file to an arbitrary location.
/// </summary>
[McpServerToolType]
public sealed class GetBrowserSessionTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "get-browser-session";

	/// <summary>Obtains (or reuses) an authenticated browser session and returns its file path.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Obtain an authenticated Creatio browser session (Playwright storageState) for an environment. " +
		"Returns the absolute path to the session file in 'session-file-path' — cookie values are never returned. " +
		"Feed the file to Playwright's storageState option to open Creatio already authenticated (no login page). " +
		"Forms-auth environments only (login + password); OAuth-only environments return success=false with an error.")]
	public GetBrowserSessionResult GetBrowserSession(
		[Description("Parameters: environment-name (required), force-refresh (optional, default false)")]
		[Required]
		GetBrowserSessionArgs args) {
		var environmentOptions = new EnvironmentOptions { Environment = args.EnvironmentName };
		try {
			IBrowserSessionService service = commandResolver.Resolve<IBrowserSessionService>(environmentOptions);
			EnvironmentSettings environment = commandResolver.Resolve<EnvironmentSettings>(environmentOptions);
			string sessionFilePath = service
				.GetSessionPathAsync(environment, overrideOutputPath: null, forceRefresh: args.ForceRefresh)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			return new GetBrowserSessionResult(true, sessionFilePath);
		} catch (CreatioAuthenticationException ex) {
			return new GetBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact(ex.Message));
		} catch (SafeEnvironmentConfirmationRequiredException ex) {
			// A Safe-flagged environment in a non-interactive (MCP) context fails closed here
			// instead of deadlocking — surface a structured error, never hang.
			return new GetBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact(ex.Message));
		} catch (InvalidOperationException ex) {
			// Unknown environment / broken settings bootstrap — the message is safe to surface.
			return new GetBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact(ex.Message));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return new GetBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact($"Failed to obtain the browser session: {ex.Message}"));
		}
	}
}

/// <summary>MCP arguments for the <c>get-browser-session</c> tool.</summary>
public sealed record GetBrowserSessionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,
	[property: JsonPropertyName("force-refresh")]
	[property: Description("Bypass the cached session and authenticate again")]
	bool ForceRefresh = false);

/// <summary>MCP response for the <c>get-browser-session</c> tool. Never carries cookie values.</summary>
public sealed record GetBrowserSessionResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("session-file-path")] string SessionFilePath,
	[property: JsonPropertyName("error")] string? Error = null);
