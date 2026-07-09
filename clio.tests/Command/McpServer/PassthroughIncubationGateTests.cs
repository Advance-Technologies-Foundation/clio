using Clio.Command;
using Clio.Command.McpServer;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PassthroughIncubationGateTests
{
	private const string FeatureName = "mcp-http-credential-passthrough";
	private const string PlatformKey = "primary-platform-key";

	[Test]
	[Description("ShouldEnablePassthrough is false (passthrough middleware not wired) when the incubation flag is disabled.")]
	public void ShouldEnablePassthrough_ShouldReturnFalse_WhenIncubationFlagDisabled() {
		// Arrange
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsFeatureEnabled(FeatureName).Returns(false);

		// Act
		bool wired = McpHttpServerCommand.ShouldEnablePassthrough(featureToggleService);

		// Assert
		wired.Should().BeFalse(
			because: "with the incubation flag off (default) the passthrough leg must not be wired, so the "
				+ "credential header is ignored and the verb/stdio/-e behave as pre-passthrough (AC-01)");
	}

	[Test]
	[Description("ShouldEnablePassthrough is true (passthrough middleware wired) when the incubation flag is enabled.")]
	public void ShouldEnablePassthrough_ShouldReturnTrue_WhenIncubationFlagEnabled() {
		// Arrange
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsFeatureEnabled(FeatureName).Returns(true);

		// Act
		bool wired = McpHttpServerCommand.ShouldEnablePassthrough(featureToggleService);

		// Assert
		wired.Should().BeTrue(
			because: "an enabled incubation flag wires the passthrough leg, still gated at request time "
				+ "by the api-key gate (AC-02)");
	}

	[Test]
	[Description("ShouldEnablePassthrough queries the incubation flag by its stable feature key, not the verb.")]
	public void ShouldEnablePassthrough_ShouldQueryByFeatureKey_WhenEvaluatingGate() {
		// Arrange
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsFeatureEnabled(FeatureName).Returns(true);

		// Act
		McpHttpServerCommand.ShouldEnablePassthrough(featureToggleService);

		// Assert
		featureToggleService.Received(1).IsFeatureEnabled(
			McpHttpServerCommand.CredentialPassthroughFeatureName);
		McpHttpServerCommand.CredentialPassthroughFeatureName.Should().Be(FeatureName,
			because: "the incubation gate is keyed on a stable feature key decoupled from the verb, so the "
				+ "mcp-http verb stays available while only the passthrough behavior is gated (AC-03)");
	}

	[Test]
	[Description("ShouldEnablePassthrough is false when the feature-toggle service is unavailable, failing closed.")]
	public void ShouldEnablePassthrough_ShouldReturnFalse_WhenServiceIsNull() {
		// Arrange & Act
		bool wired = McpHttpServerCommand.ShouldEnablePassthrough(null);

		// Assert
		wired.Should().BeFalse(
			because: "an absent feature-toggle service must fail closed so passthrough is never wired by "
				+ "accident (AC-01)");
	}

	[Test]
	[Description("Passthrough is honored only when BOTH the incubation flag is enabled AND a platform api-key is configured (doubly-gated).")]
	public void Passthrough_ShouldBeHonored_OnlyWhenFlagEnabledAndKeyConfigured() {
		// Arrange
		IFeatureToggleService flagOn = Substitute.For<IFeatureToggleService>();
		flagOn.IsFeatureEnabled(FeatureName).Returns(true);
		IFeatureToggleService flagOff = Substitute.For<IFeatureToggleService>();
		flagOff.IsFeatureEnabled(FeatureName).Returns(false);

		IPlatformApiKeyGate gateWithKey = new PlatformApiKeyGate([PlatformKey]);
		IPlatformApiKeyGate gateWithoutKey = new PlatformApiKeyGate([]);

		// Act: "honored" requires gate 1 (wiring, incubation flag) AND gate 2 (request-time api-key).
		bool honoredFlagOnKeyOn =
			McpHttpServerCommand.ShouldEnablePassthrough(flagOn) && gateWithKey.PassthroughEnabled;
		bool honoredFlagOnKeyOff =
			McpHttpServerCommand.ShouldEnablePassthrough(flagOn) && gateWithoutKey.PassthroughEnabled;
		bool honoredFlagOffKeyOn =
			McpHttpServerCommand.ShouldEnablePassthrough(flagOff) && gateWithKey.PassthroughEnabled;

		// Assert
		honoredFlagOnKeyOn.Should().BeTrue(
			because: "passthrough is honored only when both gates open: incubation flag enabled AND a "
				+ "platform api-key configured (AC-04)");
		honoredFlagOnKeyOff.Should().BeFalse(
			because: "an enabled flag without a configured api-key leaves passthrough dormant — the second "
				+ "gate (api-key) is still required (AC-04)");
		honoredFlagOffKeyOn.Should().BeFalse(
			because: "a configured api-key cannot enable passthrough while the incubation flag is off — the "
				+ "first gate (flag) is still required (AC-04)");
	}
}
