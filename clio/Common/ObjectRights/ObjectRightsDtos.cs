using System;
using System.Text.Json.Serialization;

namespace Clio.Common.ObjectRights;

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
