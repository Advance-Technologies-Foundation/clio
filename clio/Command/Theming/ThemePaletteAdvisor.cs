using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Theming;

namespace Clio.Command.Theming;

/// <summary>
/// Projects the deterministic <c>Clio.Theming</c> engine into the pre-computed verdict packets the
/// <c>advise-theme-palette</c> MCP tool returns. Owns the tool-facing concepts the engine has no notion of —
/// verdicts, warning codes, and threshold-free messages — while every threshold (3:1 contrast, 0.10/0.07
/// similarity) stays in the engine. All colour math is delegated; this type never re-implements it.
/// </summary>
public interface IThemePaletteAdvisor {
	/// <summary>Triages raw brand colours: normalizes each, scores contrast on white, and identifies the primary candidate.</summary>
	ThemePaletteAdvisorResult Triage(IReadOnlyList<string> colors);

	/// <summary>Evaluates the primary for readability and returns the compliant / adapted / could-not-adapt outcome.</summary>
	ThemePaletteAdvisorResult AdaptPrimary(string primary);

	/// <summary>Derives the secondary from the primary; validates a supplied override against the secondary role.</summary>
	ThemePaletteAdvisorResult DeriveSecondary(string primary, string secondaryOverride);

	/// <summary>Scores already-collected candidate hexes for similarity to the primary (accent path A).</summary>
	ThemePaletteAdvisorResult EvaluateStoredAccents(string primary, IReadOnlyList<string> candidateHexes);

	/// <summary>Validates a single manually-entered colour against a role, returning the role-aware verdict.</summary>
	ThemePaletteAdvisorResult ValidateColor(string role, string color, string primary);

	/// <summary>Generates and scores the three accent candidates, marking valid ones and the best (accent path C).</summary>
	ThemePaletteAdvisorResult SuggestAccents(string primary);

	/// <summary>Builds the palette preview for every brand and system role — the base -500 per role by default, or the full 12-stop scale when <paramref name="fullStops"/> is true.</summary>
	ThemePaletteAdvisorResult Preview(string primary, string secondary, string accent, string success, string error, string version, bool fullStops);
}

/// <summary>Default <see cref="IThemePaletteAdvisor"/> over the bundled engine and theme templates.</summary>
public sealed class ThemePaletteAdvisor : IThemePaletteAdvisor {

	private const string RolePrimary = "primary";
	private const string RoleSecondary = "secondary";
	private const string RoleAccent = "accent";
	private const string RoleSuccess = "success";
	private const string RoleError = "error";

	private const string VerdictPass = "pass";
	private const string VerdictWarn = "warn";
	private const string VerdictStrong = "strong";
	private const string BandClean = "clean";
	private const string SeverityWarning = "warning";

	private const string UserOverrideSource = "user-override";
	private const string TemplateDefaultSource = "template-default";

	private static readonly int[] PreviewStops = { 500 };

	private static readonly IReadOnlyDictionary<ThemeRole, string> LowContrastCodeByRole = new Dictionary<ThemeRole, string> {
		[ThemeRole.Primary] = "PRIMARY_LOW_CONTRAST_ON_WHITE",
		[ThemeRole.Secondary] = "SECONDARY_LOW_CONTRAST_ON_WHITE",
		[ThemeRole.Accent] = "ACCENT_LOW_CONTRAST_ON_WHITE",
		[ThemeRole.Success] = "SUCCESS_LOW_CONTRAST_ON_WHITE",
		[ThemeRole.Error] = "ERROR_LOW_CONTRAST_ON_WHITE"
	};

	private const string AccentTooSimilarCode = "ACCENT_TOO_SIMILAR_TO_PRIMARY";

	private readonly IThemeTemplateProvider _templateProvider;

