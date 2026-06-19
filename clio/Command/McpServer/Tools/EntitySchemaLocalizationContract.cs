using System;
using System.Collections.Generic;
using System.Text;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.McpServer.Tools;

internal static class EntitySchemaLocalizationContract {
	private const string DescriptionFieldName = "description";
	private const string DescriptionLocalizationsFieldName = "description-localizations";
	private const string TitleFieldName = "title";
	private const string TitleLocalizationsFieldName = "title-localizations";
	private const string UsrColumnNamePrefix = "Usr";

	/// <summary>
	/// Resolves the title localizations for an ADD/create path. <c>title-localizations</c> is OPTIONAL:
	/// when absent (or it carries no <c>en-US</c> value) the mandatory <c>en-US</c> value is auto-derived
	/// using the precedence <c>title-localizations.en-US &gt; scalar legacy title &gt; scalar legacy caption &gt;
	/// humanized column-name</c>. An add therefore NEVER hard-fails purely for a missing localization map
	/// (field-test title-localizations blocker). Explicit maps are honored unchanged and any <c>en-US</c>
	/// value (explicit or derived) is still validated against the ENG-91044 script/culture guard.
	/// </summary>
	internal static IReadOnlyDictionary<string, string> RequireTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string context) {
		return RequireTitleLocalizations(titleLocalizations, legacyTitle, null, null, context);
	}

	/// <summary>
	/// Resolves the title localizations for an ADD/create path that may also carry a scalar legacy
	/// <c>caption</c> and a <c>column-name</c> fallback. See the no-caption overload for the en-US
	/// derivation precedence and the auto-default contract.
	/// </summary>
	internal static IReadOnlyDictionary<string, string> RequireTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string? legacyCaption,
		string context) {
		return RequireTitleLocalizations(titleLocalizations, legacyTitle, legacyCaption, null, context);
	}

	/// <summary>
	/// Resolves the title localizations for an ADD/create path, threading the optional <c>column-name</c>
	/// used for the humanized en-US fallback. Precedence:
	/// <c>title-localizations.en-US &gt; scalar legacy title &gt; scalar legacy caption &gt; humanized column-name</c>.
	/// </summary>
	internal static IReadOnlyDictionary<string, string> RequireTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string? legacyCaption,
		string? columnName,
		string context) {
		IReadOnlyDictionary<string, string>? normalized =
			NormalizeAddLocalizations(titleLocalizations, TitleLocalizationsFieldName, context);
		if (normalized != null
			&& normalized.TryGetValue(EntitySchemaDesignerSupport.DefaultCultureName, out string? existingEnUs)
			&& !string.IsNullOrWhiteSpace(existingEnUs)) {
			return normalized;
		}

		string? derivedEnUs = DeriveEnUsValue(legacyTitle, legacyCaption, columnName);
		if (string.IsNullOrWhiteSpace(derivedEnUs)) {
			throw new InvalidOperationException(
				$"{context} requires '{TitleLocalizationsFieldName}' with a non-empty "
				+ $"'{EntitySchemaDesignerSupport.DefaultCultureName}' value.");
		}

		return BuildLocalizationsWithDerivedEnUs(normalized, derivedEnUs, context);
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeOptionalTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string context) {
		RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
		return NormalizeOptionalLocalizations(titleLocalizations, TitleLocalizationsFieldName, context);
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeOptionalTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string? legacyCaption,
		string context) {
		RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
		RejectLegacyField(legacyCaption, "caption", TitleLocalizationsFieldName, context);
		return NormalizeOptionalLocalizations(titleLocalizations, TitleLocalizationsFieldName, context);
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeOptionalDescriptionLocalizations(
		IReadOnlyDictionary<string, string>? descriptionLocalizations,
		string? legacyDescription,
		string context) {
		RejectLegacyField(legacyDescription, DescriptionFieldName, DescriptionLocalizationsFieldName, context);
		return NormalizeOptionalLocalizations(descriptionLocalizations, DescriptionLocalizationsFieldName, context);
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeMutationTitleLocalizations(
		string? action,
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string context) {
		return NormalizeMutationTitleLocalizations(action, titleLocalizations, legacyTitle, null, null, context);
	}

	/// <summary>
	/// Normalizes title localizations for an update-operation. For <c>add</c> the title map is OPTIONAL and
	/// the mandatory <c>en-US</c> value is auto-derived (legacy title &gt; legacy caption &gt; humanized
	/// column-name) exactly like the columns add-batch / create paths, so a bare <c>{column-name, type}</c>
	/// add never fails (field-test title-localizations blocker).
	/// </summary>
	internal static IReadOnlyDictionary<string, string>? NormalizeMutationTitleLocalizations(
		string? action,
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string? legacyCaption,
		string? columnName,
		string context) {
		if (IsRemoveAction(action)) {
			RejectLocalizationField(titleLocalizations, TitleLocalizationsFieldName, context);
			RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
			RejectLegacyField(legacyCaption, "caption", TitleLocalizationsFieldName, context);
			return null;
		}

		return IsAddAction(action)
			? RequireTitleLocalizations(titleLocalizations, legacyTitle, legacyCaption, columnName, context)
			: NormalizeOptionalTitleLocalizations(titleLocalizations, legacyTitle, legacyCaption, context);
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeMutationDescriptionLocalizations(
		string? action,
		IReadOnlyDictionary<string, string>? descriptionLocalizations,
		string? legacyDescription,
		string context) {
		if (IsRemoveAction(action)) {
			RejectLocalizationField(descriptionLocalizations, DescriptionLocalizationsFieldName, context);
			RejectLegacyField(legacyDescription, DescriptionFieldName, DescriptionLocalizationsFieldName, context);
			return null;
		}

		return NormalizeOptionalDescriptionLocalizations(descriptionLocalizations, legacyDescription, context);
	}

	internal static string GetDefaultTitle(
		IReadOnlyDictionary<string, string> titleLocalizations,
		string context) {
		try {
			return EntitySchemaDesignerSupport.GetRequiredLocalizationValue(
				titleLocalizations,
				TitleLocalizationsFieldName,
				EntitySchemaDesignerSupport.DefaultCultureName);
		} catch (Exception exception) {
			throw new InvalidOperationException($"{context}: {exception.Message}", exception);
		}
	}

	/// <summary>
	/// Picks the best available scalar source for the auto-defaulted <c>en-US</c> caption following the
	/// precedence legacy title &gt; legacy caption &gt; humanized column-name. Returns <see langword="null"/>
	/// when no source yields a non-empty value.
	/// </summary>
	private static string? DeriveEnUsValue(string? legacyTitle, string? legacyCaption, string? columnName) {
		if (!string.IsNullOrWhiteSpace(legacyTitle)) {
			return legacyTitle.Trim();
		}

		if (!string.IsNullOrWhiteSpace(legacyCaption)) {
			return legacyCaption.Trim();
		}

		return HumanizeColumnName(columnName);
	}

	/// <summary>
	/// Builds a "Title Cased" caption from a column code by dropping a leading <c>Usr</c> prefix and
	/// space-splitting PascalCase (e.g. <c>UsrDueDate</c> → <c>Due Date</c>). Falls back to the raw
	/// column-name when humanization yields an empty string. Returns <see langword="null"/> for a blank
	/// column-name.
	/// </summary>
	private static string? HumanizeColumnName(string? columnName) {
		if (string.IsNullOrWhiteSpace(columnName)) {
			return null;
		}

		string trimmed = columnName.Trim();
		string withoutPrefix = trimmed.StartsWith(UsrColumnNamePrefix, StringComparison.Ordinal)
			&& trimmed.Length > UsrColumnNamePrefix.Length
				? trimmed.Substring(UsrColumnNamePrefix.Length)
				: trimmed;

		StringBuilder builder = new(withoutPrefix.Length + 8);
		for (int index = 0; index < withoutPrefix.Length; index++) {
			char current = withoutPrefix[index];
			bool isBoundary = index > 0
				&& char.IsUpper(current)
				&& !char.IsUpper(withoutPrefix[index - 1]);
			if (isBoundary) {
				builder.Append(' ');
			}

			builder.Append(current);
		}

		string humanized = builder.ToString().Trim();
		return string.IsNullOrWhiteSpace(humanized) ? trimmed : humanized;
	}

	/// <summary>
	/// Produces a normalized localization map containing the derived <c>en-US</c> value, preserving any
	/// additional non-default cultures from the explicit map. The result is re-validated against the
	/// script/culture guard so a derived ASCII caption passes while explicit non-English en-US still fails.
	/// </summary>
	private static IReadOnlyDictionary<string, string> BuildLocalizationsWithDerivedEnUs(
		IReadOnlyDictionary<string, string>? normalizedExplicit,
		string derivedEnUs,
		string context) {
		Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);
		if (normalizedExplicit != null) {
			foreach (KeyValuePair<string, string> pair in normalizedExplicit) {
				merged[pair.Key] = pair.Value;
			}
		}

		merged[EntitySchemaDesignerSupport.DefaultCultureName] = derivedEnUs;
		return NormalizeOptionalLocalizations(merged, TitleLocalizationsFieldName, context)
			?? throw new InvalidOperationException(
				$"{context} requires '{TitleLocalizationsFieldName}' with a non-empty "
				+ $"'{EntitySchemaDesignerSupport.DefaultCultureName}' value.");
	}

	private static IReadOnlyDictionary<string, string>? NormalizeOptionalLocalizations(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context) {
		return NormalizeLocalizations(localizations, fieldName, context, requireDefaultCulture: true);
	}

	/// <summary>
	/// Normalizes a localization map for an ADD path WITHOUT requiring <c>en-US</c>: a partial map (for
	/// example only <c>uk-UA</c>) is preserved so the missing <c>en-US</c> can be auto-derived and merged.
	/// The script/culture guard still runs on whatever cultures are present.
	/// </summary>
	private static IReadOnlyDictionary<string, string>? NormalizeAddLocalizations(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context) {
		return NormalizeLocalizations(localizations, fieldName, context, requireDefaultCulture: false);
	}

	private static IReadOnlyDictionary<string, string>? NormalizeLocalizations(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context,
		bool requireDefaultCulture) {
		if (localizations == null) {
			return null;
		}

		try {
			IReadOnlyDictionary<string, string>? normalized =
				EntitySchemaDesignerSupport.NormalizeLocalizationMap(localizations, fieldName, requireDefaultCulture);
			// ENG-91044: reject text whose script does not match its culture key (e.g. Cyrillic under
			// 'en-US'), so a caption can never be stored in the wrong language for the resolved profile.
			CaptionCultureScriptGuard.EnsureLocalizationMapMatchesCulture(normalized, fieldName);
			return normalized;
		} catch (Exception exception) {
			throw new InvalidOperationException($"{context}: {exception.Message}", exception);
		}
	}

	private static void RejectLegacyField(
		string? legacyValue,
		string legacyFieldName,
		string replacementFieldName,
		string context) {
		if (!string.IsNullOrWhiteSpace(legacyValue)) {
			throw new InvalidOperationException(
				$"{context} does not accept legacy '{legacyFieldName}'. Use '{replacementFieldName}' instead.");
		}
	}

	private static void RejectLocalizationField(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context) {
		if (localizations?.Count > 0) {
			throw new InvalidOperationException(
				$"{context} does not accept '{fieldName}' when action is 'remove'.");
		}
	}

	private static bool IsAddAction(string? action) {
		return string.Equals(action?.Trim(), "add", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsRemoveAction(string? action) {
		return string.Equals(action?.Trim(), "remove", StringComparison.OrdinalIgnoreCase);
	}
}
