using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Live-stand configuration + skip-gate for the <c>clio mcp-http</c> credential-passthrough e2e
/// fixtures (ENG-93208, Story 15c/15d/AC-07). These tests are MANUAL — they need a live Creatio stand,
/// a clio build whose <c>mcp-http-credential-passthrough</c> incubation flag is enabled, and two tenant
/// credential sets — so they are NOT part of the CI suite. When the required environment variables are
/// absent, <see cref="RequireOrIgnore"/> calls <see cref="Assert.Ignore(string)"/> so the fixture is
/// skipped rather than failed, mirroring the <c>McpE2E:Sandbox</c> guard in the other e2e fixtures.
/// </summary>
/// <remarks>
/// Required environment variables (set before a manual run):
/// <list type="bullet">
/// <item><description><c>CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY</c> — the platform API key the edge gate
/// requires (passed to <c>--platform-api-key</c> and sent as <c>Authorization: Bearer</c>).</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_TENANT1_URL</c> / <c>CLIO_MCP_HTTP_E2E_TENANT1_TOKEN</c> —
/// first tenant's Creatio URL + bearer access token.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_TENANT2_URL</c> / <c>CLIO_MCP_HTTP_E2E_TENANT2_TOKEN</c> —
/// second, DISTINCT tenant's Creatio URL + bearer access token.</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_REGISTERED_ENV</c> — (15d only) name of a pre-registered
/// clio environment, used for the <c>mcp-http -e &lt;env&gt;</c> no-regression leg.</description></item>
/// </list>
/// </remarks>
internal sealed class McpHttpPassthroughStand {
	/// <summary>The per-request credential header name the edge reads (matches the CLI default).</summary>
	public const string CredentialsHeaderName = "X-Integration-Credentials";

	public required string PlatformApiKey { get; init; }
	public required string TenantOneUrl { get; init; }
	public required string TenantOneToken { get; init; }
	public required string TenantTwoUrl { get; init; }
	public required string TenantTwoToken { get; init; }
	public string? RegisteredEnvironmentName { get; init; }

	/// <summary>
	/// Reads the live-stand configuration from environment variables, or calls
	/// <see cref="Assert.Ignore(string)"/> (skipping the test) when any required variable is missing.
	/// </summary>
	public static McpHttpPassthroughStand RequireOrIgnore() {
		string? platformApiKey = Read("CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY");
		string? tenantOneUrl = Read("CLIO_MCP_HTTP_E2E_TENANT1_URL");
		string? tenantOneToken = Read("CLIO_MCP_HTTP_E2E_TENANT1_TOKEN");
		string? tenantTwoUrl = Read("CLIO_MCP_HTTP_E2E_TENANT2_URL");
		string? tenantTwoToken = Read("CLIO_MCP_HTTP_E2E_TENANT2_TOKEN");

		if (string.IsNullOrWhiteSpace(platformApiKey)
			|| string.IsNullOrWhiteSpace(tenantOneUrl) || string.IsNullOrWhiteSpace(tenantOneToken)
			|| string.IsNullOrWhiteSpace(tenantTwoUrl) || string.IsNullOrWhiteSpace(tenantTwoToken)) {
			Assert.Ignore(
				"clio mcp-http credential-passthrough e2e is MANUAL (not in CI). Set "
				+ "CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY, CLIO_MCP_HTTP_E2E_TENANT1_URL/_TOKEN and "
				+ "CLIO_MCP_HTTP_E2E_TENANT2_URL/_TOKEN (two DISTINCT live tenants) to run it against a "
				+ "live stand whose clio build has the 'mcp-http-credential-passthrough' incubation flag enabled.");
		}

		return new McpHttpPassthroughStand {
			PlatformApiKey = platformApiKey!,
			TenantOneUrl = tenantOneUrl!,
			TenantOneToken = tenantOneToken!,
			TenantTwoUrl = tenantTwoUrl!,
			TenantTwoToken = tenantTwoToken!,
			RegisteredEnvironmentName = Read("CLIO_MCP_HTTP_E2E_REGISTERED_ENV")
		};
	}

	private static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