	/// <summary>Initializes the advisor with the bundled theme-template provider (for preview system defaults).</summary>
	public ThemePaletteAdvisor(IThemeTemplateProvider templateProvider) {
		_templateProvider = templateProvider;
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult Triage(IReadOnlyList<string> colors) {
		if (colors is null || colors.Count == 0) {
			return Failure("INVALID_COLOR: at least one colour is required for triage.");
		}
		List<TriagedColor> triaged = new(colors.Count);
		foreach (string raw in colors) {
			if (!ColorNormalizer.TryNormalize(raw, out string hex, out string rejectionCode)) {
				triaged.Add(new TriagedColor { Input = raw, Accepted = false, RejectionCode = rejectionCode });
				continue;
			}
			double contrast = ColorMetrics.ContrastRatio(hex, ColorMetrics.White);
			triaged.Add(new TriagedColor {
				Input = raw,
				NormalizedHex = hex,
				Accepted = true,
				WasConverted = WasConverted(raw, hex),
				ContrastOnWhite = contrast,
				PassesNonTextContrast = ColorMetrics.MeetsMinContrastOnWhite(hex)
			});
		}
		List<TriagedColor> accepted = triaged.Where(color => color.Accepted).ToList();
		string highestContrastHex = accepted
			.OrderByDescending(color => color.ContrastOnWhite)
			.FirstOrDefault()?.NormalizedHex;
		return Success() with {
			Colors = triaged,
			AcceptedCount = accepted.Count,
			PassingCount = accepted.Count(color => color.PassesNonTextContrast == true),
			HighestContrastHex = highestContrastHex
		};
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult AdaptPrimary(string primary) {
		if (!TryNormalizeRequired(primary, out string p, out ThemePaletteAdvisorResult failure)) {
			return failure;
		}
		AdaptedPrimaryResult result = ColorMetrics.AdaptPrimary500(p);
		return result.Outcome switch {
			AdaptedPrimaryOutcome.Compliant => Success() with {
				AdaptationState = "compliant"
			},
			AdaptedPrimaryOutcome.Adapted => Success() with {
				AdaptationState = "adapted",
				Original500 = result.Original500,
				Adapted500 = result.Adapted.Adapted500,
				OriginalContrastOnWhite = result.OriginalContrastOnWhite,
				AdaptedContrastOnWhite = result.Adapted.AdaptedContrastOnWhite,
				DistanceFromOriginal = result.Adapted.DistanceFromOriginal,
				Warning = LowContrastWarning(ThemeRole.Primary, result.OriginalContrastOnWhite)
			},
			AdaptedPrimaryOutcome.CouldNotAdapt => Success() with {
				AdaptationState = "could-not-adapt",
				Original500 = result.Original500,
				OriginalContrastOnWhite = result.OriginalContrastOnWhite,
				Warning = LowContrastWarning(ThemeRole.Primary, result.OriginalContrastOnWhite)
			},
			_ => throw new InvalidOperationException($"Unhandled adapted-primary outcome: {result.Outcome}")
		};
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult DeriveSecondary(string primary, string secondaryOverride) {
		if (!TryNormalizeRequired(primary, out string p, out ThemePaletteAdvisorResult failure)) {
			return failure;
		}
		string derived = PaletteGenerator.DeriveSecondary(p);
		ThemePaletteAdvisorResult baseResult = Success() with { DerivedSecondary = derived };
		if (string.IsNullOrWhiteSpace(secondaryOverride)) {
			return baseResult;
		}
		if (!ColorNormalizer.TryNormalize(secondaryOverride, out string s, out string rejectionCode)) {
			return Failure($"{rejectionCode}: \"{secondaryOverride}\"");
		}
		double contrast = ColorMetrics.ContrastRatio(s, ColorMetrics.White);
		bool readable = ColorMetrics.MeetsMinContrastOnWhite(s);
		return baseResult with {
			SecondaryHex = s,
			SecondaryWasConverted = WasConverted(secondaryOverride, s),
			SecondaryReadable = readable,
			SecondaryContrastOnWhite = contrast,
			Warning = readable ? null : LowContrastWarning(ThemeRole.Secondary, contrast)
		};
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult EvaluateStoredAccents(string primary, IReadOnlyList<string> candidateHexes) {
		if (!TryNormalizeRequired(primary, out string p, out ThemePaletteAdvisorResult failure)) {
			return failure;
		}
		List<EvaluatedAccentCandidate> evaluated = new();
		foreach (string raw in candidateHexes ?? Array.Empty<string>()) {
			if (!ColorNormalizer.TryNormalize(raw, out string hex, out _)) {
				continue;
			}
			double distance = ColorMetrics.DistanceOklab(p, hex);
			AccentSimilarityBand band = ColorMetrics.ClassifySimilarityBand(distance);
			evaluated.Add(new EvaluatedAccentCandidate {
				Hex = hex,
				DistanceFromPrimary = distance,
				SimilarityBand = BandToWire(band),
				Recommend = band != AccentSimilarityBand.Strong,
				Warning = band == AccentSimilarityBand.Warn ? TooSimilarWarning(SeverityWarning, distance) : null
			});
		}
		return Success() with { EvaluatedCandidates = evaluated };
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult ValidateColor(string role, string color, string primary) {
		if (role is null || !TryParseRole(role, out ThemeRole parsedRole)) {
			return Failure("INVALID_ROLE: role must be primary, secondary, accent, success, or error.");
		}
		if (!ColorNormalizer.TryNormalize(color, out string c, out string rejectionCode)) {
			return Failure($"{rejectionCode}: \"{color}\"");
		}
		ThemePaletteAdvisorResult baseResult = Success() with {
			NormalizedColor = c,
			WasConverted = WasConverted(color, c)
		};
		if (parsedRole == ThemeRole.Accent) {
			return ValidateAccent(baseResult, c, primary);
		}
		double contrast = ColorMetrics.ContrastRatio(c, ColorMetrics.White);
		if (ColorMetrics.MeetsMinContrastOnWhite(c)) {
			return baseResult with { Verdict = VerdictPass };
		}
		string severity = parsedRole == ThemeRole.Primary ? VerdictStrong : SeverityWarning;
		return baseResult with {
			Verdict = VerdictOf(severity),
			Warning = LowContrastWarning(parsedRole, contrast)
		};
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult SuggestAccents(string primary) {
		if (!TryNormalizeRequired(primary, out string p, out ThemePaletteAdvisorResult failure)) {
			return failure;
		}
		ScoredAccentCandidate best = ColorMetrics.SelectBestValidAccent(
			p, PaletteGenerator.GenerateAccentCandidates(p), out int validCount, out IReadOnlyList<ScoredAccentCandidate> scored);
		List<SuggestedAccentCandidate> suggested = scored
			.Select(candidate => new SuggestedAccentCandidate {
				Hex = candidate.Hex,
				Offset = candidate.Offset,
				ContrastOnWhite = candidate.ContrastOnWhite,
				DistanceFromPrimary = candidate.DistanceFromPrimary,
				Valid = ColorMetrics.IsValidAccent(candidate.ContrastOnWhite, candidate.DistanceFromPrimary),
				IsBest = best is not null && candidate.Hex == best.Hex
			})
			.ToList();
		return Success() with {
			SuggestedCandidates = suggested,
			ValidCandidateCount = validCount,
			BestCandidateHex = best?.Hex,
			PrimaryAsAccentAvailable = validCount <= 1
		};
	}

	/// <inheritdoc />
	public ThemePaletteAdvisorResult Preview(string primary, string secondary, string accent, string success, string error, string version, bool fullStops) {
		if (!TryNormalizeRequired(primary, out string p, out ThemePaletteAdvisorResult primaryFailure)) {
			return primaryFailure;
		}
		if (!TryNormalizeRequired(secondary, out string s, out ThemePaletteAdvisorResult secondaryFailure)) {
			return secondaryFailure;
		}
		if (!TryNormalizeRequired(accent, out string a, out ThemePaletteAdvisorResult accentFailure)) {
			return accentFailure;
		}
		string resolvedVersion;
		try {
			resolvedVersion = _templateProvider.ResolveCompatibleVersion(version);
		}
		catch (ArgumentException) {
			return Failure($"VERSION_NOT_SUPPORTED: \"{version}\"");
		}
		string templateCss = LoadTemplateCss(resolvedVersion, success, error);
		SystemColorResolution successColor = ResolveSystemColor(templateCss, resolvedVersion, ThemeRole.Success, success);
		if (!successColor.Resolved) {
			return Failure(successColor.FailureMessage);
		}
		SystemColorResolution errorColor = ResolveSystemColor(templateCss, resolvedVersion, ThemeRole.Error, error);
		if (!errorColor.Resolved) {
			return Failure(errorColor.FailureMessage);
		}
		Dictionary<string, IReadOnlyDictionary<string, string>> palettes = new() {
			[RolePrimary] = BuildStops(p, fullStops),
			[RoleSecondary] = BuildStops(s, fullStops),
			[RoleAccent] = BuildStops(a, fullStops),
			[RoleSuccess] = BuildStops(successColor.Hex, fullStops),
			[RoleError] = BuildStops(errorColor.Hex, fullStops)
		};
		List<string> warnings = new();
		if (successColor.Verdict == false) {
			warnings.Add(LowContrastCodeByRole[ThemeRole.Success]);
		}
		if (errorColor.Verdict == false) {
			warnings.Add(LowContrastCodeByRole[ThemeRole.Error]);
		}
		ThemePaletteAdvisorResult result = Success() with {
			Palettes = palettes,
			SuccessSource = successColor.Source,
			ErrorSource = errorColor.Source,
			ResolvedVersion = resolvedVersion,
			Warnings = warnings.Count > 0 ? warnings : null
		};
		return WithErrorOverride(WithSuccessOverride(result, successColor), errorColor);
	}

	private ThemePaletteAdvisorResult ValidateAccent(ThemePaletteAdvisorResult baseResult, string accentHex, string primary) {
		if (!ColorNormalizer.TryNormalize(primary, out string p, out _)) {
			return Failure("INVALID_COLOR: a valid primary is required to validate an accent.");
		}
		double distance = ColorMetrics.DistanceOklab(p, accentHex);
		AccentSimilarityBand band = ColorMetrics.ClassifySimilarityBand(distance);
		bool contrastPasses = ColorMetrics.MeetsMinContrastOnWhite(accentHex);
		if (band == AccentSimilarityBand.Strong) {
			return baseResult with { Verdict = VerdictStrong, Warning = TooSimilarWarning(VerdictStrong, distance) };
		}
		if (!contrastPasses) {
			double contrast = ColorMetrics.ContrastRatio(accentHex, ColorMetrics.White);
			return baseResult with { Verdict = VerdictWarn, Warning = LowContrastWarning(ThemeRole.Accent, contrast) };
		}
		if (band == AccentSimilarityBand.Warn) {
			return baseResult with { Verdict = VerdictWarn, Warning = TooSimilarWarning(SeverityWarning, distance) };
		}
		return baseResult with { Verdict = VerdictPass };
	}

	private string LoadTemplateCss(string resolvedVersion, string success, string error) {
		if (!string.IsNullOrWhiteSpace(success) && !string.IsNullOrWhiteSpace(error)) {
			return null;
		}
		try {
			return _templateProvider.GetCssTemplate(resolvedVersion);
		}
		catch (InvalidOperationException) {
			return null;
		}
	}

	private static SystemColorResolution ResolveSystemColor(string templateCss, string resolvedVersion, ThemeRole role, string overrideValue) {
		if (!string.IsNullOrWhiteSpace(overrideValue)) {
			if (!ColorNormalizer.TryNormalize(overrideValue, out string overrideHex, out string rejectionCode)) {
				return new SystemColorResolution { Resolved = false, FailureMessage = $"{rejectionCode}: \"{overrideValue}\"" };
			}
			return new SystemColorResolution {
				Resolved = true,
				Hex = overrideHex,
				Source = UserOverrideSource,
				Contrast = ColorMetrics.ContrastRatio(overrideHex, ColorMetrics.White),
				Verdict = ColorMetrics.MeetsMinContrastOnWhite(overrideHex),
				Converted = WasConverted(overrideValue, overrideHex)
			};
		}
		if (templateCss == null || !ThemeTemplateDefaults.TryGetPaletteBase(templateCss, RoleToWire(role), out string hex)) {
			return new SystemColorResolution { Resolved = false, FailureMessage = $"TEMPLATE_DEFAULT_MISSING: \"{RoleToWire(role)}@{resolvedVersion}\"" };
		}
		return new SystemColorResolution { Resolved = true, Hex = hex, Source = TemplateDefaultSource };
	}

	private static ThemePaletteAdvisorResult WithSuccessOverride(ThemePaletteAdvisorResult result, SystemColorResolution resolution) {
		if (resolution.Source != UserOverrideSource) {
			return result;
		}
		return result with {
			NormalizedSuccess = resolution.Hex,
			SuccessWasConverted = resolution.Converted,
			SuccessContrastVerdict = resolution.Verdict,
			SuccessContrastOnWhite = resolution.Contrast
		};
	}

	private static ThemePaletteAdvisorResult WithErrorOverride(ThemePaletteAdvisorResult result, SystemColorResolution resolution) {
		if (resolution.Source != UserOverrideSource) {
			return result;
		}
		return result with {
			NormalizedError = resolution.Hex,
			ErrorWasConverted = resolution.Converted,
			ErrorContrastVerdict = resolution.Verdict,
			ErrorContrastOnWhite = resolution.Contrast
		};
	}

	private static IReadOnlyDictionary<string, string> BuildStops(string hex500, bool fullStops) {
		IReadOnlyDictionary<int, string> scale = PaletteGenerator.GenerateScale(hex500);
		IEnumerable<int> steps = fullStops ? scale.Keys.OrderBy(step => step) : PreviewStops;
		Dictionary<string, string> result = new();
		foreach (int step in steps) {
			if (scale.TryGetValue(step, out string hex)) {
				result[step.ToString(System.Globalization.CultureInfo.InvariantCulture)] = hex;
			}
		}
		return result;
	}

	private static bool TryNormalizeRequired(string input, out string normalizedHex, out ThemePaletteAdvisorResult failure) {
		failure = null;
		if (ColorNormalizer.TryNormalize(input, out normalizedHex, out string rejectionCode)) {
			return true;
		}
		failure = Failure($"{rejectionCode}: \"{input}\"");
		return false;
	}

	private static bool WasConverted(string input, string normalizedHex) {
		return !string.Equals(input?.Trim(), normalizedHex, StringComparison.OrdinalIgnoreCase);
	}

	private static string BandToWire(AccentSimilarityBand band) {
		return band switch {
			AccentSimilarityBand.Clean => BandClean,
			AccentSimilarityBand.Warn => VerdictWarn,
			_ => VerdictStrong
		};
	}

	private static string VerdictOf(string severity) {
		return severity == VerdictStrong ? VerdictStrong : VerdictWarn;
	}

	private static bool TryParseRole(string wire, out ThemeRole role) {
		switch (wire) {
			case RolePrimary:
				role = ThemeRole.Primary;
				return true;
			case RoleSecondary:
				role = ThemeRole.Secondary;
				return true;
			case RoleAccent:
				role = ThemeRole.Accent;
				return true;
			case RoleSuccess:
				role = ThemeRole.Success;
				return true;
			case RoleError:
				role = ThemeRole.Error;
				return true;
			default:
				role = default;
				return false;
		}
	}

	private static string RoleToWire(ThemeRole role) {
		return role switch {
			ThemeRole.Primary => RolePrimary,
			ThemeRole.Secondary => RoleSecondary,
			ThemeRole.Accent => RoleAccent,
			ThemeRole.Success => RoleSuccess,
			ThemeRole.Error => RoleError,
			_ => throw new InvalidOperationException($"Unhandled theme role: {role}")
		};
	}

	private static AdvisorWarning LowContrastWarning(ThemeRole role, double contrastOnWhite) {
		string severity = role == ThemeRole.Primary ? VerdictStrong : SeverityWarning;
		string message = role switch {
			ThemeRole.Primary => "This colour is hard to read on white; a darker variant is recommended.",
			ThemeRole.Secondary => "This secondary colour is hard to read on white.",
			ThemeRole.Accent => "This accent colour is hard to read on white.",
			ThemeRole.Success => "This success colour is hard to read on white.",
			ThemeRole.Error => "This error colour is hard to read on white.",
			_ => throw new InvalidOperationException($"Unhandled theme role: {role}")
		};
		return new AdvisorWarning {
			Code = LowContrastCodeByRole[role],
			Severity = severity,
			Message = message,
			Values = new Dictionary<string, double> { ["contrastOnWhite"] = contrastOnWhite }
		};
	}

	private static AdvisorWarning TooSimilarWarning(string severity, double distanceFromPrimary) {
		return new AdvisorWarning {
			Code = AccentTooSimilarCode,
			Severity = severity,
			Message = "This accent is very close to the primary colour.",
			Values = new Dictionary<string, double> { ["distanceFromPrimary"] = distanceFromPrimary }
		};
	}

	private static ThemePaletteAdvisorResult Success() {
		return new ThemePaletteAdvisorResult { Success = true };
	}

	private static ThemePaletteAdvisorResult Failure(string error) {
		return new ThemePaletteAdvisorResult { Success = false, Error = error };
	}

	private enum ThemeRole {
		Primary,
		Secondary,
		Accent,
		Success,
		Error
	}

	private sealed record SystemColorResolution {
		public bool Resolved { get; init; }
		public string FailureMessage { get; init; }
		public string Hex { get; init; }
		public string Source { get; init; }
		public double? Contrast { get; init; }
		public bool? Verdict { get; init; }
		public bool? Converted { get; init; }
	}
}
