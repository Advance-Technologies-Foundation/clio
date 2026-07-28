using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-93386 Story 8 (final-review fix) unit coverage for
/// <see cref="McpHttpServerCommand.EvaluateAudienceScopeGuard"/>: standard OAuth authorization enabled
/// with NEITHER an accepted audience NOR a required scope configured must REFUSE to start by default
/// (security-first) — the adversarial review found this combination silently disabled audience
/// validation, letting the endpoint accept any token the configured authority ever mints for any
/// client/resource. The operator can only proceed via the explicit <c>--auth-allow-any-audience</c>
/// opt-in, mirroring <see cref="McpHttpServerCommand.EvaluatePublicBindGuard"/>'s posture.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class AudienceScopeGuardTests
{
	[Test]
	[Description("Authorization disabled is never gated, regardless of audience/scope configuration.")]
	public void EvaluateAudienceScopeGuard_ShouldReturnOk_WhenAuthorizationDisabled() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluateAudienceScopeGuard(
				authorizationEnabled: false, audienceCount: 0, requiredScopeCount: 0, allowAnyAudience: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Ok,
			because: "with authorization off there is no token-validation posture to gate");
	}

	[Test]
	[Description("Authorization enabled with at least one configured audience is never gated, even with no required scopes.")]
	public void EvaluateAudienceScopeGuard_ShouldReturnOk_WhenAudienceConfigured() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluateAudienceScopeGuard(
				authorizationEnabled: true, audienceCount: 1, requiredScopeCount: 0, allowAnyAudience: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Ok,
			because: "audience validation alone already restricts acceptance to tokens minted for this resource");
	}

	[Test]
	[Description("Authorization enabled with at least one required scope is never gated, even with no configured audience.")]
	public void EvaluateAudienceScopeGuard_ShouldReturnOk_WhenRequiredScopeConfigured() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluateAudienceScopeGuard(
				authorizationEnabled: true, audienceCount: 0, requiredScopeCount: 1, allowAnyAudience: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Ok,
			because: "a required scope already restricts acceptance to tokens carrying that scope");
	}

	[Test]
	[Description("Authorization enabled with NEITHER an audience NOR a required scope, and no explicit opt-in, REFUSES to start (security-first default).")]
	public void EvaluateAudienceScopeGuard_ShouldReturnRefuse_WhenBothUnconfiguredAndNoOptIn() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluateAudienceScopeGuard(
				authorizationEnabled: true, audienceCount: 0, requiredScopeCount: 0, allowAnyAudience: false);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Refuse,
			because: "with neither restriction configured, the endpoint would accept any token the authority ever mints for any client/resource");
	}

	[Test]
	[Description("Authorization enabled with NEITHER an audience NOR a required scope, when the operator explicitly opts in, WARNS but proceeds.")]
	public void EvaluateAudienceScopeGuard_ShouldReturnWarn_WhenOptedIn() {
		// Arrange & Act
		McpHttpServerCommand.PublicBindGuardOutcome outcome =
			McpHttpServerCommand.EvaluateAudienceScopeGuard(
				authorizationEnabled: true, audienceCount: 0, requiredScopeCount: 0, allowAnyAudience: true);

		// Assert
		outcome.Should().Be(McpHttpServerCommand.PublicBindGuardOutcome.Warn,
			because: "the operator explicitly accepted the risk via --auth-allow-any-audience");
	}
}
