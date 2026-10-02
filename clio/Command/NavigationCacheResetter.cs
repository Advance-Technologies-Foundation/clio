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
	/// until they reset it themselves or end; open browser tabs reset theirs over the websocket.
	/// </summary>
	/// <param name="client">The client whose session made the change.</param>
	/// <param name="environmentSettings">Settings of the environment the client targets.</param>
	/// <returns>
	/// <see langword="null"/> when the reset succeeded; otherwise a warning for the caller's result. The
	/// reset is best-effort and never throws, so a failed reset cannot hide a change that was already made.
	/// </returns>
	string? TryReset(IApplicationClient client, EnvironmentSettings environmentSettings);
}

/// <inheritdoc />
public sealed class NavigationCacheResetter(IServiceUrlBuilder serviceUrlBuilder) : INavigationCacheResetter
{
	internal const string WarningPrefix = "navigation cache reset failed: ";

	// GetData returns the whole module structure; bound the call so a stalled reset cannot hold the command.
	private const int RequestTimeoutMs = 30_000;

	// The contract is WebMessageBodyStyle.Bare GetData(bool forceGet), so the body is the bare JSON boolean.
	private const string ForceGetBody = "true";

	/// <inheritdoc />
	public string? TryReset(IApplicationClient client, EnvironmentSettings environmentSettings)
	{
		try
		{
			string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetConfigurationData, environmentSettings);
			string response = client.ExecutePostRequest(url, ForceGetBody, RequestTimeoutMs, maxAttempts: 1, delaySec: 0);
			string? failure = ReadFailure(response);
			return failure is null ? null : WarningPrefix + failure;
		}
		catch (Exception exception)
		{
			return WarningPrefix + exception.Message;
		}
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
