using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Unit tests for the parts of <see cref="MobilePageConversionGuideTool"/> that guard which source pages
/// the converter accepts — the safety-critical "only Freedom UI web, never an already-mobile or Classic
/// page" rule. These live on the TOOL (not the <c>Analyze</c> engine, which the service tests exercise),
/// so without them the source-type gate is only reached through a live page read. Both members under test
/// are internal static and reachable via InternalsVisibleTo("clio.tests"), so no server/environment is needed.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolTests {

	private static MobilePageConversionGuideArgs Args(string schemaName = "UsrLeads_FormPage") =>
		new(schemaName, TargetSchemaName: null, Version: null, EnvironmentName: null, Uri: null, Login: null, Password: null);

	[TestCase("web", ExpectedResult = WebToMobileAnalysisService.SourceTypeFreedomWeb)]
	[TestCase("WEB", ExpectedResult = WebToMobileAnalysisService.SourceTypeFreedomWeb)]
	[TestCase("mobile", ExpectedResult = "mobile")]
	[TestCase("Mobile", ExpectedResult = "mobile")]
	[TestCase("classic", ExpectedResult = "classic")]
	[TestCase("  Classic  ", ExpectedResult = "classic")]
	[TestCase("SomethingElse", ExpectedResult = "somethingelse")]
	[TestCase(null, ExpectedResult = "unknown")]
	[TestCase("", ExpectedResult = "unknown")]
	[TestCase("   ", ExpectedResult = "unknown")]
	[Description("Maps the platform schema-type to a conversion source-type label: web -> freedom-web (case-insensitive), mobile passes through, anything else is trimmed/lower-cased verbatim, and blank/null is 'unknown'.")]
	public string DetectSourceType_MapsSchemaTypeToSourceTypeLabel(string schemaType) =>
		MobilePageConversionGuideTool.DetectSourceType(schemaType);

	[Test]
	[Description("A freedom-web source is accepted: RejectUnsupportedSourceType returns null so conversion may proceed.")]
	public void RejectUnsupportedSourceType_ReturnsNull_ForFreedomWeb() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(
			Args(), WebToMobileAnalysisService.SourceTypeFreedomWeb);

		// Assert
		rejection.Should().BeNull(
			because: "a Freedom UI web page is the supported source, so the gate must not short-circuit conversion");
	}

	[Test]
	[Description("An already-mobile source is rejected with a structured failure that echoes the source type and explains there is nothing to convert.")]
	public void RejectUnsupportedSourceType_Rejects_MobileSource() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(
			Args("UsrLeads_MobileFormPage"), "mobile");

		// Assert
		rejection.Should().NotBeNull(because: "an already-mobile page must never start conversion");
		rejection!.Success.Should().BeFalse(because: "the gate short-circuits with a failure");
		rejection.SourceType.Should().Be("mobile", because: "the detected source type is echoed back for the caller");
		rejection.SourceSchemaName.Should().Be("UsrLeads_MobileFormPage", because: "the failure names the source page");
		rejection.Error.Should().Contain("already a mobile page",
			because: "the diagnostic must explain why the mobile source was rejected");
	}

	[Test]
	[Description("A Classic UI (or any non-freedom-web) source is rejected with a structured failure that names the unsupported source type and points at the classic->freedom-web migration.")]
	public void RejectUnsupportedSourceType_Rejects_ClassicSource() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(
			Args("UsrLegacyPage"), "classic");

		// Assert
		rejection.Should().NotBeNull(because: "a non-Freedom-web source is not supported and must not start conversion");
		rejection!.Success.Should().BeFalse(because: "the gate short-circuits with a failure");
		rejection.SourceType.Should().Be("classic", because: "the unsupported source type is surfaced verbatim");
		rejection.Error.Should().Contain("not yet supported",
			because: "the diagnostic must state the source type is unsupported");
		rejection.Error.Should().Contain(WebToMobileAnalysisService.SourceTypeFreedomWeb,
			because: "the diagnostic must name the supported source type so the caller knows what to migrate to");
	}

	[TestCase("environment", "environment", ExpectedResult = "environment")]
	[TestCase("environment", "environment-superset", ExpectedResult = "environment-superset")]
	[TestCase("environment-superset", "environment", ExpectedResult = "environment-superset")]
	[TestCase("environment", "latest-fallback", ExpectedResult = "latest-fallback")]
	[TestCase("latest-fallback", "environment-superset", ExpectedResult = "latest-fallback")]
	[TestCase("environment-superset", "environment-superset", ExpectedResult = "environment-superset")]
	[Description("WorseResolvedFrom returns the LESS authoritative tier (environment < environment-superset < latest-fallback) " +
		"so when the mobile and web catalogs resolve to different tiers, a superset/fallback on either side is reported and " +
		"never masked by the other catalog's exact tier.")]
	public string WorseResolvedFrom_ReturnsLeastAuthoritativeTier(string a, string b) =>
		MobilePageConversionGuideTool.WorseResolvedFrom(a, b);

	[Test]
	[Description("ENG-95827: a web template no templates entry matches falls back to the rules' defaultMobileTemplate, so the page still gets a mobile target AND clio still gets a template bundle to diff the data sections against — without one both diffs degrade to a root merge. The fallback deliberately carries NO container or component correspondence: for an unrecognised web template no name twins are known, and asserting them would relocate elements.")]
	public void DefaultTemplateRule_FallsBackToTheRulesDefault_WithNoNameCorrespondence() {
		// Arrange
		var rules = new WebToMobilePageConversionRules {
			DefaultMobileTemplate = "BaseMobilePageTemplate",
			Templates = [new TemplateMappingRule { Web = "ListPageV3Template", Mobile = "BaseMobileListTemplate" }]
		};

		// Act
		TemplateMappingRule unmatched = MobilePageConversionGuideTool.ResolveTemplateRule(rules, "UsrCustomTemplate");
		TemplateMappingRule fallback = MobilePageConversionGuideTool.DefaultTemplateRule(rules);

		// Assert
		unmatched.Should().BeNull(
			because: "ResolveTemplateRule must keep answering 'no match' — it is also the predicate that finds the first ANCESTOR matching a rule, and a never-null result would make every ancestor match");
		fallback!.Mobile.Should().Be("BaseMobilePageTemplate",
			because: "a generic mobile base is a far better answer than none: it gives create-page a target and the differ a real base");
		fallback.Containers.Should().BeNullOrEmpty(
			because: "no container name twins are known for an unrecognised web template, and inventing them would misplace elements rather than leave them where the tree walk puts them");
		fallback.Components.Should().BeNullOrEmpty(
			because: "same reasoning as the containers — a guessed component twin is worse than none");
		fallback.Note.Should().Contain("generic mobile base",
			because: "the caller must not read the recommendation as a matched counterpart");
	}

	[Test]
	[Description("ENG-95827: an unreadable mobile template FAILS the tool instead of degrading the guide. Without that template MobileTypesByName is empty, which silently stops the automatic same-name twin from being detected — so an element the template already provides (Feed, Tabs) falls through to the insert path and the page ships a DUPLICATE of a native element — and RetargetTargetMissing fails open, so a retarget into a container the template lacks is no longer caught. Shipping that guide with a footnote about the least of it (a root-merge data-section diff) is what this replaces.")]
	public void RejectUnobtainableMobileTemplate_WhenNamedTemplateIsUnreadable_FailsWithTheReRunRemedy() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnobtainableMobileTemplate(
			Args(), WebToMobileAnalysisService.SourceTypeFreedomWeb, "MobilePageWithTabsFreedomTemplate",
			templateUnavailable: true);

		// Assert
		rejection.Should().NotBeNull(because: "a guide that cannot be trusted is worse than no guide");
		rejection!.Success.Should().BeFalse();
		rejection.Error.Should().Contain("MobilePageWithTabsFreedomTemplate",
			because: "naming the schema is what makes the failure actionable — the caller checks that one thing");
		rejection.Error.Should().Contain("DUPLICATES",
			because: "the duplicate-native-element consequence is the severe one and the reason this is a failure rather than a diagnostic");
		rejection.Error.Should().Contain("mobile package is installed",
			because: "the usual cause is the mobile package missing from the target environment, so the fix is named");
	}

	[Test]
	[Description("ENG-95827: the two unobtainable-template causes get DIFFERENT fixes, so the failure distinguishes them — a template that was never named cannot be re-read, and telling the caller to re-run would be advice that cannot work. The fix there is a rules-file entry.")]
	public void RejectUnobtainableMobileTemplate_WhenNoTemplateWasNamed_PointsAtTheRulesFileInstead() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnobtainableMobileTemplate(
			Args(), WebToMobileAnalysisService.SourceTypeFreedomWeb, mobileTemplateName: null,
			templateUnavailable: true);

		// Assert
		rejection!.Error.Should().Contain("defaultMobileTemplate",
			because: "with nothing named there is no schema to re-read; the actionable fix is a rules-file entry");
		rejection.Error.Should().NotContain("mobile package is installed",
			because: "suggesting an environment check for a template that was never named sends the caller after the wrong thing");
	}

	[Test]
	[Description("ENG-95827: a readable mobile template is the normal path and must not be rejected — the gate fires on unavailability alone, never on a template that simply lacks one config section.")]
	public void RejectUnobtainableMobileTemplate_WhenTemplateIsReadable_ReturnsNull() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnobtainableMobileTemplate(
			Args(), WebToMobileAnalysisService.SourceTypeFreedomWeb, "BaseMobilePageTemplate",
			templateUnavailable: false);

		// Assert
		rejection.Should().BeNull(because: "the guide is trustworthy whenever the template was read, so nothing blocks it");
	}

	[Test]
	[Description("ENG-95827: with no defaultMobileTemplate declared, the fallback stays null rather than inventing a schema name — a partner rules file that omits it must not send create-page at a template that may not exist.")]
	public void DefaultTemplateRule_WithoutADeclaredDefault_ReturnsNull() {
		// Arrange
		var rules = new WebToMobilePageConversionRules { Templates = [] };

		// Act
		TemplateMappingRule fallback = MobilePageConversionGuideTool.DefaultTemplateRule(rules);

		// Assert
		fallback.Should().BeNull(
			because: "the default is rules-file data, not a hardcoded name — and when it is absent the root-merge degradation is reported with cause no-template-base instead");
	}

	[Test]
	[Description("The detection and the gate compose: a 'web' schema-type detects as freedom-web and passes the gate (no rejection).")]
	public void DetectThenReject_AcceptsWebSchemaType() {
		// Act
		string sourceType = MobilePageConversionGuideTool.DetectSourceType("web");
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(Args(), sourceType);

		// Assert
		sourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb);
		rejection.Should().BeNull(because: "a detected freedom-web source must pass the gate end to end");
	}
}
