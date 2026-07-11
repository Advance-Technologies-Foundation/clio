using System.Net.Http;
using System.Text.Json;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Minimal <c>grant_type=client_credentials</c> token fetch against a live identity-platform
/// Authorization Server, for the ENG-93386 Story 8 OAuth e2e fixtures. Deliberately standalone
/// (a plain <see cref="HttpClient"/>, not routed through clio's own DI container) — the e2e
/// harness spawns clio as a separate process and talks to it and to the AS purely over HTTP, the
/// same shape as the mechanics <c>Clio.Command.OAuthAppConfiguration.IdentityServerProbe</c> uses
/// for Creatio's own server-to-server OAuth apps.
/// </summary>
internal static class OAuthClientCredentialsTokenFetcher {
	/// <summary>
	/// Requests an access token from <c>{authority}/connect/token</c> via the
	/// <c>client_credentials</c> grant. Throws if the Authorization Server does not return a
	/// successful response with an <c>access_token</c> — a manual e2e run that explicitly opted in
	/// via the required environment variables should fail loudly on a broken AS, not silently skip.
	/// </summary>
	/// <param name="authority">The identity-platform OIDC authority base URL.</param>
	/// <param name="clientId">The pre-registered confidential client's identifier.</param>
	/// <param name="clientSecret">The pre-registered confidential client's secret.</param>
	/// <param name="scope">Optional space/comma-agnostic scope value to request (single string, as OpenIddict expects).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The acquired access token.</returns>
	public static async Task<string> AcquireAsync(
		string authority, string clientId, string clientSecret, string? scope,
		CancellationToken cancellationToken) {
		using HttpClient client = new();
		Dictionary<string, string> form = new() {
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = clientSecret
		};
		if (!string.IsNullOrWhiteSpace(scope)) {
			form["scope"] = scope;
		}
		using FormUrlEncodedContent content = new(form);
		using HttpResponseMessage response = await client.PostAsync(
			$"{authority.TrimEnd('/')}/connect/token", content, cancellationToken);
		string body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"Token endpoint at '{authority}' returned {(int)response.StatusCode}: {body}");
		}
		using JsonDocument document = JsonDocument.Parse(body);
		if (!document.RootElement.TryGetProperty("access_token", out JsonElement tokenElement)
			|| tokenElement.ValueKind != JsonValueKind.String) {
			throw new InvalidOperationException(
				$"Token endpoint at '{authority}' did not return an access_token: {body}");
		}
		return tokenElement.GetString()!;
	}
}
