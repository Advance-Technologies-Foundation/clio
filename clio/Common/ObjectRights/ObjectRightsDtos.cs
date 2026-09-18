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
