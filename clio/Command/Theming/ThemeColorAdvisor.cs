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
public interface IThemeColorAdvisor {
	/// <summary>Triages raw brand colours: normalizes each, scores contrast on white, and identifies the primary candidate.</summary>
	ThemeColorAdvisorResult Triage(IReadOnlyList<string> colors);

	/// <summary>Evaluates the primary for readability and returns the compliant / adapted / could-not-adapt outcome.</summary>
	ThemeColorAdvisorResult AdaptPrimary(string primary);

	/// <summary>Derives the secondary from the primary; validates a supplied override against the secondary role.</summary>
	ThemeColorAdvisorResult DeriveSecondary(string primary, string secondaryOverride);

	/// <summary>Scores already-collected candidate hexes for similarity to the primary (accent path A).</summary>
	ThemeColorAdvisorResult EvaluateStoredAccents(string primary, IReadOnlyList<string> candidateHexes);

	/// <summary>Validates a single manually-entered colour against a role, returning the role-aware verdict.</summary>
	ThemeColorAdvisorResult ValidateColor(string role, string color, string primary);

	/// <summary>Generates and scores the three accent candidates, marking valid ones and the best (accent path C).</summary>
	ThemeColorAdvisorResult SuggestAccents(string primary);

	/// <summary>Builds the palette preview for every brand and system role — the base -500 per role by default, or the full 12-stop scale when <paramref name="fullStops"/> is true.</summary>
	ThemeColorAdvisorResult Preview(string primary, string secondary, string accent, string success, string error, string version, bool fullStops);
}

/// <summary>Default <see cref="IThemeColorAdvisor"/> over the bundled engine and theme templates.</summary>
public sealed class ThemeColorAdvisor : IThemeColorAdvisor {

	private static readonly int[] PreviewStops = { 500 };

	private static readonly IReadOnlyDictionary<string, string> LowContrastCodeByRole = new Dictionary<string, string> {
		["primary"] = "PRIMARY_LOW_CONTRAST_ON_WHITE",
		["secondary"] = "SECONDARY_LOW_CONTRAST_ON_WHITE",
		["accent"] = "ACCENT_LOW_CONTRAST_ON_WHITE",
		["success"] = "SUCCESS_LOW_CONTRAST_ON_WHITE",
		["error"] = "ERROR_LOW_CONTRAST_ON_WHITE"
	};

	private const string AccentTooSimilarCode = "ACCENT_TOO_SIMILAR_TO_PRIMARY";

	private readonly IThemeTemplateProvider _templateProvider;

	/// <summary>Initializes the advisor with the bundled theme-template provider (for preview system defaults).</summary>
	public ThemeColorAdvisor(IThemeTemplateProvider templateProvider) {
		_templateProvider = templateProvider;
	}

	/// <inheritdoc />
	public ThemeColorAdvisorResult Triage(IReadOnlyList<string> colors) {
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
	public ThemeColorAdvisorResult AdaptPrimary(string primary) {
		if (!TryNormalizeRequired(primary, out string p, out ThemeColorAdvisorResult failure)) {
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
				Warning = LowContrastWarning("primary", result.OriginalContrastOnWhite)
			},
			AdaptedPrimaryOutcome.CouldNotAdapt => Success() with {
				AdaptationState = "could-not-adapt",
				Original500 = result.Original500,
				OriginalContrastOnWhite = result.OriginalContrastOnWhite,
				Warning = LowContrastWarning("primary", result.OriginalContrastOnWhite)
			},
			_ => throw new InvalidOperationException($"Unhandled adapted-primary outcome: {result.Outcome}")
		};
	}

