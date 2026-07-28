using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.RecordRights;

public record GetRecordRightsRequest
{
	[JsonPropertyName("tableName")]
	public string TableName { get; init; }

	[JsonPropertyName("recordId")]
	public string RecordId { get; init; }
}

public record GetRecordRightsResponse
{
	[JsonPropertyName("GetRecordRightsResult")]
	public IReadOnlyList<RecordRightRow> Rows { get; init; }
}

public record RecordRightRow
{
	[JsonPropertyName("Id")]
	public string Id { get; init; }

	[JsonPropertyName("Operation")]
	public int Operation { get; init; }

	[JsonPropertyName("Position")]
	public int Position { get; init; }

	[JsonPropertyName("RightLevel")]
	public int RightLevel { get; init; }

	[JsonPropertyName("SysAdminUnit")]
	public SysAdminUnitRef SysAdminUnit { get; init; }

	[JsonPropertyName("SysAdminUnitType")]
	public string SysAdminUnitType { get; init; }

	[JsonPropertyName("isDeleted")]
	public bool IsDeleted { get; init; }

	[JsonPropertyName("isNew")]
	public bool IsNew { get; init; }
}

public record SysAdminUnitRef
{
	[JsonPropertyName("value")]
	public string Value { get; init; }

	[JsonPropertyName("displayValue")]
	public string DisplayValue { get; init; }
}

public record ApplyChangesRecordRef
{
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	[JsonPropertyName("primaryColumnValue")]
	public string PrimaryColumnValue { get; init; }
}

public record ApplyChangesRequest
{
	[JsonPropertyName("record")]
	public ApplyChangesRecordRef Record { get; init; }

	[JsonPropertyName("recordRights")]
	public IReadOnlyList<RecordRightRow> RecordRights { get; init; }
}
