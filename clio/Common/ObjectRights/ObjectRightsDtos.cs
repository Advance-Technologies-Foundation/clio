using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.ObjectRights;

/// <summary>
/// Request body for the native Creatio <c>SectionService/SetConnectedEntitiesAdministratedByEntity</c>
/// service. Carries only the root object name; the server resolves the connected (lookup) entities from
/// the root entity schema and turns on their permissions itself.
/// </summary>
public record SetConnectedEntitiesAdministratedRequest
{
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }
}

/// <summary>
/// Response envelope for <c>SectionService/SetConnectedEntitiesAdministratedByEntity</c>. The live
/// response shape is not verified (a live rights write cannot be performed from a unit test), so it is
/// parsed tolerantly: any valid JSON body deserializes into this empty record and counts as success.
/// </summary>
public record SetConnectedEntitiesAdministratedResponse
{
}

/// <summary>
/// Request body for the native Creatio <c>RightManagementService.svc/GetAdministratedObject</c> service.
/// Identifies the target object by its entity schema UId (the service accepts only <c>schemaUId</c>; a
/// schema name or record id is rejected with HTTP 400).
/// </summary>
public record GetAdministratedObjectRequest
{
	[JsonPropertyName("schemaUId")]
	public Guid SchemaUId { get; init; }
}

/// <summary>
/// Response envelope for <c>RightManagementService.svc/GetAdministratedObject</c>.
/// </summary>
public record GetAdministratedObjectResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("administratedObject")]
	public AdministratedObjectData AdministratedObject { get; init; }
}

/// <summary>
/// The administered-object detail: the three administration flags plus the per-role operation rights.
/// Only the fields this tool needs are mapped; record- and column-rights collections are intentionally omitted.
/// </summary>
public record AdministratedObjectData
{
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	[JsonPropertyName("administratedByOperations")]
	public bool AdministratedByOperations { get; init; }

	[JsonPropertyName("entitySchemaOperationsRights")]
	public List<AdministratedObjectOperationRight> EntitySchemaOperationsRights { get; init; }
}

/// <summary>
/// One role's object operation rights (a row in the object-permissions grid).
/// </summary>
public record AdministratedObjectOperationRight
{
	[JsonPropertyName("canRead")]
	public bool CanRead { get; init; }

	[JsonPropertyName("canAppend")]
	public bool CanAppend { get; init; }

	[JsonPropertyName("canEdit")]
	public bool CanEdit { get; init; }

	[JsonPropertyName("canDelete")]
	public bool CanDelete { get; init; }

	[JsonPropertyName("sysAdminUnit")]
	public AdministratedObjectGrantee SysAdminUnit { get; init; }
}

/// <summary>
/// The role (user or organizational/functional role) an operation-rights row is granted to.
/// </summary>
public record AdministratedObjectGrantee
{
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; init; }
}
