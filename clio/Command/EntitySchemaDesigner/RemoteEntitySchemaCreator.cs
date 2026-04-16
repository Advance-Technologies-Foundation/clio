using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaCreator{
	#region Methods: Public

	void Create(CreateEntitySchemaOptions options);

	#endregion
}

internal sealed class RemoteEntitySchemaCreator : IRemoteEntitySchemaCreator{
	#region Fields: Private

	private const string TitleLocalizationsArgumentName = "title-localizations";
	private const string SchemaNamePrefixSettingCode = "SchemaNamePrefix";
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IEntitySchemaDefaultValueSourceResolver _defaultValueSourceResolver;
	private readonly IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient;
	private readonly ILogger _logger;
	private readonly ISysSettingsManager _sysSettingsManager;

	#endregion

	#region Class: Nested

	private sealed record ParsedColumn(
		string Name,
		string Type,
		string Title,
		IReadOnlyDictionary<string, string>? TitleLocalizations,
		string? ReferenceSchemaName,
		bool? Required,
		string? DefaultValueSource,
		string? DefaultValue,
		EntitySchemaDefaultValueConfig? DefaultValueConfig,
		bool? Masked){
		public bool IsLookup => EntitySchemaDesignerSupport.IsLookupTypeName(Type);
	}

	private sealed record StructuredColumnSpec(
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName(TitleLocalizationsArgumentName)] Dictionary<string, string>? TitleLocalizations = null) {
		[property: JsonPropertyName("title")]
		public string? Title { get; init; }

		[property: JsonPropertyName("caption")]
		public string? Caption { get; init; }

		[property: JsonPropertyName("reference-schema-name")]
		public string? ReferenceSchemaName { get; init; }

		[property: JsonPropertyName("required")]
		public bool? Required { get; init; }

		[property: JsonPropertyName("default-value-source")]
		public string? DefaultValueSource { get; init; }

		[property: JsonPropertyName("default-value")]
		public string? DefaultValue { get; init; }

		[property: JsonPropertyName("default-value-config")]
		public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; init; }

		[property: JsonPropertyName("masked")]
		public bool? Masked { get; init; }
	}

	#endregion

	#region Constructors: Public

	public RemoteEntitySchemaCreator(
		IApplicationPackageListProvider applicationPackageListProvider,
		IEntitySchemaDefaultValueSourceResolver defaultValueSourceResolver,
		IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
		ILogger logger,
		ISysSettingsManager sysSettingsManager) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_defaultValueSourceResolver = defaultValueSourceResolver;
		_entitySchemaDesignerClient = entitySchemaDesignerClient;
		_logger = logger;
		_sysSettingsManager = sysSettingsManager;
	}

	#endregion

	#region Methods: Private

	private void ApplySchemaMetadata(
		EntityDesignSchemaDto schema,
		CreateEntitySchemaOptions options,
		IReadOnlyCollection<ParsedColumn> parsedColumns,
		PackageInfo package) {
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		TitleLocalizationNormalizationResult schemaTitleNormalization =
			EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
				options.TitleLocalizations,
				options.Title,
				TitleLocalizationsArgumentName);
		schema.Name = options.SchemaName;
		schema.Caption = EntitySchemaDesignerSupport.CreateLocalizableStrings(
			schemaTitleNormalization.Localizations,
			schemaTitleNormalization.EffectiveTitle);
		EntitySchemaDesignerSupport.EnsurePackageAssigned(schema, package);
		schema.Columns ??= [];
		schema.Indexes ??= [];
		schema.InheritedColumns ??= [];

		Dictionary<string, ManagerItemDto> referenceSchemas = parsedColumns.Any(c => c.IsLookup)
			? GetReferenceSchemas(package.Descriptor.UId, options)
			: new Dictionary<string, ManagerItemDto>(StringComparer.OrdinalIgnoreCase);
		List<EntitySchemaColumnDto> columns = schema.Columns.ToList();
		foreach (ParsedColumn parsedColumn in parsedColumns) {
			columns.Add(CreateColumn(parsedColumn, referenceSchemas, cultureName, options));
		}

		if (!schema.ParentSchema.HasValue() && columns.All(column => !column.IsGuidType())) {
			columns.Insert(0, CreateColumn(
				new ParsedColumn(ResolvePrimaryColumnName(), "guid", "Id", null, null, null, null, null, null, null),
				referenceSchemas,
				cultureName,
				options));
		}

		schema.Columns = columns;
		schema.PrimaryColumn = columns.FirstOrDefault(column => column.IsGuidType()) ?? schema.PrimaryColumn;
		schema.PrimaryDisplayColumn ??= columns.FirstOrDefault(column => column.IsTextType());
	}

	private EntityDesignSchemaDto AssignParentSchema(
		EntityDesignSchemaDto schema,
		string parentSchemaName,
		Guid packageUId,
		CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse parentResponse = _entitySchemaDesignerClient.GetAvailableParentSchemas(
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false
			},
			options);
		ManagerItemDto parent = parentResponse.Items?.FirstOrDefault(item =>
			string.Equals(item.Name, parentSchemaName, StringComparison.OrdinalIgnoreCase));
		if (parent == null) {
			throw new InvalidOperationException(
				$"Parent schema '{parentSchemaName}' is not available for package '{schema.Package?.Name ?? packageUId.ToString()}'.");
		}

		DesignerResponse<EntityDesignSchemaDto> response = _entitySchemaDesignerClient.AssignParentSchema(
			new AssignParentSchemaRequestDto<EntityDesignSchemaDto> {
				DesignSchema = schema,
				ParentSchemaUId = parent.UId,
				UseFullHierarchy = false
			},
			options);
		return response.Schema ?? throw new InvalidOperationException("AssignParentSchema returned no schema.");
	}

	private void ApplyDefaultValue(EntitySchemaColumnDto column, ParsedColumn parsedColumn, CreateEntitySchemaOptions options) {
		EntitySchemaDefaultValueConfig? defaultValueConfig = EntitySchemaDesignerSupport.ResolveDefaultValueConfig(
			parsedColumn.DefaultValueConfig,
			parsedColumn.DefaultValueSource,
			parsedColumn.DefaultValue,
			$"Column '{parsedColumn.Name}'");
		if (defaultValueConfig == null) {
			return;
		}
		EntitySchemaColumnDefSource defaultValueSource = EntitySchemaDesignerSupport.ParseDefaultValueSource(
			defaultValueConfig.Source)
			?? throw new InvalidOperationException(
				$"Column '{parsedColumn.Name}' requires default-value-config.source.");
		if (defaultValueSource == EntitySchemaColumnDefSource.None) {
			column.DefValue = null;
			return;
		}
		defaultValueConfig = _defaultValueSourceResolver.Resolve(
			defaultValueConfig,
			column.DataValueType ?? 0,
			$"Column '{parsedColumn.Name}'",
			options);
		column.DefValue = EntitySchemaDesignerSupport.CreateDefaultValueDto(defaultValueConfig,
			$"Column '{parsedColumn.Name}'");
	}

	private bool CheckUniqueSchemaName(string schemaName, Guid excludeUId, CreateEntitySchemaOptions options) {
		BoolResponse response = _entitySchemaDesignerClient.CheckUniqueSchemaName(
			EntitySchemaDesignerSupport.EntitySchemaManagerName,
			schemaName,
			excludeUId,
			options);
		return response.Value;
	}

	private EntitySchemaColumnDto CreateColumn(
		ParsedColumn parsedColumn,
		IReadOnlyDictionary<string, ManagerItemDto> referenceSchemas,
		string cultureName,
		CreateEntitySchemaOptions options) {
		if (!EntitySchemaDesignerSupport.TryResolveDataValueType(parsedColumn.Type, out int dataValueType)) {
			throw new InvalidOperationException(
				$"Column type '{parsedColumn.Type}' is not supported. Supported types: {GetSupportedTypesList()}.");
		}
		ValidateDefaultValue(parsedColumn, dataValueType, options);
		ValidateMaskedOption(parsedColumn, dataValueType);
		TitleLocalizationNormalizationResult titleNormalization =
			EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
				parsedColumn.TitleLocalizations,
				parsedColumn.Title,
				TitleLocalizationsArgumentName);

		EntitySchemaColumnDto column = new() {
			UId = Guid.NewGuid(),
			Name = parsedColumn.Name,
			DataValueType = dataValueType,
			Caption = EntitySchemaDesignerSupport.CreateLocalizableStrings(
				titleNormalization.Localizations,
				titleNormalization.EffectiveTitle),
			RequirementType = parsedColumn.Required == true
				? (int)EntitySchemaColumnRequirementType.ApplicationLevel
				: (int)EntitySchemaColumnRequirementType.None,
			Masked = parsedColumn.Masked ?? false,
			ValueMasked = parsedColumn.Masked ?? false
		};
		ApplyDefaultValue(column, parsedColumn, options);
		if (parsedColumn.IsLookup) {
			if (!referenceSchemas.TryGetValue(parsedColumn.ReferenceSchemaName!, out ManagerItemDto referenceSchema)) {
				throw new InvalidOperationException(
					$"Reference schema '{parsedColumn.ReferenceSchemaName}' was not found for lookup column '{parsedColumn.Name}'.");
			}

			column.ReferenceSchema = new EntityDesignSchemaDto {
				UId = referenceSchema.UId,
				Name = referenceSchema.Name,
				Caption = [
					new LocalizableStringDto {
						CultureName = cultureName,
						Value = referenceSchema.Caption
					}
				]
			};
		}

		return column;
	}

	private void ValidateDefaultValue(ParsedColumn parsedColumn, int dataValueType, CreateEntitySchemaOptions options) {
		if (UsesUnsupportedLegacyBinaryDefaultValue(parsedColumn, dataValueType)) {
			throw new InvalidOperationException(
				$"Column '{parsedColumn.Name}' of type '{EntitySchemaDesignerSupport.GetFriendlyTypeName(dataValueType)}' does not support default-value or default-value-source Const.");
		}
		EntitySchemaDefaultValueConfig? defaultValueConfig = EntitySchemaDesignerSupport.ResolveDefaultValueConfig(
			parsedColumn.DefaultValueConfig,
			parsedColumn.DefaultValueSource,
			parsedColumn.DefaultValue,
			$"Column '{parsedColumn.Name}'");
		if (defaultValueConfig != null) {
			defaultValueConfig = _defaultValueSourceResolver.Resolve(
				defaultValueConfig,
				dataValueType,
				$"Column '{parsedColumn.Name}'",
				options);
		}
		EntitySchemaDesignerSupport.ValidateDefaultValueConfig(defaultValueConfig, dataValueType,
			$"Column '{parsedColumn.Name}'");
	}

	private string ResolvePrimaryColumnName() {
		try {
			string schemaNamePrefix = NormalizeTextSysSettingValue(
				_sysSettingsManager.GetSysSettingValueByCode(SchemaNamePrefixSettingCode));
			return string.IsNullOrWhiteSpace(schemaNamePrefix)
				? "Id"
				: $"{schemaNamePrefix}Id";
		}
		catch {
			return "Id";
		}
	}

	private static string NormalizeTextSysSettingValue(string? value) {
		return value?.Trim().Trim('"') ?? string.Empty;
	}

	private static bool UsesUnsupportedLegacyBinaryDefaultValue(ParsedColumn parsedColumn, int dataValueType) {
		if (parsedColumn.DefaultValueConfig != null
			|| !EntitySchemaDesignerSupport.IsBinaryLikeDataValueType(dataValueType)) {
			return false;
		}
		EntitySchemaColumnDefSource? defaultValueSource = EntitySchemaDesignerSupport.ParseDefaultValueSource(parsedColumn.DefaultValueSource);
		return parsedColumn.DefaultValue != null || defaultValueSource == EntitySchemaColumnDefSource.Const;
	}

	private static void ValidateMaskedOption(ParsedColumn parsedColumn, int dataValueType) {
		if (!parsedColumn.Masked.HasValue) {
			return;
		}

		bool isTextLikeType = EntitySchemaDesignerSupport.IsTextLikeDataValueType(dataValueType);
		bool isSecureTextType = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["secureText"];
		if (!isTextLikeType && !isSecureTextType) {
			throw new InvalidOperationException(
				$"Column '{parsedColumn.Name}' can use masked only for Text or SecureText types.");
		}
	}

	private Dictionary<string, ManagerItemDto> GetReferenceSchemas(Guid packageUId, CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse response = _entitySchemaDesignerClient.GetAvailableReferenceSchemas(
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false,
				AllowVirtual = false
			},
			options);
		return response.Items?
					   .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
					   .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
			   ?? new Dictionary<string, ManagerItemDto>(StringComparer.OrdinalIgnoreCase);
	}

	private IEnumerable<ParsedColumn> ParseColumns(IEnumerable<string> columnSpecs) {
		if (columnSpecs == null) {
			yield break;
		}

		foreach (string columnSpec in columnSpecs.Where(spec => !string.IsNullOrWhiteSpace(spec))) {
			yield return ParseColumn(columnSpec);
		}
	}

	private static ParsedColumn ParseStructuredColumn(string columnSpec) {
		StructuredColumnSpec structuredColumn = JsonSerializer.Deserialize<StructuredColumnSpec>(
			columnSpec,
			new JsonSerializerOptions {
				PropertyNameCaseInsensitive = true
			}) ?? throw new InvalidOperationException("Structured column payload is empty.");

		string name = structuredColumn.Name?.Trim();
		string type = structuredColumn.Type?.Trim();
		ValidateSupportedColumnValues(columnSpec, name, type);
		IReadOnlyDictionary<string, string>? titleLocalizations = structuredColumn.TitleLocalizations == null
			? null
			: EntitySchemaDesignerSupport.NormalizeLocalizationMap(
				structuredColumn.TitleLocalizations,
				TitleLocalizationsArgumentName);
		string title = ResolveTitle(structuredColumn, name);
		string? referenceSchemaName = string.IsNullOrWhiteSpace(structuredColumn.ReferenceSchemaName)
			? null
			: structuredColumn.ReferenceSchemaName.Trim();
		ValidateLookupReferenceSchema(columnSpec, type, referenceSchemaName);
		_ = EntitySchemaDesignerSupport.ParseDefaultValueSource(structuredColumn.DefaultValueSource);

		return new ParsedColumn(
			name,
			type,
			title,
			titleLocalizations,
			referenceSchemaName,
			structuredColumn.Required,
			structuredColumn.DefaultValueSource,
			structuredColumn.DefaultValue,
			structuredColumn.DefaultValueConfig,
			structuredColumn.Masked);
	}

	private static string GetLookupReferenceSchemaName(string[] parts) {
		return parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3])
			? parts[3].Trim()
			: null;
	}

	private static string GetSupportedTypesList() {
		return EntitySchemaDesignerSupport.GetSupportedTypesList();
	}

	private static string GetTitle(string[] parts, string name) {
		return parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
			? parts[2].Trim()
			: name;
	}

	private static void ValidateColumnFormat(string columnSpec, string[] parts) {
		if (parts.Length is < 2 or > 4) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' has invalid format. Expected <name>:<type>[:<title>[:<refSchema>]].");
		}
	}

	private static void ValidateLookupReferenceSchema(string columnSpec, string type, string? referenceSchemaName) {
		bool isLookup = EntitySchemaDesignerSupport.IsLookupTypeName(type);
		if (isLookup && string.IsNullOrWhiteSpace(referenceSchemaName)) {
			throw new InvalidOperationException(
				$"Lookup column '{columnSpec}' must specify a reference schema name.");
		}

		if (!isLookup && !string.IsNullOrWhiteSpace(referenceSchemaName)) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' can specify a reference schema name only for lookup columns.");
		}
	}

	private static void ValidateSupportedColumnValues(string columnSpec, string name, string type) {
		if (string.IsNullOrWhiteSpace(name)
			|| !EntitySchemaDesignerSupport.TryResolveDataValueType(type, out _)) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' has unsupported values. Supported types: {GetSupportedTypesList()}.");
		}
	}

	private ParsedColumn ParseColumn(string columnSpec) {
		if (columnSpec.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
			return ParseStructuredColumn(columnSpec);
		}

		string[] parts = columnSpec.Split(':');
		ValidateColumnFormat(columnSpec, parts);

		string name = parts[0].Trim();
		string type = parts[1].Trim();
		ValidateSupportedColumnValues(columnSpec, name, type);

		string title = GetTitle(parts, name);
		string? referenceSchemaName = GetLookupReferenceSchemaName(parts);
		ValidateLookupReferenceSchema(columnSpec, type, referenceSchemaName);

		return new ParsedColumn(name, type, title, null, referenceSchemaName, null, null, null, null, null);
	}

	private static string ResolveTitle(StructuredColumnSpec column, string fallbackName) {
		if (column.TitleLocalizations?.Count > 0) {
			return EntitySchemaDesignerSupport.GetRequiredLocalizationValue(
				column.TitleLocalizations,
				TitleLocalizationsArgumentName);
		}
		if (!string.IsNullOrWhiteSpace(column.Title)) {
			return column.Title.Trim();
		}
		if (!string.IsNullOrWhiteSpace(column.Caption)) {
			return column.Caption.Trim();
		}
		return fallbackName;
	}

	private PackageInfo ResolvePackage(string packageName) {
		PackageInfo package = _applicationPackageListProvider
							  .GetPackages()
							  .FirstOrDefault(p =>
								  string.Equals(p.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		return package ?? throw new InvalidOperationException($"Package '{packageName}' was not found.");
	}

	#endregion

	#region Methods: Public

	public void Create(CreateEntitySchemaOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		List<ParsedColumn> parsedColumns = ParseColumns(options.Columns).ToList();
		DesignerResponse<EntityDesignSchemaDto> createResponse = _entitySchemaDesignerClient.CreateNewSchema(
			new CreateEntitySchemaRequestDto {
				PackageUId = package.Descriptor.UId,
				ExtendParent = options.ExtendParent
			},
			options);
		EntityDesignSchemaDto schema = createResponse.Schema ??
									   throw new InvalidOperationException("CreateNewSchema returned no schema.");
		EntitySchemaDesignerSupport.EnsurePackageAssigned(schema, package);
		if (!CheckUniqueSchemaName(options.SchemaName, schema.UId, options)) {
			throw new InvalidOperationException($"Schema '{options.SchemaName}' already exists.");
		}

		if (!string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			schema = AssignParentSchema(schema, options.ParentSchemaName, package.Descriptor.UId, options);
		}

		ApplySchemaMetadata(schema, options, parsedColumns, package);
		SaveDesignItemDesignerResponse saveResponse = _entitySchemaDesignerClient.SaveSchema(schema, options);
		Guid schemaUId = saveResponse.SchemaUId != Guid.Empty ? saveResponse.SchemaUId : schema.UId;
		if (schemaUId == Guid.Empty) {
			throw new InvalidOperationException(
				$"Schema '{options.SchemaName}' was saved but schema UId is unavailable.");
		}
		_entitySchemaDesignerClient.SaveSchemaDbStructure(schemaUId, options);
		RuntimeEntitySchemaResponse runtimeResponse = _entitySchemaDesignerClient.GetRuntimeEntitySchema(schemaUId,
			options);
		if (!runtimeResponse.Success || runtimeResponse.Schema == null) {
			throw new InvalidOperationException(
				$"Schema '{options.SchemaName}' was saved but is not available in runtime.");
		}
		EntityDesignSchemaDto reloadedSchema = _entitySchemaDesignerClient.GetSchemaDesignItem(
			new GetSchemaDesignItemRequestDto {
				Name = options.SchemaName,
				PackageUId = package.Descriptor.UId,
				UseFullHierarchy = false,
				Cultures = [EntitySchemaDesignerSupport.GetCurrentCultureName()]
			},
			options).Schema ?? throw new InvalidOperationException(
			$"Schema '{options.SchemaName}' could not be reloaded after save.");
		if (!string.Equals(reloadedSchema.Name, options.SchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Schema '{options.SchemaName}' was reloaded with unexpected name '{reloadedSchema.Name}'.");
		}
		_logger.WriteInfo($"Entity schema '{options.SchemaName}' created in package '{options.Package}'.");
	}

	#endregion
}
