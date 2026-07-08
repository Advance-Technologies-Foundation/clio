using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
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
/// packet each operation returns (over the real <see cref="ThemePaletteAdvisor"/> engine, with a substituted
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
		_tool = new AdviseThemePaletteTool(new ThemePaletteAdvisor(_templateProvider));
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
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void AdviseThemePaletteTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(AdviseThemePaletteTool)
			.GetMethod(nameof(AdviseThemePaletteTool.Advise))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Description("An empty or unknown operation returns a graceful failure, not an exception.")]
	public void Advise_ShouldReturnFailure_WhenOperationMissingOrUnknown() {
		// Act
		ThemePaletteAdvisorResult empty = _tool.Advise(new AdviseThemePaletteArgs(Operation: " "));
		ThemePaletteAdvisorResult unknown = _tool.Advise(new AdviseThemePaletteArgs(Operation: "make-magic"));

		// Assert
		empty.Success.Should().BeFalse(because: "operation is required");
		unknown.Success.Should().BeFalse(because: "an unknown operation cannot be dispatched");
		unknown.Error.Should().Contain("make-magic", because: "the error names the unknown operation");
	}

	[Test]
	[Description("Returns a structured failure naming operation when the required field is omitted entirely.")]
	public void Advise_ShouldReturnFailure_WhenOperationOmitted() {
		// Act
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs());

		// Assert
		result.Success.Should().BeFalse(because: "an advisory request without the required operation is invalid");
		result.Error.Should().Contain("operation is required",
			because: "the failure must name the exact required field the caller has to add");
	}

	[Test]
	[Description("Returns an actionable rename hint instead of silently ignoring a camelCase alias of a kebab-case argument.")]
	public void Advise_ShouldReturnRenameHint_WhenCamelCaseAliasPassed() {
		// Arrange
		AdviseThemePaletteArgs args = new(Operation: "preview", Primary: "#004fd6") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["fullStops"] = JsonSerializer.SerializeToElement(true)
			}
		};

		// Act
		ThemePaletteAdvisorResult result = _tool.Advise(args);

		// Assert
		result.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'fullStops' -> 'full-stops'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}

	[Test]
	[Description("Binds the advise-theme-palette argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag — the exact JSON->record binding the MCP host performs, which direct method calls bypass.")]
	public void AdviseThemePaletteArgs_ShouldBindKebabAndRouteCamelToExtensionData_WhenDeserializedFromRawJson() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		AdviseThemePaletteArgs kebab = JsonSerializer.Deserialize<AdviseThemePaletteArgs>(
			"""{"operation":"preview","primary":"#004fd6","candidate-hexes":["#f94e11"],"full-stops":true}""",
			options)!;
		AdviseThemePaletteArgs camel = JsonSerializer.Deserialize<AdviseThemePaletteArgs>(
			"""{"fullStops":true}""", options)!;

		// Assert
		kebab.Operation.Should().Be("preview", because: "the advertised operation field must bind");
		kebab.Primary.Should().Be("#004fd6", because: "the advertised primary field must bind");
		kebab.CandidateHexes.Should().BeEquivalentTo(new[] { "#f94e11" },
			because: "the advertised kebab-case candidate-hexes array field must bind");
		kebab.FullStops.Should().BeTrue(because: "the advertised kebab-case full-stops field must bind");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");
		camel.FullStops.Should().BeNull(
			because: "fullStops is not a declared wire name, so it must not bind");
		camel.ExtensionData.Should().ContainKey("fullStops",
			because: "the unbound camelCase spelling must land in the overflow bag so the tool can return a rename hint");
	}

	[Test]
	[Description("triage normalizes each colour, scores contrast on white, counts accepted/passing, and names the highest-contrast candidate.")]
	public void Advise_ShouldTriageColours_WhenOperationTriage() {
		// Act
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "triage", Colors: new[] { "#004fd6", "not-a-color", "#cccccc" }));

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
		ThemePaletteAdvisorResult compliant = _tool.Advise(new AdviseThemePaletteArgs(Operation: "adapt-primary", Primary: "#000000"));
		ThemePaletteAdvisorResult adapted = _tool.Advise(new AdviseThemePaletteArgs(Operation: "adapt-primary", Primary: "#cccccc"));

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
		ThemePaletteAdvisorResult auto = _tool.Advise(new AdviseThemePaletteArgs(Operation: "derive-secondary", Primary: "#004fd6"));
		ThemePaletteAdvisorResult lowOverride = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "derive-secondary", Primary: "#004fd6", Secondary: "#cccccc"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(Operation: "accent-suggest", Primary: "#004fd6"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "accent-evaluate-stored", Primary: "#004fd6", CandidateHexes: new[] { "#004fd6", "#f94e11" }));

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
		ThemePaletteAdvisorResult primary = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "primary", Color: "#cccccc"));
		ThemePaletteAdvisorResult secondary = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "secondary", Color: "#cccccc"));
		ThemePaletteAdvisorResult ok = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "primary", Color: "#004fd6"));

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
		ThemePaletteAdvisorResult tooSimilar = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "accent", Color: "#004fd6", Primary: "#004fd6"));
		ThemePaletteAdvisorResult distinct = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "accent", Color: "#f94e11", Primary: "#004fd6"));

		// Assert
		tooSimilar.Verdict.Should().Be("strong", because: "an accent identical to the primary is strongly too similar");
		tooSimilar.Warning!.Code.Should().Be("ACCENT_TOO_SIMILAR_TO_PRIMARY", because: "strong similarity outranks a contrast warning");
		distinct.Verdict.Should().Be("pass", because: "a distinct, readable accent passes both gates");
	}

	[Test]
	[Description("accent-validate-manual routes to the canonical accent validation shape.")]
	public void Advise_ShouldValidateManualAccent_WhenOperationAccentValidateManual() {
		// Act
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "accent-validate-manual", Color: "#f94e11", Primary: "#004fd6"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "preview", Primary: "#004fd6", Secondary: "#0d2e4e", Accent: "#f94e11", Version: "10.0"));

		// Assert
		result.Success.Should().BeTrue(because: "a valid preview with template-sourced system colours completes");
		result.Palettes.Should().ContainKeys(new[] { "primary", "secondary", "accent", "success", "error" },
			because: "every brand and system role is previewed; neutral is never emitted");
		result.Palettes!["primary"].Should().ContainKey("500").And.HaveCount(1, because: "the default preview surfaces only the base -500 per role, not the palette stops");
		result.SuccessSource.Should().Be("template-default", because: "no success override was supplied");
		result.ResolvedVersion.Should().Be("10.0", because: "the offline resolver reports the version used");
	}

	[Test]
	[Description("preview emits the full 12-stop scale per role when full-stops is true, anchored on the supplied -500 values.")]
	public void Advise_ShouldBuildFullStopPreview_WhenFullStopsRequested() {
		// Arrange
		_templateProvider.ResolveCompatibleVersion(Arg.Any<string>()).Returns("10.0");
		string successDefault;
		_templateProvider.TryGetPaletteDefault(Arg.Any<string>(), "success", out successDefault)
			.Returns(callInfo => { callInfo[2] = "#0b8500"; return true; });
		string errorDefault;
		_templateProvider.TryGetPaletteDefault(Arg.Any<string>(), "error", out errorDefault)
			.Returns(callInfo => { callInfo[2] = "#d2310d"; return true; });

		// Act
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "preview", Primary: "#004fd6", Secondary: "#0d2e4e", Accent: "#f94e11", Version: "10.0",
			FullStops: true));

		// Assert
		result.Success.Should().BeTrue(because: "a valid full-stop preview completes");
		result.Palettes!["primary"].Should().HaveCount(12,
			because: "full-stops=true surfaces every palette stop per role, not just the base -500");
		result.Palettes["primary"].Should().ContainKey("500",
			because: "the base -500 anchor stays part of the full scale");
		result.Palettes["primary"]["500"].Should().Be("#004fd6",
			because: "the supplied primary -500 anchors its scale");
	}

	[Test]
	[Description("validate-color rejects a null or unknown role with a graceful INVALID_ROLE failure.")]
	public void Advise_ShouldReturnInvalidRole_WhenRoleUnknown() {
		// Act
		ThemePaletteAdvisorResult nullRole = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Color: "#004fd6"));
		ThemePaletteAdvisorResult unknownRole = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "brand", Color: "#004fd6"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "validate-color", Role: "accent", Color: "#cccccc", Primary: "#004fd6"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "preview", Primary: "#004fd6", Secondary: "#0d2e4e", Accent: "#f94e11", Version: "10.0"));

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
		ThemePaletteAdvisorResult result = _tool.Advise(new AdviseThemePaletteArgs(
			Operation: "preview", Primary: "#004fd6", Secondary: "#0d2e4e", Accent: "#f94e11", Version: "9.0"));

		// Assert
		result.Success.Should().BeFalse(because: "an unsupported version cannot source system defaults");
		result.Error.Should().Contain("VERSION_NOT_SUPPORTED", because: "the version failure maps to a stable code");
	}
}