	/// <inheritdoc />
	public ThemeColorAdvisorResult DeriveSecondary(string primary, string secondaryOverride) {
		if (!TryNormalizeRequired(primary, out string p, out ThemeColorAdvisorResult failure)) {
			return failure;
		}
		string derived = PaletteGenerator.DeriveSecondary(p);
		ThemeColorAdvisorResult baseResult = Success() with { DerivedSecondary = derived };
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
			Warning = readable ? null : LowContrastWarning("secondary", contrast)
		};
	}

	/// <inheritdoc />
	public ThemeColorAdvisorResult EvaluateStoredAccents(string primary, IReadOnlyList<string> candidateHexes) {
		if (!TryNormalizeRequired(primary, out string p, out ThemeColorAdvisorResult failure)) {
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
				Warning = band == AccentSimilarityBand.Warn ? TooSimilarWarning("warning", distance) : null
			});
		}
		return Success() with { EvaluatedCandidates = evaluated };
	}

	/// <inheritdoc />
	public ThemeColorAdvisorResult ValidateColor(string role, string color, string primary) {
		if (role is null || !LowContrastCodeByRole.ContainsKey(role)) {
			return Failure("INVALID_ROLE: role must be primary, secondary, accent, success, or error.");
		}
		if (!ColorNormalizer.TryNormalize(color, out string c, out string rejectionCode)) {
			return Failure($"{rejectionCode}: \"{color}\"");
		}
		ThemeColorAdvisorResult baseResult = Success() with {
			NormalizedColor = c,
			WasConverted = WasConverted(color, c)
		};
		if (role == "accent") {
			return ValidateAccent(baseResult, c, primary);
		}
		double contrast = ColorMetrics.ContrastRatio(c, ColorMetrics.White);
		if (ColorMetrics.MeetsMinContrastOnWhite(c)) {
			return baseResult with { Verdict = "pass" };
		}
		string severity = role == "primary" ? "strong" : "warning";
		return baseResult with {
			Verdict = VerdictOf(severity),
			Warning = LowContrastWarning(role, contrast)
		};
	}

	/// <inheritdoc />
	public ThemeColorAdvisorResult SuggestAccents(string primary) {
		if (!TryNormalizeRequired(primary, out string p, out ThemeColorAdvisorResult failure)) {
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
	public ThemeColorAdvisorResult Preview(string primary, string secondary, string accent, string success, string error, string version, bool fullStops) {
		if (!TryNormalizeRequired(primary, out string p, out ThemeColorAdvisorResult primaryFailure)) {
			return primaryFailure;
		}
		if (!TryNormalizeRequired(secondary, out string s, out ThemeColorAdvisorResult secondaryFailure)) {
			return secondaryFailure;
		}
		if (!TryNormalizeRequired(accent, out string a, out ThemeColorAdvisorResult accentFailure)) {
			return accentFailure;
		}
		string resolvedVersion;
		try {
			resolvedVersion = _templateProvider.ResolveCompatibleVersion(version);
		} catch (ArgumentException) {
			return Failure($"VERSION_NOT_SUPPORTED: \"{version}\"");
		}
		if (!TryResolveSystemColor(version, resolvedVersion, "success", success, out string successHex, out string successSource,
				out double? successContrast, out bool? successVerdict, out bool? successConverted, out string successFailure)) {
			return Failure(successFailure);
		}
		if (!TryResolveSystemColor(version, resolvedVersion, "error", error, out string errorHex, out string errorSource,
				out double? errorContrast, out bool? errorVerdict, out bool? errorConverted, out string errorFailure)) {
			return Failure(errorFailure);
		}
		Dictionary<string, IReadOnlyDictionary<string, string>> palettes = new() {
			["primary"] = BuildStops(p, fullStops),
			["secondary"] = BuildStops(s, fullStops),
			["accent"] = BuildStops(a, fullStops),
			["success"] = BuildStops(successHex, fullStops),
			["error"] = BuildStops(errorHex, fullStops)
		};
		List<string> warnings = new();
		if (successVerdict == false) {
			warnings.Add(LowContrastCodeByRole["success"]);
		}
		if (errorVerdict == false) {
			warnings.Add(LowContrastCodeByRole["error"]);
		}
		return Success() with {
			Palettes = palettes,
			SuccessSource = successSource,
			ErrorSource = errorSource,
			ResolvedVersion = resolvedVersion,
			NormalizedSuccess = successSource == "user-override" ? successHex : null,
			SuccessWasConverted = successSource == "user-override" ? successConverted : null,
			SuccessContrastVerdict = successSource == "user-override" ? successVerdict : null,
			SuccessContrastOnWhite = successSource == "user-override" ? successContrast : null,
			NormalizedError = errorSource == "user-override" ? errorHex : null,
			ErrorWasConverted = errorSource == "user-override" ? errorConverted : null,
			ErrorContrastVerdict = errorSource == "user-override" ? errorVerdict : null,
			ErrorContrastOnWhite = errorSource == "user-override" ? errorContrast : null,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}

	private ThemeColorAdvisorResult ValidateAccent(ThemeColorAdvisorResult baseResult, string accentHex, string primary) {
		if (!ColorNormalizer.TryNormalize(primary, out string p, out _)) {
			return Failure("INVALID_COLOR: a valid primary is required to validate an accent.");
		}
		double distance = ColorMetrics.DistanceOklab(p, accentHex);
		AccentSimilarityBand band = ColorMetrics.ClassifySimilarityBand(distance);
		bool contrastPasses = ColorMetrics.MeetsMinContrastOnWhite(accentHex);
		if (band == AccentSimilarityBand.Strong) {
			return baseResult with { Verdict = "strong", Warning = TooSimilarWarning("strong", distance) };
		}
		if (!contrastPasses) {
			double contrast = ColorMetrics.ContrastRatio(accentHex, ColorMetrics.White);
			return baseResult with { Verdict = "warn", Warning = LowContrastWarning("accent", contrast) };
		}
		if (band == AccentSimilarityBand.Warn) {
			return baseResult with { Verdict = "warn", Warning = TooSimilarWarning("warning", distance) };
		}
		return baseResult with { Verdict = "pass" };
	}

	private bool TryResolveSystemColor(string version, string resolvedVersion, string role, string overrideValue, out string hex,
		out string source, out double? contrast, out bool? verdict, out bool? wasConverted, out string failure) {
		hex = null;
		source = null;
		contrast = null;
		verdict = null;
		wasConverted = null;
		failure = null;
		if (!string.IsNullOrWhiteSpace(overrideValue)) {
			if (!ColorNormalizer.TryNormalize(overrideValue, out string overrideHex, out string rejectionCode)) {
				failure = $"{rejectionCode}: \"{overrideValue}\"";
				return false;
			}
			hex = overrideHex;
			source = "user-override";
			contrast = ColorMetrics.ContrastRatio(overrideHex, ColorMetrics.White);
			verdict = ColorMetrics.MeetsMinContrastOnWhite(overrideHex);
			wasConverted = WasConverted(overrideValue, overrideHex);
			return true;
		}
		bool found;
		try {
			found = _templateProvider.TryGetPaletteDefault(version, role, out hex);
		} catch (ArgumentException) {
			failure = $"VERSION_NOT_SUPPORTED: \"{version}\"";
			return false;
		} catch (InvalidOperationException) {
			found = false;
		}
		if (!found) {
			failure = $"TEMPLATE_DEFAULT_MISSING: \"{role}@{resolvedVersion}\"";
			return false;
		}
		source = "template-default";
		return true;
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

	private static bool TryNormalizeRequired(string input, out string normalizedHex, out ThemeColorAdvisorResult failure) {
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
			AccentSimilarityBand.Clean => "clean",
			AccentSimilarityBand.Warn => "warn",
			_ => "strong"
		};
	}

	private static string VerdictOf(string severity) {
		return severity == "strong" ? "strong" : "warn";
	}

	private static AdvisorWarning LowContrastWarning(string role, double contrastOnWhite) {
		string severity = role == "primary" ? "strong" : "warning";
		string message = role switch {
			"primary" => "This colour is hard to read on white; a darker variant is recommended.",
			"secondary" => "This secondary colour is hard to read on white.",
			"accent" => "This accent colour is hard to read on white.",
			"success" => "This success colour is hard to read on white.",
			_ => "This error colour is hard to read on white."
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

	private static ThemeColorAdvisorResult Success() {
		return new ThemeColorAdvisorResult { Success = true };
	}

	private static ThemeColorAdvisorResult Failure(string error) {
		return new ThemeColorAdvisorResult { Success = false, Error = error };
	}
}
