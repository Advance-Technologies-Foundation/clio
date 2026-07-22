using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Authentication;
using McpAspNetAuthentication = ModelContextProtocol.AspNetCore.Authentication;

namespace Clio.Command.McpServer;

/// <summary>
/// Wires standard OAuth 2.1 Resource-Server authorization for the <c>mcp-http</c> edge (ENG-93386,
/// Story 3/4): JWT bearer validation of tokens issued by the configured Authorization Server, the SDK's
/// Protected Resource Metadata (RFC 9728) discovery + <c>WWW-Authenticate</c> challenge enrichment, and
/// a scope authorization policy. Only registered when <see cref="AuthConfiguration.Enabled"/> is true;
/// when disabled the caller must NOT add the authentication/authorization middleware (a
/// <c>UseAuthentication</c> with no registered scheme throws at runtime — fail-safe off).
/// </summary>
/// <remarks>
/// <para>
/// The scope check is a lambda <c>RequireAssertion</c> policy — deliberately NOT an
/// <c>IAuthorizationHandler</c>/<c>IAuthorizationRequirement</c> class: clio's assembly-scan DI
/// registration plus <c>ValidateOnBuild=true</c> would otherwise try to construct such a class.
/// </para>
/// <para>
/// Discovery composition (verified against the decompiled <c>ModelContextProtocol.AspNetCore</c> 1.4.0
/// <c>McpAuthenticationHandler</c>): its <c>ForwardAuthenticate</c> defaults to <c>"Bearer"</c>, matching
/// <see cref="JwtBearerDefaults.AuthenticationScheme"/>, so token validation is delegated to the JWT
/// bearer scheme with no extra wiring. <c>DefaultChallengeScheme</c> is set to the MCP scheme
/// (<c>McpAuthenticationDefaults.AuthenticationScheme</c>) so a <c>401</c> goes through
/// <c>McpAuthenticationHandler.HandleChallengeAsync</c>, which appends
/// <c>WWW-Authenticate: Bearer resource_metadata="..."</c>. The MCP scheme also implements
/// <c>IAuthenticationRequestHandler</c>, so ASP.NET Core's authentication middleware serves
/// <c>/.well-known/oauth-protected-resource</c> automatically and unconditionally (before routing /
/// authorization run) — no extra endpoint mapping is needed.
/// </para>
/// </remarks>
public static class McpHttpAuthentication
{
	/// <summary>The authorization policy name applied to the MCP endpoint (Story 5).</summary>
	public const string PolicyName = "mcp";

	// The identity-platform (OpenIddict) signs access tokens with RS256; PS256 accepted for robustness.
	private static readonly string[] AllowedSigningAlgorithms =
		[SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256];

	// OAuth carries scopes in a single space-delimited "scope" claim; some issuers use "scp".
	private static readonly string[] ScopeClaimTypes = ["scope", "scp"];

