using System;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the feature-flag merge the MCP e2e suite writes into its suite-owned clio home.
/// </summary>
/// <remarks>
/// This lives in <c>clio.tests</c> (not <c>clio.mcp.e2e</c>) so it runs in the standard pre-merge Unit
/// lane, following the precedent of <c>McpFixturePolicyTests</c>. The merge decides whether a
/// feature-gated MCP tool is advertised to every fixture in that assembly, and its failure is silent —
/// the tool simply goes missing — so it must not be covered only by a full e2e bootstrap that needs a
/// stand (issue #1382).
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SuiteFeatureFlagsTests {

	[Test]
	[Description("Writes the canonical features object with the gated flag enabled when the settings carry no features key at all.")]
	public void Enable_Creates_The_Features_Object_When_None_Exists() {
		// Arrange
		JsonObject root = new() { ["ActiveEnvironmentKey"] = "dev" };

		// Act
		SuiteFeatureFlags.Enable(root, typeof(MobilePageConversionGuideTool));

		// Assert
		root["features"]!.AsObject()["mobile-page-converter"]!.GetValue<bool>().Should().BeTrue(
			because: "the suite must enable the gated tool even on a settings file that declares no features at all");
		root["ActiveEnvironmentKey"]!.GetValue<string>().Should().Be("dev",
			because: "the merge must leave every unrelated settings key untouched");
	}

	[Test]
	[Description("Folds a differently-cased features key into the canonical one, preserving the flags it carried.")]
	public void Enable_Folds_A_DifferentlyCased_Features_Key_And_Keeps_Its_Flags() {
		// Arrange
		JsonObject root = new() {
			["Features"] = new JsonObject {
				["some-other-feature"] = true,
				["a-disabled-feature"] = false
			}
		};

		// Act
		SuiteFeatureFlags.Enable(root, typeof(MobilePageConversionGuideTool));

		// Assert
		root.ContainsKey("Features").Should().BeFalse(
			because: "Newtonsoft matches the property case-insensitively, so a surviving 'Features' sibling could shadow the canonical key and hide the flag the suite just wrote");
		JsonObject features = root["features"]!.AsObject();
		features["some-other-feature"]!.GetValue<bool>().Should().BeTrue(
			because: "a flag the machine already had enabled must survive the merge rather than be dropped");
		features["a-disabled-feature"]!.GetValue<bool>().Should().BeFalse(
			because: "the merge carries flag VALUES over, so a deliberately disabled feature stays disabled");
		features["mobile-page-converter"]!.GetValue<bool>().Should().BeTrue(
			because: "the gated tool must be enabled regardless of how the pre-existing key was cased");
	}

	[Test]
	[Description("Overrides an already-canonical features key that explicitly disables the gated flag.")]
	public void Enable_Overrides_An_Explicitly_Disabled_Flag() {
		// Arrange
		JsonObject root = new() {
			["features"] = new JsonObject { ["mobile-page-converter"] = false }
		};

		// Act
		SuiteFeatureFlags.Enable(root, typeof(MobilePageConversionGuideTool));

		// Assert
		root["features"]!.AsObject()["mobile-page-converter"]!.GetValue<bool>().Should().BeTrue(
			because: "the machine having the feature switched off is precisely the state that made the suite's test set depend on which agent ran it");
	}

	[Test]
	[Description("Reads the feature key from the gated type's own FeatureToggle attribute rather than a repeated literal.")]
	public void FeatureKeyOf_Reads_The_Key_From_The_Attribute() {
		// Act
		string featureKey = SuiteFeatureFlags.FeatureKeyOf(typeof(MobilePageConversionGuideTool));

		// Assert
		featureKey.Should().Be("mobile-page-converter",
			because: "the suite must enable the key the product actually reads, so a rename in clio cannot leave it writing a flag nothing consumes");
	}

	[Test]
	[Description("Refuses a type that carries no FeatureToggle attribute instead of silently writing nothing.")]
	public void FeatureKeyOf_Throws_For_A_Type_Without_The_Attribute() {
		// Act
		Action act = () => SuiteFeatureFlags.FeatureKeyOf(typeof(SuiteFeatureFlagsTests));

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a type with no [FeatureToggle] cannot be enabled by attribute, and failing loudly beats writing an empty or guessed key");
	}
}
