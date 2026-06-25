using Clio.Command;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProgramFeatureToggleGateTests {

	private IFeatureToggleService _featureToggleService;

	[SetUp]
	public void SetUp() {
		_featureToggleService = Substitute.For<IFeatureToggleService>();
	}

	[Verb("gated-verb")]
	[FeatureToggle("experimental-feature")]
	private sealed class GatedOptions { }

	private sealed class UngatedOptions { }

	[Test]
	[Description("TryGetDisabledFeatureName refuses dispatch and yields the feature key when the gated type's flag is off.")]
	public void TryGetDisabledFeatureName_ShouldReturnTrueWithFeatureName_WhenFlagOff() {
		// Arrange
		GatedOptions options = new();
		_featureToggleService.IsEnabled(typeof(GatedOptions)).Returns(false);

		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(options, _featureToggleService, out string featureName);

		// Assert
		blocked.Should().BeTrue(
			because: "a gated options type whose feature flag is off must be refused at the dispatch chokepoint");
		featureName.Should().Be("experimental-feature",
			because: "the disabled feature key is derived from the [FeatureToggle] attribute for the error message");
	}

	[Test]
	[Description("TryGetDisabledFeatureName allows dispatch when the gated type's flag is on.")]
	public void TryGetDisabledFeatureName_ShouldReturnFalse_WhenFlagOn() {
		// Arrange
		GatedOptions options = new();
		_featureToggleService.IsEnabled(typeof(GatedOptions)).Returns(true);

		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(options, _featureToggleService, out string featureName);

		// Assert
		blocked.Should().BeFalse(
			because: "a gated options type whose feature flag is on must be allowed to dispatch");
		featureName.Should().BeNull(
			because: "no feature name is reported when dispatch is permitted");
	}

	[Test]
	[Description("TryGetDisabledFeatureName allows dispatch for an ungated options type without consulting the flag value.")]
	public void TryGetDisabledFeatureName_ShouldReturnFalse_WhenTypeIsUngated() {
		// Arrange
		UngatedOptions options = new();
		_featureToggleService.IsEnabled(typeof(UngatedOptions)).Returns(true);

		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(options, _featureToggleService, out string featureName);

		// Assert
		blocked.Should().BeFalse(
			because: "an ungated options type is always reachable on every dispatch path");
		featureName.Should().BeNull(
			because: "an ungated type produces no disabled-feature name");
	}

	[Test]
	[Description("TryGetDisabledFeatureName delegates the enablement decision to IFeatureToggleService.IsEnabled.")]
	public void TryGetDisabledFeatureName_ShouldDelegateToService_WhenEvaluatingType() {
		// Arrange
		GatedOptions options = new();
		_featureToggleService.IsEnabled(Arg.Any<System.Type>()).Returns(false);

		// Act
		_ = Clio.Program.TryGetDisabledFeatureName(options, _featureToggleService, out _);

		// Assert
		_featureToggleService.Received(1).IsEnabled(typeof(GatedOptions));
	}
}