	/// <summary>
	/// Registers JWT bearer authentication and the MCP scope authorization policy for the resolved
	/// configuration. No-op guard: callers should only invoke this when <paramref name="config"/> is
	/// <see cref="AuthConfiguration.Enabled"/>.
	/// </summary>
	/// <param name="services">The host service collection.</param>
	/// <param name="config">The resolved auth configuration (must be enabled).</param>
	public static void ConfigureServices(IServiceCollection services, AuthConfiguration config) {
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(config);

		services
			.AddAuthentication(options => {
				// Token validation happens on the JwtBearer scheme; the MCP scheme is the CHALLENGE
				// scheme so a 401 goes through McpAuthenticationHandler (adds the RFC 9728
				// resource_metadata WWW-Authenticate parameter) instead of JwtBearer's bare challenge.
				options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = McpAspNetAuthentication.McpAuthenticationDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(options => ConfigureJwtBearer(options, config))
			.AddMcp(options => options.ResourceMetadata = BuildResourceMetadata(config));

		services.AddAuthorization(options =>
			options.AddPolicy(PolicyName, policy => {
				policy.RequireAuthenticatedUser();
				policy.RequireAssertion(context => HasRequiredScopes(context.User, config.RequiredScopes));
			}));
	}

	/// <summary>
	/// Builds the Protected Resource Metadata (RFC 9728) document: the accepted authorization server(s)
	/// (the explicit issuer set when configured, else the discovery authority) and supported scopes.
	/// <see cref="ProtectedResourceMetadata.Resource"/> is left unset unless <see cref="AuthConfiguration.Resource"/>
	/// is explicitly configured, so the SDK derives it per-request from the incoming scheme/host/path
	/// (correct behind any Host-forwarding ingress without clio needing to know its own external URL).
	/// </summary>
	/// <param name="config">The resolved auth configuration.</param>
	/// <returns>The resource metadata document for the MCP authentication scheme.</returns>
	internal static ProtectedResourceMetadata BuildResourceMetadata(AuthConfiguration config) {
		List<string> authorizationServers = config.Issuers.Count > 0
			? [.. config.Issuers]
			: [config.Authority];

		return new ProtectedResourceMetadata {
			Resource = string.IsNullOrWhiteSpace(config.Resource) ? null : config.Resource,
			AuthorizationServers = authorizationServers,
			ScopesSupported = config.RequiredScopes.Count > 0 ? [.. config.RequiredScopes] : null
		};
	}

	/// <summary>Applies the resolved configuration to a <see cref="JwtBearerOptions"/> instance.</summary>
	/// <param name="options">The options to configure.</param>
	/// <param name="config">The resolved auth configuration.</param>
	internal static void ConfigureJwtBearer(JwtBearerOptions options, AuthConfiguration config) {
		options.Authority = config.Authority;
		options.RequireHttpsMetadata = config.RequireHttpsMetadata;
		// Keep original claim types ("scope"/"scp") instead of remapping to the long SOAP URIs,
		// so the scope assertion reads the same claim names the issuer emits.
		options.MapInboundClaims = false;
		options.TokenValidationParameters = BuildTokenValidationParameters(config);
	}

	/// <summary>
	/// Builds the token validation parameters: issuer (accepted set or discovery-doc default),
	/// audience, lifetime, RS256/PS256 signing, and issuer-signing-key validation.
	/// </summary>
	/// <param name="config">The resolved auth configuration.</param>
	/// <returns>The validation parameters for the JWT bearer handler.</returns>
	internal static TokenValidationParameters BuildTokenValidationParameters(AuthConfiguration config) {
		IReadOnlyList<string> issuers = NormalizeIssuers(config.Issuers);
		return new TokenValidationParameters {
			// When no explicit issuer set is given, the JWT bearer handler validates 'iss' against the
			// discovery document's issuer; an explicit set covers the public-iss vs internal-authority split.
			ValidateIssuer = true,
			ValidIssuers = issuers.Count > 0 ? issuers : null,
			ValidateAudience = config.Audiences.Count > 0,
			ValidAudiences = config.Audiences.Count > 0 ? config.Audiences : null,
			ValidateLifetime = true,
			ClockSkew = TimeSpan.FromMinutes(1),
			ValidateIssuerSigningKey = true,
			ValidAlgorithms = AllowedSigningAlgorithms
		};
	}

	/// <summary>
	/// Returns <see langword="true"/> when the principal carries every required scope. An empty
	/// requirement set is satisfied by any authenticated principal.
	/// </summary>
	/// <param name="user">The authenticated principal.</param>
	/// <param name="requiredScopes">The scopes all of which must be present.</param>
	/// <returns>Whether all required scopes are granted.</returns>
	internal static bool HasRequiredScopes(ClaimsPrincipal user, IReadOnlyList<string> requiredScopes) {
		if (requiredScopes is null || requiredScopes.Count == 0) {
			return true;
		}
		if (user is null) {
			return false;
		}

		HashSet<string> granted = new(StringComparer.Ordinal);
		foreach (string claimType in ScopeClaimTypes) {
			foreach (Claim claim in user.FindAll(claimType)) {
				// A scope claim is space-delimited; a token may also carry multiple scope claims.
				foreach (string scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
					granted.Add(scope);
				}
			}
		}

		return requiredScopes.All(granted.Contains);
	}

	// Accept both the configured issuer and its trailing-slash-normalized variant, so a token 'iss'
	// with or without a trailing slash matches (mirrors the control-plane precedent).
	private static IReadOnlyList<string> NormalizeIssuers(IReadOnlyList<string> issuers) {
		if (issuers is null || issuers.Count == 0) {
			return [];
		}

		HashSet<string> result = new(StringComparer.Ordinal);
		foreach (string issuer in issuers) {
			if (string.IsNullOrWhiteSpace(issuer)) {
				continue;
			}
			string trimmed = issuer.Trim();
			result.Add(trimmed);
			result.Add(trimmed.TrimEnd('/'));
			result.Add(trimmed.TrimEnd('/') + "/");
		}
		return [.. result];
	}
}
