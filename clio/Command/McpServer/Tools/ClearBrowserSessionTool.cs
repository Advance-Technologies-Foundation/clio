using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.BrowserSession;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for <c>clear-browser-session</c>. Deletes the cached Playwright storageState for
/// an environment so the next <c>get-browser-session</c> re-authenticates. Idempotent.
/// </summary>
[McpServerToolType]
public sealed class ClearBrowserSessionTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "clear-browser-session";

	/// <summary>Deletes the cached browser session for the environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Delete the cached Creatio browser session for an environment so the next get-browser-session " +
		"performs a fresh login. Idempotent — succeeds even when no session is cached.")]
	public ClearBrowserSessionResult ClearBrowserSession(
		[Description("Parameters: environment-name (required)")]
		[Required]
		ClearBrowserSessionArgs args) {
		var environmentOptions = new EnvironmentOptions { Environment = args.EnvironmentName };
		try {
			IBrowserSessionService service = commandResolver.Resolve<IBrowserSessionService>(environmentOptions);
			EnvironmentSettings environment = commandResolver.Resolve<EnvironmentSettings>(environmentOptions);
			service.ClearSessionAsync(environment).ConfigureAwait(false).GetAwaiter().GetResult();
			return new ClearBrowserSessionResult(true, $"Browser session for '{args.EnvironmentName}' cleared.");
		} catch (SafeEnvironmentConfirmationRequiredException ex) {
			return new ClearBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact(ex.Message));
		} catch (InvalidOperationException ex) {
			return new ClearBrowserSessionResult(false, null, SensitiveErrorTextRedactor.Redact(ex.Message));
		} catch (Exception) {
			return new ClearBrowserSessionResult(false, null, "Failed to clear the browser session.");
		}
	}
}

/// <summary>MCP arguments for the <c>clear-browser-session</c> tool.</summary>
public sealed record ClearBrowserSessionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName);

/// <summary>MCP response for the <c>clear-browser-session</c> tool.</summary>
public sealed record ClearBrowserSessionResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("message")] string Message = null,
	[property: JsonPropertyName("error")] string Error = null);
