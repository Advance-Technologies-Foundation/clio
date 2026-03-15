using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaColumnManager
{
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
		switch (NormalizeAction(options.Action)) {
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
				throw new InvalidOperationException($"Unsupported action '{options.Action}'.");
		}

		_entitySchemaDesignerClient.SaveSchema(schema, options);
		_logger.WriteInfo(
			$"Column '{options.ColumnName}' action '{options.Action}' completed for schema '{options.SchemaName}'.");
	}

	public void PrintColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, options);
		(EntitySchemaColumnDto column, string source) = FindColumnForRead(schema, options.ColumnName);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		WriteInfo("Entity schema column properties");
		WriteInfo($"Schema: {schema.Name}");
		WriteInfo($"Package: {schema.Package?.Name ?? options.Package}");
		WriteInfo($"Column: {column.Name}");
		WriteInfo($"Source: {source}");
		WriteInfo($"Title: {EntitySchemaDesignerSupport.GetLocalizableValue(column.Caption, cultureName) ?? "<none>"}");
		WriteInfo(
			$"Description: {EntitySchemaDesignerSupport.GetLocalizableValue(column.Description, cultureName) ?? "<none>"}");
		WriteInfo($"Type: {GetFriendlyTypeName(column.DataValueType)}");
		WriteInfo($"Required: {FormatRequired(column.RequirementType)}");
		WriteInfo($"Indexed: {FormatBoolean(column.Indexed)}");
		WriteInfo($"Cloneable: {FormatBoolean(column.IsValueCloneable)}");
		WriteInfo($"Track changes: {FormatBoolean(column.IsTrackChangesInDB)}");
		WriteInfo($"Default value: {column.DefValue?.Value?.ToString() ?? "<none>"}");
		WriteInfo($"Reference schema: {column.ReferenceSchema?.Name ?? "<none>"}");
		WriteInfo($"Simple lookup: {FormatBoolean(column.List)}");
		WriteInfo($"Cascade: {FormatBoolean(column.CascadeConnection)}");
		WriteInfo($"Do not control integrity: {FormatBoolean(column.DoNotControlIntegrity)}");
		WriteInfo($"Multiline text: {FormatBoolean(column.MultiLineText)}");
		WriteInfo($"Localizable text: {FormatBoolean(column.LocalizableText)}");
		WriteInfo($"Accent insensitive: {FormatBoolean(column.AccentInsensitive)}");
		WriteInfo($"Masked: {FormatBoolean(column.Masked)}");
		WriteInfo($"Format validated: {FormatBoolean(column.FormatValidated)}");
		WriteInfo($"Use seconds: {FormatBoolean(column.UseSeconds)}");
	}

	public void PrintSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, options);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		WriteInfo("Entity schema properties");
		WriteInfo($"Name: {schema.Name}");
		WriteInfo($"Title: {EntitySchemaDesignerSupport.GetLocalizableValue(schema.Caption, cultureName) ?? "<none>"}");
		WriteInfo(
			$"Description: {EntitySchemaDesignerSupport.GetLocalizableValue(schema.Description, cultureName) ?? "<none>"}");
		WriteInfo($"Package: {schema.Package?.Name ?? package.Descriptor.Name}");
		WriteInfo($"Parent schema: {schema.ParentSchema?.Name ?? "<none>"}");
		WriteInfo($"Extend parent: {FormatBoolean(schema.ExtendParent)}");
		WriteInfo($"Primary column: {schema.PrimaryColumn?.Name ?? "<none>"}");
		WriteInfo($"Primary display column: {schema.PrimaryDisplayColumn?.Name ?? "<none>"}");
		WriteInfo($"Own columns: {ownColumns.Count}");
		WriteInfo($"Inherited columns: {inheritedColumns.Count}");
		WriteInfo($"Indexes: {schema.Indexes?.Count() ?? 0}");
		WriteInfo($"Track changes in DB: {FormatBoolean(schema.IsTrackChangesInDB)}");
		WriteInfo($"DB view: {FormatBoolean(schema.IsDBView)}");
		WriteInfo($"SSP available: {FormatBoolean(schema.IsSSPAvailable)}");
		WriteInfo($"Virtual: {FormatBoolean(schema.IsVirtual)}");
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
		if (options.DefaultValue != null) {
			column.DefValue = new EntitySchemaColumnDefValueDto {
				ValueSourceType = EntitySchemaColumnDefSource.Const,
				Value = options.DefaultValue
			};
		}

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
		if (options.DefaultValue != null) {
			column.DefValue = new EntitySchemaColumnDefValueDto {
				ValueSourceType = EntitySchemaColumnDefSource.Const,
				Value = options.DefaultValue
			};
		}
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
				throw new InvalidOperationException(
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
			throw new InvalidOperationException(
				$"Cannot remove column '{removedColumn.Name}' because it is the {requiredReferenceName.ToLowerInvariant()} and no valid fallback exists.");
		}
		return fallbackColumn;
	}

	private void ValidateOptionsForType(ModifyEntitySchemaColumnOptions options, int dataValueType, bool isAdd) {
		bool isLookup = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"];
		bool isText = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["text"];
		bool isDateTime = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["datetime"];
		if (isLookup) {
			if (string.IsNullOrWhiteSpace(options.ReferenceSchemaName) && isAdd) {
				throw new InvalidOperationException("Lookup columns require --reference-schema.");
			}
		} else if (!string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.SimpleLookup.HasValue
			|| options.Cascade.HasValue
			|| options.DoNotControlIntegrity.HasValue) {
			throw new InvalidOperationException(
				"Lookup-specific options can be used only when the effective column type is Lookup.");
		}

		if (!isText && (options.MultilineText.HasValue
			|| options.LocalizableText.HasValue
			|| options.AccentInsensitive.HasValue
			|| options.Masked.HasValue
			|| options.FormatValidated.HasValue)) {
			throw new InvalidOperationException(
				"Text-specific options can be used only when the effective column type is Text.");
		}

		if (!isDateTime && options.UseSeconds.HasValue) {
			throw new InvalidOperationException(
				"--use-seconds can be used only when the effective column type is DateTime.");
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
			throw new InvalidOperationException(
				$"Column '{columnName}' is inherited and read-only in v1.");
		}

		throw new InvalidOperationException(
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

		throw new InvalidOperationException(
			$"Column '{columnName}' was not found in schema '{schema.Name}'.");
	}

	private void EnsureNameIsUnique(EntityDesignSchemaDto schema, string name, Guid? excludeUId) {
		bool hasDuplicate = GetAllColumns(schema)
			.Any(column => column.UId != excludeUId
				&& string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase));
		if (hasDuplicate) {
			throw new InvalidOperationException(
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
			?? throw new InvalidOperationException(
				$"GetSchemaDesignItem returned no schema for '{schemaName}'.");
		schema.Columns = schema.Columns?.ToList() ?? [];
		schema.InheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		schema.Indexes = schema.Indexes?.ToList() ?? [];
		return schema;
	}

	private static int ParseSupportedType(string typeName, string actionName) {
		if (string.IsNullOrWhiteSpace(typeName)) {
			throw new InvalidOperationException($"--type is required for '{actionName}' action.");
		}
		if (!EntitySchemaDesignerSupport.SupportedDataValueTypes.TryGetValue(typeName.Trim(), out int dataValueType)) {
			throw new InvalidOperationException(
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
		return referenceSchema ?? throw new InvalidOperationException(
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
		return package ?? throw new InvalidOperationException($"Package '{packageName}' was not found.");
	}

	private static List<EntitySchemaColumnDto> GetAllColumns(EntityDesignSchemaDto schema) {
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		return ownColumns.Concat(inheritedColumns).ToList();
	}

	private static EntitySchemaColumnAction NormalizeAction(string action) {
		if (!Enum.TryParse(action, true, out EntitySchemaColumnAction normalizedAction)) {
			throw new InvalidOperationException(
				$"Unsupported action '{action}'. Supported actions: add, modify, remove.");
		}
		return normalizedAction;
	}

	private static int MapRequirementType(bool? required) {
		return required == true
			? (int)EntitySchemaColumnRequirementType.ApplicationLevel
			: (int)EntitySchemaColumnRequirementType.None;
	}

	private static string GetFriendlyTypeName(int? dataValueType) {
		if (dataValueType == null) {
			return "<none>";
		}

		return EntitySchemaDesignerSupport.SupportedDataValueTypes
			.FirstOrDefault(pair => pair.Value == dataValueType.Value).Key switch {
				"guid" => "Guid",
				"text" => "Text",
				"integer" => "Integer",
				"datetime" => "DateTime",
				"lookup" => "Lookup",
				"boolean" => "Boolean",
				_ => dataValueType.Value.ToString()
			};
	}

	private static string FormatRequired(int requirementType) {
		return requirementType == (int)EntitySchemaColumnRequirementType.None ? "false" : "true";
	}

	private static string FormatBoolean(bool value) {
		return value ? "true" : "false";
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
