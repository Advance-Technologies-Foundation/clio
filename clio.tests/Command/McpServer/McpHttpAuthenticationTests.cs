using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpHttpAuthenticationTests
{
	private static AuthConfiguration Config(
		string authority = "https://id.example",
		string audience = "creatio_ai_api",
		string requiredScopes = null,
		string issuer = null,
		bool allowInsecureMetadata = false,
		string resource = null) =>
		AuthConfiguration.Resolve(
			new McpHttpServerCommandOptions {
				AuthAuthority = authority,
				AuthAudience = audience,
				AuthRequiredScopes = requiredScopes,
				AuthIssuer = issuer,
				AuthAllowInsecureMetadata = allowInsecureMetadata,
				AuthResource = resource
			},
			new AuthEnvironment(null, null, null, null, null));

	private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
		new(new ClaimsIdentity(claims, authenticationType: "test"));

	[Test]
	[Description("Token validation enforces lifetime, issuer-signing-key, and a 1-minute clock skew.")]
	public void BuildTokenValidationParameters_ShouldEnforceLifetimeAndSigningKey() {
		// Arrange & Act
		TokenValidationParameters tvp = McpHttpAuthentication.BuildTokenValidationParameters(Config());

		// Assert
		tvp.ValidateLifetime.Should().BeTrue(because: "expired tokens must be rejected");
		tvp.ValidateIssuerSigningKey.Should().BeTrue(because: "the token signature must be verified");
		tvp.ClockSkew.Should().Be(TimeSpan.FromMinutes(1), because: "a tight 1-minute skew is used");
	}

	[Test]
	[Description("Only RSA RS256/PS256 signing algorithms are accepted (identity-platform signs RS256).")]
	public void BuildTokenValidationParameters_ShouldAllowOnlyRsaAlgorithms() {
		// Arrange & Act
		TokenValidationParameters tvp = McpHttpAuthentication.BuildTokenValidationParameters(Config());

		// Assert
		tvp.ValidAlgorithms.Should().BeEquivalentTo(
			[SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256],
			because: "the IdP signs with RS256; PS256 is accepted for robustness, nothing else");
	}

	[Test]
	[Description("Audience validation is enabled and bound to the configured audience(s).")]
	public void BuildTokenValidationParameters_ShouldValidateConfiguredAudiences() {
		// Arrange & Act
		TokenValidationParameters tvp =
			McpHttpAuthentication.BuildTokenValidationParameters(Config(audience: "creatio_ai_api"));

		// Assert
		tvp.ValidateAudience.Should().BeTrue(because: "an audience is configured so it must be validated");
		tvp.ValidAudiences.Should().Contain("creatio_ai_api",
			because: "the token aud must match the configured audience");
	}

	[Test]
	[Description("Audience validation is disabled when no audience is configured.")]
	public void BuildTokenValidationParameters_ShouldNotValidateAudience_WhenNoneConfigured() {
		// Arrange & Act
		TokenValidationParameters tvp =
			McpHttpAuthentication.BuildTokenValidationParameters(Config(audience: null));

		// Assert
		tvp.ValidateAudience.Should().BeFalse(because: "no configured audience means no aud constraint");
		tvp.ValidAudiences.Should().BeNull(because: "an empty audience set is left null");
	}

	[Test]
	[Description("Explicit issuers are accepted with and without a trailing slash (iss normalization).")]
	public void BuildTokenValidationParameters_ShouldNormalizeIssuerTrailingSlash() {
		// Arrange & Act
		TokenValidationParameters tvp = McpHttpAuthentication.BuildTokenValidationParameters(
			Config(issuer: "https://id.example/"));

		// Assert
		tvp.ValidIssuers.Should().Contain("https://id.example",
			because: "a token iss without a trailing slash must still match");
		tvp.ValidIssuers.Should().Contain("https://id.example/",
			because: "a token iss with a trailing slash must still match");
	}

	[Test]
	[Description("With no explicit issuer, ValidIssuers is null so JwtBearer uses the discovery issuer.")]
	public void BuildTokenValidationParameters_ShouldLeaveIssuersNull_WhenNoneConfigured() {
		// Arrange & Act
		TokenValidationParameters tvp =
			McpHttpAuthentication.BuildTokenValidationParameters(Config(issuer: null));

		// Assert
		tvp.ValidateIssuer.Should().BeTrue(because: "issuer is always validated");
		tvp.ValidIssuers.Should().BeNull(
			because: "with no explicit set the discovery document's issuer is used by default");
	}

	[Test]
	[Description("JwtBearer options carry the authority, https-metadata policy, and keep original claim names.")]
	public void ConfigureJwtBearer_ShouldApplyAuthorityAndClaimPolicy() {
		// Arrange
		JwtBearerOptions options = new();

		// Act
		McpHttpAuthentication.ConfigureJwtBearer(options, Config(allowInsecureMetadata: true));

		// Assert
		options.Authority.Should().Be("https://id.example", because: "the discovery authority is applied");
		options.RequireHttpsMetadata.Should().BeFalse(because: "insecure metadata was allowed in config");
		options.MapInboundClaims.Should().BeFalse(
			because: "original 'scope'/'scp' claim names must survive for the scope policy");
	}

	[Test]
	[Description("An empty required-scope set is satisfied by any principal.")]
	public void HasRequiredScopes_ShouldReturnTrue_WhenNoScopesRequired() {
		// Arrange
		ClaimsPrincipal user = PrincipalWith(new Claim("scope", "unrelated"));

		// Act & Assert
		McpHttpAuthentication.HasRequiredScopes(user, []).Should().BeTrue(
			because: "no scope requirement means authentication alone suffices");
	}

	[Test]
	[Description("A space-delimited 'scope' claim satisfies a required scope it contains.")]
	public void HasRequiredScopes_ShouldReturnTrue_WhenSpaceDelimitedScopeClaimContainsRequired() {
		// Arrange
		ClaimsPrincipal user = PrincipalWith(new Claim("scope", "openid mcp:tools profile"));

		// Act & Assert
		McpHttpAuthentication.HasRequiredScopes(user, ["mcp:tools"]).Should().BeTrue(
			because: "the space-delimited scope claim grants mcp:tools");
	}

	[Test]
	[Description("The 'scp' claim name is recognized in addition to 'scope'.")]
	public void HasRequiredScopes_ShouldRecognize_ScpClaim() {
		// Arrange
		ClaimsPrincipal user = PrincipalWith(new Claim("scp", "mcp:tools"));

		// Act & Assert
		McpHttpAuthentication.HasRequiredScopes(user, ["mcp:tools"]).Should().BeTrue(
			because: "some issuers emit scopes in the 'scp' claim");
	}

	[Test]
	[Description("A missing required scope fails the check.")]
	public void HasRequiredScopes_ShouldReturnFalse_WhenRequiredScopeMissing() {
		// Arrange
		ClaimsPrincipal user = PrincipalWith(new Claim("scope", "openid profile"));

		// Act & Assert
		McpHttpAuthentication.HasRequiredScopes(user, ["mcp:tools"]).Should().BeFalse(
			because: "the principal does not carry the required mcp:tools scope");
	}

	[Test]
	[Description("A null principal never satisfies a non-empty scope requirement.")]
	public void HasRequiredScopes_ShouldReturnFalse_WhenUserNull() {
		// Act & Assert
		McpHttpAuthentication.HasRequiredScopes(null, ["mcp:tools"]).Should().BeFalse(
			because: "an absent principal cannot carry any scope");
	}

	[Test]
	[Description("Resource metadata advertises the explicit issuer set as the authorization server(s).")]
	public void BuildResourceMetadata_ShouldAdvertiseExplicitIssuers() {
		// Arrange & Act
		ModelContextProtocol.Authentication.ProtectedResourceMetadata metadata =
			McpHttpAuthentication.BuildResourceMetadata(Config(issuer: "https://id.example"));

		// Assert
		metadata.AuthorizationServers.Should().ContainSingle().Which.Should().Be("https://id.example",
			because: "the explicit issuer set is advertised as the authorization server");
	}

	[Test]
	[Description("Resource metadata falls back to the discovery authority when no explicit issuer is configured.")]
	public void BuildResourceMetadata_ShouldFallBackToAuthority_WhenNoIssuerConfigured() {
		// Arrange & Act
		ModelContextProtocol.Authentication.ProtectedResourceMetadata metadata =
			McpHttpAuthentication.BuildResourceMetadata(Config(authority: "https://id.example", issuer: null));

		// Assert
		metadata.AuthorizationServers.Should().ContainSingle().Which.Should().Be("https://id.example",
			because: "with no explicit issuer, the discovery authority is the best-known authorization server");
	}

	[Test]
	[Description("Resource metadata advertises the configured required scopes.")]
	public void BuildResourceMetadata_ShouldAdvertiseRequiredScopes() {
		// Arrange & Act
		ModelContextProtocol.Authentication.ProtectedResourceMetadata metadata =
			McpHttpAuthentication.BuildResourceMetadata(Config(requiredScopes: "mcp:tools,mcp:admin"));

		// Assert
		metadata.ScopesSupported.Should().BeEquivalentTo(["mcp:tools", "mcp:admin"],
			because: "the configured required scopes are advertised to clients");
	}

	[Test]
	[Description("Resource is left null by default so the SDK derives it per-request from the incoming URL.")]
	public void BuildResourceMetadata_ShouldLeaveResourceNull_ByDefault() {
		// Arrange & Act
		ModelContextProtocol.Authentication.ProtectedResourceMetadata metadata =
			McpHttpAuthentication.BuildResourceMetadata(Config());

		// Assert
		metadata.Resource.Should().BeNull(
			because: "no explicit override means the handler auto-derives Resource per-request");
	}

	[Test]
	[Description("An explicit Resource override is carried through to the metadata document.")]
	public void BuildResourceMetadata_ShouldUseExplicitResource_WhenConfigured() {
		// Arrange & Act
		ModelContextProtocol.Authentication.ProtectedResourceMetadata metadata =
			McpHttpAuthentication.BuildResourceMetadata(Config(resource: "https://mcp.example.com/mcp"));

		// Assert
		metadata.Resource.Should().Be("https://mcp.example.com/mcp",
			because: "an explicit override must win over per-request auto-derivation");
	}

	[Test]
	[Description("ConfigureServices registers the bearer scheme and the 'mcp' policy resolves (no handler-class trap).")]
	public async System.Threading.Tasks.Task ConfigureServices_ShouldRegisterBearerSchemeAndMcpPolicy() {
		// Arrange
		ServiceCollection services = new();
		services.AddLogging();

		// Act
		McpHttpAuthentication.ConfigureServices(services, Config(requiredScopes: "mcp:tools"));
		using ServiceProvider provider = services.BuildServiceProvider(
			new ServiceProviderOptions { ValidateScopes = true });

		Microsoft.AspNetCore.Authentication.AuthenticationScheme scheme =
			await provider.GetRequiredService<IAuthenticationSchemeProvider>()
				.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);
		Microsoft.AspNetCore.Authorization.AuthorizationPolicy policy =
			await provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>()
				.GetPolicyAsync(McpHttpAuthentication.PolicyName);

		// Assert
		scheme.Should().NotBeNull(because: "AddJwtBearer registered the Bearer authentication scheme");
		policy.Should().NotBeNull(
			because: "the 'mcp' policy (lambda scope assertion, no IAuthorizationHandler class) is registered");
		policy!.Requirements.Should().NotBeEmpty(
			because: "the policy carries the authenticated-user + scope-assertion requirements");
	}
}
