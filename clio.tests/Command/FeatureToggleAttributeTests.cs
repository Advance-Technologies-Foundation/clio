using System;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class FeatureToggleAttributeTests {

	[Test]
	[Description("Constructor exposes the supplied feature name via the FeatureName property.")]
	public void Constructor_ShouldExposeFeatureName_WhenNameIsValid() {
		// Arrange
		const string featureName = "my-feature";

		// Act
		var attribute = new FeatureToggleAttribute(featureName);

		// Assert
		attribute.FeatureName.Should().Be(featureName, because: "the attribute must surface the feature key it was constructed with");
	}

	[Test]
	[Description("Constructor throws ArgumentException when the feature name is null.")]
	public void Constructor_ShouldThrowArgumentException_WhenNameIsNull() {
		// Arrange
		string featureName = null;

		// Act
		Action act = () => _ = new FeatureToggleAttribute(featureName);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a null feature name is not a valid feature key");
	}

	[Test]
	[Description("Constructor throws ArgumentException when the feature name is empty.")]
	public void Constructor_ShouldThrowArgumentException_WhenNameIsEmpty() {
		// Arrange
		string featureName = string.Empty;

		// Act
		Action act = () => _ = new FeatureToggleAttribute(featureName);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an empty feature name is not a valid feature key");
	}

	[Test]
	[Description("Constructor throws ArgumentException when the feature name is whitespace.")]
	public void Constructor_ShouldThrowArgumentException_WhenNameIsWhitespace() {
		// Arrange
		const string featureName = "   ";

		// Act
		Action act = () => _ = new FeatureToggleAttribute(featureName);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a whitespace-only feature name is not a valid feature key");
	}
}
