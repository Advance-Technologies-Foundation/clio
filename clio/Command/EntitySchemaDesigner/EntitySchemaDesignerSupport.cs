using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

internal static class EntitySchemaDesignerSupport
{
	internal const string DefaultCultureName = "en-US";
	internal const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string BinaryTypeName = "binary";
	private const string DateTimeTypeName = "datetime";
	private const string FileTypeName = "file";
	private const string ImageTypeName = "image";
	private const string SecureTextTypeName = "secureText";

	internal static readonly Dictionary<string, int> SupportedDataValueTypes =
		new(StringComparer.OrdinalIgnoreCase) {
			["guid"] = 0,
			["text"] = 1,
			["text50"] = 27,
			["text250"] = 28,
			["textUnlimited"] = 29,
			["text500"] = 30,
			["phoneNumber"] = 42,
			["webLink"] = 44,
			["email"] = 45,
			["richText"] = 43,
			[BinaryTypeName] = 13,
			[ImageTypeName] = 14,
			[FileTypeName] = 25,
			[SecureTextTypeName] = 24,
			["integer"] = 4,
			[DateTimeTypeName] = 7,
			["lookup"] = 10,
			["boolean"] = 12,
			["decimal0"] = 47,
			["decimal1"] = 31,
			["decimal2"] = 32,
			["decimal3"] = 33,
			["decimal4"] = 34,
			["decimal8"] = 40,
			["currency0"] = 48,
			["currency1"] = 49,
			["currency2"] = 6,
			["currency3"] = 50
		};

