using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.McpServer.Tools;

internal static class EntitySchemaLocalizationContract {
	private const string DescriptionFieldName = "description";
	private const string DescriptionLocalizationsFieldName = "description-localizations";
	private const string TitleFieldName = "title";
	private const string TitleLocalizationsFieldName = "title-localizations";

	internal static IReadOnlyDictionary<string, string> RequireTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string context) {
		RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
		return RequireLocalizations(titleLocalizations, TitleLocalizationsFieldName, context);
	}

	internal static IReadOnlyDictionary<string, string> RequireTitleLocalizations(
		IReadOnlyDictionary<string, string>? titleLocalizations,
		string? legacyTitle,
		string? legacyCaption,
		string context) {
		RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
		RejectLegacyField(legacyCaption, "caption", TitleLocalizationsFieldName, context);
		return RequireLocalizations(titleLocalizations, TitleLocalizationsFieldName, context);
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
		if (IsRemoveAction(action)) {
			RejectLocalizationField(titleLocalizations, TitleLocalizationsFieldName, context);
			RejectLegacyField(legacyTitle, TitleFieldName, TitleLocalizationsFieldName, context);
			return null;
		}

		return IsAddAction(action)
			? RequireTitleLocalizations(titleLocalizations, legacyTitle, context)
			: NormalizeOptionalTitleLocalizations(titleLocalizations, legacyTitle, context);
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

	private static IReadOnlyDictionary<string, string> RequireLocalizations(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context) {
		if (localizations == null) {
			throw new InvalidOperationException(
				$"{context} requires '{fieldName}' with a non-empty '{EntitySchemaDesignerSupport.DefaultCultureName}' value.");
		}

		return NormalizeOptionalLocalizations(localizations, fieldName, context)
			?? throw new InvalidOperationException(
				$"{context} requires '{fieldName}' with a non-empty '{EntitySchemaDesignerSupport.DefaultCultureName}' value.");
	}

	private static IReadOnlyDictionary<string, string>? NormalizeOptionalLocalizations(
		IReadOnlyDictionary<string, string>? localizations,
		string fieldName,
		string context) {
		if (localizations == null) {
			return null;
		}

		try {
			return EntitySchemaDesignerSupport.NormalizeLocalizationMap(localizations, fieldName);
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
