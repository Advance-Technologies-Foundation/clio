using System.Linq;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for <see cref="LegacyMobileSettingsClassifier"/>: the merged legacy settings are classified
/// STRUCTURALLY (property presence on the settings node), never by string-sniffing a body.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LegacyMobileSettingsClassifierTests {

	private static JObject Settings(string extra = "") =>
		JObject.Parse($$"""{ "name": "settings", "entitySchemaName": "Order", "settingsType": "GridPage", "items": [], "subtitleItems": [], "groupItems": [] {{extra}} }""");

	[Test]
	[Description("Wizard-only settings classify as plain with no override sections.")]
	public void Classify_ShouldReturnPlain_WhenOnlyWizardBucketsExist() {
		// Act
		LegacySettingsClassification result = LegacyMobileSettingsClassifier.Classify(Settings());

		// Assert
		result.Kind.Should().Be(LegacySettingsKind.Plain, because: "nothing beyond the wizard buckets is present");
		result.Label.Should().Be("plain", because: "the caller-facing label mirrors the kind");
		result.OverrideSections.Should().BeEmpty(because: "there are no override sections");
	}

	[Test]
	[Description("Override sections stored as JSON-encoded STRINGS (the wizard's storage format) are recognised and their operations counted after parsing; an array section is counted directly; diffV2 is recognised too.")]
	public void Classify_ShouldReturnOverrides_WhenSectionsArePresentAsStringsOrArrays() {
		// Arrange
		JObject settings = Settings(
			", \"viewConfigDiff\": \"[{\\\"operation\\\":\\\"merge\\\",\\\"name\\\":\\\"A\\\",\\\"values\\\":{}},{\\\"operation\\\":\\\"remove\\\",\\\"name\\\":\\\"B\\\"}]\"" +
			", \"viewModelConfigDiff\": [ { \"operation\": \"merge\", \"path\": [], \"values\": {} } ]" +
			", \"diffV2\": \"not json\"");

		// Act
		LegacySettingsClassification result = LegacyMobileSettingsClassifier.Classify(settings);

		// Assert
		result.Kind.Should().Be(LegacySettingsKind.FreedomUiOverrides, because: "override sections are present");
		result.Label.Should().Be("freedom-ui-overrides", because: "the caller-facing label mirrors the kind");
		result.OverrideSections.Should().Contain(s => s.Section == "viewConfigDiff" && s.OperationCount == 2 && s.Supported,
			because: "a string-encoded section is parsed before counting, and this format is processed");
		result.OverrideSections.Should().Contain(s => s.Section == "viewModelConfigDiff" && s.OperationCount == 1,
			because: "an array section is counted directly");
		result.OverrideSections.Should().Contain(s => s.Section == "diffV2" && s.OperationCount == -1,
			because: "a section that cannot be parsed is still reported, with an unknown count");
		result.Notes.Should().Contain(n => n.Contains("diffV2"), because: "the uncountable section is explained");
	}

	[Test]
	[Description("The three *ConfigDiff sections and diffV2 get DIFFERENT verdicts: the former are processed operation by operation, diffV2 is permanently unsupported and carries a reason instead — collapsing the two would tell the user to wait for something that is not coming.")]
	public void Classify_ShouldSeparateProcessedSectionsFromPermanentlyUnsupportedOnes() {
		// Arrange
		JObject settings = Settings(
			", \"viewConfigDiff\": [ { \"operation\": \"remove\", \"name\": \"ViewConfig\", \"properties\": [\"floatAction\"] } ]" +
			", \"diffV2\": [ { \"operation\": \"insert\", \"name\": \"Root\", \"values\": {} } ]");

		// Act
		LegacySettingsClassification result = LegacyMobileSettingsClassifier.Classify(settings);

		// Assert
		LegacyOverrideSection pending = result.OverrideSections.Single(s => s.Section == "viewConfigDiff");
		LegacyOverrideSection refused = result.OverrideSections.Single(s => s.Section == "diffV2");
		pending.Supported.Should().BeTrue(because: "this override format is processed operation by operation");
		pending.Ticket.Should().BeNull(
			because: "the format is carried across here, so naming a story would tell the user to wait for work that is already done");
		pending.Reason.Should().BeNull(because: "there is no permanent reason it cannot be carried");
		pending.Operations.Should().NotBeNull(because: "the parsed operations are retained for the rebase step");
		pending.Operations!.Count.Should().Be(1, because: "the retained payload is the section's own operations");
		refused.Ticket.Should().BeNull(because: "no story will carry diffV2 across; a ticket would promise otherwise");
		refused.Reason.Should().NotBeNullOrWhiteSpace(because: "an unsupported verdict must say why");
		refused.Reason.Should().Contain("verbatim",
			because: "the reason is that the mobile runtime passes diffV2 through rather than translating it");
		refused.Operations.Should().BeNull(because: "operations are only retained for sections that will be processed");
	}

	[TestCase("viewConfig")]
	[TestCase("modelViewConfig")]
	[Description("A hand-authored viewConfig / modelViewConfig classifies as custom-viewconfig (refused), and wins even when override sections are also present.")]
	public void Classify_ShouldReturnCustomViewConfig_WhenHandAuthoredConfigExists(string key) {
		// Arrange
		JObject settings = Settings($", \"{key}\": {{ \"items\": [] }}, \"viewConfigDiff\": [ {{ \"operation\": \"merge\", \"name\": \"X\", \"values\": {{}} }} ]");

		// Act
		LegacySettingsClassification result = LegacyMobileSettingsClassifier.Classify(settings);

		// Assert
		result.Kind.Should().Be(LegacySettingsKind.CustomViewConfig, because: "a custom config cannot be converted or even opened by the classic designer");
		result.Label.Should().Be("custom-viewconfig", because: "the caller-facing label mirrors the kind");
		result.OverrideSections.Should().ContainSingle(s => s.Section == "viewConfigDiff", because: "the override sections are still reported");
		result.Notes.Should().Contain(n => n.Contains(key), because: "the refusal reason names the offending property");
	}

	[Test]
	[Description("Null, blank-string and EMPTY sections ([] or \"[]\") do not count as overrides, so a placeholder does not misclassify plain settings or tell the user something was dropped.")]
	public void Classify_ShouldIgnoreNullBlankOrEmptySections() {
		// Arrange
		JObject settings = Settings(", \"viewConfigDiff\": null, \"modelConfigDiff\": \"  \", \"viewModelConfigDiff\": [], \"diffV2\": \"[]\", \"viewConfig\": null");

		// Act
		LegacySettingsClassification result = LegacyMobileSettingsClassifier.Classify(settings);

		// Assert
		result.Kind.Should().Be(LegacySettingsKind.Plain, because: "absent-by-value and empty sections are not overrides");
		result.OverrideSections.Should().BeEmpty(because: "nothing was left unconverted");
		result.Notes.Should().Contain(n => n.Contains("'viewModelConfigDiff'") && n.Contains("empty"), because: "an empty placeholder is still mentioned");
	}
}
