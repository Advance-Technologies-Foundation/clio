using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaColumnManager
{
	/// <summary>
	/// Returns a structured snapshot of schema properties for the requested remote entity schema.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, and remote environment.</param>
	/// <returns>Structured schema properties for MCP and CLI formatting.</returns>
	EntitySchemaPropertiesInfo GetSchemaProperties(GetEntitySchemaPropertiesOptions options);

	/// <summary>
	/// Returns a structured snapshot of column properties for the requested remote entity schema column.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, column, and remote environment.</param>
	/// <returns>Structured column properties for MCP and CLI formatting.</returns>
	EntitySchemaColumnPropertiesInfo GetColumnProperties(GetEntitySchemaColumnPropertiesOptions options);

	void ModifyColumn(ModifyEntitySchemaColumnOptions options);
	void PrintColumnProperties(GetEntitySchemaColumnPropertiesOptions options);
	void PrintSchemaProperties(GetEntitySchemaPropertiesOptions options);
}

internal sealed class RemoteEntitySchemaColumnManager : IRemoteEntitySchemaColumnManager
{
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient;
	private readonly ILogger _logger;

	public RemoteEntitySchemaColumnManager(IApplicationPackageListProvider applicationPackageListProvider,
		IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient, ILogger logger) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_entitySchemaDesignerClient = entitySchemaDesignerClient;
		_logger = logger;
	}

	public void ModifyColumn(ModifyEntitySchemaColumnOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, options);
		EntitySchemaColumnAction action = NormalizeAction(options.Action);
		switch (action) {
			case EntitySchemaColumnAction.Add:
				AddColumn(schema, package, options);
				break;
			case EntitySchemaColumnAction.Modify:
				ModifyColumn(schema, package, options);
				break;
			case EntitySchemaColumnAction.Remove:
				RemoveColumn(schema, options.ColumnName);
				break;
			default:
				throw new EntitySchemaDesignerException($"Unsupported action '{options.Action}'.");
		}

		SaveDesignItemDesignerResponse saveResponse = _entitySchemaDesignerClient.SaveSchema(schema, options);
		VerifyColumnMutation(schema.Name, package.Descriptor.UId, action, options);
		Guid schemaUId = saveResponse.SchemaUId != Guid.Empty ? saveResponse.SchemaUId : schema.UId;
		if (schemaUId == Guid.Empty) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' was saved but schema UId is unavailable.");
		}
		_entitySchemaDesignerClient.SaveSchemaDbStructure(schemaUId, options);
		RuntimeEntitySchemaResponse runtimeResponse = _entitySchemaDesignerClient.GetRuntimeEntitySchema(schemaUId,
			options);
		if (!runtimeResponse.Success || runtimeResponse.Schema == null) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' was saved but is not available in runtime.");
		}
		_logger.WriteInfo(
			$"Column '{options.ColumnName}' action '{options.Action}' completed for schema '{options.SchemaName}'.");
	}

	public EntitySchemaColumnPropertiesInfo GetColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, options);
		(EntitySchemaColumnDto column, string source) = FindColumnForRead(schema, options.ColumnName);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		return new EntitySchemaColumnPropertiesInfo(
			schema.Name,
			schema.Package?.Name ?? options.Package,
			column.Name,
			source,
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Caption, cultureName),
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Description, cultureName),
			EntitySchemaDesignerSupport.GetFriendlyTypeName(column.DataValueType),
			IsRequired(column.RequirementType),
			column.Indexed,
			column.IsValueCloneable,
			column.IsTrackChangesInDB,
			EntitySchemaDesignerSupport.GetFriendlyDefaultValueSource(column.DefValue),
			column.DefValue?.Value?.ToString(),
			column.ReferenceSchema?.Name,
			column.List,
			column.CascadeConnection,
			column.DoNotControlIntegrity,
			column.MultiLineText,
			column.LocalizableText,
			column.AccentInsensitive,
			column.Masked,
			column.FormatValidated,
			column.UseSeconds);
	}

	public void PrintColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		EntitySchemaColumnPropertiesInfo column = GetColumnProperties(options);
		WriteInfo("Entity schema column properties");
		WriteInfo($"Schema: {column.SchemaName}");
		WriteInfo($"Package: {column.PackageName}");
		WriteInfo($"Column: {column.ColumnName}");
		WriteInfo($"Source: {column.Source}");
		WriteInfo($"Title: {FormatText(column.Title)}");
		WriteInfo($"Description: {FormatText(column.Description)}");
		WriteInfo($"Type: {column.Type}");
		WriteInfo($"Required: {FormatBoolean(column.Required)}");
		WriteInfo($"Indexed: {FormatBoolean(column.Indexed)}");
		WriteInfo($"Cloneable: {FormatBoolean(column.Cloneable)}");
		WriteInfo($"Track changes: {FormatBoolean(column.TrackChanges)}");
		WriteInfo($"Default value source: {FormatText(column.DefaultValueSource)}");
		WriteInfo($"Default value: {FormatText(column.DefaultValue)}");
		WriteInfo($"Reference schema: {FormatText(column.ReferenceSchemaName)}");
		WriteInfo($"Simple lookup: {FormatBoolean(column.SimpleLookup)}");
		WriteInfo($"Cascade: {FormatBoolean(column.Cascade)}");
		WriteInfo($"Do not control integrity: {FormatBoolean(column.DoNotControlIntegrity)}");
		WriteInfo($"Multiline text: {FormatBoolean(column.MultilineText)}");
		WriteInfo($"Localizable text: {FormatBoolean(column.LocalizableText)}");
		WriteInfo($"Accent insensitive: {FormatBoolean(column.AccentInsensitive)}");
		WriteInfo($"Masked: {FormatBoolean(column.Masked)}");
		WriteInfo($"Format validated: {FormatBoolean(column.FormatValidated)}");
		WriteInfo($"Use seconds: {FormatBoolean(column.UseSeconds)}");
	}

	public EntitySchemaPropertiesInfo GetSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, options);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		return new EntitySchemaPropertiesInfo(
			schema.Name,
			EntitySchemaDesignerSupport.GetLocalizableValue(schema.Caption, cultureName),
			EntitySchemaDesignerSupport.GetLocalizableValue(schema.Description, cultureName),
			schema.Package?.Name ?? package.Descriptor.Name,
			schema.ParentSchema?.Name,
			schema.ExtendParent,
			schema.PrimaryColumn?.Name,
			schema.PrimaryDisplayColumn?.Name,
			ownColumns.Count,
			inheritedColumns.Count,
			schema.Indexes?.Count() ?? 0,
			schema.IsTrackChangesInDB,
			schema.IsDBView,
			schema.IsSSPAvailable,
			schema.IsVirtual,
			schema.UseRecordDeactivation,
			schema.ShowInAdvancedMode,
			schema.AdministratedByOperations,
			schema.AdministratedByColumns,
			schema.AdministratedByRecords,
			schema.UseDenyRecordRights,
			schema.UseLiveEditing);
	}

	public void PrintSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		EntitySchemaPropertiesInfo schema = GetSchemaProperties(options);
		WriteInfo("Entity schema properties");
		WriteInfo($"Name: {schema.Name}");
		WriteInfo($"Title: {FormatText(schema.Title)}");
		WriteInfo($"Description: {FormatText(schema.Description)}");
		WriteInfo($"Package: {schema.PackageName}");
		WriteInfo($"Parent schema: {FormatText(schema.ParentSchemaName)}");
		WriteInfo($"Extend parent: {FormatBoolean(schema.ExtendParent)}");
		WriteInfo($"Primary column: {FormatText(schema.PrimaryColumnName)}");
		WriteInfo($"Primary display column: {FormatText(schema.PrimaryDisplayColumnName)}");
		WriteInfo($"Own columns: {schema.OwnColumnCount}");
		WriteInfo($"Inherited columns: {schema.InheritedColumnCount}");
		WriteInfo($"Indexes: {schema.IndexesCount}");
		WriteInfo($"Track changes in DB: {FormatBoolean(schema.TrackChangesInDb)}");
		WriteInfo($"DB view: {FormatBoolean(schema.DbView)}");
		WriteInfo($"SSP available: {FormatBoolean(schema.SspAvailable)}");
		WriteInfo($"Virtual: {FormatBoolean(schema.Virtual)}");
		WriteInfo($"Use record deactivation: {FormatBoolean(schema.UseRecordDeactivation)}");
		WriteInfo($"Show in advanced mode: {FormatBoolean(schema.ShowInAdvancedMode)}");
		WriteInfo($"Administrated by operations: {FormatBoolean(schema.AdministratedByOperations)}");
		WriteInfo($"Administrated by columns: {FormatBoolean(schema.AdministratedByColumns)}");
		WriteInfo($"Administrated by records: {FormatBoolean(schema.AdministratedByRecords)}");
		WriteInfo($"Use deny record rights: {FormatBoolean(schema.UseDenyRecordRights)}");
		WriteInfo($"Use live editing: {FormatBoolean(schema.UseLiveEditing)}");
	}

	private void AddColumn(EntityDesignSchemaDto schema, PackageInfo package, ModifyEntitySchemaColumnOptions options) {
		EnsureNameIsUnique(schema, options.ColumnName, null);
		int dataValueType = ParseSupportedType(options.Type, "add");
		ValidateOptionsForType(options, dataValueType, isAdd: true);
		EntitySchemaColumnDto column = new() {
			UId = Guid.NewGuid(),
			Name = options.ColumnName,
			DataValueType = dataValueType,
			Caption = [EntitySchemaDesignerSupport.CreateLocalizableString(options.Title ?? options.ColumnName)],
			Description = string.IsNullOrWhiteSpace(options.Description)
				? []
				: [EntitySchemaDesignerSupport.CreateLocalizableString(options.Description)],
			RequirementType = MapRequirementType(options.Required),
			Indexed = options.Indexed ?? false,
			IsValueCloneable = options.Cloneable ?? false,
			IsTrackChangesInDB = options.TrackChanges ?? false,
			MultiLineText = options.MultilineText ?? false,
			LocalizableText = options.LocalizableText ?? false,
			AccentInsensitive = options.AccentInsensitive ?? false,
			Masked = options.Masked ?? false,
			FormatValidated = options.FormatValidated ?? false,
			UseSeconds = options.UseSeconds ?? false,
			List = options.SimpleLookup ?? false,
			CascadeConnection = options.Cascade ?? false,
			DoNotControlIntegrity = options.DoNotControlIntegrity ?? false
		};
		ApplyDefaultValue(column, options, preserveWhenUnspecified: false);

		if (dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"]) {
			ManagerItemDto referenceSchema = ResolveReferenceSchema(package.Descriptor.UId, options.ReferenceSchemaName,
				options);
			column.ReferenceSchema = CreateReferenceSchema(referenceSchema);
		}

		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		ownColumns.Add(column);
		schema.Columns = ownColumns;
		if (schema.PrimaryDisplayColumn == null && column.IsTextType()) {
			schema.PrimaryDisplayColumn = column;
		}
	}

	private void ModifyColumn(EntityDesignSchemaDto schema, PackageInfo package, ModifyEntitySchemaColumnOptions options) {
		EntitySchemaColumnDto column = FindOwnColumnForMutation(schema, options.ColumnName);
		int effectiveDataValueType = options.Type == null
			? column.DataValueType ?? 0
			: ParseSupportedType(options.Type, "modify");
		ValidateOptionsForType(options, effectiveDataValueType, isAdd: false);
		if (!string.IsNullOrWhiteSpace(options.NewName)) {
			EnsureNameIsUnique(schema, options.NewName, column.UId);
			column.Name = options.NewName.Trim();
		}

		if (options.Type != null) {
			column.DataValueType = effectiveDataValueType;
		}

		List<LocalizableStringDto> caption = column.Caption?.ToList() ?? [];
		List<LocalizableStringDto> description = column.Description?.ToList() ?? [];
		if (!string.IsNullOrWhiteSpace(options.Title)) {
			EntitySchemaDesignerSupport.SetLocalizableValue(caption, options.Title);
		}
		if (!string.IsNullOrWhiteSpace(options.Description)) {
			EntitySchemaDesignerSupport.SetLocalizableValue(description, options.Description);
		}
		column.Caption = caption;
		column.Description = description;
		if (options.Required.HasValue) {
			column.RequirementType = MapRequirementType(options.Required);
		}
		if (options.Indexed.HasValue) {
			column.Indexed = options.Indexed.Value;
		}
		if (options.Cloneable.HasValue) {
			column.IsValueCloneable = options.Cloneable.Value;
		}
		if (options.TrackChanges.HasValue) {
			column.IsTrackChangesInDB = options.TrackChanges.Value;
		}
		ApplyDefaultValue(column, options, preserveWhenUnspecified: true);
		if (options.MultilineText.HasValue) {
			column.MultiLineText = options.MultilineText.Value;
		}
		if (options.LocalizableText.HasValue) {
			column.LocalizableText = options.LocalizableText.Value;
		}
		if (options.AccentInsensitive.HasValue) {
			column.AccentInsensitive = options.AccentInsensitive.Value;
		}
		if (options.Masked.HasValue) {
			column.Masked = options.Masked.Value;
		}
		if (options.FormatValidated.HasValue) {
			column.FormatValidated = options.FormatValidated.Value;
		}
		if (options.UseSeconds.HasValue) {
			column.UseSeconds = options.UseSeconds.Value;
		}

		bool isLookupType = effectiveDataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"];
		if (isLookupType) {
			if (!string.IsNullOrWhiteSpace(options.ReferenceSchemaName)) {
				ManagerItemDto referenceSchema = ResolveReferenceSchema(package.Descriptor.UId, options.ReferenceSchemaName,
					options);
				column.ReferenceSchema = CreateReferenceSchema(referenceSchema);
			} else if (column.ReferenceSchema == null) {
				throw new EntitySchemaDesignerException(
					$"Lookup column '{options.ColumnName}' must specify --reference-schema.");
			}
			if (options.SimpleLookup.HasValue) {
				column.List = options.SimpleLookup.Value;
			}
			if (options.Cascade.HasValue) {
				column.CascadeConnection = options.Cascade.Value;
			}
			if (options.DoNotControlIntegrity.HasValue) {
				column.DoNotControlIntegrity = options.DoNotControlIntegrity.Value;
			}
		} else {
			column.ReferenceSchema = null;
			column.List = false;
			column.CascadeConnection = false;
			column.DoNotControlIntegrity = false;
		}
	}

	private void RemoveColumn(EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto column = FindOwnColumnForMutation(schema, columnName);
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		ownColumns.RemoveAll(item => item.UId == column.UId);
		schema.Columns = ownColumns;
		List<EntitySchemaColumnDto> remainingColumns = GetAllColumns(schema);
		schema.PrimaryColumn = ReplaceReferencedColumn(schema.PrimaryColumn, column,
			remainingColumns.FirstOrDefault(item => item.IsGuidType()), requiredReferenceName: "Primary column");
		schema.PrimaryDisplayColumn = ReplaceReferencedColumn(schema.PrimaryDisplayColumn, column,
			remainingColumns.FirstOrDefault(item => item.IsTextType()));
		schema.PrimaryImageColumn = ReplaceReferencedColumn(schema.PrimaryImageColumn, column, null);
		schema.PrimaryColorColumn = ReplaceReferencedColumn(schema.PrimaryColorColumn, column, null);
		schema.PrimaryOrderColumn = ReplaceReferencedColumn(schema.PrimaryOrderColumn, column, null);
		schema.HierarchyColumn = ReplaceReferencedColumn(schema.HierarchyColumn, column, null);
		schema.OwnerColumn = ReplaceReferencedColumn(schema.OwnerColumn, column, null);
		schema.MasterRecordColumn = ReplaceReferencedColumn(schema.MasterRecordColumn, column, null);
		schema.CreatedByColumn = ReplaceReferencedColumn(schema.CreatedByColumn, column, null);
		schema.CreatedOnColumn = ReplaceReferencedColumn(schema.CreatedOnColumn, column, null);
		schema.ModifiedByColumn = ReplaceReferencedColumn(schema.ModifiedByColumn, column, null);
		schema.ModifiedOnColumn = ReplaceReferencedColumn(schema.ModifiedOnColumn, column, null);
	}

	private EntitySchemaColumnDto ReplaceReferencedColumn(EntitySchemaColumnDto currentReference,
		EntitySchemaColumnDto removedColumn, EntitySchemaColumnDto fallbackColumn, string requiredReferenceName = null) {
		if (currentReference?.UId != removedColumn.UId) {
			return currentReference;
		}
		if (!string.IsNullOrWhiteSpace(requiredReferenceName) && fallbackColumn == null) {
			throw new EntitySchemaDesignerException(
				$"Cannot remove column '{removedColumn.Name}' because it is the {requiredReferenceName.ToLowerInvariant()} and no valid fallback exists.");
		}
		return fallbackColumn;
	}

	private void ValidateOptionsForType(ModifyEntitySchemaColumnOptions options, int dataValueType, bool isAdd) {
		bool isLookup = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"];
		bool isText = EntitySchemaDesignerSupport.IsTextLikeDataValueType(dataValueType);
		bool isDateTime = EntitySchemaDesignerSupport.IsDateTimeLikeDataValueType(dataValueType);
		if (isLookup) {
			if (string.IsNullOrWhiteSpace(options.ReferenceSchemaName) && isAdd) {
				throw new EntitySchemaDesignerException("Lookup columns require --reference-schema.");
			}
		} else if (!string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.SimpleLookup.HasValue
			|| options.Cascade.HasValue
			|| options.DoNotControlIntegrity.HasValue) {
			throw new EntitySchemaDesignerException(
				"Lookup-specific options can be used only when the effective column type is Lookup.");
		}

		if (!isText && (options.MultilineText.HasValue
			|| options.LocalizableText.HasValue
			|| options.AccentInsensitive.HasValue
			|| options.Masked.HasValue
			|| options.FormatValidated.HasValue)) {
			throw new EntitySchemaDesignerException(
				"Text-specific options can be used only when the effective column type is Text.");
		}

		if (!isDateTime && options.UseSeconds.HasValue) {
			throw new EntitySchemaDesignerException(
				"--use-seconds can be used only when the effective column type is DateTime.");
		}

		EntitySchemaColumnDefSource? defaultValueSource =
			EntitySchemaDesignerSupport.ParseDefaultValueSource(options.DefaultValueSource);
		if (defaultValueSource == EntitySchemaColumnDefSource.None && options.DefaultValue != null) {
			throw new EntitySchemaDesignerException(
				"--default-value cannot be used when --default-value-source is None.");
		}

		if (defaultValueSource == EntitySchemaColumnDefSource.Const && options.DefaultValue == null) {
			throw new EntitySchemaDesignerException(
				"--default-value is required when --default-value-source is Const.");
		}
	}

	private EntitySchemaColumnDto FindOwnColumnForMutation(EntityDesignSchemaDto schema, string columnName) {
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		EntitySchemaColumnDto ownColumn = ownColumns.FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (ownColumn != null) {
			return ownColumn;
		}

		EntitySchemaColumnDto inheritedColumn = (schema.InheritedColumns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (inheritedColumn != null) {
			throw new EntitySchemaDesignerException(
				$"Column '{columnName}' is inherited and read-only in v1.");
		}

		throw new EntitySchemaDesignerException(
			$"Column '{columnName}' was not found in schema '{schema.Name}'.");
	}

	private (EntitySchemaColumnDto Column, string Source) FindColumnForRead(EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto ownColumn = (schema.Columns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (ownColumn != null) {
			return (ownColumn, "own");
		}

		EntitySchemaColumnDto inheritedColumn = (schema.InheritedColumns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (inheritedColumn != null) {
			return (inheritedColumn, "inherited");
		}

		throw new EntitySchemaDesignerException(
			$"Column '{columnName}' was not found in schema '{schema.Name}'.");
	}

	private void EnsureNameIsUnique(EntityDesignSchemaDto schema, string name, Guid? excludeUId) {
		bool hasDuplicate = GetAllColumns(schema)
			.Any(column => column.UId != excludeUId
				&& string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase));
		if (hasDuplicate) {
			throw new EntitySchemaDesignerException(
				$"Column '{name}' already exists in schema '{schema.Name}'.");
		}
	}

	private EntityDesignSchemaDto LoadSchema(string schemaName, Guid packageUId, RemoteCommandOptions options) {
		DesignerResponse<EntityDesignSchemaDto> response = _entitySchemaDesignerClient.GetSchemaDesignItem(
			new GetSchemaDesignItemRequestDto {
				Name = schemaName,
				PackageUId = packageUId,
				UseFullHierarchy = false,
				Cultures = [EntitySchemaDesignerSupport.GetCurrentCultureName()]
			},
			options);
		EntityDesignSchemaDto schema = response.Schema
			?? throw new EntitySchemaDesignerException(
				$"GetSchemaDesignItem returned no schema for '{schemaName}'.");
		schema.Columns = schema.Columns?.ToList() ?? [];
		schema.InheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		schema.Indexes = schema.Indexes?.ToList() ?? [];
		return schema;
	}

	private static int ParseSupportedType(string typeName, string actionName) {
		if (string.IsNullOrWhiteSpace(typeName)) {
			throw new EntitySchemaDesignerException($"--type is required for '{actionName}' action.");
		}
		if (!EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType)) {
			throw new EntitySchemaDesignerException(
				$"Unsupported type '{typeName}'. Supported types: {EntitySchemaDesignerSupport.GetSupportedTypesList()}.");
		}
		return dataValueType;
	}

	private ManagerItemDto ResolveReferenceSchema(Guid packageUId, string referenceSchemaName,
		RemoteCommandOptions options) {
		AvailableEntitySchemasResponse response = _entitySchemaDesignerClient.GetAvailableReferenceSchemas(
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false,
				AllowVirtual = false
			},
			options);
		ManagerItemDto referenceSchema = response.Items?.FirstOrDefault(item =>
			string.Equals(item.Name, referenceSchemaName, StringComparison.OrdinalIgnoreCase));
		return referenceSchema ?? throw new EntitySchemaDesignerException(
			$"Reference schema '{referenceSchemaName}' was not found.");
	}

	private static EntityDesignSchemaDto CreateReferenceSchema(ManagerItemDto referenceSchema) {
		return new EntityDesignSchemaDto {
			UId = referenceSchema.UId,
			Name = referenceSchema.Name,
			Caption = [EntitySchemaDesignerSupport.CreateLocalizableString(referenceSchema.Caption)]
		};
	}

	private PackageInfo ResolvePackage(string packageName) {
		PackageInfo package = _applicationPackageListProvider.GetPackages()
			.FirstOrDefault(item =>
				string.Equals(item.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		return package ?? throw new EntitySchemaDesignerException($"Package '{packageName}' was not found.");
	}

	private static List<EntitySchemaColumnDto> GetAllColumns(EntityDesignSchemaDto schema) {
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		return ownColumns.Concat(inheritedColumns).ToList();
	}

	private static EntitySchemaColumnAction NormalizeAction(string action) {
		if (!Enum.TryParse(action, true, out EntitySchemaColumnAction normalizedAction)) {
			throw new EntitySchemaDesignerException(
				$"Unsupported action '{action}'. Supported actions: add, modify, remove.");
		}
		return normalizedAction;
	}

	private static int MapRequirementType(bool? required) {
		return required == true
			? (int)EntitySchemaColumnRequirementType.ApplicationLevel
			: (int)EntitySchemaColumnRequirementType.None;
	}

	private static bool IsRequired(int requirementType) {
		return requirementType != (int)EntitySchemaColumnRequirementType.None;
	}

	private static void ApplyDefaultValue(
		EntitySchemaColumnDto column,
		ModifyEntitySchemaColumnOptions options,
		bool preserveWhenUnspecified) {
		EntitySchemaColumnDefSource? defaultValueSource =
			EntitySchemaDesignerSupport.ParseDefaultValueSource(options.DefaultValueSource);
		if (defaultValueSource == null && options.DefaultValue == null) {
			if (!preserveWhenUnspecified) {
				column.DefValue = null;
			}
			return;
		}

		if (defaultValueSource == EntitySchemaColumnDefSource.None) {
			column.DefValue = null;
			return;
		}

		column.DefValue = new EntitySchemaColumnDefValueDto {
			ValueSourceType = defaultValueSource ?? EntitySchemaColumnDefSource.Const,
			Value = options.DefaultValue
		};
	}

	private void VerifyColumnMutation(
		string schemaName,
		Guid packageUId,
		EntitySchemaColumnAction action,
		ModifyEntitySchemaColumnOptions options) {
		EntityDesignSchemaDto reloadedSchema = LoadSchema(schemaName, packageUId, options);
		string expectedColumnName = !string.IsNullOrWhiteSpace(options.NewName)
			? options.NewName.Trim()
			: options.ColumnName.Trim();
		bool ownColumnExists = (reloadedSchema.Columns?.ToList() ?? []).Any(column =>
			string.Equals(column.Name, expectedColumnName, StringComparison.OrdinalIgnoreCase));

		switch (action) {
			case EntitySchemaColumnAction.Add:
			case EntitySchemaColumnAction.Modify:
				if (!ownColumnExists) {
					throw new EntitySchemaDesignerException(
						$"Column '{expectedColumnName}' could not be reloaded after save.");
				}
				break;
			case EntitySchemaColumnAction.Remove:
				if ((reloadedSchema.Columns?.ToList() ?? []).Any(column =>
					string.Equals(column.Name, options.ColumnName, StringComparison.OrdinalIgnoreCase))) {
					throw new EntitySchemaDesignerException(
						$"Column '{options.ColumnName}' is still present after save.");
				}
				break;
		}
	}

	private static string FormatBoolean(bool value) {
		return value ? "true" : "false";
	}

	private static string FormatText(string? value) {
		return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
	}

	private void WriteInfo(string message) {
		_logger.WriteInfo(message);
	}
}

internal enum EntitySchemaColumnAction
{
	Add,
	Modify,
	Remove
}
