using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for the legacy Mobile-wizard detection and gate on <see cref="MobilePageConversionGuideTool"/>
/// (ENG-95730): a schema whose platform type is unknown and whose name carries GridPageSettings is routed to the
/// legacy list mechanism; a RecordPageSettings schema is recognised and rejected with the owning story; the
/// existing freedom-web / mobile / classic verdicts are unchanged.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolLegacyDetectionTests {

	private static MobilePageConversionGuideArgs Args(string schemaName) =>
		new(schemaName, TargetSchemaName: null, Version: null, EnvironmentName: null, Uri: null, Login: null, Password: null);

	[TestCase("MobileOrderGridPageSettingsDefaultWorkplace", "unknown", ExpectedResult = LegacyMobileListAnalysisService.SourceTypeLegacyGridPage)]
	[TestCase("mobilecasegridpagesettings", "UNKNOWN", ExpectedResult = LegacyMobileListAnalysisService.SourceTypeLegacyGridPage)]
	[TestCase("MobileActivityRecordPageSettingsDefaultWorkplace", "unknown", ExpectedResult = LegacyMobileListAnalysisService.SourceTypeLegacyRecordPage)]
	[TestCase("MobileOrderGridPageSettingsDefaultWorkplace", "web", ExpectedResult = null)]
	[TestCase("MobileOrderGridPageSettingsDefaultWorkplace", "mobile", ExpectedResult = null)]
	[TestCase("UsrOrder_ListPage", "unknown", ExpectedResult = null)]
	[TestCase("UsrGridPageSettingsHelper", "unknown", ExpectedResult = null)]
	[TestCase("", "unknown", ExpectedResult = null)]
	[TestCase(null, "unknown", ExpectedResult = null)]
	[Description("Legacy detection refines only an 'unknown' platform label and only for the wizard name pattern Mobile<Entity>(Grid|Record)PageSettings<Workplace> (case-insensitive); a web/mobile label, an unrelated name, or a name that merely contains the words stays as detected (null).")]
	public string DetectLegacySourceType_ShouldRefineUnknownLabelByName(string schemaName, string label) =>
		MobilePageConversionGuideTool.DetectLegacySourceType(schemaName, label);

	[Test]
	[Description("A legacy list settings source passes the gate (null rejection) so the legacy mechanism may run.")]
	public void RejectUnsupportedSourceType_ShouldReturnNull_ForLegacyGridPage() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(
			Args("MobileOrderGridPageSettingsDefaultWorkplace"), LegacyMobileListAnalysisService.SourceTypeLegacyGridPage);

		// Assert
		rejection.Should().BeNull(because: "legacy wizard list settings are a supported source type");
	}

	[Test]
	[Description("A legacy RECORD settings source is rejected with a structured failure naming ENG-95731 and the supported source types.")]
	public void RejectUnsupportedSourceType_ShouldRejectLegacyRecordPage_WithOwningStory() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(
			Args("MobileActivityRecordPageSettingsDefaultWorkplace"), LegacyMobileListAnalysisService.SourceTypeLegacyRecordPage);

		// Assert
		rejection.Should().NotBeNull(because: "record pages are a later story");
		rejection!.Success.Should().BeFalse(because: "the gate short-circuits with a failure");
		rejection.SourceType.Should().Be(LegacyMobileListAnalysisService.SourceTypeLegacyRecordPage, because: "the detected source type is echoed");
		rejection.Error.Should().Contain("ENG-95731", because: "the caller learns which story owns record pages");
		rejection.Error.Should().Contain(LegacyMobileListAnalysisService.SourceTypeLegacyGridPage, because: "the supported list source is named");
	}

	[Test]
	[Description("The generic not-supported verdict (e.g. Classic UI) now lists BOTH supported source types.")]
	public void RejectUnsupportedSourceType_ShouldListBothSupportedTypes_ForClassicSource() {
		// Act
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(Args("UsrLegacyPage"), "classic");

		// Assert
		rejection.Should().NotBeNull(because: "Classic UI is still unsupported");
		rejection!.Error.Should().Contain("freedom-web").And.Contain("legacy-mobile-grid-page",
			because: "the supported list must stay truthful after the legacy source was added");
	}

	[Test]
	[Description("Detection and gate compose end to end: an unknown-typed GridPageSettings schema detects as legacy list and passes the gate.")]
	public void DetectThenReject_ShouldAcceptLegacyGridPageSettings() {
		// Act
		string label = MobilePageConversionGuideTool.DetectSourceType("unknown");
		string sourceType = MobilePageConversionGuideTool.DetectLegacySourceType("MobileOrderGridPageSettingsDefaultWorkplace", label) ?? label;
		MobilePageConversionGuideResponse rejection = MobilePageConversionGuideTool.RejectUnsupportedSourceType(Args("MobileOrderGridPageSettingsDefaultWorkplace"), sourceType);

		// Assert
		sourceType.Should().Be(LegacyMobileListAnalysisService.SourceTypeLegacyGridPage, because: "the unknown label is refined by the name");
		rejection.Should().BeNull(because: "the refined source type is supported");
	}
}
