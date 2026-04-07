using System;
using System.Collections.Generic;
using Clio.Common.Responses;
using Newtonsoft.Json;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

internal sealed class CreateEntitySchemaRequestDto
{
	[JsonProperty("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonProperty("extendParent")]
	public bool ExtendParent { get; set; }
}

internal sealed class GetAvailableSchemasRequestDto
{
	[JsonProperty("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonProperty("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }

	[JsonProperty("allowVirtual")]
	public bool AllowVirtual { get; set; }
}

internal sealed class AssignParentSchemaRequestDto<TSchemaDto>
{
	[JsonProperty("designSchema")]
	public TSchemaDto DesignSchema { get; set; }

	[JsonProperty("parentSchemaUId")]
	public Guid ParentSchemaUId { get; set; }

	[JsonProperty("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }
}

internal sealed class DesignerResponse<TSchema> : BaseResponse
{
	[JsonProperty("schema")]
	public TSchema Schema { get; set; }
}

internal sealed class AvailableEntitySchemasResponse : BaseResponse
{
	[JsonProperty("items")]
	public ManagerItemDto[] Items { get; set; }
}

internal sealed class SaveDesignItemDesignerResponse : BaseResponse
{
	[JsonProperty("schemaUid")]
	public Guid SchemaUId { get; set; }
}

internal sealed class SchemaDesignerRequestDto
{
	[JsonProperty("saveSchemaDBStructure")]
	public List<Guid> SaveSchemaDbStructure { get; set; } = [];
}

internal sealed class RuntimeEntitySchemaRequestDto
{
	[JsonProperty("uId")]
	public Guid UId { get; set; }
}

internal sealed class RuntimeEntitySchemaResponse : BaseResponse
{
	[JsonProperty("schema")]
	public RuntimeEntitySchemaDto Schema { get; set; }
}

internal sealed class SystemValuesResponse : BaseResponse
{
	[JsonProperty("items")]
	public SystemValueLookupValueDto[] Items { get; set; } = [];
}

internal sealed class SystemValueLookupValueDto
{
	[JsonProperty("value")]
	public Guid Value { get; set; }

	[JsonProperty("displayValue")]
	public string DisplayValue { get; set; } = string.Empty;
}

internal sealed class SysSettingsSelectQueryResponse : BaseResponse
{
	[JsonProperty("rows")]
	public SysSettingsSelectQueryRowDto[] Rows { get; set; } = [];
}

internal sealed class SysSettingsSelectQueryRowDto
{
	[JsonProperty("Id")]
	public Guid Id { get; set; }

	[JsonProperty("Code")]
	public string Code { get; set; } = string.Empty;

	[JsonProperty("Name")]
	public string Name { get; set; } = string.Empty;

	[JsonProperty("ValueTypeName")]
	public string ValueTypeName { get; set; } = string.Empty;
}

internal sealed class RuntimeEntitySchemaDto
{
	[JsonProperty("uId")]
	public Guid UId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

internal sealed class ManagerItemDto
{
	[JsonProperty("uId")]
	public Guid UId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("caption")]
	public string Caption { get; set; }
}

internal sealed class WorkspacePackageDto
{
	[JsonProperty("uId")]
	public Guid UId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

internal sealed class LocalizableStringDto
{
	[JsonProperty("cultureName")]
	public string CultureName { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }
}

internal sealed class EntitySchemaColumnDefValueDto
{
	[JsonProperty("valueSourceType")]
	public EntitySchemaColumnDefSource ValueSourceType { get; set; }

	[JsonProperty("value")]
	public object Value { get; set; }

	[JsonProperty("valueSource")]
	public string ValueSource { get; set; }

	[JsonProperty("sequencePrefix")]
	public string SequencePrefix { get; set; }

	[JsonProperty("sequenceNumberOfChars")]
	public int SequenceNumberOfChars { get; set; }
}

internal sealed class EntityDesignSchemaDto
{
	[JsonProperty("id")]
	public Guid Id { get; set; }

	[JsonProperty("uId")]
	public Guid UId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("package")]
	public WorkspacePackageDto Package { get; set; }

	[JsonProperty("caption")]
	public List<LocalizableStringDto> Caption { get; set; } = [];

	[JsonProperty("description")]
	public List<LocalizableStringDto> Description { get; set; } = [];

	[JsonProperty("columns")]
	public IEnumerable<EntitySchemaColumnDto> Columns { get; set; } = [];

	[JsonProperty("inheritedColumns")]
	public IEnumerable<EntitySchemaColumnDto> InheritedColumns { get; set; } = [];

	[JsonProperty("indexes")]
	public IEnumerable<object> Indexes { get; set; } = [];

	[JsonProperty("primaryColumn")]
	public EntitySchemaColumnDto PrimaryColumn { get; set; }

	[JsonProperty("primaryDisplayColumn")]
	public EntitySchemaColumnDto PrimaryDisplayColumn { get; set; }

	[JsonProperty("primaryImageColumn")]
	public EntitySchemaColumnDto PrimaryImageColumn { get; set; }

	[JsonProperty("primaryColorColumn")]
	public EntitySchemaColumnDto PrimaryColorColumn { get; set; }

	[JsonProperty("primaryOrderColumn")]
	public EntitySchemaColumnDto PrimaryOrderColumn { get; set; }

	[JsonProperty("hierarchyColumn")]
	public EntitySchemaColumnDto HierarchyColumn { get; set; }

	[JsonProperty("ownerColumn")]
	public EntitySchemaColumnDto OwnerColumn { get; set; }

	[JsonProperty("parentSchema")]
	public EntityDesignSchemaDto ParentSchema { get; set; }

	[JsonProperty("extendParent")]
	public bool ExtendParent { get; set; }

	[JsonProperty("isDBView")]
	public bool IsDBView { get; set; }

	[JsonProperty("isSSPAvailable")]
	public bool IsSSPAvailable { get; set; }

	[JsonProperty("isVirtual")]
	public bool IsVirtual { get; set; }

	[JsonProperty("useRecordDeactivation")]
	public bool UseRecordDeactivation { get; set; }

	[JsonProperty("showInAdvancedMode")]
	public bool ShowInAdvancedMode { get; set; }

	[JsonProperty("isTrackChangesInDB")]
	public bool IsTrackChangesInDB { get; set; }

	[JsonProperty("administratedByOperations")]
	public bool AdministratedByOperations { get; set; }

	[JsonProperty("administratedByColumns")]
	public bool AdministratedByColumns { get; set; }

	[JsonProperty("administratedByRecords")]
	public bool AdministratedByRecords { get; set; }

	[JsonProperty("useDenyRecordRights")]
	public bool UseDenyRecordRights { get; set; }

	[JsonProperty("useLiveEditing")]
	public bool UseLiveEditing { get; set; }

	[JsonProperty("trackChangesSchemaName")]
	public string TrackChangesSchemaName { get; set; }

	[JsonProperty("rightSchemaName")]
	public string RightSchemaName { get; set; }

	[JsonProperty("localizationSchemaName")]
	public string LocalizationSchemaName { get; set; }

	[JsonProperty("masterRecordColumn")]
	public EntitySchemaColumnDto MasterRecordColumn { get; set; }

	[JsonProperty("createdByColumn")]
	public EntitySchemaColumnDto CreatedByColumn { get; set; }

	[JsonProperty("createdOnColumn")]
	public EntitySchemaColumnDto CreatedOnColumn { get; set; }

	[JsonProperty("modifiedByColumn")]
	public EntitySchemaColumnDto ModifiedByColumn { get; set; }

	[JsonProperty("modifiedOnColumn")]
	public EntitySchemaColumnDto ModifiedOnColumn { get; set; }

	[JsonProperty("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }
}

internal sealed class EntitySchemaColumnDto
{
	[JsonProperty("uId")]
	public Guid UId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("caption")]
	public IEnumerable<LocalizableStringDto> Caption { get; set; } = [];

	[JsonProperty("description")]
	public IEnumerable<LocalizableStringDto> Description { get; set; } = [];

	[JsonProperty("type")]
	public int? DataValueType { get; set; }

	[JsonProperty("isInherited")]
	public bool IsInherited { get; set; }

	[JsonProperty("requirementType")]
	public int RequirementType { get; set; }

	[JsonProperty("usageType")]
	public int UsageType { get; set; }

	[JsonProperty("defValue")]
	public EntitySchemaColumnDefValueDto DefValue { get; set; }

	[JsonProperty("isValueCloneable")]
	public bool IsValueCloneable { get; set; }

	[JsonProperty("isTrackChangesInDB")]
	public bool IsTrackChangesInDB { get; set; }

	[JsonProperty("indexed")]
	public bool Indexed { get; set; }

	[JsonProperty("isMultiLineText")]
	public bool MultiLineText { get; set; }

	[JsonProperty("isAccentInsensitive")]
	public bool AccentInsensitive { get; set; }

	[JsonProperty("isLocalizableText")]
	public bool LocalizableText { get; set; }

	[JsonProperty("useSeconds")]
	public bool UseSeconds { get; set; }

	[JsonProperty("isMasked")]
	public bool Masked { get; set; }

	[JsonProperty("isValueMasked")]
	public bool ValueMasked { get; set; }

	[JsonProperty("isFormatValidated")]
	public bool FormatValidated { get; set; }

	[JsonProperty("referenceSchema")]
	public EntityDesignSchemaDto ReferenceSchema { get; set; }

	[JsonProperty("isSimpleLookup")]
	public bool List { get; set; }

	[JsonProperty("isCascade")]
	public bool CascadeConnection { get; set; }

	[JsonProperty("doNotControlIntegrity")]
	public bool DoNotControlIntegrity { get; set; }

	[JsonProperty("isSensitiveData")]
	public bool SensitiveData { get; set; }
}

internal sealed class GetSchemaDesignItemRequestDto
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonProperty("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }

	[JsonProperty("cultures")]
	public List<string> Cultures { get; set; } = [];
}
