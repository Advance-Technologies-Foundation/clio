using System.Reflection;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Theming;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the <c>advise-theme-palette</c> MCP tool: the flat tool shape and the verdict
/// packet each operation returns (over the real <see cref="ThemeColorAdvisor"/> engine, with a substituted
/// template provider for the preview system defaults).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class AdviseThemePaletteToolTests {

	private IThemeTemplateProvider _templateProvider;
	private AdviseThemePaletteTool _tool;

	[SetUp]
	public void SetUp() {
		_templateProvider = Substitute.For<IThemeTemplateProvider>();
		_tool = new AdviseThemePaletteTool(new ThemeColorAdvisor(_templateProvider));
	}

	[Test]
	[Description("Is a flat MCP tool named advise-theme-palette with read-only, non-destructive, idempotent, closed-world safety flags.")]
	public void AdviseThemePaletteTool_ShouldBeFlatReadOnlyTool_WhenInspected() {
		// Arrange
		System.Type toolType = typeof(AdviseThemePaletteTool);

		// Assert
		toolType.BaseType.Should().Be(typeof(object), because: "the advisor is a flat ComponentInfoTool-style tool, not a BaseTool subclass");
		toolType.GetCustomAttribute<McpServerToolTypeAttribute>().Should().NotBeNull(because: "it must be discoverable as an MCP tool type");
		MethodInfo method = toolType.GetMethod(nameof(AdviseThemePaletteTool.Advise));
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
		attribute!.Name.Should().Be("advise-theme-palette", because: "the advertised tool name is advise-theme-palette");
		attribute.ReadOnly.Should().BeTrue(because: "the advisor never writes and never touches an environment");
		attribute.Destructive.Should().BeFalse(because: "the advisor is pure compute");
		attribute.Idempotent.Should().BeTrue(because: "the advisor is pure compute — the same inputs always yield the same verdict");
		attribute.OpenWorld.Should().BeFalse(because: "the advisor is offline and never reaches beyond the process");
	}

	[Test]
	[Description("An empty or unknown operation returns a graceful failure, not an exception.")]
	public void Advise_ShouldReturnFailure_WhenOperationMissingOrUnknown() {
		// Act
		ThemeColorAdvisorResult empty = _tool.Advise(operation: " ");
		ThemeColorAdvisorResult unknown = _tool.Advise(operation: "make-magic");

		// Assert
		empty.Success.Should().BeFalse(because: "operation is required");
		unknown.Success.Should().BeFalse(because: "an unknown operation cannot be dispatched");
		unknown.Error.Should().Contain("make-magic", because: "the error names the unknown operation");
	}

	[Test]
	[Description("triage normalizes each colour, scores contrast on white, counts accepted/passing, and names the highest-contrast candidate.")]
	public void Advise_ShouldTriageColours_WhenOperationTriage() {
		// Act
		ThemeColorAdvisorResult result = _tool.Advise(operation: "triage", colors: new[] { "#004fd6", "not-a-color", "#cccccc" });

		// Assert
		result.Success.Should().BeTrue(because: "triage completes even when some inputs are rejected");
		result.Colors.Should().HaveCount(3, because: "every input is reported");
		result.AcceptedCount.Should().Be(2, because: "two of three inputs normalized");
		result.PassingCount.Should().Be(1, because: "only #004fd6 passes 3:1 on white among the accepted");
		result.HighestContrastHex.Should().Be("#004fd6", because: "#004fd6 has the highest contrast of the accepted inputs");
		result.Colors![1].Accepted.Should().BeFalse(because: "the middle input is not a colour");
		result.Colors[1].RejectionCode.Should().Be(ColorNormalizer.InvalidColorCode, because: "the rejection code is surfaced");
		result.Colors[1].WasConverted.Should().BeNull(because: "wasConverted is scoped to accepted inputs and omitted on a rejected one");
	}

	[Test]
	[Description("adapt-primary reports compliant for a readable primary and adapted (with the darker variant) for a low-contrast one.")]
	public void Advise_ShouldReportAdaptationState_WhenOperationAdaptPrimary() {
		// Act
		ThemeColorAdvisorResult compliant = _tool.Advise(operation: "adapt-primary", primary: "#000000");
		ThemeColorAdvisorResult adapted = _tool.Advise(operation: "adapt-primary", primary: "#cccccc");

		// Assert
		compliant.AdaptationState.Should().Be("compliant", because: "black already passes 3:1 on white");
		compliant.OriginalContrastOnWhite.Should().BeNull(because: "the compliant state omits originalContrastOnWhite per the contract (present only for non-compliant states)");
		adapted.AdaptationState.Should().Be("adapted", because: "a light grey is below 3:1 but a darker variant exists");
		adapted.Adapted500.Should().Be("#949494", because: "the calibrated darker variant is returned");
		adapted.Warning!.Code.Should().Be("PRIMARY_LOW_CONTRAST_ON_WHITE", because: "the skill can record it if the user keeps the original");
		adapted.Warning.Severity.Should().Be("strong", because: "a low-contrast primary is a strong warning");
	}

	[Test]
	[Description("derive-secondary returns the auto secondary, and validates an override against the secondary role.")]
	public void Advise_ShouldDeriveAndValidateSecondary_WhenOperationDeriveSecondary() {
		// Act
		ThemeColorAdvisorResult auto = _tool.Advise(operation: "derive-secondary", primary: "#004fd6");
		ThemeColorAdvisorResult lowOverride = _tool.Advise(operation: "derive-secondary", primary: "#004fd6", secondary: "#cccccc");

		// Assert
		auto.DerivedSecondary.Should().Be("#0d2e4e", because: "the calibrated secondary is derived from the primary");
		lowOverride.SecondaryHex.Should().Be("#cccccc", because: "the override is normalized and returned");
		lowOverride.SecondaryReadable.Should().BeFalse(because: "a light grey is below 3:1 on white");
		lowOverride.Warning!.Code.Should().Be("SECONDARY_LOW_CONTRAST_ON_WHITE", because: "a low-contrast secondary override is flagged (acceptable)");
	}

	[Test]
	[Description("accent-suggest generates three candidates, marks valid/best, counts them, and gates the primary-as-accent fallback.")]
	public void Advise_ShouldSuggestAccents_WhenOperationAccentSuggest() {
		// Act
		ThemeColorAdvisorResult result = _tool.Advise(operation: "accent-suggest", primary: "#004fd6");

		// Assert
		result.SuggestedCandidates.Should().HaveCount(3, because: "three candidates are generated at +135/180/225");
		result.ValidCandidateCount.Should().BeGreaterThan(0, because: "at least one candidate is valid for the calibration primary");
		result.BestCandidateHex.Should().Be("#f94e11", because: "the +135 candidate is the most distinct valid accent");
		result.SuggestedCandidates!.Should().ContainSingle(candidate => candidate.IsBest, because: "exactly one candidate is marked best");
	}

	[Test]
	[Description("accent-evaluate-stored bands each stored candidate: an identical-to-primary colour is strong (not recommended); a distinct one is clean.")]
	public void Advise_ShouldBandStoredAccents_WhenOperationAccentEvaluateStored() {
		// Act
		ThemeColorAdvisorResult result = _tool.Advise(
			operation: "accent-evaluate-stored", primary: "#004fd6", candidateHexes: new[] { "#004fd6", "#f94e11" });

		// Assert
		result.EvaluatedCandidates.Should().HaveCount(2, because: "both stored candidates are scored");
		result.EvaluatedCandidates![0].SimilarityBand.Should().Be("strong", because: "a candidate identical to the primary is maximally similar");
		result.EvaluatedCandidates[0].Recommend.Should().BeFalse(because: "a strong-similarity candidate is not offered");
		result.EvaluatedCandidates[1].SimilarityBand.Should().Be("clean", because: "#f94e11 is distinct enough from the primary");
		result.EvaluatedCandidates[1].Recommend.Should().BeTrue(because: "a clean candidate is offered plainly");
	}

	[Test]
	[Description("validate-color role=primary returns a strong verdict below 3:1; role=secondary returns warn.")]
	public void Advise_ShouldApplyRoleOverlay_WhenOperationValidateColor() {
		// Act
		ThemeColorAdvisorResult primary = _tool.Advise(operation: "validate-color", role: "primary", color: "#cccccc");
		ThemeColorAdvisorResult secondary = _tool.Advise(operation: "validate-color", role: "secondary", color: "#cccccc");
		ThemeColorAdvisorResult ok = _tool.Advise(operation: "validate-color", role: "primary", color: "#004fd6");

		// Assert
		primary.Verdict.Should().Be("strong", because: "a low-contrast primary is a strong failure");
		primary.Warning!.Code.Should().Be("PRIMARY_LOW_CONTRAST_ON_WHITE", because: "the primary low-contrast code is returned");
		secondary.Verdict.Should().Be("warn", because: "a low-contrast secondary is a soft warning, not strong");
		ok.Verdict.Should().Be("pass", because: "a readable primary passes");
	}

	[Test]
	[Description("validate-color role=accent applies the strong-similarity-first precedence: a colour identical to the primary is strong.")]
	public void Advise_ShouldRankAccentSimilarityFirst_WhenOperationValidateColorAccent() {
		// Act
		ThemeColorAdvisorResult tooSimilar = _tool.Advise(operation: "validate-color", role: "accent", color: "#004fd6", primary: "#004fd6");
		ThemeColorAdvisorResult distinct = _tool.Advise(operation: "validate-color", role: "accent", color: "#f94e11", primary: "#004fd6");

		// Assert
		tooSimilar.Verdict.Should().Be("strong", because: "an accent identical to the primary is strongly too similar");
		tooSimilar.Warning!.Code.Should().Be("ACCENT_TOO_SIMILAR_TO_PRIMARY", because: "strong similarity outranks a contrast warning");
		distinct.Verdict.Should().Be("pass", because: "a distinct, readable accent passes both gates");
	}

	[Test]
	[Description("accent-validate-manual routes to the canonical accent validation shape.")]
	public void Advise_ShouldValidateManualAccent_WhenOperationAccentValidateManual() {
		// Act
		ThemeColorAdvisorResult result = _tool.Advise(operation: "accent-validate-manual", color: "#f94e11", primary: "#004fd6");

		// Assert
		result.Success.Should().BeTrue(because: "a valid manual accent completes");
		result.NormalizedColor.Should().Be("#f94e11", because: "the manual colour is normalized");
		result.Verdict.Should().Be("pass", because: "a distinct readable accent passes");
	}

	[Test]
	[Description("preview emits only the base -500 for all five roles and sources success/error from the template default when no override is given.")]
	public void Advise_ShouldBuildPreview_WhenOperationPreview() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>()).Returns("10.0");
		string successDefault;
		_templateProvider.TryGetPaletteDefault(Arg.Any<string>(), "success", out successDefault)
			.Returns(callInfo => { callInfo[2] = "#0b8500"; return true; });
		string errorDefault;
		_templateProvider.TryGetPaletteDefault(Arg.Any<string>(), "error", out errorDefault)
			.Returns(callInfo => { callInfo[2] = "#d2310d"; return true; });

		// Act
		ThemeColorAdvisorResult result = _tool.Advise(
			operation: "preview", primary: "#004fd6", secondary: "#0d2e4e", accent: "#f94e11", version: "10.0");

		// Assert
		result.Success.Should().BeTrue(because: "a valid preview with template-sourced system colours completes");
		result.Palettes.Should().ContainKeys(new[] { "primary", "secondary", "accent", "success", "error" },
			because: "every brand and system role is previewed; neutral is never emitted");
		result.Palettes!["primary"].Should().ContainKey("500").And.HaveCount(1, because: "the default preview surfaces only the base -500 per role, not the palette stops");
		result.SuccessSource.Should().Be("template-default", because: "no success override was supplied");
		result.ResolvedVersion.Should().Be("10.0", because: "the offline resolver reports the version used");
	}

	[Test]
	[Description("validate-color rejects a null or unknown role with a graceful INVALID_ROLE failure.")]
	public void Advise_ShouldReturnInvalidRole_WhenRoleUnknown() {
		// Act
		ThemeColorAdvisorResult nullRole = _tool.Advise(operation: "validate-color", color: "#004fd6");
		ThemeColorAdvisorResult unknownRole = _tool.Advise(operation: "validate-color", role: "brand", color: "#004fd6");

		// Assert
		nullRole.Success.Should().BeFalse(because: "a role is required to validate a colour");
		nullRole.Error.Should().StartWith("INVALID_ROLE", because: "a missing role is reported with the role code");
		unknownRole.Success.Should().BeFalse(because: "an unknown role cannot be validated");
		unknownRole.Error.Should().StartWith("INVALID_ROLE", because: "an unknown role is reported with the role code");
	}

	[Test]
	[Description("validate-color role=accent takes the contrast-fail arm for a distinct-but-unreadable accent (not the similarity arm).")]
	public void Advise_ShouldFlagAccentLowContrast_WhenDistinctButUnreadable() {
		// Act — #cccccc is far from the blue primary (clean band) yet below 3:1 on white.
		ThemeColorAdvisorResult result = _tool.Advise(operation: "validate-color", role: "accent", color: "#cccccc", primary: "#004fd6");

		// Assert
		result.Verdict.Should().Be("warn", because: "a distinct but low-contrast accent is a soft warning");
		result.Warning!.Code.Should().Be("ACCENT_LOW_CONTRAST_ON_WHITE", because: "with no strong similarity, the contrast failure is the reported warning");
		result.Warning.Severity.Should().Be("warning", because: "an accent contrast failure is a warning, not strong");
	}

	[Test]
	[Description("preview reports success=false with TEMPLATE_DEFAULT_MISSING (qualified by the resolved version) when a system default cannot be sourced.")]
	public void Advise_ShouldFailPreview_WhenTemplateDefaultMissing() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>()).Returns("10.0");
		string missing;
		_templateProvider.TryGetPaletteDefault(Arg.Any<string>(), "success", out missing).Returns(false);

		// Act
		ThemeColorAdvisorResult result = _tool.Advise(
			operation: "preview", primary: "#004fd6", secondary: "#0d2e4e", accent: "#f94e11", version: "10.0");

		// Assert
		result.Success.Should().BeFalse(because: "a preview cannot render a role whose system default is missing");
		result.Error.Should().Contain("TEMPLATE_DEFAULT_MISSING", because: "the missing default maps to a stable code");
		result.Error.Should().Contain("@10.0", because: "the code is qualified by the resolved template version per the contract");
	}

	[Test]
	[Description("preview reports success=false with VERSION_NOT_SUPPORTED when the offline resolver rejects the version.")]
	public void Advise_ShouldFailPreview_WhenVersionUnsupported() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>())
			.Returns(_ => throw new System.ArgumentException("Themes require Creatio 10.0 or newer."));

		// Act
		ThemeColorAdvisorResult result = _tool.Advise(
			operation: "preview", primary: "#004fd6", secondary: "#0d2e4e", accent: "#f94e11", version: "9.0");

		// Assert
		result.Success.Should().BeFalse(because: "an unsupported version cannot source system defaults");
		result.Error.Should().Contain("VERSION_NOT_SUPPORTED", because: "the version failure maps to a stable code");
	}
}
