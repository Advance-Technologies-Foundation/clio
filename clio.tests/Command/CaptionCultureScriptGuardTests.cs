using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="CaptionCultureScriptGuard"/> (ENG-91044). The guard rejects a caption
/// whose script does not match a Latin-script culture key (e.g. Cyrillic under <c>en-US</c>) and is
/// deliberately permissive for non-Latin and unknown cultures so localized captions are never blocked.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CaptionCultureScriptGuardTests {

	private const string FieldName = "title-localizations";

	[Test]
	[Description("EnsureCaptionMatchesCulture throws when Cyrillic text is stored under the Latin-script en-US key.")]
	public void EnsureCaptionMatchesCulture_ShouldThrow_WhenCyrillicUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "Заявки";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because non-Latin text under a Latin-script locale is the exact ENG-91044 regression")
			.Which.Message.Should().Contain("en-US",
				"because the error must name the offending culture so the agent can correct it");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows plain English text under the en-US key.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenEnglishUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "Service Requests";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because Latin-script English text is valid for the en-US locale");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows accented Latin text (e.g. German) under a Latin-script key.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenAccentedLatinUnderLatinCulture() {
		// Arrange
		const string cultureName = "de-DE";
		const string caption = "Ausrüstung für Café";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because accented Latin characters belong to the Latin script and are valid for de-DE");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture throws for Cyrillic text under another Latin-script locale (de-DE).")]
	public void EnsureCaptionMatchesCulture_ShouldThrow_WhenCyrillicUnderGermanCulture() {
		// Arrange
		const string cultureName = "de-DE";
		const string caption = "Заявки";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			"because the Latin-script rule applies to every Latin-script locale, not only en-US");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows Cyrillic text under a Cyrillic-script culture key (uk-UA).")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenCyrillicUnderUkrainianCulture() {
		// Arrange
		const string cultureName = "uk-UA";
		const string caption = "Заявки";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because a Cyrillic caption is correct for the uk-UA locale and must not be blocked");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows Latin acronyms inside a Cyrillic-culture caption (no reverse check).")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenLatinAcronymUnderUkrainianCulture() {
		// Arrange
		const string cultureName = "uk-UA";
		const string caption = "Email адреса";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because the guard is asymmetric: Latin text in a non-Latin caption is common and must not be a false positive");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture does not enforce a script for a non-Latin culture (ja-JP).")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenJapaneseUnderJapaneseCulture() {
		// Arrange
		const string cultureName = "ja-JP";
		const string caption = "設備";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because ja-JP is not on the Latin-script allow-list, so its CJK captions are valid");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture treats digits, punctuation, and emoji as neutral under en-US.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenOnlyNeutralCharactersUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "#1 — ID (2024) ✅";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because only letters are script-checked; digits, punctuation, symbols and emoji are neutral");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture throws when a Cyrillic word is mixed into an otherwise English en-US caption.")]
	public void EnsureCaptionMatchesCulture_ShouldThrow_WhenMixedLatinAndCyrillicUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "Email адреса";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			"because the en-US value still contains non-Latin (Cyrillic) letters and must be authored in English");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture is a no-op for null or whitespace inputs.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenInputsAreBlank() {
		// Arrange, Act
		System.Action nullCulture = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(null, "Заявки", "caption");
		System.Action nullValue = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture("en-US", null, "caption");
		System.Action blankValue = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture("en-US", "   ", "caption");

		// Assert
		nullCulture.Should().NotThrow("because a missing culture cannot be validated");
		nullValue.Should().NotThrow("because a missing value cannot be validated");
		blankValue.Should().NotThrow("because a blank value carries no letters to validate");
	}

	[Test]
	[Description("EnsureLocalizationMapMatchesCulture throws when the only en-US entry holds Cyrillic text.")]
	public void EnsureLocalizationMapMatchesCulture_ShouldThrow_WhenEnUsEntryIsCyrillic() {
		// Arrange
		Dictionary<string, string> localizations = new() {
			["en-US"] = "Заявка"
		};

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureLocalizationMapMatchesCulture(localizations, FieldName);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			"because the mandatory en-US localization value must be English, not Cyrillic");
	}

	[Test]
	[Description("EnsureLocalizationMapMatchesCulture allows English en-US paired with a Cyrillic uk-UA entry.")]
	public void EnsureLocalizationMapMatchesCulture_ShouldNotThrow_WhenEnglishEnUsAndCyrillicUkUa() {
		// Arrange
		Dictionary<string, string> localizations = new() {
			["en-US"] = "Application",
			["uk-UA"] = "Заявка"
		};

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureLocalizationMapMatchesCulture(localizations, FieldName);

		// Assert
		act.Should().NotThrow(
			"because each value matches its culture: English under en-US and Cyrillic under uk-UA");
	}

	[Test]
	[Description("EnsureLocalizationMapMatchesCulture is a no-op when the map is null.")]
	public void EnsureLocalizationMapMatchesCulture_ShouldNotThrow_WhenMapIsNull() {
		// Arrange, Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureLocalizationMapMatchesCulture(null, FieldName);

		// Assert
		act.Should().NotThrow("because an absent localization map carries nothing to validate");
	}

	[TestCase("en-US", true)]
	[TestCase("de-DE", true)]
	[TestCase("fr-FR", true)]
	[TestCase("vi-VN", true)]
	[TestCase("en", true)]
	[TestCase("es-419", true)]
	[TestCase("uk-UA", false)]
	[TestCase("ru-RU", false)]
	[TestCase("ja-JP", false)]
	[TestCase("zh-CN", false)]
	[TestCase("el-GR", false)]
	[TestCase("az-Latn-AZ", true)]
	[TestCase("sr-Latn-RS", true)]
	[TestCase("az-Cyrl-AZ", false)]
	[TestCase("sr-Cyrl-RS", false)]
	[TestCase("zh-Hans", false)]
	[TestCase("en-1234", true)]
	[Description("IsLatinScriptCulture recognises Latin-script locales, honours explicit script subtags, and excludes non-Latin ones.")]
	public void IsLatinScriptCulture_ShouldClassifyCulture_ByScriptSubtagThenLanguage(string cultureName, bool expected) {
		// Act
		bool isLatin = CaptionCultureScriptGuard.IsLatinScriptCulture(cultureName);

		// Assert
		isLatin.Should().Be(expected,
			$"because '{cultureName}' Latin-script classification must be {expected}");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows Cyrillic text when the culture explicitly requests Cyrillic script via a subtag (az-Cyrl-AZ), overriding the Latin language allow-list.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenScriptSubtagDeclaresCyrillic() {
		// Arrange
		const string cultureName = "az-Cyrl-AZ";
		const string caption = "Аваданлыг";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because the explicit Cyrl script subtag declares Cyrillic, so Cyrillic text is correct even though 'az' is on the Latin allow-list");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture rejects Cyrillic text when the culture explicitly requests Latin script via a subtag (az-Latn-AZ).")]
	public void EnsureCaptionMatchesCulture_ShouldThrow_WhenScriptSubtagDeclaresLatin() {
		// Arrange
		const string cultureName = "az-Latn-AZ";
		const string caption = "Заявки";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			"because the explicit Latn script subtag requires Latin text, so Cyrillic must be rejected");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows Latin small ligatures (e.g. ﬁ) under en-US — they are Latin letters and must not be a false positive.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenLatinLigatureUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "Oﬃce ﬁle";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because Latin presentation-form ligatures are Latin script and must not be rejected under en-US");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture allows fullwidth Latin letters under en-US — they are Latin letters and must not be a false positive.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenFullwidthLatinUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "ＩＤ Code";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			"because fullwidth Latin letters are Latin script and must not be rejected under en-US");
	}

	[TestCase("en-US", "µm")]
	[TestCase("en-US", "µg/mL")]
	[TestCase("pt-PT", "Nº 5")]
	[TestCase("es-ES", "1ª Linha")]
	[Description("EnsureCaptionMatchesCulture allows the Latin-1 ordinal/micro letters (ª µ º) that sit below U+00C0 under Latin-script cultures — they are Latin script and must not be a false positive.")]
	public void EnsureCaptionMatchesCulture_ShouldNotThrow_WhenLatin1OrdinalOrMicroSignUnderLatinCulture(string cultureName, string caption) {
		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().NotThrow(
			$"because '{caption}' uses only Latin-1 letters (ª/µ/º are Latin script), so it is valid for the Latin-script '{cultureName}' locale");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture rejects a mixed Latin+Cyrillic caption under en-US and reports only the Cyrillic runes, never the accented Latin letter.")]
	public void EnsureCaptionMatchesCulture_ShouldReportOnlyNonLatinRunes_WhenLatinAndCyrillicMixedUnderEnUs() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "Café (кофе)";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		EntitySchemaDesignerException exception = act.Should().Throw<EntitySchemaDesignerException>(
				"because the en-US caption still contains Cyrillic letters and must be authored in English")
			.Which;
		exception.Message.Should().Contain("'к'",
			"because the offending Cyrillic rune must be listed so the author can find it");
		exception.Message.Should().NotContain("'é'",
			"because the accented Latin letter é is valid Latin script and must not be reported as an offender");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture lists a repeated offender only once (offender de-duplication).")]
	public void EnsureCaptionMatchesCulture_ShouldDeduplicateOffenders_WhenSameNonLatinRuneRepeats() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "ккк";

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because Cyrillic text under en-US must be rejected")
			.Which.Message.Should().Contain("('к')",
				"because a repeated offender must be reported once, not once per occurrence");
	}

	[Test]
	[Description("EnsureCaptionMatchesCulture truncates the offender list to the first 8 distinct runes.")]
	public void EnsureCaptionMatchesCulture_ShouldTruncateOffenderList_WhenMoreThanEightDistinctOffenders() {
		// Arrange
		const string cultureName = "en-US";
		const string caption = "АБВГДЕЖЗИК"; // 10 distinct Cyrillic letters; only the first 8 must be listed

		// Act
		System.Action act = () => CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(cultureName, caption, "caption");

		// Assert
		string message = act.Should().Throw<EntitySchemaDesignerException>(
				"because Cyrillic text under en-US must be rejected")
			.Which.Message;
		message.Should().Contain("'З'",
			"because the 8th distinct offender is within the truncation limit and must be listed");
		message.Should().NotContain("'И'",
			"because the 9th distinct offender is beyond the 8-offender limit and must be truncated");
	}
}
