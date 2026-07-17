using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

internal sealed record TitleLocalizationNormalizationResult(
	IReadOnlyDictionary<string, string>? Localizations,
	string? EffectiveTitle);

internal static class EntitySchemaDesignerSupport
{
	internal const string DefaultCultureName = "en-US";
	internal const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string BinaryTypeName = "binary";
	private const string DateTimeTypeName = "datetime";
	private const string FileTypeName = "file";
	private const string ImageTypeName = "image";
	private const string ImageLookupTypeName = "imageLookup";
	private const string SecureTextTypeName = "secureText";

	/// <summary>
	/// Fixed UId of the platform <c>SysImage</c> schema that every <c>ImageLookup</c> ("Image link") column references.
	/// clio must supply this explicitly: the <c>EntitySchemaDesignerService.svc</c> save path clio uses persists
	/// <c>ReferenceSchema.UId</c> verbatim and performs NO server-side SysImage auto-resolution (only the legacy
	/// <c>Terrasoft.Nui</c> <c>EntitySchemaDesigner</c> web service resolved it via <c>SchemaManager.GetItemByName("SysImage")</c>).
	/// The value is install-invariant (defined in core <c>Terrasoft.Core/Configuration/SysImage.cs</c>).
	/// </summary>
	internal static readonly Guid SysImageReferenceSchemaUId = new("93986bfe-2dbd-46bc-9bf9-d03dfefbf3b8");

	/// <summary>Name of the platform <c>SysImage</c> schema referenced by <c>ImageLookup</c> columns.</summary>
	internal const string SysImageReferenceSchemaName = "SysImage";

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
			[ImageLookupTypeName] = 16,
			[FileTypeName] = 25,
			[SecureTextTypeName] = 24,
			["integer"] = 4,
			[DateTimeTypeName] = 7,
			["lookup"] = 10,
			["boolean"] = 12,
			// Color (ColorDataValueType, GUID {DAFB71F9-EE9F-4E0B-A4D7-37AA15987155}) derives from
			// TextDataValueType and stores a hex string, but it is deliberately NOT registered in the
			// text-like sets below: enabling text-only options (multiline / accent / format-validated /
			// masked) on a Color column would produce a malformed column (ENG-93040 FR-07).
			["color"] = 18,
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
			["emailaddress"] = "email",
			["imagelink"] = ImageLookupTypeName,
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

	private const string CurrencyUIdString = "{969093E2-2B4E-463B-883A-3D3B8C61F0CD}";

