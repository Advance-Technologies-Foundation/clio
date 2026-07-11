using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-93386 Story 5 (OQ-A) + Story 8 (final-review fix) unit coverage for
/// <see cref="McpHttpServerCommand.EvaluatePublicBindGuard"/> and <see cref="McpHttpServerCommand.IsPublicBind"/>:
/// a reachable (public/wildcard OR any concrete non-loopback) <c>--host</c> combined with authorization
/// OFF must REFUSE to start by default (security-first), and only proceed (with a loud warning) when the
/// operator explicitly opts in via <c>--allow-insecure-public</c>. The final adversarial review for Story
/// 8 found that the guard originally only recognized the four literal wildcard spellings
/// (<c>0.0.0.0</c>/<c>*</c>/<c>::</c>/<c>[::]</c>) — a bind to a concrete LAN/public IP or DNS hostname
/// silently bypassed it. <see cref="IsPublicBind"/> now covers any non-loopback host.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PublicBindGuardTests
{
	[Test]
	[Description("A loopback host is never gated, regardless of authorization state.")]
	public void EvaluatePublicBindGuard_ShouldReturnOk_WhenHostIsNotPublic() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluatePublicBindGuard(
				isPublicBind: false, authorizationEnabled: false, allowInsecurePublic: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Ok,
			because: "a private bind has no public-exposure risk to gate");
	}

	[Test]
	[Description("A wildcard host with authorization enabled is never gated.")]
	public void EvaluatePublicBindGuard_ShouldReturnOk_WhenAuthorizationEnabled() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluatePublicBindGuard(
				isPublicBind: true, authorizationEnabled: true, allowInsecurePublic: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Ok,
			because: "every request is already protected by standard OAuth authorization");
	}

	[Test]
	[Description("A wildcard host with authorization off and no explicit opt-in REFUSES to start (security-first default).")]
	public void EvaluatePublicBindGuard_ShouldReturnRefuse_WhenWildcardHostAndAuthDisabledAndNoOptIn() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluatePublicBindGuard(
				isPublicBind: true, authorizationEnabled: false, allowInsecurePublic: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Refuse,
			because: "an unauthenticated public bind would expose every registered environment's stored credentials");
	}

	[Test]
	[Description("A wildcard host with authorization off, when the operator explicitly opts in, WARNS but proceeds.")]
	public void EvaluatePublicBindGuard_ShouldReturnWarn_WhenOptedIn() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluatePublicBindGuard(
				isPublicBind: true, authorizationEnabled: false, allowInsecurePublic: true);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Warn,
			because: "the operator explicitly accepted the risk via --allow-insecure-public");
	}

	[Test]
	[Description("IsPublicBind treats the literal wildcard spellings as public (unchanged from before the Story 8 fix).")]
	[TestCase("0.0.0.0")]
	[TestCase("*")]
	[TestCase("::")]
	[TestCase("[::]")]
	public void IsPublicBind_ShouldReturnTrue_ForWildcardSpellings(string host) {
		// Arrange & Act
		bool result = McpHttpServerCommand.IsPublicBind(host);

		// Assert
		result.Should().BeTrue(because: $"'{host}' is a wildcard bind reachable from any interface");
	}

	[Test]
	[Description("Story 8 final-review fix: IsPublicBind treats a concrete non-loopback host (a LAN/public IP or DNS name) as public too -- the original guard silently missed this and would have started unauthenticated.")]
	[TestCase("10.0.0.5")]
	[TestCase("203.0.113.7")]
	[TestCase("mcp.example.com")]
	public void IsPublicBind_ShouldReturnTrue_ForConcreteNonLoopbackHost(string host) {
		// Arrange & Act
		bool result = McpHttpServerCommand.IsPublicBind(host);

		// Assert
		result.Should().BeTrue(
			because: $"'{host}' is reachable by anyone who can route to it, exactly like a wildcard bind, and must not silently bypass the guard");
	}

	[Test]
	[Description("IsPublicBind treats every recognized loopback alias as NOT public.")]
	[TestCase("localhost")]
	[TestCase("127.0.0.1")]
	[TestCase("[::1]")]
	[TestCase("::1")]
	public void IsPublicBind_ShouldReturnFalse_ForLoopbackAliases(string host) {
		// Arrange & Act
		bool result = McpHttpServerCommand.IsPublicBind(host);

		// Assert
		result.Should().BeFalse(because: $"'{host}' is only reachable from this machine itself");
	}

	[Test]
	[Description("Codex review fix: IsPublicBind treats any other address in the loopback range (127.0.0.0/8) as NOT public too, not just the fixed 127.0.0.1 alias -- a first-pass fix using only the narrow alias check would have misclassified these as public and refused a harmless local bind.")]
	[TestCase("127.0.0.2")]
	[TestCase("127.255.255.254")]
	public void IsPublicBind_ShouldReturnFalse_ForOtherLoopbackRangeAddresses(string host) {
		// Arrange & Act
		bool result = McpHttpServerCommand.IsPublicBind(host);

		// Assert
		result.Should().BeFalse(
			because: $"'{host}' is within the 127.0.0.0/8 loopback range and is not reachable from outside this machine");
	}
}
