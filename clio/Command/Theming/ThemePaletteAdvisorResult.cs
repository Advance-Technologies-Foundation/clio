using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.Theming;

/// <summary>
/// A tool-owned advisory warning: a stable machine <see cref="Code"/> the skill records on acceptance, a
/// <see cref="Severity"/> on the same axis as the top-level verdict, a threshold-free plain-words
/// <see cref="Message"/>, and the raw metric(s) behind it in <see cref="Values"/> (display only).
/// </summary>
public sealed record AdvisorWarning {
	/// <summary>Stable machine code (e.g. <c>ACCENT_TOO_SIMILAR_TO_PRIMARY</c>).</summary>
	[JsonPropertyName("code")]
	public string Code { get; init; }

	/// <summary>Severity on the verdict axis: <c>warning</c> or <c>strong</c>.</summary>
	[JsonPropertyName("severity")]
	public string Severity { get; init; }

	/// <summary>Threshold-free plain-words explanation the skill relays verbatim.</summary>
	[JsonPropertyName("message")]
	public string Message { get; init; }

	/// <summary>The raw metric(s) behind the warning (display only); omitted when empty.</summary>
	[JsonPropertyName("values")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, double> Values { get; init; }
}

/// <summary>One raw brand colour after triage: how it normalized and whether it is usable as a primary.</summary>
public sealed record TriagedColor {
	/// <summary>The raw input as supplied.</summary>
	[JsonPropertyName("input")]
	public string Input { get; init; }

