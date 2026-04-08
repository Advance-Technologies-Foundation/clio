using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

internal interface IEntitySchemaDefaultValueSourceResolver
{
	EntitySchemaDefaultValueConfig Resolve(
		EntitySchemaDefaultValueConfig config,
		int dataValueType,
		string context,
		RemoteCommandOptions options);
}

internal sealed class EntitySchemaDefaultValueSourceResolver : IEntitySchemaDefaultValueSourceResolver
{
	private const string DecimalTypeName = "Decimal";
	private const string CurrencyTypeName = "Currency";

	private static readonly IReadOnlyDictionary<string, Guid> SystemValueAliasMap =
		new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) {
			["AutoGuid"] = new Guid("{03FAC162-6A98-4F29-8D28-DC2F23AB48DA}"),
			["SequentialGuid"] = new Guid("{4E8109A3-DFD7-45ED-BADD-6E8663EA7994}"),
			["CurrentDateTime"] = new Guid("{D7C295D3-3146-4EE1-AC49-3A7BD0EDC45D}"),
			["CurrentDate"] = new Guid("{91BD3856-9686-4EB2-9E7C-99F1E6CA0F02}"),
			["CurrentTime"] = new Guid("{0EC58691-F653-4C6B-927E-7CD26F7F5511}"),
			["CurrentUser"] = new Guid("{B70AFA32-18C9-4E69-949E-89DDAC35CA98}"),
			["CurrentUserContact"] = new Guid("{4F367CA9-549B-4A1A-B64E-A40123F52AC0}"),
			["CurrentUserAccount"] = new Guid("{C9A1FA4A-0392-4DC0-887C-B6E3D67470B8}"),
			["CurrentUserRoles"] = new Guid("{843FA5CD-D859-4B4E-AE27-9BFE37268785}")
		};

	private readonly IRemoteEntitySchemaDesignerClient _designerClient;
	private readonly Dictionary<Guid, IReadOnlyList<SystemValueLookupValueDto>> _systemValuesCache = new();
	private readonly Dictionary<string, IReadOnlyList<SysSettingsSelectQueryRowDto>> _settingsCache =
		new(StringComparer.OrdinalIgnoreCase);

	public EntitySchemaDefaultValueSourceResolver(IRemoteEntitySchemaDesignerClient designerClient) {
		_designerClient = designerClient;
	}

	public EntitySchemaDefaultValueConfig Resolve(
		EntitySchemaDefaultValueConfig config,
		int dataValueType,
		string context,
		RemoteCommandOptions options) {
		EntitySchemaColumnDefSource source = EntitySchemaDesignerSupport.ParseDefaultValueSource(config.Source)
			?? throw new EntitySchemaDesignerException($"{context} requires default-value-config.source.");
		return source switch {
			EntitySchemaColumnDefSource.SystemValue => ResolveSystemValue(config, dataValueType, context, options),
			EntitySchemaColumnDefSource.Settings => ResolveSettings(config, dataValueType, context, options),
			_ => config
		};
	}

	private EntitySchemaDefaultValueConfig ResolveSystemValue(
		EntitySchemaDefaultValueConfig config,
		int dataValueType,
		string context,
		RemoteCommandOptions options) {
		string input = RequireInputValueSource(config, context, "SystemValue");
		Guid dataValueTypeUId = EntitySchemaDesignerSupport.GetDataValueTypeUIdForRuntimeType(dataValueType);
		IReadOnlyList<SystemValueLookupValueDto> systemValues = GetSystemValues(dataValueTypeUId, options);
		if (systemValues.Count == 0) {
			throw new EntitySchemaDesignerException(
				$"{context} has no system variables available for dataValueType '{dataValueType}'.");
		}

		Guid resolvedGuid;
		if (Guid.TryParse(input, out Guid explicitGuid)) {
			resolvedGuid = RequireSingleMatch(
				systemValues.Where(item => item.Value == explicitGuid),
				context,
				"SystemValue GUID",
				input,
				candidate => $"{candidate.Value:D} ({candidate.DisplayValue})",
				disambiguation: "Use an exact GUID returned by get-system-values.").Value;
		} else if (TryResolveSystemAlias(input, systemValues, out Guid aliasGuid)) {
			resolvedGuid = aliasGuid;
		} else {
			resolvedGuid = RequireSingleMatch(
				systemValues.Where(item => IsCaptionMatch(input, item.DisplayValue)),
				context,
				"SystemValue caption",
				input,
				candidate => $"{candidate.Value:D} ({candidate.DisplayValue})",
				disambiguation: "Use a GUID or enum alias such as CurrentDateTime.").Value;
		}

		string canonical = resolvedGuid.ToString("D", CultureInfo.InvariantCulture);
		return new EntitySchemaDefaultValueConfig {
			Source = config.Source,
			ValueSource = canonical,
			ResolvedValueSource = canonical
		};
	}

	private EntitySchemaDefaultValueConfig ResolveSettings(
		EntitySchemaDefaultValueConfig config,
		int dataValueType,
		string context,
		RemoteCommandOptions options) {
		string input = RequireInputValueSource(config, context, "Settings");
		IReadOnlyList<SysSettingsSelectQueryRowDto> settings = GetSysSettings(dataValueType, options);
		if (settings.Count == 0) {
			throw new EntitySchemaDesignerException(
				$"{context} has no system settings available for dataValueType '{dataValueType}'.");
		}

		SysSettingsSelectQueryRowDto resolved;
		if (Guid.TryParse(input, out Guid inputId)) {
			resolved = RequireSingleMatch(
				settings.Where(item => item.Id == inputId),
				context,
				"setting id",
				input,
				candidate => $"{candidate.Code} ({candidate.Name}, {candidate.Id:D})",
				disambiguation: "Use an exact setting id or setting code.");
		} else {
			List<SysSettingsSelectQueryRowDto> codeMatches = settings
				.Where(item => string.Equals(item.Code, input, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (codeMatches.Count == 1) {
				resolved = codeMatches[0];
			} else if (codeMatches.Count > 1) {
				string candidates = string.Join(", ", codeMatches.Select(candidate =>
					$"{candidate.Code} ({candidate.Name}, {candidate.Id:D})"));
				throw new EntitySchemaDesignerException(
					$"{context} matched multiple setting code values for '{input}': {candidates}. " +
					"Use an exact setting id.");
			} else {
				resolved = RequireSingleMatch(
					settings.Where(item => string.Equals(item.Name, input, StringComparison.OrdinalIgnoreCase)),
					context,
					"setting name",
					input,
					candidate => $"{candidate.Code} ({candidate.Name}, {candidate.Id:D})",
					disambiguation: "Use a unique setting code or exact setting id.");
			}
		}

		return new EntitySchemaDefaultValueConfig {
			Source = config.Source,
			ValueSource = resolved.Code,
			ResolvedValueSource = resolved.Code
		};
	}

	private static string RequireInputValueSource(EntitySchemaDefaultValueConfig config, string context, string source) {
		string? input = config.ValueSource?.Trim();
		if (string.IsNullOrWhiteSpace(input)) {
			throw new EntitySchemaDesignerException(
				$"{context} requires default-value-config.value-source when source is {source}.");
		}
		return input;
	}

	private IReadOnlyList<SystemValueLookupValueDto> GetSystemValues(Guid dataValueTypeUId, RemoteCommandOptions options) {
		if (_systemValuesCache.TryGetValue(dataValueTypeUId, out IReadOnlyList<SystemValueLookupValueDto>? cachedValues)) {
			return cachedValues;
		}
		IReadOnlyList<SystemValueLookupValueDto> fetchedValues = _designerClient.GetSystemValues(dataValueTypeUId, options);
		_systemValuesCache[dataValueTypeUId] = fetchedValues;
		return fetchedValues;
	}

	private IReadOnlyList<SysSettingsSelectQueryRowDto> GetSysSettings(int dataValueType, RemoteCommandOptions options) {
		List<SysSettingsSelectQueryRowDto> result = [];
		foreach (string valueTypeName in GetSettingsValueTypeCandidates(dataValueType)) {
			if (!_settingsCache.TryGetValue(valueTypeName, out IReadOnlyList<SysSettingsSelectQueryRowDto>? cachedRows)) {
				cachedRows = _designerClient.GetSysSettingsByValueTypeName(valueTypeName, options);
				_settingsCache[valueTypeName] = cachedRows;
			}
			result.AddRange(cachedRows);
		}
		return result
			.GroupBy(item => item.Id)
			.Select(group => group.First())
			.ToList();
	}

	private static IEnumerable<string> GetSettingsValueTypeCandidates(int dataValueType) {
		return dataValueType switch {
			0 => ["Guid"],
			1 => ["Text"],
			4 => ["Integer"],
			5 => [DecimalTypeName],
			6 => [CurrencyTypeName],
			7 => ["DateTime"],
			10 => ["Lookup"],
			12 => ["Boolean"],
			24 => ["SecureText"],
			27 => ["ShortText", "Text"],
			28 => ["MediumText", "Text"],
			29 => ["MaxSizeText", "Text"],
			30 => ["LongText", "Text"],
			31 => [DecimalTypeName],
			32 => [DecimalTypeName],
			33 => [DecimalTypeName],
			34 => [DecimalTypeName],
			40 => [DecimalTypeName],
			42 => ["Text"],
			43 => ["Text"],
			44 => ["Text"],
			45 => ["Text"],
			47 => [DecimalTypeName],
			48 => [CurrencyTypeName],
			49 => [CurrencyTypeName],
			50 => [CurrencyTypeName],
			_ => throw new EntitySchemaDesignerException(
				$"Unsupported dataValueType '{dataValueType}' for default-value-config source Settings.")
		};
	}

	private static bool TryResolveSystemAlias(
		string input,
		IReadOnlyCollection<SystemValueLookupValueDto> systemValues,
		out Guid resolvedGuid) {
		resolvedGuid = Guid.Empty;
		if (!SystemValueAliasMap.TryGetValue(input, out Guid aliasGuid)) {
			return false;
		}
		SystemValueLookupValueDto? matched = TryResolveSingle(systemValues.Where(item => item.Value == aliasGuid));
		if (matched == null) {
			throw new EntitySchemaDesignerException(
				$"System value alias '{input}' is not available for the selected column type.");
		}
		resolvedGuid = matched.Value;
		return true;
	}

	private static bool IsCaptionMatch(string input, string caption) {
		if (string.Equals(input, caption, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		string normalizedInput = NormalizeLookupToken(input);
		string normalizedCaption = NormalizeLookupToken(caption);
		if (normalizedInput == normalizedCaption) {
			return true;
		}
		// Handle common date-time caption variant.
		return normalizedInput == normalizedCaption.Replace("and", string.Empty);
	}

	private static string NormalizeLookupToken(string value) {
		return new string(value
			.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant)
			.ToArray());
	}

	private static T RequireSingleMatch<T>(
		IEnumerable<T> matches,
		string context,
		string targetKind,
		string targetValue,
		Func<T, string> formatter,
		string disambiguation) {
		List<T> materializedMatches = matches.ToList();
		if (materializedMatches.Count == 1) {
			return materializedMatches[0];
		}
		if (materializedMatches.Count == 0) {
			throw new EntitySchemaDesignerException(
				$"{context} could not resolve {targetKind} '{targetValue}'. {disambiguation}");
		}
		string candidates = string.Join(", ", materializedMatches.Select(formatter));
		throw new EntitySchemaDesignerException(
			$"{context} matched multiple {targetKind} values for '{targetValue}': {candidates}. {disambiguation}");
	}

	private static T? TryResolveSingle<T>(IEnumerable<T> matches)
		where T : class {
		List<T> materializedMatches = matches.ToList();
		return materializedMatches.Count == 1 ? materializedMatches[0] : null;
	}
}
