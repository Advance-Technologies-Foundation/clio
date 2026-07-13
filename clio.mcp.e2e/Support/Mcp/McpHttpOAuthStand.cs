using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Live-stand configuration + skip-gate for the <c>clio mcp-http</c> standard OAuth 2.1
/// Resource-Server authorization e2e fixtures (ENG-93386, Story 8). These tests are MANUAL — they
/// need a live identity-platform (Authorization Server) with a pre-registered confidential
/// <c>client_credentials</c> client, so they are NOT part of the CI suite. When the required
/// environment variables are absent, <see cref="RequireOrIgnore"/> calls
/// <see cref="Assert.Ignore(string)"/> so the fixture is skipped rather than failed, mirroring
/// <see cref="McpHttpPassthroughStand"/>.
/// </summary>
/// <remarks>
/// Required environment variables (set before a manual run):
/// <list type="bullet">
/// <item><description><c>CLIO_MCP_HTTP_E2E_AUTH_AUTHORITY</c> — the identity-platform OIDC
/// authority (discovery/JWKS base URL), passed to <c>--auth-authority</c>.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_AUTH_AUDIENCE</c> — the accepted audience, passed to
/// <c>--auth-audience</c>.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_AUTH_CLIENT_ID</c> / <c>_AUTH_CLIENT_SECRET</c> — the
/// pre-registered confidential client's credentials, used to acquire a <c>client_credentials</c>
/// token via <see cref="OAuthClientCredentialsTokenFetcher"/>.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_AUTH_REQUIRED_SCOPES</c> — (optional) comma-separated
/// scope(s) passed to <c>--auth-required-scopes</c> and requested from the token endpoint.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_TENANT1_URL</c> / <c>CLIO_MCP_HTTP_E2E_TENANT1_TOKEN</c>
/// — reused from <see cref="McpHttpPassthroughStand"/> for the OAuth+passthrough interop leg.</description></item>
/// </list>
/// </remarks>
internal sealed class McpHttpOAuthStand {
	public required string AuthAuthority { get; init; }
	public required string AuthAudience { get; init; }
	public required string ClientId { get; init; }
	public required string ClientSecret { get; init; }
	public string? RequiredScopes { get; init; }
	public string? TenantUrl { get; init; }
	public string? TenantToken { get; init; }

	/// <summary>
	/// Reads the live-stand configuration from environment variables, or calls
	/// <see cref="Assert.Ignore(string)"/> (skipping the test) when any required variable is missing.
	/// </summary>
	public static McpHttpOAuthStand RequireOrIgnore() {
		string? authority = Read("CLIO_MCP_HTTP_E2E_AUTH_AUTHORITY");
		string? audience = Read("CLIO_MCP_HTTP_E2E_AUTH_AUDIENCE");
		string? clientId = Read("CLIO_MCP_HTTP_E2E_AUTH_CLIENT_ID");
		string? clientSecret = Read("CLIO_MCP_HTTP_E2E_AUTH_CLIENT_SECRET");

		if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience)
			|| string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) {
			Assert.Ignore(
				"clio mcp-http standard-OAuth-authorization e2e is MANUAL (not in CI). Set "
				+ "CLIO_MCP_HTTP_E2E_AUTH_AUTHORITY, _AUDIENCE, _CLIENT_ID, and _CLIENT_SECRET to run it "
				+ "against a live identity-platform stand with a pre-registered confidential client.");
		}

		return new McpHttpOAuthStand {
			AuthAuthority = authority!,
			AuthAudience = audience!,
			ClientId = clientId!,
			ClientSecret = clientSecret!,
			RequiredScopes = Read("CLIO_MCP_HTTP_E2E_AUTH_REQUIRED_SCOPES"),
			TenantUrl = Read("CLIO_MCP_HTTP_E2E_TENANT1_URL"),
			TenantToken = Read("CLIO_MCP_HTTP_E2E_TENANT1_TOKEN")
		};
	}

	private static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
