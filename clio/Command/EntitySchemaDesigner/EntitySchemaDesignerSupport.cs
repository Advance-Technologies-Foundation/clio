using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

internal static class EntitySchemaDesignerSupport
{
	internal const string DefaultCultureName = "en-US";
	internal const string EntitySchemaManagerName = "EntitySchemaManager";

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
			
			["integer"] = 4,
			["datetime"] = 7,
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

	internal static string GetCurrentCultureName() {
		string cultureName = CultureInfo.CurrentCulture.Name;
		return string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName;
	}

	internal static string GetSupportedTypesList() {
		return string.Join(", ", SupportedDataValueTypes.Keys.OrderBy(key => key));
	}

	internal static LocalizableStringDto CreateLocalizableString(string value, string cultureName = null) {
		return new LocalizableStringDto {
			CultureName = string.IsNullOrWhiteSpace(cultureName) ? GetCurrentCultureName() : cultureName,
			Value = value
		};
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

	internal static bool IsLookupType(this EntitySchemaColumnDto column) {
		return column?.DataValueType == SupportedDataValueTypes["lookup"];
	}

	internal static bool IsGuidType(this EntitySchemaColumnDto column) {
		return column?.DataValueType == SupportedDataValueTypes["guid"];
	}

	internal static bool IsTextType(this EntitySchemaColumnDto column) {
		return column?.DataValueType == SupportedDataValueTypes["text"];
	}

	internal static bool HasValue(this EntityDesignSchemaDto schema) {
		return schema != null && schema.UId != Guid.Empty;
	}

	internal static void EnsurePackageAssigned(EntityDesignSchemaDto schema, PackageInfo package) {
		schema.Package ??= new WorkspacePackageDto();
		schema.Package.UId = package.Descriptor.UId;
		schema.Package.Name = package.Descriptor.Name;
	}
}
