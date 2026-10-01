using System;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Category("Unit"), Property("Module", "Command")]
public class FeatureToggleAttributeTests {
	[TestCase(ExperimentalFeature.DeployIdentity, "deploy-identity")]
	[TestCase(ExperimentalFeature.MobilePageConverter, "mobile-page-converter")]
	[TestCase(ExperimentalFeature.Ring, "ring")]
	[TestCase(ExperimentalFeature.Runtime, "runtime")]
	[TestCase(ExperimentalFeature.WatchCompilation, "watch-compilation")]
	[TestCase(ExperimentalFeature.KnowledgeAllowUnsequenced, "knowledge-allow-unsequenced")]
	[Description("Enum-backed attributes preserve the existing persisted feature names.")]
	public void PreservesExternalNames(ExperimentalFeature feature, string key) {
		// Arrange / Act
		var attribute = new FeatureToggleAttribute(feature);
		// Assert
		attribute.Feature.Should().Be(feature, because: "code uses the typed feature identity");
		attribute.FeatureName.Should().Be(key, because: "existing CLI and saved settings must remain compatible");
	}

	[Test]
	[Description("Undefined enum values cannot silently introduce feature flags.")]
	public void RejectsUnknownFeature() {
		// Arrange / Act
		Action act = () => _ = new FeatureToggleAttribute((ExperimentalFeature)999);
		// Assert
		act.Should().Throw<ArgumentOutOfRangeException>(because: "features must be centrally declared");
	}
}