	internal static readonly IReadOnlyDictionary<int, Guid> RuntimeDataValueTypeUIdMap =
		new Dictionary<int, Guid> {
			[0] = new("{23018567-A13C-4320-8687-FD6F9E3699BD}"),  // Guid
			[1] = new("{8B3F29BB-EA14-4CE5-A5C5-293A929B6BA2}"),  // Text
			[4] = new("{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}"),  // Integer
			[6] = new(CurrencyUIdString),                          // Currency2
			[7] = new("{D21E9EF4-C064-4012-B286-FA1A8171DA44}"),  // DateTime
			[10] = new("{B295071F-7EA9-4E62-8D1A-919BF3732FF2}"), // Lookup
			[12] = new("{90B65BF8-0FFC-4141-8779-2420877AF907}"), // Boolean
			[24] = new("{3509B9DD-2C90-4540-B82E-8F6AE85D8248}"), // SecureText
			[27] = new("{325A73B8-0F47-44A0-8412-7606F78003AC}"), // Text50
			[28] = new("{DDB3A1EE-07E8-4D62-B7A9-D0E618B00FBD}"), // Text250
			[29] = new("{C0F04627-4620-4BC0-84E5-9419DC8516B1}"), // TextUnlimited
			[30] = new("{5CA35F10-A101-4C67-A96A-383DA6AFACFC}"), // Text500
			[31] = new("{07BA84CE-0BF7-44B4-9F2C-7B15032EB98C}"), // Decimal1
			[32] = new("{5CC8060D-6D10-4773-89FC-8C12D6F659A6}"), // Decimal2
			[33] = new("{3F62414E-6C25-4182-BCEF-A73C9E396F31}"), // Decimal3
			[34] = new("{FF22E049-4D16-46EE-A529-92D8808932DC}"), // Decimal4
			[40] = new("{A4AAF398-3531-4A0D-9D75-A587F5B5B59E}"), // Decimal8
			[42] = new("{26CBA63C-DAF1-4F36-B2EA-73C0D675D90C}"), // PhoneNumber
			[43] = new("{79BCCFFA-8C8B-4863-B376-A69D2244182B}"), // RichText
			[44] = new("{26CBA64C-DAF1-4F36-B2EA-73C0D695D90C}"), // WebLink
			[45] = new("{66CBA64C-DAF1-4F36-B8EA-73C0D695D90C}"), // Email
			[47] = new("{57EE4C31-5EC4-45FA-B95D-3A2868AA89A8}"), // Decimal0
			[48] = new(CurrencyUIdString),                         // Currency0
			[49] = new(CurrencyUIdString),                         // Currency1
			[50] = new(CurrencyUIdString)                          // Currency3
		};

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
		string fieldName,
		bool requireDefaultCulture = true) {
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

		if (requireDefaultCulture && !normalizedValues.ContainsKey(DefaultCultureName)) {
			throw new EntitySchemaDesignerException(
				$"{fieldName} must contain a non-empty '{DefaultCultureName}' value.");
		}

		return normalizedValues;
	}

	internal static TitleLocalizationNormalizationResult NormalizeTitleLocalizations(
		IReadOnlyDictionary<string, string>? values,
		string? fallbackValue,
		string fieldName,
		string? effectiveCultureName = null) {
		string? normalizedFallbackValue = string.IsNullOrWhiteSpace(fallbackValue)
			? null
			: fallbackValue.Trim();
		if (values == null) {
			return new TitleLocalizationNormalizationResult(null, normalizedFallbackValue);
		}

		Dictionary<string, string> normalizedValues = new(
			NormalizeLocalizationMap(values, fieldName) ?? throw new EntitySchemaDesignerException(
				$"{fieldName} must contain at least one localization."),
			StringComparer.OrdinalIgnoreCase);
		// Creation paths pass the resolved profile culture (override > profile > en-US). When no
		// effective culture is supplied, fall back to en-US (DefaultCultureName) — NOT the host
		// machine's CultureInfo.CurrentCulture (M-1 / AC-08).
		string currentCultureName = string.IsNullOrWhiteSpace(effectiveCultureName)
			? DefaultCultureName
			: effectiveCultureName;
		string? effectiveTitle = null;
		if (normalizedValues.TryGetValue(currentCultureName, out string? currentCultureValue)
			&& !string.IsNullOrWhiteSpace(currentCultureValue)) {
			effectiveTitle = currentCultureValue;
		} else if (normalizedValues.TryGetValue(DefaultCultureName, out string? defaultCultureValue)
			&& !string.IsNullOrWhiteSpace(defaultCultureValue)) {
			effectiveTitle = defaultCultureValue;
		} else {
			effectiveTitle = normalizedValues.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		}
		return new TitleLocalizationNormalizationResult(normalizedValues, effectiveTitle ?? normalizedFallbackValue);
	}

	internal static List<LocalizableStringDto> CreateLocalizableStrings(
		IReadOnlyDictionary<string, string>? values,
		string? fallbackValue,
		string? cultureName = null) {
		if (values != null) {
			return BuildLocalizableStrings(NormalizeLocalizationMap(values, "localizations"));
		}

		if (string.IsNullOrWhiteSpace(fallbackValue)) {
			return [];
		}

		// When only a scalar fallback title is supplied (no localization map), anchor it to the
		// effective creation culture (override > profile > en-US) instead of the host
		// CultureInfo.CurrentCulture. A null cultureName preserves the legacy default.
		return [CreateLocalizableString(fallbackValue, cultureName)];
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

		string normalizedTypeName = NormalizeDataValueTypeName(typeName);
		if (SupportedDataValueTypeAliases.TryGetValue(normalizedTypeName, out string? aliasedTypeName)) {
			normalizedTypeName = aliasedTypeName;
		}

		return SupportedDataValueTypes.TryGetValue(normalizedTypeName, out dataValueType);
	}

	private static string NormalizeDataValueTypeName(string typeName) {
		return new string(typeName
			.Trim()
			.Where(char.IsLetterOrDigit)
			.ToArray());
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

	/// <summary>
	/// Returns <see langword="true"/> when the data value type is <c>ImageLookup</c> ("Image link", code 16),
	/// the reference type required by the <c>crt.ImageInput</c> Freedom UI component.
	/// </summary>
	internal static bool IsImageLookupDataValueType(int dataValueType) {
		return dataValueType == SupportedDataValueTypes[ImageLookupTypeName];
	}

	/// <summary>
	/// Builds the implicit <c>SysImage</c> reference schema carried by every <c>ImageLookup</c> column.
	/// The server-side designer mapper persists <c>ReferenceSchema.UId</c>, so clio must supply it explicitly.
	/// </summary>
	internal static EntityDesignSchemaDto CreateSysImageReferenceSchema() {
		return new EntityDesignSchemaDto {
			UId = SysImageReferenceSchemaUId,
			Name = SysImageReferenceSchemaName,
			Caption = [CreateLocalizableString(SysImageReferenceSchemaName)]
		};
	}

	internal static Guid GetDataValueTypeUIdForRuntimeType(int runtimeDataValueType) {
		if (RuntimeDataValueTypeUIdMap.TryGetValue(runtimeDataValueType, out Guid dataValueTypeUId)) {
			return dataValueTypeUId;
		}

		throw new EntitySchemaDesignerException(
			$"Unsupported dataValueType '{runtimeDataValueType}' for default-value-config source SystemValue.");
	}

	internal static string GetFriendlyTypeName(int? dataValueType) {
		if (dataValueType == null) {
			return "<none>";
		}

		return dataValueType.Value switch {
			13 => "Binary",
			14 => "Image",
			16 => "ImageLookup",
			18 => "Color",
			24 => "SecureText",
			25 => "File",
			45 => "Email",
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

	/// <summary>
	/// Single source of truth mapping the friendly <c>UsageType</c> names to the backend
	/// <c>EntitySchemaColumnUsageType</c> ordinals (General=0, Advanced=1, None=2). Used by both the
	/// write path (name -&gt; ordinal on <see cref="EntitySchemaColumnDto.UsageType"/>) and the read path
	/// (ordinal -&gt; name on the column-properties read model).
	/// </summary>
	private static readonly IReadOnlyDictionary<string, int> UsageTypeOrdinalByName =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
			["General"] = 0,
			["Advanced"] = 1,
			["None"] = 2
		};

	/// <summary>
	/// Parses a friendly <c>UsageType</c> name (General/Advanced/None, case-insensitive) to its backend ordinal.
	/// </summary>
	/// <param name="name">The friendly usage type name.</param>
	/// <param name="ordinal">The resolved ordinal when parsing succeeds.</param>
	/// <returns><see langword="true"/> when the name is recognized; otherwise <see langword="false"/>.</returns>
	internal static bool TryParseUsageType(string? name, out int ordinal) {
		if (!string.IsNullOrWhiteSpace(name)
			&& UsageTypeOrdinalByName.TryGetValue(name.Trim(), out ordinal)) {
			return true;
		}
		ordinal = default;
		return false;
	}

	/// <summary>
	/// Maps a backend <c>UsageType</c> ordinal to its friendly name via the single-source-of-truth map
	/// (0-&gt;General, 1-&gt;Advanced, 2-&gt;None). Falls back to the raw ordinal for any unexpected value; such a
	/// value is not a valid write input (the backend enum is closed at General/Advanced/None).
	/// </summary>
	internal static string GetFriendlyUsageType(int ordinal) {
		foreach (KeyValuePair<string, int> pair in UsageTypeOrdinalByName) {
			if (pair.Value == ordinal) {
				return pair.Key;
			}
		}
		return ordinal.ToString();
	}

	internal static EntitySchemaColumnDefSource? ParseDefaultValueSource(string? defaultValueSource) {
		if (string.IsNullOrWhiteSpace(defaultValueSource)) {
			return null;
		}

		return defaultValueSource.Trim().ToLowerInvariant() switch {
			"none" => EntitySchemaColumnDefSource.None,
			"const" => EntitySchemaColumnDefSource.Const,
			"settings" => EntitySchemaColumnDefSource.Settings,
			"systemvalue" => EntitySchemaColumnDefSource.SystemValue,
			"sequence" => EntitySchemaColumnDefSource.Sequence,
			_ => throw new EntitySchemaDesignerException(
				$"Unsupported default-value-source '{defaultValueSource}'. Supported values: None, Const, Settings, SystemValue, Sequence.")
		};
	}

	internal static string? GetFriendlyDefaultValueSource(EntitySchemaColumnDefValueDto? defValue) {
		if (defValue == null) {
			return null;
		}

		return defValue.ValueSourceType switch {
			EntitySchemaColumnDefSource.Settings => "Settings",
			EntitySchemaColumnDefSource.SystemValue => "SystemValue",
			EntitySchemaColumnDefSource.Sequence => "Sequence",
			EntitySchemaColumnDefSource.Const => "Const",
			EntitySchemaColumnDefSource.None => "None",
			_ => defValue.ValueSourceType.ToString()
		};
	}

	internal static EntitySchemaDefaultValueConfig? ResolveDefaultValueConfig(
		EntitySchemaDefaultValueConfig? defaultValueConfig,
		string? legacyDefaultValueSource,
		string? legacyDefaultValue,
		string context) {
		if (defaultValueConfig != null) {
			if (!string.IsNullOrWhiteSpace(legacyDefaultValueSource) || legacyDefaultValue != null) {
				throw new EntitySchemaDesignerException(
					$"{context} cannot mix legacy default-value/default-value-source with default-value-config.");
			}
			return NormalizeDefaultValueConfig(defaultValueConfig, context);
		}
		if (string.IsNullOrWhiteSpace(legacyDefaultValueSource) && legacyDefaultValue == null) {
			return null;
		}
		EntitySchemaColumnDefSource? legacySource = ParseLegacyDefaultValueSource(legacyDefaultValueSource);
		if (legacySource == EntitySchemaColumnDefSource.None) {
			if (legacyDefaultValue != null) {
				throw new EntitySchemaDesignerException(
					$"{context} cannot specify default-value when default-value-source is None.");
			}
			return new EntitySchemaDefaultValueConfig {
				Source = GetFriendlyDefaultValueSource(EntitySchemaColumnDefSource.None)
			};
		}
		if (legacyDefaultValue == null) {
			throw new EntitySchemaDesignerException(
				$"{context} requires default-value when legacy default-value-source is Const.");
		}
		return new EntitySchemaDefaultValueConfig {
			Source = GetFriendlyDefaultValueSource(EntitySchemaColumnDefSource.Const),
			Value = legacyDefaultValue
		};
	}

	internal static EntitySchemaDefaultValueConfig? CreateDefaultValueConfig(EntitySchemaColumnDefValueDto? defValue) {
		if (defValue == null) {
			return null;
		}
		string source = GetFriendlyDefaultValueSource(defValue)
			?? throw new EntitySchemaDesignerException("Default value source is missing.");
		return defValue.ValueSourceType switch {
			EntitySchemaColumnDefSource.Const => new EntitySchemaDefaultValueConfig {
				Source = source,
				Value = NormalizeScalarDefaultValue(defValue.Value, "designer default value")
			},
			EntitySchemaColumnDefSource.Settings => new EntitySchemaDefaultValueConfig {
				Source = source,
				ValueSource = NormalizeTextValue(defValue.ValueSource),
				ResolvedValueSource = NormalizeTextValue(defValue.ValueSource)
			},
			EntitySchemaColumnDefSource.SystemValue => new EntitySchemaDefaultValueConfig {
				Source = source,
				ValueSource = NormalizeTextValue(defValue.ValueSource),
				ResolvedValueSource = NormalizeTextValue(defValue.ValueSource)
			},
			EntitySchemaColumnDefSource.Sequence => new EntitySchemaDefaultValueConfig {
				Source = source,
				SequencePrefix = NormalizeTextValue(defValue.SequencePrefix, allowEmpty: true),
				SequenceNumberOfChars = defValue.SequenceNumberOfChars > 0 ? defValue.SequenceNumberOfChars : null
			},
			// A None source means the column has no default. Project it the same as a missing
			// DefValue (null) so a cleared default reads back as "no default" — consistent with a
			// column that never had one, and with the modify contract that clears via source=None.
			EntitySchemaColumnDefSource.None => null,
			_ => new EntitySchemaDefaultValueConfig {
				Source = source
			}
		};
	}

	internal static string? GetFriendlyDefaultValue(EntitySchemaColumnDefValueDto? defValue) {
		EntitySchemaDefaultValueConfig? config = CreateDefaultValueConfig(defValue);
		if (config == null) {
			return null;
		}
		EntitySchemaColumnDefSource source = ParseDefaultValueSource(config.Source)
			?? throw new EntitySchemaDesignerException("Default value source is missing.");
		return source switch {
			EntitySchemaColumnDefSource.Const => config.Value?.ToString(),
			EntitySchemaColumnDefSource.Settings => config.ValueSource,
			EntitySchemaColumnDefSource.SystemValue => config.ValueSource,
			EntitySchemaColumnDefSource.Sequence => null,
			EntitySchemaColumnDefSource.None => null,
			_ => null
		};
	}

	internal static EntitySchemaColumnDefValueDto CreateDefaultValueDto(
		EntitySchemaDefaultValueConfig config,
		string context) {
		EntitySchemaColumnDefSource source = ParseDefaultValueSource(config.Source)
			?? throw new EntitySchemaDesignerException($"{context} requires default-value-config.source.");
		return source switch {
			EntitySchemaColumnDefSource.Const => new EntitySchemaColumnDefValueDto {
				ValueSourceType = source,
				Value = NormalizeScalarDefaultValue(config.Value, $"{context} default-value-config.value")
					?? throw new EntitySchemaDesignerException(
						$"{context} requires default-value-config.value when source is Const.")
			},
			EntitySchemaColumnDefSource.Settings => new EntitySchemaColumnDefValueDto {
				ValueSourceType = source,
				ValueSource = RequireTextValue(
					config.ValueSource,
					$"{context} requires default-value-config.value-source when source is Settings.")
			},
			EntitySchemaColumnDefSource.SystemValue => new EntitySchemaColumnDefValueDto {
				ValueSourceType = source,
				ValueSource = RequireTextValue(
					config.ValueSource,
					$"{context} requires default-value-config.value-source when source is SystemValue.")
			},
			EntitySchemaColumnDefSource.Sequence => new EntitySchemaColumnDefValueDto {
				ValueSourceType = source,
				SequencePrefix = ResolveSequencePrefix(config, context),
				SequenceNumberOfChars = RequirePositiveNumber(
					config.SequenceNumberOfChars,
					$"{context} requires default-value-config.sequence-number-of-chars when source is Sequence.")
			},
			EntitySchemaColumnDefSource.None => throw new EntitySchemaDesignerException(
				$"{context} must not create a default-value DTO when source is None."),
			_ => throw new EntitySchemaDesignerException($"{context} has unsupported default-value source '{config.Source}'.")
		};
	}

	internal static void ValidateDefaultValueConfig(
		EntitySchemaDefaultValueConfig? config,
		int dataValueType,
		string context) {
		if (config == null) {
			return;
		}
		EntitySchemaColumnDefSource source = ParseDefaultValueSource(config.Source)
			?? throw new EntitySchemaDesignerException($"{context} requires default-value-config.source.");
		if (source == EntitySchemaColumnDefSource.Const
			&& IsBinaryLikeDataValueType(dataValueType)) {
			throw new EntitySchemaDesignerException(
				$"{context} type '{GetFriendlyTypeName(dataValueType)}' does not support default-value-config source Const.");
		}
		if (source == EntitySchemaColumnDefSource.None) {
			if (config.Value != null
				|| NormalizeTextValue(config.ValueSource) != null
				|| NormalizeTextValue(config.SequencePrefix) != null
				|| config.SequenceNumberOfChars != null) {
				throw new EntitySchemaDesignerException(
					$"{context} cannot set value, value-source, or sequence fields when default-value-config source is None.");
			}
			return;
		}
		if (source == EntitySchemaColumnDefSource.Sequence
			&& !IsTextLikeDataValueType(dataValueType)) {
			throw new EntitySchemaDesignerException(
				$"{context} type '{GetFriendlyTypeName(dataValueType)}' supports default-value-config source Sequence only for text columns.");
		}
		_ = CreateDefaultValueDto(config, context);
	}

	private static EntitySchemaColumnDefSource? ParseLegacyDefaultValueSource(string? defaultValueSource) {
		if (string.IsNullOrWhiteSpace(defaultValueSource)) {
			return null;
		}
		EntitySchemaColumnDefSource source = ParseDefaultValueSource(defaultValueSource)
			?? throw new EntitySchemaDesignerException("Default value source is required.");
		if (source is not EntitySchemaColumnDefSource.Const and not EntitySchemaColumnDefSource.None) {
			throw new EntitySchemaDesignerException(
				$"Legacy default-value-source supports only Const or None. Use default-value-config for '{GetFriendlyDefaultValueSource(source)}'.");
		}
		return source;
	}

	private static EntitySchemaDefaultValueConfig NormalizeDefaultValueConfig(
		EntitySchemaDefaultValueConfig config,
		string context) {
		EntitySchemaColumnDefSource source = ParseDefaultValueSource(config.Source)
			?? throw new EntitySchemaDesignerException($"{context} requires default-value-config.source.");
		return source switch {
			EntitySchemaColumnDefSource.Const => new EntitySchemaDefaultValueConfig {
				Source = GetFriendlyDefaultValueSource(source),
				Value = NormalizeScalarDefaultValue(config.Value, $"{context} default-value-config.value")
			},
			EntitySchemaColumnDefSource.Settings => new EntitySchemaDefaultValueConfig {
				Source = GetFriendlyDefaultValueSource(source),
				ValueSource = NormalizeTextValue(config.ValueSource)
			},
			EntitySchemaColumnDefSource.SystemValue => new EntitySchemaDefaultValueConfig {
				Source = GetFriendlyDefaultValueSource(source),
				ValueSource = NormalizeTextValue(config.ValueSource)
			},
			EntitySchemaColumnDefSource.Sequence => NormalizeSequenceDefaultValueConfig(config, source, context),
			EntitySchemaColumnDefSource.None => new EntitySchemaDefaultValueConfig {
				Source = GetFriendlyDefaultValueSource(source)
			},
			_ => throw new EntitySchemaDesignerException($"{context} has unsupported default-value source '{config.Source}'.")
		};
	}

	private static string GetFriendlyDefaultValueSource(EntitySchemaColumnDefSource source) {
		return source switch {
			EntitySchemaColumnDefSource.None => "None",
			EntitySchemaColumnDefSource.Const => "Const",
			EntitySchemaColumnDefSource.Settings => "Settings",
			EntitySchemaColumnDefSource.SystemValue => "SystemValue",
			EntitySchemaColumnDefSource.Sequence => "Sequence",
			_ => source.ToString()
		};
	}

	private static object? NormalizeScalarDefaultValue(object? value, string context) {
		if (value == null) {
			return null;
		}
		if (value is JsonElement jsonValue) {
			return NormalizeJsonScalarValue(jsonValue, context);
		}
		return value;
	}

	private static object? NormalizeJsonScalarValue(JsonElement value, string context) {
		return value.ValueKind switch {
			JsonValueKind.Null => null,
			JsonValueKind.Undefined => null,
			JsonValueKind.String => value.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number => NormalizeJsonNumber(value),
			_ => throw new EntitySchemaDesignerException(
				$"{context} must be a scalar JSON value.")
		};
	}

	private static object NormalizeJsonNumber(JsonElement value) {
		if (value.TryGetInt32(out int intValue)) {
			return intValue;
		}
		if (value.TryGetInt64(out long longValue)) {
			return longValue;
		}
		if (value.TryGetDecimal(out decimal decimalValue)) {
			return decimalValue;
		}
		return value.GetDouble();
	}

	// The platform sequence default supports only a static prefix before the padded number;
	// a mask therefore must keep '{0}' as its single, trailing placeholder.
	private const string SequenceMaskPlaceholder = "{0}";

	// Normalizes a Sequence default without consuming the mask. The mask/prefix/value-source
	// combination is validated up front (throws on conflict or unsupported mask), but the raw
	// mask stays in Value so the single authoritative parse happens later in CreateDefaultValueDto.
	// Parsing here and again there would run the extracted prefix through the prefix trim twice and
	// silently drop a mask's edge whitespace ('INV {0}' -> 'INV') on the request path only (ENG-93375).
	private static EntitySchemaDefaultValueConfig NormalizeSequenceDefaultValueConfig(
		EntitySchemaDefaultValueConfig config,
		EntitySchemaColumnDefSource source,
		string context) {
		_ = ResolveSequencePrefix(config, context);
		return new EntitySchemaDefaultValueConfig {
			Source = GetFriendlyDefaultValueSource(source),
			Value = NormalizeScalarDefaultValue(config.Value, $"{context} default-value-config.value"),
			SequencePrefix = NormalizeTextValue(config.SequencePrefix, allowEmpty: true),
			SequenceNumberOfChars = config.SequenceNumberOfChars
		};
	}

	private static string? ResolveSequencePrefix(EntitySchemaDefaultValueConfig config, string context) {
		if (NormalizeTextValue(config.ValueSource) != null) {
			throw new EntitySchemaDesignerException(
				$"{context} cannot set default-value-config.value-source when source is Sequence.");
		}
		string? explicitPrefix = NormalizeTextValue(config.SequencePrefix, allowEmpty: true);
		object? maskValue = NormalizeScalarDefaultValue(config.Value, $"{context} default-value-config.value");
		if (maskValue == null) {
			return explicitPrefix;
		}
		if (explicitPrefix != null) {
			throw new EntitySchemaDesignerException(
				$"{context} cannot combine default-value-config.value and sequence-prefix when source is Sequence. Set the static prefix in one of them.");
		}
		if (maskValue is not string mask) {
			throw new EntitySchemaDesignerException(
				$"{context} default-value-config.value for source Sequence must be a text mask that ends with '{{0}}' (for example 'LN-{{0}}'), or use sequence-prefix for the static prefix.");
		}
		return ParseSequenceMask(mask, context);
	}

	private static string? ParseSequenceMask(string mask, string context) {
		int placeholderIndex = mask.IndexOf(SequenceMaskPlaceholder, StringComparison.Ordinal);
		if (placeholderIndex < 0) {
			throw new EntitySchemaDesignerException(
				$"{context} default-value-config.value '{mask}' for source Sequence must contain the sequence placeholder '{{0}}' (for example 'LN-{{0}}'), or use sequence-prefix for the static prefix.");
		}
		// When the first '{0}' is the string tail there can be no second one, so a single
		// trailing-placeholder check also rejects repeated placeholders.
		if (placeholderIndex != mask.Length - SequenceMaskPlaceholder.Length) {
			throw new EntitySchemaDesignerException(
				$"{context} default-value-config.value mask '{mask}' is not supported: sequence defaults apply only a static prefix before a single trailing '{{0}}' (for example 'LN-{{0}}' produces LN-00001). Static text after the number cannot be applied.");
		}
		string prefix = mask[..placeholderIndex];
		return prefix.Length == 0 ? null : prefix;
	}

	private static string? NormalizeTextValue(string? value, bool allowEmpty = false) {
		if (value == null) {
			return null;
		}
		string trimmedValue = value.Trim();
		if (trimmedValue.Length == 0) {
			return allowEmpty ? string.Empty : null;
		}
		return trimmedValue;
	}

	private static string RequireTextValue(string? value, string errorMessage) {
		string? normalizedValue = NormalizeTextValue(value);
		return normalizedValue ?? throw new EntitySchemaDesignerException(errorMessage);
	}

	private static int RequirePositiveNumber(int? value, string errorMessage) {
		if (value is > 0) {
			return value.Value;
		}
		throw new EntitySchemaDesignerException(errorMessage);
	}
}
