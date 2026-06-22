using System;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class FeatureToggleFilterTests {

	private IFeatureToggleService _featureToggleService;

	[SetUp]
	public void SetUp() {
		_featureToggleService = Substitute.For<IFeatureToggleService>();
	}

	private sealed class UngatedTypeA { }

	private sealed class UngatedTypeB { }

	[FeatureToggle("gated-feature")]
	private sealed class GatedType { }

	[Test]
	[Description("GetEnabled excludes a gated type when its IsEnabled check returns false.")]
	public void GetEnabled_ShouldExcludeGatedType_WhenFeatureDisabled() {
		// Arrange
		Type[] types = [typeof(UngatedTypeA), typeof(GatedType), typeof(UngatedTypeB)];
		_featureToggleService.IsEnabled(typeof(UngatedTypeA)).Returns(true);
		_featureToggleService.IsEnabled(typeof(UngatedTypeB)).Returns(true);
		_featureToggleService.IsEnabled(typeof(GatedType)).Returns(false);

		// Act
		Type[] result = FeatureToggleFilter.GetEnabled(types, _featureToggleService);

		// Assert
		result.Should().NotContain(typeof(GatedType),
			because: "a gated type whose feature flag is off must be filtered out of the enabled set");
		result.Should().Contain(typeof(UngatedTypeA),
			because: "ungated types are always enabled regardless of any feature flag");
		result.Should().Contain(typeof(UngatedTypeB),
			because: "ungated types are always enabled regardless of any feature flag");
	}

	[Test]
	[Description("GetEnabled includes a gated type when its IsEnabled check returns true.")]
	public void GetEnabled_ShouldIncludeGatedType_WhenFeatureEnabled() {
		// Arrange
		Type[] types = [typeof(UngatedTypeA), typeof(GatedType)];
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(true);

		// Act
		Type[] result = FeatureToggleFilter.GetEnabled(types, _featureToggleService);

		// Assert
		result.Should().Contain(typeof(GatedType),
			because: "a gated type whose feature flag is on must be present in the enabled set");
		result.Should().HaveCount(2,
			because: "all supplied types are enabled when the service reports every type as enabled");
	}

	[Test]
	[Description("GetEnabled keeps all ungated types and preserves their input order.")]
	public void GetEnabled_ShouldKeepAllUngatedTypesInOrder_WhenNoTypeIsGated() {
		// Arrange
		Type[] types = [typeof(UngatedTypeB), typeof(UngatedTypeA)];
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(true);

		// Act
		Type[] result = FeatureToggleFilter.GetEnabled(types, _featureToggleService);

		// Assert
		result.Should().Equal([typeof(UngatedTypeB), typeof(UngatedTypeA)],
			because: "the filter must preserve the input order of enabled types");
	}

	[Test]
	[Description("GetEnabled delegates the enabled decision entirely to the supplied service predicate.")]
	public void GetEnabled_ShouldDelegateToService_WhenEvaluatingEachType() {
		// Arrange
		Type[] types = [typeof(UngatedTypeA), typeof(GatedType)];
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(true);

		// Act
		_ = FeatureToggleFilter.GetEnabled(types, _featureToggleService);

		// Assert
		_featureToggleService.Received(1).IsEnabled(typeof(UngatedTypeA));
		_featureToggleService.Received(1).IsEnabled(typeof(GatedType));
	}

	[Test]
	[Description("GetEnabled throws ArgumentNullException when the types argument is null.")]
	public void GetEnabled_ShouldThrowArgumentNullException_WhenTypesIsNull() {
		// Arrange
		// (no arrange beyond the null argument)

		// Act
		Action act = () => FeatureToggleFilter.GetEnabled(null, _featureToggleService);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the filter cannot evaluate a null type collection");
	}

	[Test]
	[Description("GetEnabled throws ArgumentNullException when the feature toggle service is null.")]
	public void GetEnabled_ShouldThrowArgumentNullException_WhenServiceIsNull() {
		// Arrange
		Type[] types = [typeof(UngatedTypeA)];

		// Act
		Action act = () => FeatureToggleFilter.GetEnabled(types, null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the filter has no predicate to evaluate enablement without a service");
	}
}
