using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Real-pipeline (in-memory <see cref="TestServer"/>) coverage for ENG-93386 Story 4 (the Protected
/// Resource Metadata / RFC 9728 document and the enriched <c>WWW-Authenticate</c> 401 challenge) and
/// Story 5 (whole-endpoint enforcement via <c>RequireAuthorization</c>). These tests mount the auth
/// machinery (<see cref="McpHttpAuthentication.ConfigureServices"/> + <c>UseAuthentication</c>/
/// <c>UseAuthorization</c>) against a minimal test endpoint carrying the <c>mcp</c> policy — the same
/// composition <c>McpHttpServerCommand.Run</c> applies to the real <c>/mcp</c> endpoint, without
/// spinning up the full MCP transport.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpHttpAuthenticationPipelineTests
{
	private const string Audience = "creatio_ai_api";
	private const string Issuer = "https://id.example";

	private RsaSecurityKey _signingKey;
	private IHost _host;
	private HttpClient _client;

	[SetUp]
	public void SetUp() {
		_signingKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048));
	}

	[TearDown]
	public void TearDown() {
		_client?.Dispose();
		_host?.Dispose();
	}

	// Mirrors McpHttpServerCommand.Run's exact conditional pattern: auth service registration,
	// UseAuthentication/UseAuthorization, and RequireAuthorization on the endpoint all apply ONLY
	// when authEnabled — so the disabled path exercises the SAME "nothing added" wiring as production.
	private async Task StartHostAsync(AuthConfiguration config, bool authEnabled = true) {
		IHostBuilder builder = new HostBuilder()
			.ConfigureWebHost(webBuilder => {
				webBuilder.UseTestServer();
				webBuilder.ConfigureServices(services => {
					services.AddRouting();
					if (authEnabled) {
						McpHttpAuthentication.ConfigureServices(services, config);
						// Test-only override: validate against our in-memory RSA key instead of a live
						// JWKS endpoint, so the pipeline test needs no network access.
						services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
							Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
							options => {
								options.Authority = null;
								options.RequireHttpsMetadata = false;
								options.TokenValidationParameters.IssuerSigningKey = _signingKey;
							});
					}
				});
				webBuilder.Configure(app => {
					app.UseRouting();
					if (authEnabled) {
						app.UseAuthentication();
						app.UseAuthorization();
					}
					app.UseEndpoints(endpoints => {
						Microsoft.AspNetCore.Builder.IEndpointConventionBuilder endpoint =
							endpoints.MapGet("/mcp", () => Results.Ok("ok"));
						if (authEnabled) {
							endpoint.RequireAuthorization(McpHttpAuthentication.PolicyName);
						}
					});
				});
			});

		_host = await builder.StartAsync();
		_client = _host.GetTestServer().CreateClient();
	}

	private static AuthConfiguration Config(string requiredScopes = null) =>
		AuthConfiguration.Resolve(
			new McpHttpServerCommandOptions {
				AuthAuthority = Issuer,
				AuthAudience = Audience,
				AuthIssuer = Issuer,
				AuthRequiredScopes = requiredScopes
			},
			new AuthEnvironment(null, null, null, null, null));

	private string IssueToken(string audience = Audience, string issuer = Issuer,
		DateTime? expires = null, string scope = null) {
		JwtSecurityTokenHandler handler = new();
		List<Claim> claims = [];
		if (scope is not null) {
			claims.Add(new Claim("scope", scope));
		}
		SigningCredentials credentials = new(_signingKey, SecurityAlgorithms.RsaSha256);
		DateTime effectiveExpires = expires ?? DateTime.UtcNow.AddMinutes(5);
		JwtSecurityToken token = new(
			issuer: issuer,
			audience: audience,
			claims: claims,
			notBefore: effectiveExpires.AddMinutes(-6),
			expires: effectiveExpires,
			signingCredentials: credentials);
		return handler.WriteToken(token);
	}

	[Test]
	[Description("An unauthenticated request is rejected with 401 and a WWW-Authenticate header naming the resource_metadata URI (RFC 9728).")]
	public async Task UnauthenticatedRequest_ShouldReturn401WithResourceMetadataChallenge() {
		// Arrange
		await StartHostAsync(Config());

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
			because: "no bearer token was presented");
		IEnumerable<AuthenticationHeaderValue> challenges = response.Headers.WwwAuthenticate;
		challenges.Should().ContainSingle(c => c.Scheme == "Bearer" && c.Parameter.Contains("resource_metadata"),
			because: "the MCP scheme's HandleChallengeAsync must enrich the 401 with the RFC 9728 resource_metadata URI");
	}

	[Test]
	[Description("The Protected Resource Metadata document is served anonymously at the well-known path.")]
	public async Task WellKnownEndpoint_ShouldServeResourceMetadata_Anonymously() {
		// Arrange
		await StartHostAsync(Config(requiredScopes: "mcp:tools"));

		// Act
		HttpResponseMessage response = await _client.GetAsync("/.well-known/oauth-protected-resource");
		string body = await response.Content.ReadAsStringAsync();

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.OK,
			because: "discovery must be reachable without any credential");
		using JsonDocument document = JsonDocument.Parse(body);
		document.RootElement.GetProperty("authorization_servers")[0].GetString().Should().Be(Issuer,
			because: "the configured issuer is advertised as the authorization server");
		document.RootElement.GetProperty("scopes_supported")[0].GetString().Should().Be("mcp:tools",
			because: "the configured required scope is advertised");
	}

	[Test]
	[Description("A valid token (correct issuer/audience, unexpired, sufficient scope) is authorized.")]
	public async Task ValidToken_ShouldAuthorize() {
		// Arrange
		await StartHostAsync(Config(requiredScopes: "mcp:tools"));
		string token = IssueToken(scope: "mcp:tools");
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.OK,
			because: "a token with the correct issuer/audience/scope must be accepted");
	}

	[Test]
	[Description("A token issued for the wrong audience is rejected with 401.")]
	public async Task WrongAudienceToken_ShouldReturn401() {
		// Arrange
		await StartHostAsync(Config());
		string token = IssueToken(audience: "some-other-api");
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
			because: "the token's aud does not match the configured audience");
	}

	[Test]
	[Description("An expired token is rejected with 401.")]
	public async Task ExpiredToken_ShouldReturn401() {
		// Arrange
		await StartHostAsync(Config());
		string token = IssueToken(expires: DateTime.UtcNow.AddMinutes(-10));
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "the token has expired");
	}

	[Test]
	[Description("A validly-authenticated token missing a required scope is rejected with 403.")]
	public async Task ValidTokenMissingScope_ShouldReturn403() {
		// Arrange
		await StartHostAsync(Config(requiredScopes: "mcp:admin"));
		string token = IssueToken(scope: "mcp:tools");
		_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
			because: "authentication succeeded but the required mcp:admin scope is absent");
	}

	[Test]
	[Description("Story 5 AC-04: with authorization disabled, the endpoint is reachable with no token at all — behavior is unchanged from before this feature.")]
	public async Task AuthorizationDisabled_ShouldReachEndpoint_WithNoToken() {
		// Arrange
		await StartHostAsync(Config(), authEnabled: false);

		// Act
		HttpResponseMessage response = await _client.GetAsync("/mcp");

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.OK,
			because: "no --auth-authority is configured, so RequireAuthorization is never applied and "
				+ "the endpoint behaves exactly as it did before ENG-93386 (fail-safe off)");
	}
}