	internal static readonly IReadOnlyDictionary<string, string> SupportedDataValueTypeAliases =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["shorttext"] = "text50",
			["mediumtext"] = "text250",
			["longtext"] = "text500",
			["maxsizetext"] = "textUnlimited",
			["blob"] = BinaryTypeName,
			["float"] = "decimal2",
			["date"] = DateTimeTypeName,
			["time"] = DateTimeTypeName,
			["encrypted"] = SecureTextTypeName,
			["securetext"] = SecureTextTypeName,
			["password"] = SecureTextTypeName
		};

	private static readonly HashSet<int> TextDataValueTypes = [
		SupportedDataValueTypes["text"],
		SupportedDataValueTypes["text50"],
		SupportedDataValueTypes["text250"],
		SupportedDataValueTypes["text500"],
		SupportedDataValueTypes["textUnlimited"],
		SupportedDataValueTypes["phoneNumber"],
		SupportedDataValueTypes["webLink"],
		SupportedDataValueTypes["email"],
		SupportedDataValueTypes["richText"]
	];
	private static readonly HashSet<int> BinaryLikeDataValueTypes = [
		SupportedDataValueTypes[BinaryTypeName],
		SupportedDataValueTypes[ImageTypeName],
		SupportedDataValueTypes[FileTypeName]
	];

	internal static string GetCurrentCultureName() {
		string cultureName = CultureInfo.CurrentCulture.Name;
		return string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName;
	}

	internal static string GetSupportedTypesList() {
		return string.Join(", ", SupportedDataValueTypes.Keys
			.Concat(SupportedDataValueTypeAliases.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(key => key));
	}

	internal static LocalizableStringDto CreateLocalizableString(string value, string cultureName = null) {
		return new LocalizableStringDto {
			CultureName = string.IsNullOrWhiteSpace(cultureName) ? GetCurrentCultureName() : cultureName,
			Value = value
		};
	}

	internal static IReadOnlyDictionary<string, string>? NormalizeLocalizationMap(
		IReadOnlyDictionary<string, string>? values,
		string fieldName) {
		if (values == null) {
			return null;
		}

		Dictionary<string, string> normalizedValues = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> value in values) {
			string cultureName = value.Key?.Trim();
			if (string.IsNullOrWhiteSpace(cultureName)) {
				throw new EntitySchemaDesignerException($"{fieldName} must not contain empty culture names.");
			}

			if (string.IsNullOrWhiteSpace(value.Value)) {
				throw new EntitySchemaDesignerException($"{fieldName} must not contain empty values.");
			}

			normalizedValues[cultureName] = value.Value.Trim();
		}

		if (normalizedValues.Count == 0) {
			throw new EntitySchemaDesignerException($"{fieldName} must contain at least one localization.");
		}

		if (!normalizedValues.ContainsKey(DefaultCultureName)) {
			throw new EntitySchemaDesignerException(
				$"{fieldName} must contain a non-empty '{DefaultCultureName}' value.");
		}

		return normalizedValues;
	}

	internal static List<LocalizableStringDto> CreateLocalizableStrings(
		IReadOnlyDictionary<string, string>? values,
		string? fallbackValue) {
		if (values != null) {
			return BuildLocalizableStrings(NormalizeLocalizationMap(values, "localizations"));
		}

		if (string.IsNullOrWhiteSpace(fallbackValue)) {
			return [];
		}

		return [CreateLocalizableString(fallbackValue)];
	}

	internal static List<LocalizableStringDto> CreateLocalizableStrings(
		IReadOnlyDictionary<string, string> values) {
		return BuildLocalizableStrings(NormalizeLocalizationMap(values, "localizations"));
	}

	internal static void ReplaceLocalizableValues(
		ICollection<LocalizableStringDto> values,
		IReadOnlyDictionary<string, string> localizations) {
		ArgumentNullException.ThrowIfNull(values);
		values.Clear();
		foreach (LocalizableStringDto localizableValue in CreateLocalizableStrings(localizations)) {
			values.Add(localizableValue);
		}
	}

	internal static string GetRequiredLocalizationValue(
		IReadOnlyDictionary<string, string> values,
		string fieldName,
		string cultureName = DefaultCultureName) {
		IReadOnlyDictionary<string, string> normalizedValues = NormalizeLocalizationMap(values, fieldName)
			?? throw new EntitySchemaDesignerException($"{fieldName} must contain at least one localization.");
		if (!normalizedValues.TryGetValue(cultureName, out string? value) || string.IsNullOrWhiteSpace(value)) {
			throw new EntitySchemaDesignerException(
				$"{fieldName} must contain a non-empty '{cultureName}' value.");
		}

		return value;
	}

	internal static string GetLocalizableValue(IEnumerable<LocalizableStringDto> values, string cultureName = null) {
		List<LocalizableStringDto> localizableValues = values?.ToList() ?? [];
		if (localizableValues.Count == 0) {
			return null;
		}

		string effectiveCultureName = string.IsNullOrWhiteSpace(cultureName) ? GetCurrentCultureName() : cultureName;
		LocalizableStringDto exactMatch = localizableValues.FirstOrDefault(value =>
			string.Equals(value.CultureName, effectiveCultureName, StringComparison.OrdinalIgnoreCase));
		if (exactMatch != null) {
			return exactMatch.Value;
		}

		LocalizableStringDto neutralMatch = localizableValues.FirstOrDefault(value =>
			string.Equals(value.CultureName, DefaultCultureName, StringComparison.OrdinalIgnoreCase));
		return neutralMatch?.Value ?? localizableValues.First().Value;
	}

	internal static void SetLocalizableValue(ICollection<LocalizableStringDto> values, string value,
		string cultureName = null) {
		if (string.IsNullOrWhiteSpace(value)) {
			return;
		}

		string effectiveCultureName = string.IsNullOrWhiteSpace(cultureName) ? GetCurrentCultureName() : cultureName;
		LocalizableStringDto existingValue = values?.FirstOrDefault(item =>
			string.Equals(item.CultureName, effectiveCultureName, StringComparison.OrdinalIgnoreCase));
		if (existingValue != null) {
			existingValue.Value = value;
			return;
		}

		values?.Add(CreateLocalizableString(value, effectiveCultureName));
	}

	private static List<LocalizableStringDto> BuildLocalizableStrings(
		IReadOnlyDictionary<string, string>? values) {
		if (values == null) {
			return [];
		}

		return values
			.OrderBy(value => string.Equals(value.Key, DefaultCultureName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
			.ThenBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
			.Select(value => new LocalizableStringDto {
				CultureName = value.Key,
				Value = value.Value
			})
			.ToList();
	}

	internal static bool IsLookupType(this EntitySchemaColumnDto column) {
		return column?.DataValueType == SupportedDataValueTypes["lookup"];
	}

	internal static bool IsGuidType(this EntitySchemaColumnDto column) {
		return column?.DataValueType == SupportedDataValueTypes["guid"];
	}

	internal static bool IsTextType(this EntitySchemaColumnDto column) {
		return column?.DataValueType is int dataValueType && IsTextLikeDataValueType(dataValueType);
	}

	internal static bool HasValue(this EntityDesignSchemaDto schema) {
		return schema != null && schema.UId != Guid.Empty;
	}

	internal static void EnsurePackageAssigned(EntityDesignSchemaDto schema, PackageInfo package) {
		schema.Package ??= new WorkspacePackageDto();
		schema.Package.UId = package.Descriptor.UId;
		schema.Package.Name = package.Descriptor.Name;
	}

	internal static bool TryResolveDataValueType(string? typeName, out int dataValueType) {
		dataValueType = default;
		if (string.IsNullOrWhiteSpace(typeName)) {
			return false;
		}

		string normalizedTypeName = typeName.Trim();
		if (SupportedDataValueTypeAliases.TryGetValue(normalizedTypeName, out string? aliasedTypeName)) {
			normalizedTypeName = aliasedTypeName;
		}

		return SupportedDataValueTypes.TryGetValue(normalizedTypeName, out dataValueType);
	}

	internal static bool IsLookupTypeName(string? typeName) {
		return TryResolveDataValueType(typeName, out int dataValueType) &&
			dataValueType == SupportedDataValueTypes["lookup"];
	}

	internal static bool IsTextLikeDataValueType(int dataValueType) {
		return TextDataValueTypes.Contains(dataValueType);
	}

	internal static bool IsDateTimeLikeDataValueType(int dataValueType) {
		return dataValueType == SupportedDataValueTypes[DateTimeTypeName];
	}

	internal static bool IsBinaryLikeDataValueType(int dataValueType) {
		return BinaryLikeDataValueTypes.Contains(dataValueType);
	}

	internal static string GetFriendlyTypeName(int? dataValueType) {
		if (dataValueType == null) {
			return "<none>";
		}

		return dataValueType.Value switch {
			13 => "Binary",
			14 => "Image",
			16 => "ImageLookup",
			24 => "SecureText",
			25 => "File",
			27 => "ShortText",
			28 => "MediumText",
			30 => "LongText",
			29 => "MaxSizeText",
			32 => "Float",
			0 => "Guid",
			1 => "Text",
			4 => "Integer",
			7 => "DateTime",
			10 => "Lookup",
			12 => "Boolean",
			_ => dataValueType.Value.ToString()
		};
	}

	internal static EntitySchemaColumnDefSource? ParseDefaultValueSource(string? defaultValueSource) {
		if (string.IsNullOrWhiteSpace(defaultValueSource)) {
			return null;
		}

		return defaultValueSource.Trim().ToLowerInvariant() switch {
			"const" => EntitySchemaColumnDefSource.Const,
			"none" => EntitySchemaColumnDefSource.None,
			_ => throw new EntitySchemaDesignerException(
				$"Unsupported default-value-source '{defaultValueSource}'. Supported values: Const, None.")
		};
	}

	internal static string? GetFriendlyDefaultValueSource(EntitySchemaColumnDefValueDto? defValue) {
		if (defValue == null) {
			return null;
		}

		return defValue.ValueSourceType switch {
			EntitySchemaColumnDefSource.Const => "Const",
			EntitySchemaColumnDefSource.None => "None",
			_ => defValue.ValueSourceType.ToString()
		};
	}
}
