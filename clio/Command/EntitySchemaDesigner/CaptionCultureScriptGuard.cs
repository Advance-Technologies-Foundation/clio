using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Deterministic write-path guard that rejects a caption/label whose text is written in a script that
/// is incompatible with its localization culture key (ENG-91044). The reproduced failure was an agent
/// that detected the connected user's profile culture correctly (<c>en-US</c>) but then stored
/// non-English text (e.g. Cyrillic <c>"Заявки"</c>) under the <c>en-US</c> key, so the entity rendered
/// foreign-language labels for an English profile.
/// <para>
/// The guard is intentionally <b>asymmetric and conservative</b>: it enforces "no non-Latin letters"
/// ONLY for cultures whose language is on the curated Latin-script allow-list (<c>en</c>, <c>de</c>,
/// <c>fr</c>, …). For any other culture — Cyrillic (<c>uk-UA</c>, <c>ru-RU</c>), CJK, Arabic, an
/// unrecognised language, etc. — it does nothing, so legitimately localized captions (and Latin
/// acronyms inside non-Latin captions such as <c>"Email адреса"</c>) are never rejected. This keeps
/// the false-positive rate at zero while still catching the exact reproduced bug for the dominant
/// <c>en-US</c> profile and every European Latin-script profile.
/// </para>
/// </summary>
internal static class CaptionCultureScriptGuard {

	/// <summary>
	/// ISO 639-1 language codes whose modern writing system is Latin script. Membership is an
	/// allow-list on purpose (Decision in adr-user-profile-language-detection Rev 4): enforcement
	/// fires only for these languages, so a language omitted here is simply not validated rather than
	/// wrongly blocked. <c>az</c>/<c>uz</c>/<c>tk</c> are listed as Latin per their modern default
	/// orthography.
	/// </summary>
	private static readonly HashSet<string> LatinScriptLanguages = new(StringComparer.OrdinalIgnoreCase) {
		"en", "de", "fr", "es", "it", "pt", "nl", "sv", "da", "no", "nb", "nn", "fi", "is",
		"ga", "gd", "cy", "gl", "ca", "eu", "oc", "br", "co", "rm", "fur", "wa", "an", "ast", "kl",
		"cs", "sk", "pl", "sl", "hr", "bs", "ro", "hu", "et", "lv", "lt", "sq", "mt", "lb", "fo",
		"af", "sw", "id", "ms", "fil", "tl", "vi", "tr", "az", "uz", "tk",
		"ku", "so", "ha", "yo", "ig", "zu", "xh", "st", "tn", "mi", "sm", "haw"
	};

	private const int MaxOffendersInMessage = 8;

	/// <summary>
	/// Validates every entry of a localization map (each <c>culture → value</c> pair) for
	/// script/culture consistency. No-op when <paramref name="localizations"/> is <see langword="null"/>.
	/// </summary>
	/// <param name="localizations">The normalized localization map (e.g. <c>title-localizations</c>).</param>
	/// <param name="fieldName">The contract field name used to prefix the error message.</param>
	/// <exception cref="EntitySchemaDesignerException">
	/// Thrown when a value under a Latin-script culture key contains non-Latin letters.
	/// </exception>
	internal static void EnsureLocalizationMapMatchesCulture(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName) {
		if (localizations == null) {
			return;
		}

		foreach (KeyValuePair<string, string> localization in localizations) {
			EnsureCaptionMatchesCulture(localization.Key, localization.Value, fieldName);
		}
	}

