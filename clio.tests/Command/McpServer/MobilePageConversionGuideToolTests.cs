using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for the parts of <see cref="MobilePageConversionGuideTool"/> that guard which source pages
/// the converter accepts — the safety-critical "only Freedom UI web, never an already-mobile or Classic
/// page" rule. These live on the TOOL (not the <c>Analyze</c> engine, which the service tests exercise),
/// so without them the source-type gate is only reached through a live page read. Both members under test
/// are internal static and reachable via InternalsVisibleTo("clio.tests"), so no server/environment is needed.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
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
