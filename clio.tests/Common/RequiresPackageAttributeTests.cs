using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class RequiresPackageAttributeTests {

	#region Nested types: Test fixtures

	private sealed class PropertyOnlyDecoratedOptions {
		[RequiresPackage("cliogate")]
		public bool UseGatedPath { get; set; }
	}

	private sealed class NoRequirementOptions {
		public bool SomeFlag { get; set; }
	}

	#endregion

	#region Methods: Public

	[Test]
	[Description("IsDefinedOn returns true for a type whose ONLY [RequiresPackage] sits on a property (landmine guard: a property-only decoration must not be silently skipped by the cheap pre-check).")]
	public void IsDefinedOn_ShouldReturnTrue_WhenOnlyAPropertyIsDecorated() {
		// Arrange
		// (the PropertyOnlyDecoratedOptions type carries no class-level attribute)

		// Act
		bool actual = RequiresPackageAttribute.IsDefinedOn(typeof(PropertyOnlyDecoratedOptions));

		// Assert
		actual.Should().BeTrue(
			because: "a property-only [RequiresPackage] must be detected by the pre-check, otherwise the MCP gate silently skips enforcement");
	}

	[Test]
	[Description("IsDefinedOn returns false for a type with no [RequiresPackage] on the class or any property.")]
	public void IsDefinedOn_ShouldReturnFalse_WhenNeitherClassNorPropertyIsDecorated() {
		// Arrange

		// Act
		bool actual = RequiresPackageAttribute.IsDefinedOn(typeof(NoRequirementOptions));

		// Assert
		actual.Should().BeFalse(
			because: "a type with no class-level and no property-level requirement must stay zero-cost");
	}

	[Test]
	[Description("pull-pkg carries a presence-only cliogate requirement on the canonical 'Async' property (not the hidden alias).")]
	public void PullPkgOptions_ShouldDecorateCanonicalAsyncProperty_WithCliogateRequirement() {
		// Arrange
		PropertyInfo canonical = typeof(PullPkgOptions).GetProperty(nameof(PullPkgOptions.Async));
		PropertyInfo alias = typeof(PullPkgOptions).GetProperty(nameof(PullPkgOptions.AsyncAlias));

		// Act
		RequiresPackageAttribute[] canonicalAttrs = (RequiresPackageAttribute[])
			canonical!.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);
		RequiresPackageAttribute[] aliasAttrs = (RequiresPackageAttribute[])
			alias!.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);

		// Assert
		canonicalAttrs.Should().ContainSingle(
				because: "the async download path requires cliogate")
			.Which.Name.Should().Be("cliogate",
				because: "the async pull-pkg path hits the cliogate PackagesGateway endpoint");
		canonicalAttrs.Single().Version.Should().BeNullOrEmpty(
			because: "the async download requirement is presence-only — no minimum version was ever enforced");
		aliasAttrs.Should().BeEmpty(
			because: "the requirement must sit on the canonical property, never on the hidden alias");
	}

	[Test]
	[Description("set-feature carries a presence-only cliogate requirement on the canonical 'UseFeatureWebService' property (not the hidden alias).")]
	public void FeatureOptions_ShouldDecorateCanonicalUseFeatureWebServiceProperty_WithCliogateRequirement() {
		// Arrange
		PropertyInfo canonical = typeof(FeatureOptions).GetProperty(nameof(FeatureOptions.UseFeatureWebService));
		PropertyInfo alias = typeof(FeatureOptions).GetProperty(nameof(FeatureOptions.UseFeatureWebServiceAlias));

		// Act
		RequiresPackageAttribute[] canonicalAttrs = (RequiresPackageAttribute[])
			canonical!.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);
		RequiresPackageAttribute[] aliasAttrs = (RequiresPackageAttribute[])
			alias!.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);

		// Assert
		canonicalAttrs.Should().ContainSingle(
				because: "the feature-web-service path requires cliogate")
			.Which.Name.Should().Be("cliogate",
				because: "the --use-feature-web-service path hits the cliogate FeatureStateService endpoint");
		canonicalAttrs.Single().Version.Should().BeNullOrEmpty(
			because: "the feature-web-service requirement is presence-only");
		aliasAttrs.Should().BeEmpty(
			because: "the requirement must sit on the canonical property, never on the hidden alias");
	}

	[Ignore("RequiresPackageAttribute cliogate requirement was added in error, pushw command does not require cliogate of any version")]
	[Test]
	[Description("push-workspace carries a single class-level versioned cliogate requirement (name cliogate, version 2.0.0.0).")]
	public void PushWorkspaceCommandOptions_ShouldDecorateClass_WithVersionedCliogateRequirement() {
		// Arrange

		// Act
		RequiresPackageAttribute[] classAttrs = (RequiresPackageAttribute[])
			typeof(PushWorkspaceCommandOptions).GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);

		// Assert
		classAttrs.Should().ContainSingle(
				because: "push-workspace installs through the cliogate-backed workspace path and must declare exactly one class-level requirement")
			.Which.Name.Should().Be("cliogate",
				because: "the class-level requirement names cliogate");
		classAttrs.Single().Version.Should().Be("2.0.0.0",
			because: "push-workspace needs cliogate 2.0.0.0 or newer, so the minimum version must be locked in");
	}

	[Test]
	[Description("publish-app carries NO [RequiresPackage] on the class or any property (it must stay ungated).")]
	public void PublishWorkspaceCommandOptions_ShouldNotBeDecorated_WithAnyRequirement() {
		// Arrange

		// Act
		bool actual = RequiresPackageAttribute.IsDefinedOn(typeof(PublishWorkspaceCommandOptions));

		// Assert
		actual.Should().BeFalse(
			because: "publish-app produces a zip from a local workspace and contacts no cliogate endpoint, so any future class- or property-level gate would be a regression");
	}

	[Test]
	[Description("listen carries an unconditional class-level cliogate requirement.")]
	public void ListenOptions_ShouldDecorateClass_WithCliogateRequirement() {
		// Arrange

		// Act
		RequiresPackageAttribute[] classAttrs = (RequiresPackageAttribute[])
			typeof(ListenOptions).GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);

		// Assert
		classAttrs.Should().ContainSingle(
				because: "listen unconditionally calls the cliogate ATFLogService endpoint")
			.Which.Name.Should().Be("cliogate",
				because: "the class-level requirement names cliogate");
	}

	#endregion

}
