using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer;

/// <summary>
/// The raw environment-variable inputs for <see cref="AuthConfiguration"/>. Kept as an explicit
/// carrier so the resolver is pure and fully unit-testable without touching the process environment;
/// production code builds it via <see cref="FromProcessEnvironment"/>.
/// </summary>
/// <param name="Authority">Value of <see cref="AuthConfiguration.AuthorityEnvironmentVariableName"/>.</param>
/// <param name="Audience">Value of <see cref="AuthConfiguration.AudienceEnvironmentVariableName"/>.</param>
/// <param name="RequiredScopes">Value of <see cref="AuthConfiguration.RequiredScopesEnvironmentVariableName"/>.</param>
/// <param name="Issuer">Value of <see cref="AuthConfiguration.IssuerEnvironmentVariableName"/>.</param>
/// <param name="AllowInsecureMetadata">Value of <see cref="AuthConfiguration.AllowInsecureMetadataEnvironmentVariableName"/>.</param>
/// <param name="Resource">Value of <see cref="AuthConfiguration.ResourceEnvironmentVariableName"/>.</param>
public sealed record AuthEnvironment(
	string Authority,
	string Audience,
	string RequiredScopes,
	string Issuer,
	string AllowInsecureMetadata,
	string Resource = null)
{
	/// <summary>Reads the auth environment variables from the process environment.</summary>
	/// <returns>An <see cref="AuthEnvironment"/> populated from the current process environment.</returns>
	public static AuthEnvironment FromProcessEnvironment() =>
		new(
			Environment.GetEnvironmentVariable(AuthConfiguration.AuthorityEnvironmentVariableName),
			Environment.GetEnvironmentVariable(AuthConfiguration.AudienceEnvironmentVariableName),
			Environment.GetEnvironmentVariable(AuthConfiguration.RequiredScopesEnvironmentVariableName),
			Environment.GetEnvironmentVariable(AuthConfiguration.IssuerEnvironmentVariableName),
			Environment.GetEnvironmentVariable(AuthConfiguration.AllowInsecureMetadataEnvironmentVariableName),
			Environment.GetEnvironmentVariable(AuthConfiguration.ResourceEnvironmentVariableName));
}

/// <summary>
/// Resolved OAuth 2.1 Resource-Server configuration for the <c>mcp-http</c> edge (ENG-93386, Story 2).
/// The edge is a Resource Server: it validates bearer JWTs issued by an external Authorization Server
/// (the AI-Platform identity-platform). Authorization is <b>enabled iff an authority is configured</b>;
/// when disabled the edge behaves exactly as before this feature (fail-safe off).
/// </summary>
/// <remarks>
/// The model intentionally separates the discovery <see cref="Authority"/> from the accepted
/// <see cref="Issuers"/>: the identity-platform token <c>iss</c> is the <i>public</i> authority, while
/// in-cluster pods fetch OIDC discovery / JWKS over an <i>internal</i> DNS authority (often plain HTTP).
/// See the Story-1 spike findings (<c>spec/mcp-http-standard-authorization/identity-platform-spike-findings.md</c>).
/// </remarks>
public sealed record AuthConfiguration
{
	/// <summary>Environment variable carrying the OIDC discovery authority (single value).</summary>
	public const string AuthorityEnvironmentVariableName = "CLIO_MCP_HTTP_AUTH_AUTHORITY";

	/// <summary>Environment variable carrying the accepted audience(s) (comma-separated set).</summary>
	public const string AudienceEnvironmentVariableName = "CLIO_MCP_HTTP_AUTH_AUDIENCE";

	/// <summary>Environment variable carrying the required scope(s) (comma-separated set).</summary>
	public const string RequiredScopesEnvironmentVariableName = "CLIO_MCP_HTTP_AUTH_REQUIRED_SCOPES";

	/// <summary>Environment variable carrying the accepted issuer(s) (comma-separated set; optional).</summary>
	public const string IssuerEnvironmentVariableName = "CLIO_MCP_HTTP_AUTH_ISSUER";

	/// <summary>Environment variable that, when truthy, allows OIDC metadata over plain HTTP.</summary>
	public const string AllowInsecureMetadataEnvironmentVariableName =
		"CLIO_MCP_HTTP_AUTH_ALLOW_INSECURE_METADATA";

	/// <summary>
	/// Environment variable carrying an explicit Protected Resource Metadata "resource" override
	/// (single value; optional).
	/// </summary>
	public const string ResourceEnvironmentVariableName = "CLIO_MCP_HTTP_AUTH_RESOURCE";

	private AuthConfiguration() { }

	/// <summary>The OIDC discovery authority JwtBearer fetches metadata / JWKS from. Empty ⇒ disabled.</summary>
	public string Authority { get; private init; } = string.Empty;