	/// <summary>
	/// Validates a single caption value against its culture. No-op when either argument is blank or
	/// when the culture is not a known Latin-script locale.
	/// </summary>
	/// <param name="cultureName">The target culture (e.g. <c>en-US</c>).</param>
	/// <param name="value">The caption/label text authored for that culture.</param>
	/// <param name="context">The field/operation name used to prefix the error message.</param>
	/// <exception cref="EntitySchemaDesignerException">
	/// Thrown when <paramref name="value"/> contains non-Latin letters but <paramref name="cultureName"/>
	/// is a Latin-script locale.
	/// </exception>
	internal static void EnsureCaptionMatchesCulture(string? cultureName, string? value, string context) {
		if (string.IsNullOrWhiteSpace(cultureName) || string.IsNullOrWhiteSpace(value)) {
			return;
		}

		if (!IsLatinScriptCulture(cultureName)) {
			return;
		}

		List<Rune> offenders = CollectNonLatinLetters(value);
		if (offenders.Count == 0) {
			return;
		}

		throw new EntitySchemaDesignerException(
			$"{context}: the '{cultureName}' value \"{value.Trim()}\" contains non-Latin characters " +
			$"({DescribeOffenders(offenders)}), but '{cultureName}' is a Latin-script (English-style) locale. " +
			$"Author the '{cultureName}' caption in that language using Latin script, or put the localized text " +
			$"under the matching culture key (for example add a 'uk-UA' entry, or pass 'caption-culture' for the " +
			$"language you actually wrote). This usually means the connected user's profile language differs from " +
			$"the language the caption was written in — author captions in the profile language detected by " +
			$"'get-user-culture'.");
	}

	/// <summary>
	/// Returns <see langword="true"/> when the culture is written in Latin script, so the "no non-Latin
	/// letters" rule should be enforced for it. An explicit ISO 15924 script subtag (e.g. <c>Cyrl</c> in
	/// <c>az-Cyrl-AZ</c>) is authoritative and overrides the language allow-list; otherwise the base
	/// language is matched against the curated Latin-script allow-list.
	/// </summary>
	internal static bool IsLatinScriptCulture(string cultureName) {
		string[] parts = cultureName.Trim().Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) {
			return false;
		}

		// An ISO 15924 script subtag is exactly four LETTERS (e.g. "Latn", "Cyrl") and, when present,
		// declares the script explicitly — a region subtag is two letters or three digits, never four
		// letters. A culture that explicitly requests a non-Latin script must never have its
		// matching-script captions rejected, so the script subtag wins over the language allow-list.
		// The all-letters check keeps a malformed four-char subtag (e.g. "en-1234") on the language path.
		if (parts.Length >= 2 && parts[1].Length == 4 && parts[1].All(char.IsAsciiLetter)) {
			return string.Equals(parts[1], "Latn", StringComparison.OrdinalIgnoreCase);
		}

		return LatinScriptLanguages.Contains(parts[0]);
	}

	private static List<Rune> CollectNonLatinLetters(string value) {
		List<Rune> offenders = [];
		foreach (Rune rune in value.EnumerateRunes()) {
			if (Rune.IsLetter(rune) && !IsLatinLetter(rune)) {
				offenders.Add(rune);
			}
		}

		return offenders;
	}

	private static bool IsLatinLetter(Rune rune) {
		int value = rune.Value;
		return value is (>= 'A' and <= 'Z')
			or (>= 'a' and <= 'z')
			or (>= 0x00C0 and <= 0x024F)   // Latin-1 Supplement letters + Latin Extended-A/-B (accented Latin)
			or (>= 0x1E00 and <= 0x1EFF)   // Latin Extended Additional (e.g. Vietnamese)
			or (>= 0xFB00 and <= 0xFB06)   // Latin small ligatures (ﬀ ﬁ ﬂ ﬃ ﬄ ﬅ ﬆ) — deliberately NOT 0xFB13+ (Armenian/Hebrew)
			or (>= 0xFF21 and <= 0xFF3A)   // Fullwidth Latin capital letters (Ａ–Ｚ)
			or (>= 0xFF41 and <= 0xFF5A);  // Fullwidth Latin small letters (ａ–ｚ)
	}

	private static string DescribeOffenders(IEnumerable<Rune> offenders) {
		IEnumerable<string> distinct = offenders
			.Select(rune => rune.ToString())
			.Distinct(StringComparer.Ordinal)
			.Take(MaxOffendersInMessage)
			.Select(character => $"'{character}'");
		return string.Join(", ", distinct);
	}
}
