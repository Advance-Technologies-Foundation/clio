using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-93386 Story 6 (back-door hardening) coverage:
/// <list type="bullet">
/// <item><description>FR-07 header-strip — the credential header is honored only on an authenticated
/// request when standard OAuth authorization is configured; otherwise it is ignored, not merely
/// deferred.</description></item>
/// <item><description>FR-09 no-token-passthrough invariant — the inbound gateway/platform
/// <c>Authorization</c> header value can never become the outbound Creatio credential, because
/// <see cref="ToolCommandResolver.BuildEphemeralSettings"/> is built solely from the parsed
/// <see cref="CredentialContext"/> (itself derived only from the <c>X-Integration-Credentials</c>
/// header), never from <c>HttpContext.Request.Headers.Authorization</c>.</description></item>
/// </list>
/// FR-08 (gateway→tenant authorization) is deliberately NOT covered here: the Story-1 spike found the
/// identity-platform's client_credentials token authenticates the gateway as a whole and mints no
/// per-tenant/org claim for it, so there is no real claim contract to enforce yet — see the story file
/// and the ENG-93386 Jira comment for the platform-side follow-up.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CredentialPassthroughAuthHardeningTests
{
	private const string HeaderName = "X-Integration-Credentials";
	private const string SecretAccessToken = "tenant-secret-token-xyz";
	private const string GatewayAuthorizationValue = "Bearer gateway-or-platform-token-should-never-reach-creatio";

	private static DefaultHttpContext CreateAuthorizedPassthroughContext(bool authenticated) {
		DefaultHttpContext context = new() {
			RequestServices = new ServiceCollection().BuildServiceProvider()
		};
		context.Request.Headers.Authorization = GatewayAuthorizationValue;
		context.Request.Headers[HeaderName] = ValidCredentialHeader();
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey] = true;
		context.User = authenticated
			? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"))
			: new ClaimsPrincipal(new ClaimsIdentity());
		return context;
	}

	private static string ValidCredentialHeader() {
		string json =
			$"{{\"url\":\"https://acme.creatio.com\",\"accessToken\":\"{SecretAccessToken}\",\"accessTokenType\":\"Bearer\",\"isNetCore\":false}}";
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	private static ICredentialContextAccessor AccessorFor(HttpContext context) =>
		new CredentialContextAccessor(new HttpContextAccessor { HttpContext = context });

	[Test]
	[Description("FR-07/AC-03: when standard OAuth authorization is configured (requireAuthenticatedPrincipal=true), an unauthenticated request's credential header is ignored — not parsed, not captured, no error — even though the platform-API-key gate already marked passthrough enabled.")]
	public async Task CaptureCredentialContext_ShouldIgnoreHeader_WhenAuthorizationRequiredButPrincipalUnauthenticated() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context = CreateAuthorizedPassthroughContext(authenticated: false);
		ICredentialContextAccessor accessor = AccessorFor(context);
		bool nextCalled = false;
		RequestDelegate next = _ => {
			nextCalled = true;
			return Task.CompletedTask;
		};

		// Act
		await McpHttpServerCommand.CaptureCredentialContext(
			context, next, parser, accessor, HeaderName, requireAuthenticatedPrincipal: true);

		// Assert
		accessor.Current.Should().BeNull(
			because: "an unauthenticated request must never have its credential header captured once authorization is configured (FR-07)");
		nextCalled.Should().BeTrue(
			because: "the header is stripped, not rejected — the request still proceeds down the pipeline (AC-03)");
		context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
			because: "ignoring the header is not an error condition; nothing is short-circuited here");
	}

	[Test]
	[Description("FR-07 positive case: when authorization is configured AND the request carries an authenticated principal, the credential header is captured exactly as before Story 6.")]
	public async Task CaptureCredentialContext_ShouldCaptureContext_WhenAuthorizationRequiredAndPrincipalAuthenticated() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context = CreateAuthorizedPassthroughContext(authenticated: true);
		ICredentialContextAccessor accessor = AccessorFor(context);
		RequestDelegate next = _ => Task.CompletedTask;

		// Act
		await McpHttpServerCommand.CaptureCredentialContext(
			context, next, parser, accessor, HeaderName, requireAuthenticatedPrincipal: true);

		// Assert
		accessor.Current.Should().NotBeNull(
			because: "an authenticated request's credential header must still be captured when authorization is enabled");
		accessor.Current!.Url.Should().Be("https://acme.creatio.com",
			because: "the parsed target url must flow onto the captured context exactly as without Story 6's gate");
		accessor.Current.IsNetCore.Should().BeFalse(
			because: "the authenticated passthrough context must retain the explicit runtime value");
	}

	[Test]
	[Description("Backward compatibility: when authorization is NOT configured (requireAuthenticatedPrincipal=false), the credential header is captured regardless of the (default-anonymous) principal — the pre-ENG-93386 platform-API-key-only behavior is unaffected.")]
	public async Task CaptureCredentialContext_ShouldCaptureContext_WhenAuthorizationNotRequired_RegardlessOfPrincipal() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context = CreateAuthorizedPassthroughContext(authenticated: false);
		ICredentialContextAccessor accessor = AccessorFor(context);
		RequestDelegate next = _ => Task.CompletedTask;

		// Act
		await McpHttpServerCommand.CaptureCredentialContext(
			context, next, parser, accessor, HeaderName, requireAuthenticatedPrincipal: false);

		// Assert
		accessor.Current.Should().NotBeNull(
			because: "with standard OAuth not configured, passthrough continues to be gated solely by the platform-API-key gate, unchanged from before this feature");
	}

	[Test]
	[Description("FR-09 invariant: the outbound EnvironmentSettings built from a captured passthrough context never carries the inbound gateway/platform Authorization header value — only the X-Integration-Credentials-derived token becomes the Creatio credential.")]
	public async Task BuildEphemeralSettings_ShouldNeverCarryInboundAuthorizationHeaderValue() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context = CreateAuthorizedPassthroughContext(authenticated: true);
		ICredentialContextAccessor accessor = AccessorFor(context);
		RequestDelegate next = _ => Task.CompletedTask;
		await McpHttpServerCommand.CaptureCredentialContext(
			context, next, parser, accessor, HeaderName, requireAuthenticatedPrincipal: true);

		// Act
		EnvironmentSettings settings = ToolCommandResolver.BuildEphemeralSettings(accessor.Current);

		// Assert
		settings.AccessToken.Should().Be(SecretAccessToken,
			because: "the outbound Creatio credential must come solely from the X-Integration-Credentials header");
		settings.AccessToken.Should().NotBe(GatewayAuthorizationValue,
			because: "the MCP/gateway plane's Authorization header value must never become the Creatio-plane credential (FR-09)");
		context.Request.Headers.Authorization.ToString().Should().Be(GatewayAuthorizationValue,
			because: "sanity check: the inbound Authorization header genuinely differs from the outbound credential, so a passing assertion above is not a false negative");
	}
}
