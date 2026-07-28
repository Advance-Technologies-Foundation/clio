using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-93386 Story 7 (FR-12/D-6) unit coverage for the retired <c>--platform-api-key</c> disposition:
/// once standard OAuth authorization is configured, the legacy gate is bypassed entirely rather than
/// combined with it (the two schemes cannot coexist on the same <c>Authorization</c> header), and the
/// operator is warned loudly if both are configured together so the now-inert key is never a silent
/// misconfiguration.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PlatformApiKeyDispositionTests
{
	private const string HeaderName = "X-Integration-Credentials";
	private const string PlatformKey = "platform-key-abc123";

	private static DefaultHttpContext CreateContext(bool authenticated, string authorization = null) {
		DefaultHttpContext context = new() {
			RequestServices = new ServiceCollection().BuildServiceProvider()
		};
		context.Response.Body = new MemoryStream();
		if (authorization is not null) {
			context.Request.Headers.Authorization = authorization;
		}
		context.User = authenticated
			? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"))
			: new ClaimsPrincipal(new ClaimsIdentity());
		return context;
	}

	[Test]
	[Description("With no platform API key and no OAuth authority configured, the key is not being ignored by anything -- there is nothing to warn about.")]
	public void ShouldWarnPlatformApiKeyIgnored_ShouldReturnFalse_WhenNeitherIsConfigured() {
		// Arrange & Act
		bool result = McpHttpServerCommand.ShouldWarnPlatformApiKeyIgnored(authorizationEnabled: false, platformApiKeyCount: 0);

		// Assert
		result.Should().BeFalse(because: "with no OAuth and no key, there is no inert configuration to flag");
	}

	[Test]
	[Description("A configured key with no OAuth authority is the supported dev/offline fallback mode -- no warning.")]
	public void ShouldWarnPlatformApiKeyIgnored_ShouldReturnFalse_WhenOnlyPlatformApiKeyConfigured() {
		// Arrange & Act
		bool result = McpHttpServerCommand.ShouldWarnPlatformApiKeyIgnored(authorizationEnabled: false, platformApiKeyCount: 1);

		// Assert
		result.Should().BeFalse(because: "the platform API key is the retained dev/offline fallback when OAuth is not configured (D-6)");
	}

	[Test]
	[Description("OAuth configured with no platform API key set is the standard, expected public-deployment shape -- no warning.")]
	public void ShouldWarnPlatformApiKeyIgnored_ShouldReturnFalse_WhenOnlyAuthorizationConfigured() {
		// Arrange & Act
		bool result = McpHttpServerCommand.ShouldWarnPlatformApiKeyIgnored(authorizationEnabled: true, platformApiKeyCount: 0);

		// Assert
		result.Should().BeFalse(because: "OAuth-only is the intended standard shape and needs no warning");
	}

	[Test]
	[Description("Both OAuth authorization and a platform API key configured together must warn -- the key becomes inert (FR-12/D-6).")]
	public void ShouldWarnPlatformApiKeyIgnored_ShouldReturnTrue_WhenBothConfigured() {
		// Arrange & Act
		bool result = McpHttpServerCommand.ShouldWarnPlatformApiKeyIgnored(authorizationEnabled: true, platformApiKeyCount: 2);

		// Assert
		result.Should().BeTrue(because: "the operator must be told the configured key is now ignored, not silently redundant");
	}

	[Test]
	[Description("When OAuth is enabled, an authenticated request is passthrough-eligible regardless of the platform-api-key gate state -- no key configured, no Authorization-bearer-key match required.")]
	public async Task EnforcePlatformApiKeyGate_ShouldEnablePassthrough_WhenAuthorizationEnabledAndPrincipalAuthenticated() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([]);
		DefaultHttpContext context = CreateContext(authenticated: true);
		bool nextCalled = false;
		RequestDelegate next = _ => {
			nextCalled = true;
			return Task.CompletedTask;
		};

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(
			context, next, gate, HeaderName, authorizationEnabled: true);

		// Assert
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(true,
			because: "with OAuth enabled, RequireAuthorization already guaranteed the principal -- the legacy key gate is bypassed (FR-12)");
		nextCalled.Should().BeTrue(because: "the request must proceed to the credential-capture middleware");
	}

	[Test]
	[Description("When OAuth is enabled, a request with a VALID platform-api-key but an UNAUTHENTICATED principal must NOT get passthrough -- the legacy key can never re-enable what OAuth denied.")]
	public async Task EnforcePlatformApiKeyGate_ShouldDisablePassthrough_WhenAuthorizationEnabledButPrincipalUnauthenticated_EvenWithMatchingKey() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		DefaultHttpContext context = CreateContext(authenticated: false, authorization: $"Bearer {PlatformKey}");
		bool nextCalled = false;
		RequestDelegate next = _ => {
			nextCalled = true;
			return Task.CompletedTask;
		};

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(
			context, next, gate, HeaderName, authorizationEnabled: true);

		// Assert
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(false,
			because: "a matching legacy key must not substitute for a missing OAuth principal -- combining the two must never WEAKEN OAuth (D-6)");
		nextCalled.Should().BeTrue(because: "the request is not short-circuited here; RequireAuthorization already handled rejection upstream");
	}

	[Test]
	[Description("With OAuth disabled, EnforcePlatformApiKeyGate behaves exactly as before ENG-93386 -- an unmatched key still short-circuits with 401.")]
	public async Task EnforcePlatformApiKeyGate_ShouldPreserveLegacyBehavior_WhenAuthorizationDisabled() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		DefaultHttpContext context = CreateContext(authenticated: false, authorization: "Bearer wrong-key");
		context.Request.Headers[HeaderName] = "irrelevant-for-this-test";
		bool nextCalled = false;
		RequestDelegate next = _ => {
			nextCalled = true;
			return Task.CompletedTask;
		};

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(
			context, next, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
			because: "the dev/offline fallback path must reject a mismatched key exactly as before this story");
		nextCalled.Should().BeFalse(because: "a rejected legacy request must short-circuit, never forwarded");
	}
}
