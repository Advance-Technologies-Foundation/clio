using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-93386 Story 5 (OQ-A) unit coverage for <see cref="McpHttpServerCommand.EvaluatePublicBindGuard"/>:
/// a public/wildcard <c>--host</c> combined with authorization OFF must REFUSE to start by default
/// (security-first), and only proceed (with a loud warning) when the operator explicitly opts in via
/// <c>--allow-insecure-public</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PublicBindGuardTests
{
	[Test]
	[Description("A non-wildcard host (e.g. loopback) is never gated, regardless of authorization state.")]
	public void EvaluatePublicBindGuard_ShouldReturnOk_WhenHostIsNotWildcard() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluatePublicBindGuard(
				isWildcardHost: false, authorizationEnabled: false, allowInsecurePublic: false);

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
				isWildcardHost: true, authorizationEnabled: true, allowInsecurePublic: false);

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
				isWildcardHost: true, authorizationEnabled: false, allowInsecurePublic: false);

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
				isWildcardHost: true, authorizationEnabled: false, allowInsecurePublic: true);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Warn,
			because: "the operator explicitly accepted the risk via --allow-insecure-public");
	}
}
