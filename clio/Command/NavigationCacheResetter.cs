using System;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Clears the server-side navigation cache (module structure, workplaces, sections) of the client's Creatio
/// session after clio changed a section, the way the Freedom UI Shell does on <c>ConfigurationStructureChanged</c>.
/// </summary>
public interface INavigationCacheResetter
{
	/// <summary>
	/// Calls <c>ConfigurationDataService/GetData</c> with <c>forceGet = true</c> in the session of
	/// <paramref name="client"/>. The server then drops that session's cached module structure, reloads the
	/// workplace and section caches, and bumps the client cache hash. Other server sessions keep their cache
	/// until they reset it themselves or end; an open browser tab resets its own only when the
	/// <c>ConfigurationStructureChanged</c> websocket message reaches it (see <see cref="BuildBrowserSessionNote"/>).
	/// </summary>
	/// <param name="client">The client whose session made the change.</param>
	/// <param name="environmentSettings">Settings of the environment the client targets.</param>
	/// <returns>
	/// <see langword="null"/> when the reset succeeded; otherwise a warning for the caller's result. The
	/// reset is best-effort and never throws, so a failed reset cannot hide a change that was already made.
	/// </returns>
	string? TryReset(IApplicationClient client, EnvironmentSettings environmentSettings);

	/// <summary>
	/// Builds the instruction that refreshes the menu of a browser tab whose session still serves the old one.
	/// A reset clears only the calling session, and a browser tab clears its own session only when it receives
	/// the <c>ConfigurationStructureChanged</c> websocket message; a tab that was not connected at that moment
	/// keeps the old menu across reloads. The instruction runs the same <c>GetData</c> call from inside the tab,
	/// so it uses the tab's own session cookies.
	/// </summary>
	/// <param name="environmentSettings">Settings of the environment whose <c>GetData</c> URL the note names.</param>
	/// <returns>
	/// The note, with the environment's <c>ConfigurationDataService/GetData</c> URL. Never throws: when the
	/// environment URI is not absolute, the URL is the path relative to the site root.
	/// </returns>
	string BuildBrowserSessionNote(EnvironmentSettings environmentSettings);
}

/// <inheritdoc />
public sealed class NavigationCacheResetter(IServiceUrlBuilder serviceUrlBuilder) : INavigationCacheResetter
{
	internal const string WarningPrefix = "navigation cache reset failed: ";

	// GetData returns the whole module structure; bound the call so a stalled reset cannot hold the command.
	private const int RequestTimeoutMs = 30_000;

	// The contract is WebMessageBodyStyle.Bare GetData(bool forceGet), so the body is the bare JSON boolean.
	private const string ForceGetBody = "true";

	// Braces of the JavaScript object literal are doubled for string.Format; {0} is the GetData URL.
	internal const string BrowserSessionNoteFormat =
		"Open Creatio browser tabs refresh their menu over the websocket only if they were connected when the "
		+ "change was made. If a tab still does not show the change after a reload, its server session keeps the "
		+ "old menu: run fetch('{0}', {{method:'POST', headers:{{'Content-Type':'application/json', "
		+ "BPMCSRF:document.cookie.match(/BPMCSRF=([^;]+)/)[1]}}, body:'true'}}) in the developer console of that "
		+ "tab, then reload the tab. Do not clear Redis for this: it logs out every user.";

	/// <inheritdoc />
	public string? TryReset(IApplicationClient client, EnvironmentSettings environmentSettings)
	{
		try
		{
			string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetConfigurationData, environmentSettings);
			string response = client.ExecutePostRequest(url, ForceGetBody, RequestTimeoutMs, maxAttempts: 1, delaySec: 0);
			string? failure = ReadFailure(response);
			return failure is null ? null : WarningPrefix + SensitiveErrorTextRedactor.Redact(failure);
		}
		catch (Exception exception)
		{
			// Transport exceptions can carry request URIs and hosts; the warning reaches MCP output and the console.
			return WarningPrefix + SensitiveErrorTextRedactor.Redact(exception.Message);
		}
	}

	/// <inheritdoc />
	public string BuildBrowserSessionNote(EnvironmentSettings environmentSettings)
	{
		string url;
		try
		{
			url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetConfigurationData, environmentSettings);
		}
		catch (ArgumentException)
		{
			// An environment URI that is not absolute still gets a usable note: the path relative to the site root.
			string route = ServiceUrlBuilder.KnownRoutes[ServiceUrlBuilder.KnownRoute.GetConfigurationData];
			url = environmentSettings.IsNetCore ? route : "/0" + route;
		}

		return string.Format(System.Globalization.CultureInfo.InvariantCulture, BrowserSessionNoteFormat, url);
	}

	// The service catches every exception and answers HTTP 200 with success:false, and the client returns
	// error bodies without a status, so success is read from the body.
	private static string? ReadFailure(string response)
	{
		if (string.IsNullOrWhiteSpace(response))
		{
			return "empty response";
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(response);
			JsonElement root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty("success", out JsonElement success)
				&& success.ValueKind == JsonValueKind.True)
			{
				return null;
			}

			return root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty("errorInfo", out JsonElement errorInfo)
				&& errorInfo.ValueKind == JsonValueKind.Object
				&& errorInfo.TryGetProperty("message", out JsonElement message)
				&& message.ValueKind == JsonValueKind.String
					? message.GetString()
					: "the service did not report success";
		}
		catch (JsonException)
		{
			return "the response is not JSON";
		}
	}
}
