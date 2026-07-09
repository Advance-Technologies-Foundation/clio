namespace Clio.Tests.Command;

using System;
using Clio.Command.Theming;
using Clio.Theming;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Direct unit coverage for the <see cref="ThemePaletteAdvisor"/> engine: each operation is invoked on the
/// engine itself (not through the MCP tool wrapper), asserting the verdict packet it returns and the named
/// hard-failure codes. The color math is delegated to the deterministic <c>Clio.Theming</c> engine, so the
/// asserted hexes/verdicts are the calibrated golden values; only the preview system defaults are stubbed
/// through <see cref="IThemeTemplateProvider"/>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemePaletteAdvisorTests {

	private IThemeTemplateProvider _templateProvider;
	private ThemePaletteAdvisor _advisor;

	[SetUp]
	public void SetUp() {
		_templateProvider = Substitute.For<IThemeTemplateProvider>();
		_advisor = new ThemePaletteAdvisor(_templateProvider);
	}

	[TearDown]
	public void TearDown() {
		_templateProvider.ClearReceivedCalls();
	}

	[Test]
	[Description("Triage normalizes each input, scores contrast on white, counts accepted/passing, names the highest-contrast candidate, and surfaces the rejection code for an unparseable input.")]
	public void Triage_ShouldNormalizeScoreAndRank_WhenGivenMixedInputs() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.Triage(new[] { "#004fd6", "not-a-color", "#cccccc" });

		// Assert
		result.Success.Should().BeTrue(because: "triage completes even when some inputs are rejected");
		result.Colors.Should().HaveCount(3, because: "every input is reported, accepted or not");
		result.AcceptedCount.Should().Be(2, because: "two of the three inputs normalize to a hex");
		result.PassingCount.Should().Be(1, because: "only #004fd6 clears the 3:1 gate among the accepted inputs");
		result.HighestContrastHex.Should().Be("#004fd6", because: "#004fd6 has the highest contrast on white of the accepted inputs");
		result.Colors![1].Accepted.Should().BeFalse(because: "the middle input is not a colour");
		result.Colors[1].RejectionCode.Should().Be(ColorNormalizer.InvalidColorCode, because: "the engine surfaces the rejection code for an unparseable input");
	}

	[Test]
	[Description("Triage fails with INVALID_COLOR when the input list is null or empty (nothing to triage).")]
	public void Triage_ShouldFail_WhenNoColorsSupplied() {
		// Act
		ThemePaletteAdvisorResult empty = _advisor.Triage(Array.Empty<string>());
		ThemePaletteAdvisorResult nul = _advisor.Triage(null);

		// Assert
		empty.Success.Should().BeFalse(because: "there is nothing to triage for an empty list");
		empty.Error.Should().StartWith("INVALID_COLOR", because: "an empty triage is reported with the colour code");
		nul.Success.Should().BeFalse(because: "there is nothing to triage for a null list");
	}

	[Test]
	[Description("AdaptPrimary reports the compliant state for a readable primary and omits the non-compliant-only fields.")]
	public void AdaptPrimary_ShouldReportCompliant_WhenReadable() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.AdaptPrimary("#000000");

		// Assert
		result.Success.Should().BeTrue(because: "a readable primary is a successful, compliant outcome");
		result.AdaptationState.Should().Be("compliant", because: "black already clears 3:1 on white");
		result.OriginalContrastOnWhite.Should().BeNull(because: "the compliant state omits the non-compliant-only fields per the contract");
		result.Warning.Should().BeNull(because: "a compliant primary carries no warning");
	}

	[Test]
	[Description("AdaptPrimary returns the calibrated darker variant and a strong low-contrast warning for a low-contrast primary.")]
	public void AdaptPrimary_ShouldReportAdaptedVariant_WhenLowContrast() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.AdaptPrimary("#cccccc");

		// Assert
		result.AdaptationState.Should().Be("adapted", because: "a light grey is below 3:1 but a darker compliant variant exists");
		result.Adapted500.Should().Be("#949494", because: "the calibrated darker variant is returned");
		result.Warning!.Code.Should().Be("PRIMARY_LOW_CONTRAST_ON_WHITE", because: "the skill can record it if the user keeps the original");
		result.Warning.Severity.Should().Be("strong", because: "a low-contrast primary is a strong warning");
	}

	[Test]
	[Description("AdaptPrimary fails with a rejection code when the primary cannot be normalized.")]
	public void AdaptPrimary_ShouldFail_WhenPrimaryInvalid() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.AdaptPrimary("not-a-color");

		// Assert
		result.Success.Should().BeFalse(because: "an unparseable primary cannot be adapted");
		result.Error.Should().Contain("not-a-color", because: "the failure echoes the offending input");
	}

	[Test]
	[Description("DeriveSecondary returns the calibrated auto secondary and leaves the override fields unset when no override is supplied.")]
	public void DeriveSecondary_ShouldReturnCalibratedSecondary_WhenNoOverride() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.DeriveSecondary("#004fd6", secondaryOverride: null);

		// Assert
		result.Success.Should().BeTrue(because: "deriving a secondary from a valid primary succeeds");
		result.DerivedSecondary.Should().Be("#0d2e4e", because: "the calibrated secondary is derived from the primary");
		result.SecondaryHex.Should().BeNull(because: "the override fields stay unset when no override is supplied");
	}

	[Test]
	[Description("DeriveSecondary validates a supplied override against the secondary role: a readable one passes without a warning, a low-contrast one is flagged (still a success).")]
	public void DeriveSecondary_ShouldValidateOverride_WhenOverrideSupplied() {
		// Act
		ThemePaletteAdvisorResult readable = _advisor.DeriveSecondary("#004fd6", secondaryOverride: "#004fd6");
		ThemePaletteAdvisorResult low = _advisor.DeriveSecondary("#004fd6", secondaryOverride: "#cccccc");

		// Assert
		readable.SecondaryHex.Should().Be("#004fd6", because: "the override is normalized and returned");
		readable.SecondaryReadable.Should().BeTrue(because: "#004fd6 clears 3:1 on white");
		readable.Warning.Should().BeNull(because: "a readable secondary override carries no warning");
		low.SecondaryReadable.Should().BeFalse(because: "a light grey is below 3:1 on white");
		low.Warning!.Code.Should().Be("SECONDARY_LOW_CONTRAST_ON_WHITE", because: "a low-contrast secondary override is flagged as an acceptable caveat");
	}

	[Test]
	[Description("DeriveSecondary fails on an invalid primary and on an invalid override, each echoing the offending input.")]
	public void DeriveSecondary_ShouldFail_WhenPrimaryOrOverrideInvalid() {
		// Act
		ThemePaletteAdvisorResult badPrimary = _advisor.DeriveSecondary("nope", secondaryOverride: null);
		ThemePaletteAdvisorResult badOverride = _advisor.DeriveSecondary("#004fd6", secondaryOverride: "nope");

		// Assert
		badPrimary.Success.Should().BeFalse(because: "an unparseable primary cannot seed a secondary");
		badOverride.Success.Should().BeFalse(because: "an unparseable override cannot be validated");
		badOverride.Error.Should().Contain("nope", because: "the failure echoes the offending override");
	}

	[Test]
	[Description("EvaluateStoredAccents bands each stored candidate: one identical to the primary is strong (not recommended); a distinct one is clean (recommended).")]
	public void EvaluateStoredAccents_ShouldBandCandidates_WhenGivenStoredHexes() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.EvaluateStoredAccents("#004fd6", new[] { "#004fd6", "#f94e11" });

		// Assert
		result.EvaluatedCandidates.Should().HaveCount(2, because: "both stored candidates are scored");
		result.EvaluatedCandidates![0].SimilarityBand.Should().Be("strong", because: "a candidate identical to the primary is maximally similar");
		result.EvaluatedCandidates[0].Recommend.Should().BeFalse(because: "a strong-similarity candidate is not offered");
		result.EvaluatedCandidates[1].SimilarityBand.Should().Be("clean", because: "#f94e11 is distinct enough from the primary");
		result.EvaluatedCandidates[1].Recommend.Should().BeTrue(because: "a clean candidate is offered plainly");
	}

	[Test]
	[Description("EvaluateStoredAccents skips unparseable candidates and fails on an invalid primary.")]
	public void EvaluateStoredAccents_ShouldSkipInvalidAndFailOnBadPrimary() {
		// Act
		ThemePaletteAdvisorResult skipped = _advisor.EvaluateStoredAccents("#004fd6", new[] { "#f94e11", "not-a-color" });
		ThemePaletteAdvisorResult badPrimary = _advisor.EvaluateStoredAccents("nope", new[] { "#f94e11" });

		// Assert
		skipped.EvaluatedCandidates.Should().HaveCount(1, because: "an unparseable candidate is silently skipped, not reported");
		badPrimary.Success.Should().BeFalse(because: "candidates cannot be scored without a valid primary");
	}

	[Test]
	[Description("ValidateColor applies the role overlay: a low-contrast primary is strong, a low-contrast secondary is only warn, and a readable colour passes.")]
	public void ValidateColor_ShouldApplyRoleOverlay_WhenPrimaryOrSecondary() {
		// Act
		ThemePaletteAdvisorResult primary = _advisor.ValidateColor("primary", "#cccccc", primary: null);
		ThemePaletteAdvisorResult secondary = _advisor.ValidateColor("secondary", "#cccccc", primary: null);
		ThemePaletteAdvisorResult ok = _advisor.ValidateColor("primary", "#004fd6", primary: null);

		// Assert
		primary.Verdict.Should().Be("strong", because: "a low-contrast primary is a strong failure");
		primary.Warning!.Code.Should().Be("PRIMARY_LOW_CONTRAST_ON_WHITE", because: "the primary low-contrast code is returned");
		secondary.Verdict.Should().Be("warn", because: "a low-contrast secondary is a soft warning, not strong");
		ok.Verdict.Should().Be("pass", because: "a readable primary passes");
	}

	[Test]
	[Description("ValidateColor role=accent ranks strong similarity first, then a contrast failure as warn, and passes a distinct readable accent.")]
	public void ValidateColor_ShouldRankSimilarityFirst_WhenRoleAccent() {
		// Act
		ThemePaletteAdvisorResult tooSimilar = _advisor.ValidateColor("accent", "#004fd6", primary: "#004fd6");
		ThemePaletteAdvisorResult lowContrast = _advisor.ValidateColor("accent", "#cccccc", primary: "#004fd6");
		ThemePaletteAdvisorResult distinct = _advisor.ValidateColor("accent", "#f94e11", primary: "#004fd6");

		// Assert
		tooSimilar.Verdict.Should().Be("strong", because: "an accent identical to the primary is strongly too similar");
		tooSimilar.Warning!.Code.Should().Be("ACCENT_TOO_SIMILAR_TO_PRIMARY", because: "strong similarity outranks a contrast warning");
		lowContrast.Verdict.Should().Be("warn", because: "a distinct but low-contrast accent is a soft warning");
		lowContrast.Warning!.Code.Should().Be("ACCENT_LOW_CONTRAST_ON_WHITE", because: "with no strong similarity, the contrast failure is reported");
		distinct.Verdict.Should().Be("pass", because: "a distinct, readable accent clears both gates");
	}

	[Test]
	[Description("ValidateColor fails with INVALID_ROLE for a null or unknown role, and echoes the rejection code for an unparseable colour.")]
	public void ValidateColor_ShouldFail_WhenRoleUnknownOrColorInvalid() {
		// Act
		ThemePaletteAdvisorResult nullRole = _advisor.ValidateColor(null, "#004fd6", primary: null);
		ThemePaletteAdvisorResult unknownRole = _advisor.ValidateColor("brand", "#004fd6", primary: null);
		ThemePaletteAdvisorResult badColor = _advisor.ValidateColor("primary", "nope", primary: null);

		// Assert
		nullRole.Success.Should().BeFalse(because: "a role is required to validate a colour");
		nullRole.Error.Should().StartWith("INVALID_ROLE", because: "a missing role is reported with the role code");
		unknownRole.Error.Should().StartWith("INVALID_ROLE", because: "an unknown role cannot be validated");
		badColor.Success.Should().BeFalse(because: "an unparseable colour cannot be validated");
		badColor.Error.Should().Contain("nope", because: "the failure echoes the offending colour");
	}

	[Test]
	[Description("SuggestAccents generates the three candidates, scores them, marks exactly one best, counts the valid ones, and gates the primary-as-accent fallback.")]
	public void SuggestAccents_ShouldGenerateScoreAndMarkBest_WhenGivenPrimary() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.SuggestAccents("#004fd6");

		// Assert
		result.SuggestedCandidates.Should().HaveCount(3, because: "three candidates are generated at +135/180/225");
		result.ValidCandidateCount.Should().Be(1, because: "only the +135 candidate clears both the 3:1 contrast and 0.07 distance gates for #004fd6");
		result.BestCandidateHex.Should().Be("#f94e11", because: "the +135 candidate is the most distinct valid accent");
		result.SuggestedCandidates!.Should().ContainSingle(candidate => candidate.IsBest, because: "exactly one candidate is marked best");
		result.PrimaryAsAccentAvailable.Should().BeTrue(because: "with a single valid candidate the primary-as-accent fallback is offered");
	}

	[Test]
	[Description("SuggestAccents withholds the primary-as-accent fallback when more than one candidate is valid.")]
	public void SuggestAccents_ShouldWithholdPrimaryAsAccent_WhenMultipleCandidatesValid() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.SuggestAccents("#2e7d32");

		// Assert
		result.ValidCandidateCount.Should().BeGreaterThan(1, because: "the green primary yields multiple candidates that clear both gates");
		result.PrimaryAsAccentAvailable.Should().BeFalse(because: "the primary-as-accent fallback is withheld when more than one candidate is valid");
	}

	[Test]
	[Description("SuggestAccents fails on an invalid primary.")]
	public void SuggestAccents_ShouldFail_WhenPrimaryInvalid() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.SuggestAccents("nope");

		// Assert
		result.Success.Should().BeFalse(because: "accent candidates cannot be generated without a valid primary");
	}

	[Test]
	[Description("Preview emits only the base -500 stop for every brand and system role and sources success/error from the template default when no override is supplied.")]
	public void Preview_ShouldBuildBase500AndSourceDefaults_WhenNoOverrides() {
		// Arrange
		GivenTemplateDefaults();

		// Act
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "#0d2e4e", "#f94e11", success: null, error: null, version: "10.0", fullStops: false);

		// Assert
		result.Success.Should().BeTrue(because: "a valid preview with template-sourced system colours completes");
		result.Palettes.Should().ContainKeys(new[] { "primary", "secondary", "accent", "success", "error" },
			because: "every brand and system role is previewed; neutral is never emitted");
		result.Palettes!["primary"].Should().ContainKey("500").And.HaveCount(1, because: "the default preview surfaces only the base -500 per role, not the palette stops");
		result.SuccessSource.Should().Be("template-default", because: "no success override was supplied");
		result.ResolvedVersion.Should().Be("10.0", because: "the offline resolver reports the version actually used");
	}

	[Test]
	[Description("Preview returns the full 12-stop palette scale (more than the single base -500) when full stops are requested.")]
	public void Preview_ShouldReturnFullScale_WhenFullStopsRequested() {
		// Arrange
		GivenTemplateDefaults();

		// Act
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "#0d2e4e", "#f94e11", success: null, error: null, version: "10.0", fullStops: true);

		// Assert
		result.Success.Should().BeTrue(because: "a valid full-stops preview completes");
		result.Palettes!["primary"].Count.Should().BeGreaterThan(1, because: "full stops expose the whole scale, not just the base -500");
	}

	[Test]
	[Description("Preview surfaces user-override metadata for a system colour and adds a bare low-contrast warning code when that override is below 3:1.")]
	public void Preview_ShouldSurfaceOverrideAndWarn_WhenSystemColorLowContrast() {
		// Arrange
		GivenTemplateDefaults();

		// Act — success is overridden with a low-contrast grey; error still comes from the template default.
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "#0d2e4e", "#f94e11", success: "#cccccc", error: null, version: "10.0", fullStops: false);

		// Assert
		result.SuccessSource.Should().Be("user-override", because: "the success colour was supplied explicitly");
		result.NormalizedSuccess.Should().Be("#cccccc", because: "the override is normalized and surfaced");
		result.SuccessContrastVerdict.Should().BeFalse(because: "a light grey is below 3:1 on white");
		result.Warnings.Should().Contain("SUCCESS_LOW_CONTRAST_ON_WHITE", because: "a below-threshold system override adds a bare warning code");
		result.ErrorSource.Should().Be("template-default", because: "the error colour was not overridden");
	}

	[Test]
	[Description("Preview fails with VERSION_NOT_SUPPORTED when the offline resolver rejects the requested version.")]
	public void Preview_ShouldFail_WhenVersionUnsupported() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>())
			.Returns(_ => throw new ArgumentException("Themes require Creatio 10.0 or newer."));

		// Act
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "#0d2e4e", "#f94e11", success: null, error: null, version: "9.0", fullStops: false);

		// Assert
		result.Success.Should().BeFalse(because: "an unsupported version cannot source system defaults");
		result.Error.Should().Contain("VERSION_NOT_SUPPORTED", because: "the version failure maps to a stable code");
	}

	[Test]
	[Description("Preview fails with TEMPLATE_DEFAULT_MISSING (qualified by the resolved version) when a system default cannot be sourced and no override is supplied.")]
	public void Preview_ShouldFail_WhenTemplateDefaultMissing() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>()).Returns("10.0");
		_templateProvider.GetCssTemplate(Arg.Any<string>()).Returns("--crt-palette-error-500: #d2310d;\n");

		// Act
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "#0d2e4e", "#f94e11", success: null, error: null, version: "10.0", fullStops: false);

		// Assert
		result.Success.Should().BeFalse(because: "a preview cannot render a role whose system default is missing");
		result.Error.Should().Contain("TEMPLATE_DEFAULT_MISSING", because: "the missing default maps to a stable code");
		result.Error.Should().Contain("@10.0", because: "the code is qualified by the resolved template version per the contract");
	}

	[Test]
	[Description("Preview fails and echoes the offending input when a brand colour (here the secondary) cannot be normalized, before any template lookup.")]
	public void Preview_ShouldFail_WhenBrandColorInvalid() {
		// Act
		ThemePaletteAdvisorResult result = _advisor.Preview("#004fd6", "not-a-color", "#f94e11", success: null, error: null, version: "10.0", fullStops: false);

		// Assert
		result.Success.Should().BeFalse(because: "a preview cannot be built from an unparseable brand colour");
		result.Error.Should().Contain("not-a-color", because: "the failure echoes the offending brand colour");
		_templateProvider.DidNotReceive().ResolveCompatibleVersion(Arg.Any<string>());
	}

	private void GivenTemplateDefaults(string version = "10.0", string success = "#0b8500", string error = "#d2310d") {
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>()).Returns(version);
		_templateProvider.GetCssTemplate(Arg.Any<string>()).Returns(
			$"--crt-palette-success-500: {success};\n--crt-palette-error-500: {error};\n");
	}
}
