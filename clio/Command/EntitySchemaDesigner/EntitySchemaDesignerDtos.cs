using System;
using System.Collections.Generic;
using Clio.Common.Responses;
using Newtonsoft.Json;

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

	[JsonProperty("parentSchema")]
	public EntityDesignSchemaDto ParentSchema { get; set; }

	[JsonProperty("extendParent")]
	public bool ExtendParent { get; set; }

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

	[JsonProperty("type")]
	public int? DataValueType { get; set; }

	[JsonProperty("referenceSchema")]
	public EntityDesignSchemaDto ReferenceSchema { get; set; }
}