	/// <summary>
	/// The accepted token <c>iss</c> value(s). Empty ⇒ rely on JwtBearer's default issuer validation
	/// against the discovery document's issuer.
	/// </summary>
	public IReadOnlyList<string> Issuers { get; private init; } = [];

	/// <summary>The accepted audience(s) the token must be issued for.</summary>
	public IReadOnlyList<string> Audiences { get; private init; } = [];

	/// <summary>The scope(s) every request must carry (all required).</summary>
	public IReadOnlyList<string> RequiredScopes { get; private init; } = [];

	/// <summary>
	/// Whether OIDC metadata must be served over HTTPS. Defaults to <see langword="true"/>; set
	/// <see langword="false"/> only for an internal-DNS HTTP authority (via the allow-insecure flag/env).
	/// </summary>
	public bool RequireHttpsMetadata { get; private init; } = true;

	/// <summary>
	/// An explicit Protected Resource Metadata "resource" (canonical MCP endpoint URI) override.
	/// Empty by default: the SDK's <c>McpAuthenticationHandler</c> then derives it per-request from the
	/// incoming scheme/host/path, which is correct behind any ingress that forwards the Host header
	/// (the common case) without clio needing to know its own externally-visible URL. Set only when
	/// auto-derivation is wrong for a specific deployment (e.g. a path-rewriting proxy).
	/// </summary>
	public string Resource { get; private init; } = string.Empty;

	/// <summary>
	/// Whether OAuth Resource-Server authorization is enabled. Enabled iff a discovery
	/// <see cref="Authority"/> is configured (fail-safe off by default).
	/// </summary>
	public bool Enabled => !string.IsNullOrWhiteSpace(Authority);

	/// <summary>
	/// Resolves the configuration by unioning each CLI option with its environment-variable counterpart.
	/// Single-value inputs (authority) prefer the flag then the env var; set-valued inputs (audience,
	/// scopes, issuers) union both sources via <see cref="CommaSet"/>.
	/// </summary>
	/// <param name="options">The parsed <c>mcp-http</c> options.</param>
	/// <param name="environment">The environment-variable inputs (see <see cref="AuthEnvironment.FromProcessEnvironment"/>).</param>
	/// <returns>The resolved, immutable <see cref="AuthConfiguration"/>.</returns>
	public static AuthConfiguration Resolve(McpHttpServerCommandOptions options, AuthEnvironment environment) {
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(environment);

		string authority = FirstNonBlank(options.AuthAuthority, environment.Authority)?.Trim() ?? string.Empty;

		List<string> audiences = [];
		audiences.AddRange(CommaSet.Split(options.AuthAudience));
		audiences.AddRange(CommaSet.Split(environment.Audience));

		List<string> scopes = [];
		scopes.AddRange(CommaSet.Split(options.AuthRequiredScopes));
		scopes.AddRange(CommaSet.Split(environment.RequiredScopes));

		List<string> issuers = [];
		issuers.AddRange(CommaSet.Split(options.AuthIssuer));
		issuers.AddRange(CommaSet.Split(environment.Issuer));

		bool allowInsecure = options.AuthAllowInsecureMetadata
			|| IsTruthy(environment.AllowInsecureMetadata);

		string resource = FirstNonBlank(options.AuthResource, environment.Resource)?.Trim() ?? string.Empty;

		return new AuthConfiguration {
			Authority = authority,
			Issuers = issuers,
			Audiences = audiences,
			RequiredScopes = scopes,
			RequireHttpsMetadata = !allowInsecure,
			Resource = resource
		};
	}

	private static string FirstNonBlank(string first, string second) =>
		!string.IsNullOrWhiteSpace(first) ? first
		: !string.IsNullOrWhiteSpace(second) ? second
		: null;

	/// <summary>
	/// Parses a truthy flag value the same way across every mcp-http boolean env-var override
	/// (<c>"true"</c>/<c>"1"</c>, case-insensitive, tolerant of surrounding whitespace). Internal so
	/// <see cref="McpHttpServerCommand"/>'s env-var-driven boolean flags (e.g.
	/// <c>CLIO_MCP_HTTP_ALLOW_INSECURE_PUBLIC</c>) share the exact same parsing rule instead of a
	/// second, subtly different copy (a review found the two had drifted: this one trimmed, the other
	/// did not).
	/// </summary>
	/// <param name="value">The raw string value (may be <see langword="null"/>).</param>
	/// <returns><see langword="true"/> when the value is a recognized truthy spelling.</returns>
	internal static bool IsTruthy(string value) =>
		string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || value?.Trim() == "1";
}