	/// <summary>The normalized lowercase <c>#rrggbb</c>; omitted when the input was rejected.</summary>
	[JsonPropertyName("normalizedHex")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NormalizedHex { get; init; }

	/// <summary>Whether the input normalized (vs was rejected).</summary>
	[JsonPropertyName("accepted")]
	public bool Accepted { get; init; }

	/// <summary>The rejection code (<c>ALPHA_NOT_SUPPORTED</c> / <c>INVALID_COLOR</c>); omitted when accepted.</summary>
	[JsonPropertyName("rejectionCode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string RejectionCode { get; init; }

	/// <summary>Whether normalization changed the input string (drives an "I read X as Y" note); omitted when the input was rejected.</summary>
	[JsonPropertyName("wasConverted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? WasConverted { get; init; }

	/// <summary>Raw WCAG contrast on white; omitted when the input was rejected.</summary>
	[JsonPropertyName("contrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? ContrastOnWhite { get; init; }

	/// <summary>Whether the colour meets the non-text 3:1 usability gate; omitted when rejected.</summary>
	[JsonPropertyName("passesNonTextContrast")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? PassesNonTextContrast { get; init; }
}

/// <summary>A stored accent candidate scored for similarity to the primary (accent path A).</summary>
public sealed record EvaluatedAccentCandidate {
	/// <summary>Normalized candidate hex.</summary>
	[JsonPropertyName("hex")]
	public string Hex { get; init; }

	/// <summary>Raw OKLab distance from the primary (display only).</summary>
	[JsonPropertyName("distanceFromPrimary")]
	public double DistanceFromPrimary { get; init; }

	/// <summary>Similarity band: <c>clean</c> / <c>warn</c> / <c>strong</c>.</summary>
	[JsonPropertyName("similarityBand")]
	public string SimilarityBand { get; init; }

	/// <summary>Whether to offer this candidate (band is not <c>strong</c>).</summary>
	[JsonPropertyName("recommend")]
	public bool Recommend { get; init; }

	/// <summary>The too-similar warning for a <c>warn</c>-band candidate; omitted otherwise.</summary>
	[JsonPropertyName("warning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AdvisorWarning Warning { get; init; }
}

/// <summary>A generated accent candidate (accent path C), enriched with metrics and validity.</summary>
public sealed record SuggestedAccentCandidate {
	/// <summary>Candidate hex.</summary>
	[JsonPropertyName("hex")]
	public string Hex { get; init; }

	/// <summary>Hue offset (135 / 180 / 225).</summary>
	[JsonPropertyName("offset")]
	public int Offset { get; init; }

	/// <summary>Raw contrast on white (display only).</summary>
	[JsonPropertyName("contrastOnWhite")]
	public double ContrastOnWhite { get; init; }

	/// <summary>Raw OKLab distance from the primary (display only).</summary>
	[JsonPropertyName("distanceFromPrimary")]
	public double DistanceFromPrimary { get; init; }

	/// <summary>Whether the candidate passes both gates (≥3:1 contrast AND ≥0.07 distance).</summary>
	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	/// <summary>Whether this is the recommended (most distinct valid) candidate.</summary>
	[JsonPropertyName("isBest")]
	public bool IsBest { get; init; }
}

/// <summary>
/// The unified structured result of the <c>advise-theme-palette</c> tool. Only the fields relevant to the
/// requested operation are populated; everything else is omitted (null-omission mirrors <c>BuildThemeResult</c>).
/// The advisor returns pre-computed verdicts, not raw thresholds — every populated verdict field already
/// applied the clio-owned thresholds.
/// </summary>
public sealed record ThemePaletteAdvisorResult {
	/// <summary>Whether the operation completed. <c>false</c> only on a hard failure (see <see cref="Error"/>).</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The hard-failure code/message; omitted on success. A below-threshold colour is NOT a failure.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Each raw brand colour after triage; present for <c>triage</c>.</summary>
	[JsonPropertyName("colors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TriagedColor> Colors { get; init; }

	/// <summary>How many inputs normalized; present for <c>triage</c>.</summary>
	[JsonPropertyName("acceptedCount")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? AcceptedCount { get; init; }

	/// <summary>How many accepted inputs pass the 3:1 gate; present for <c>triage</c>.</summary>
	[JsonPropertyName("passingCount")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? PassingCount { get; init; }

	/// <summary>The accepted input with the highest contrast on white; present for <c>triage</c>.</summary>
	[JsonPropertyName("highestContrastHex")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string HighestContrastHex { get; init; }

	/// <summary><c>compliant</c> / <c>adapted</c> / <c>could-not-adapt</c>; present for <c>adapt-primary</c>.</summary>
	[JsonPropertyName("adaptationState")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AdaptationState { get; init; }

	/// <summary>The original primary -500; present for <c>adapt-primary</c> non-compliant states.</summary>
	[JsonPropertyName("original500")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Original500 { get; init; }

	/// <summary>The darker compliant variant; present only for <c>adapted</c>.</summary>
	[JsonPropertyName("adapted500")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Adapted500 { get; init; }

	/// <summary>Original contrast on white; present for <c>adapt-primary</c> non-compliant states.</summary>
	[JsonPropertyName("originalContrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? OriginalContrastOnWhite { get; init; }

	/// <summary>Adapted contrast on white; present only for <c>adapted</c>.</summary>
	[JsonPropertyName("adaptedContrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? AdaptedContrastOnWhite { get; init; }

	/// <summary>OKLab distance between original and adapted; present only for <c>adapted</c>.</summary>
	[JsonPropertyName("distanceFromOriginal")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? DistanceFromOriginal { get; init; }

	/// <summary>The secondary derived from the primary; present for <c>derive-secondary</c>.</summary>
	[JsonPropertyName("derivedSecondary")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string DerivedSecondary { get; init; }

	/// <summary>The normalized secondary override; present when an override was supplied.</summary>
	[JsonPropertyName("secondaryHex")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SecondaryHex { get; init; }

	/// <summary>Whether the secondary override string was converted; present when an override was supplied.</summary>
	[JsonPropertyName("secondaryWasConverted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SecondaryWasConverted { get; init; }

	/// <summary>Whether the secondary override meets 3:1 on white; present when an override was supplied.</summary>
	[JsonPropertyName("secondaryReadable")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SecondaryReadable { get; init; }

	/// <summary>Raw contrast of the secondary override; present when an override was supplied.</summary>
	[JsonPropertyName("secondaryContrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? SecondaryContrastOnWhite { get; init; }

	/// <summary>Stored candidates scored for similarity; present for <c>accent-evaluate-stored</c>.</summary>
	[JsonPropertyName("evaluatedCandidates")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<EvaluatedAccentCandidate> EvaluatedCandidates { get; init; }

	/// <summary>The three generated candidates; present for <c>accent-suggest</c>.</summary>
	[JsonPropertyName("suggestedCandidates")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<SuggestedAccentCandidate> SuggestedCandidates { get; init; }

	/// <summary>Count of valid generated candidates; present for <c>accent-suggest</c>.</summary>
	[JsonPropertyName("validCandidateCount")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? ValidCandidateCount { get; init; }

	/// <summary>The most distinct valid candidate; present for <c>accent-suggest</c> when at least one is valid.</summary>
	[JsonPropertyName("bestCandidateHex")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string BestCandidateHex { get; init; }

	/// <summary>Whether the primary-as-accent fallback should be offered; present for <c>accent-suggest</c>.</summary>
	[JsonPropertyName("primaryAsAccentAvailable")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? PrimaryAsAccentAvailable { get; init; }

	/// <summary>The normalized colour; present for <c>validate-color</c> / <c>accent-validate-manual</c>.</summary>
	[JsonPropertyName("normalizedColor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NormalizedColor { get; init; }

	/// <summary>Whether the validated colour string was converted; present for the single-colour validation ops.</summary>
	[JsonPropertyName("wasConverted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? WasConverted { get; init; }

	/// <summary><c>pass</c> / <c>warn</c> / <c>strong</c>; present for the single-colour validation ops.</summary>
	[JsonPropertyName("verdict")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Verdict { get; init; }

	/// <summary>Palette stops per role (role → step → hex); present for <c>preview</c>. By default only the base -500 per role; the full 12-stop scale when <c>fullStops</c> is true. Neutral is never emitted.</summary>
	[JsonPropertyName("palettes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Palettes { get; init; }

	/// <summary><c>template-default</c> / <c>user-override</c>; present for <c>preview</c>.</summary>
	[JsonPropertyName("successSource")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SuccessSource { get; init; }

	/// <summary><c>template-default</c> / <c>user-override</c>; present for <c>preview</c>.</summary>
	[JsonPropertyName("errorSource")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ErrorSource { get; init; }

	/// <summary>The offline template version actually used; present for <c>preview</c>.</summary>
	[JsonPropertyName("resolvedVersion")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedVersion { get; init; }

	/// <summary>The normalized success override; present for <c>preview</c> when success is a user override.</summary>
	[JsonPropertyName("normalizedSuccess")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NormalizedSuccess { get; init; }

	/// <summary>Whether the success override string was converted; present for a success override.</summary>
	[JsonPropertyName("successWasConverted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SuccessWasConverted { get; init; }

	/// <summary>Whether the success override meets 3:1; present for a success override.</summary>
	[JsonPropertyName("successContrastVerdict")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SuccessContrastVerdict { get; init; }

	/// <summary>Raw contrast of the success override; present for a success override.</summary>
	[JsonPropertyName("successContrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? SuccessContrastOnWhite { get; init; }

	/// <summary>The normalized error override; present for <c>preview</c> when error is a user override.</summary>
	[JsonPropertyName("normalizedError")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NormalizedError { get; init; }

	/// <summary>Whether the error override string was converted; present for an error override.</summary>
	[JsonPropertyName("errorWasConverted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ErrorWasConverted { get; init; }

	/// <summary>Whether the error override meets 3:1; present for an error override.</summary>
	[JsonPropertyName("errorContrastVerdict")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ErrorContrastVerdict { get; init; }

	/// <summary>Raw contrast of the error override; present for an error override.</summary>
	[JsonPropertyName("errorContrastOnWhite")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? ErrorContrastOnWhite { get; init; }

	/// <summary>Bare warning codes for failed preview overrides; omitted when empty.</summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>The single canonical warning for the operation; omitted when the verdict is a pass.</summary>
	[JsonPropertyName("warning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AdvisorWarning Warning { get; init; }

	/// <summary>Creates a hard-failure result carrying the diagnostic message.</summary>
	public static ThemePaletteAdvisorResult Failure(string error) {
		return new ThemePaletteAdvisorResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
