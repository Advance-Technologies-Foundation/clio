using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class FeatureToggleServiceTests {

	private ISettingsRepository _settingsRepository;
	private FeatureToggleService _sut;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_sut = new FeatureToggleService(_settingsRepository);
	}

	[FeatureToggle("alpha-feature")]
	private sealed class GatedType { }

	[FeatureToggle("alpha-feature")]
	private sealed class SecondGatedTypeSameKey { }

	[FeatureToggle("beta-feature")]
	private sealed class BetaGatedType { }

	[FeatureToggle("ALPHA-FEATURE")]
	private sealed class AlphaGatedTypeUpperCaseKey { }

	private sealed class UngatedType { }

	[Test]
	[Description("IsEnabled returns true for a type that carries no FeatureToggleAttribute.")]
	public void IsEnabled_ShouldReturnTrue_WhenTypeHasNoAttribute() {
		// Arrange
		Type type = typeof(UngatedType);

		// Act
		bool result = _sut.IsEnabled(type);

		// Assert
		result.Should().BeTrue(because: "a type without a feature toggle attribute is always enabled");
		_settingsRepository.DidNotReceive().IsFeatureEnabled(Arg.Any<string>());
	}

	[Test]
	[Description("IsEnabled returns true for a gated type when its feature flag is enabled.")]
	public void IsEnabled_ShouldReturnTrue_WhenAttributePresentAndFlagEnabled() {
		// Arrange
		_settingsRepository.IsFeatureEnabled("alpha-feature").Returns(true);

		// Act
		bool result = _sut.IsEnabled(typeof(GatedType));

		// Assert
		result.Should().BeTrue(because: "the gated type's feature flag is enabled in settings");
	}

	[Test]
	[Description("IsEnabled returns false for a gated type when its feature flag is disabled.")]
	public void IsEnabled_ShouldReturnFalse_WhenAttributePresentAndFlagDisabled() {
		// Arrange
		_settingsRepository.IsFeatureEnabled("alpha-feature").Returns(false);

		// Act
		bool result = _sut.IsEnabled(typeof(GatedType));

		// Assert
		result.Should().BeFalse(because: "the gated type's feature flag is explicitly disabled");
	}

	[Test]
	[Description("IsEnabled returns false for a gated type when its feature flag is absent (defaults to false).")]
	public void IsEnabled_ShouldReturnFalse_WhenAttributePresentAndFlagAbsent() {
		// Arrange
		_settingsRepository.IsFeatureEnabled("alpha-feature").Returns(false);

		// Act
		bool result = _sut.IsEnabled(typeof(GatedType));

		// Assert
		result.Should().BeFalse(because: "an absent feature flag resolves to disabled via the repository");
	}

	[Test]
	[Description("IsEnabled returns false when the supplied type is null.")]
	public void IsEnabled_ShouldReturnFalse_WhenTypeIsNull() {
		// Arrange
		Type type = null;

		// Act
		bool result = _sut.IsEnabled(type);

		// Assert
		result.Should().BeFalse(because: "a null type cannot be evaluated and is treated as not enabled");
	}

	[Test]
	[Description("IsFeatureEnabled delegates to the settings repository.")]
	public void IsFeatureEnabled_ShouldDelegateToRepository_WhenCalled() {
		// Arrange
		_settingsRepository.IsFeatureEnabled("beta-feature").Returns(true);

		// Act
		bool result = _sut.IsFeatureEnabled("beta-feature");

		// Assert
		result.Should().BeTrue(because: "the service must delegate feature lookups to the repository");
		_settingsRepository.Received(1).IsFeatureEnabled("beta-feature");
	}

	[Test]
	[Description("GetCatalog produces one entry per distinct feature key, deduplicating shared keys.")]
	public void GetCatalog_ShouldDedupeByFeatureName_WhenMultipleTypesShareKey() {
		// Arrange
		_settingsRepository.IsFeatureEnabled("alpha-feature").Returns(true);
		_settingsRepository.IsFeatureEnabled("beta-feature").Returns(false);
		Type[] types = [typeof(GatedType), typeof(SecondGatedTypeSameKey), typeof(BetaGatedType), typeof(UngatedType)];

		// Act
		IReadOnlyList<FeatureToggleInfo> catalog = _sut.GetCatalog(types);

		// Assert
		catalog.Should().HaveCount(2, because: "two distinct feature keys exist and the duplicate key is collapsed");
		catalog.Select(c => c.FeatureName).Should().BeEquivalentTo(
			["alpha-feature", "beta-feature"],
			because: "only gated types contribute and each key appears once");
		catalog.Single(c => c.FeatureName == "alpha-feature").Enabled.Should().BeTrue(
			because: "the alpha feature flag is enabled in settings");
		catalog.Single(c => c.FeatureName == "beta-feature").Enabled.Should().BeFalse(
			because: "the beta feature flag is disabled in settings");
	}

	[Test]
	[Description("GetCatalog ignores types that have no FeatureToggleAttribute.")]
	public void GetCatalog_ShouldIgnoreUngatedTypes_WhenTypesHaveNoAttribute() {
		// Arrange
		Type[] types = [typeof(UngatedType)];

		// Act
		IReadOnlyList<FeatureToggleInfo> catalog = _sut.GetCatalog(types);

		// Assert
		catalog.Should().BeEmpty(because: "types without a feature toggle attribute are not part of the catalog");
	}

	[Test]
	[Description("GetCatalog returns an empty list when the supplied type sequence is null.")]
	public void GetCatalog_ShouldReturnEmpty_WhenTypesIsNull() {
		// Arrange
		IEnumerable<Type> types = null;

		// Act
		IReadOnlyList<FeatureToggleInfo> catalog = _sut.GetCatalog(types);

		// Assert
		catalog.Should().BeEmpty(because: "a null type sequence yields an empty catalog rather than throwing");
	}

	[Test]
	[Description("GetCatalog deduplicates feature keys that differ only by casing into a single entry.")]
	public void GetCatalog_ShouldDedupeByFeatureName_WhenKeysDifferOnlyByCasing() {
		// Arrange
		_settingsRepository.IsFeatureEnabled(Arg.Any<string>()).Returns(true);
		Type[] types = [typeof(GatedType), typeof(AlphaGatedTypeUpperCaseKey)];

		// Act
		IReadOnlyList<FeatureToggleInfo> catalog = _sut.GetCatalog(types);

		// Assert
		catalog.Should().HaveCount(1, because: "feature keys are deduplicated case-insensitively so 'alpha-feature' and 'ALPHA-FEATURE' collapse to one entry");
	}

	[Test]
	[Description("Constructor throws ArgumentNullException when the settings repository is null.")]
	public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull() {
		// Arrange
		ISettingsRepository repository = null;

		// Act
		Action act = () => _ = new FeatureToggleService(repository);

		// Assert
		act.Should().Throw<ArgumentNullException>(because: "the service requires a settings repository dependency");
	}
}
