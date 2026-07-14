using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Live-stand configuration + skip-gate for the <c>clio mcp-http</c> credential-passthrough e2e
/// fixtures (ENG-93208, Story 15c/15d/AC-07). These tests are MANUAL — they need a live Creatio stand,
/// a clio build started with a <c>--platform-api-key</c> (the sole passthrough gate), and two tenant
/// credential sets — so they are NOT part of the CI suite. When the required environment variables are
/// absent, <see cref="RequireOrIgnore"/> calls <see cref="Assert.Ignore(string)"/> so the fixture is
/// skipped rather than failed, mirroring the <c>McpE2E:Sandbox</c> guard in the other e2e fixtures.
/// </summary>
/// <remarks>
/// Required environment variables (set before a manual run):
/// <list type="bullet">
/// <item><description><c>CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY</c> — the platform API key the edge gate
/// requires (passed to <c>--platform-api-key</c> and sent as <c>Authorization: Bearer</c>).</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_TENANT1_URL</c> / <c>CLIO_MCP_HTTP_E2E_TENANT1_TOKEN</c> /
/// <c>CLIO_MCP_HTTP_E2E_TENANT1_IS_NET_CORE=true|false</c> — first tenant's Creatio URL, bearer access token,
/// and explicit runtime. For an on-prem, forms-auth-only
/// (<c>IsNetCore=false</c>) tenant with no OAuth token, set <c>CLIO_MCP_HTTP_E2E_TENANT1_LOGIN</c> /
/// <c>_TENANT1_PASSWORD</c> instead of <c>_TENANT1_TOKEN</c> — exactly one of the two modes is
/// required per tenant (matches <c>CredentialHeaderParser</c>'s accessToken/login+password
/// precedence).</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_TENANT2_URL</c> / <c>CLIO_MCP_HTTP_E2E_TENANT2_TOKEN</c> /
/// <c>CLIO_MCP_HTTP_E2E_TENANT2_IS_NET_CORE=true|false</c> — second, DISTINCT tenant's Creatio URL,
/// bearer access token, and explicit runtime (or <c>_TENANT2_LOGIN</c> / <c>_TENANT2_PASSWORD</c>, same rule as tenant 1).</description></item>
/// <item><description><c>CLIO_MCP_HTTP_E2E_REGISTERED_ENV</c> — (15d only) name of a pre-registered
/// clio environment, used for the <c>mcp-http -e &lt;env&gt;</c> no-regression leg.</description></item>
/// </list>
/// </remarks>
internal sealed class McpHttpPassthroughStand {
	/// <summary>The per-request credential header name the edge reads (matches the CLI default).</summary>
	public const string CredentialsHeaderName = "X-Integration-Credentials";

	public required string PlatformApiKey { get; init; }
	public required string TenantOneUrl { get; init; }
	public required string TenantTwoUrl { get; init; }
	public required bool TenantOneIsNetCore { get; init; }
	public required bool TenantTwoIsNetCore { get; init; }

	/// <summary>The base64-encoded <c>X-Integration-Credentials</c> payload for tenant 1 — bearer or login+password, whichever was configured.</summary>
	public required string TenantOneCredentialsBase64 { get; init; }

	/// <summary>The base64-encoded <c>X-Integration-Credentials</c> payload for tenant 2 — bearer or login+password, whichever was configured.</summary>
	public required string TenantTwoCredentialsBase64 { get; init; }

	public string? RegisteredEnvironmentName { get; init; }

	/// <summary>
	/// Reads the live-stand configuration from environment variables, or calls
	/// <see cref="Assert.Ignore(string)"/> (skipping the test) when any required variable is missing.
	/// </summary>
	public static McpHttpPassthroughStand RequireOrIgnore() {
		string? platformApiKey = Read("CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY");
		string? tenantOneUrl = Read("CLIO_MCP_HTTP_E2E_TENANT1_URL");
		string? tenantTwoUrl = Read("CLIO_MCP_HTTP_E2E_TENANT2_URL");
		bool tenantOneRuntimeOk = TryReadRuntime(1, out bool tenantOneIsNetCore);
		bool tenantTwoRuntimeOk = TryReadRuntime(2, out bool tenantTwoIsNetCore);

		bool tenantOneOk = TryResolveTenantCredentials(1, tenantOneUrl, tenantOneIsNetCore, out string? tenantOneCredentials);
		bool tenantTwoOk = TryResolveTenantCredentials(2, tenantTwoUrl, tenantTwoIsNetCore, out string? tenantTwoCredentials);

		if (string.IsNullOrWhiteSpace(platformApiKey)
			|| string.IsNullOrWhiteSpace(tenantOneUrl) || !tenantOneRuntimeOk || !tenantOneOk
			|| string.IsNullOrWhiteSpace(tenantTwoUrl) || !tenantTwoRuntimeOk || !tenantTwoOk) {
			Assert.Ignore(
				"clio mcp-http credential-passthrough e2e is MANUAL (not in CI). Set "
				+ "CLIO_MCP_HTTP_E2E_PLATFORM_API_KEY, CLIO_MCP_HTTP_E2E_TENANT{1,2}_URL, "
				+ "CLIO_MCP_HTTP_E2E_TENANT{1,2}_IS_NET_CORE=true|false and, per tenant, "
				+ "either _TOKEN (bearer) or _LOGIN/_PASSWORD (forms-auth) — two DISTINCT live tenants — "
				+ "to run it against a live stand, starting clio mcp-http with --platform-api-key "
				+ "(the sole passthrough gate).");
		}

		return new McpHttpPassthroughStand {
			PlatformApiKey = platformApiKey!,
			TenantOneUrl = tenantOneUrl!,
			TenantOneIsNetCore = tenantOneIsNetCore,
			TenantOneCredentialsBase64 = tenantOneCredentials!,
			TenantTwoUrl = tenantTwoUrl!,
			TenantTwoIsNetCore = tenantTwoIsNetCore,
			TenantTwoCredentialsBase64 = tenantTwoCredentials!,
			RegisteredEnvironmentName = Read("CLIO_MCP_HTTP_E2E_REGISTERED_ENV")
		};
	}

	private static bool TryResolveTenantCredentials(int tenantNumber, string? url, bool isNetCore, out string? credentialsBase64) {
		credentialsBase64 = null;
		if (string.IsNullOrWhiteSpace(url)) {
			return false;
		}

		string? token = Read($"CLIO_MCP_HTTP_E2E_TENANT{tenantNumber}_TOKEN");
		if (!string.IsNullOrWhiteSpace(token)) {
			credentialsBase64 = McpHttpServerSession.EncodeBearerCredentials(url, token, isNetCore);
			return true;
		}

		string? login = Read($"CLIO_MCP_HTTP_E2E_TENANT{tenantNumber}_LOGIN");
		string? password = Read($"CLIO_MCP_HTTP_E2E_TENANT{tenantNumber}_PASSWORD");
		if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(password)) {
			credentialsBase64 = McpHttpServerSession.EncodeLoginPasswordCredentials(url, login, password, isNetCore);
			return true;
		}

		return false;
	}

	private static bool TryReadRuntime(int tenantNumber, out bool isNetCore) =>
		bool.TryParse(Read($"CLIO_MCP_HTTP_E2E_TENANT{tenantNumber}_IS_NET_CORE"), out isNetCore);

	private static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
