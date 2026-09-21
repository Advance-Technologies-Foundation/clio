using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Package;

namespace Clio.Common.ObjectRights;

/// <summary>An object operation (the object-permissions grid columns).</summary>
public enum ObjectOperation {
	Read,
	Create,
	Edit,
	Delete
}

/// <summary>One role's (user or organizational/functional role) object operation rights.</summary>
public sealed record RoleOperationRights(
	Guid GranteeId,
	string GranteeName,
	bool CanRead,
	bool CanCreate,
	bool CanEdit,
	bool CanDelete);

/// <summary>
/// The result of reading one object's operation-permissions state: whether it was found, whether it is
/// administered by operation permissions at all, and (when it is) every role's rights on it.
/// </summary>
public sealed record ObjectRightsInfo(
	bool Found,
	string Name,
	string Caption,
	bool AdministratedByOperations,
	IReadOnlyList<RoleOperationRights> Roles,
	string ReadError = null);

/// <summary>The result of a set-object-rights write for one object.</summary>
public sealed record ObjectRightsChange(
	bool Found,
	bool Changed,
	string Error = null);

/// <summary>
/// Reads object operation permissions (the SysSchemaOperationRight layer) for an entity, using the native
/// Creatio <c>RightManagementService.svc/GetAdministratedObject</c> service — the same service the System
/// Designer "Object permissions" section uses. Reports EVERY role's rights (not just one audience).
/// </summary>
public interface IObjectRightsReader {
	/// <summary>Reads the per-role object operation rights for the entity schema <paramref name="schemaName"/>.</summary>
	ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Grants or revokes object operation permissions for one role on an entity, via a read-modify-write against
/// <c>RightManagementService.svc/GetAdministratedObject</c> + <c>SaveAdministratedObject</c>. This is the
/// object-level analog of set-record-rights; it works for ANY role, not only the portal audience.
/// </summary>
public interface IObjectRightsWriter {
	/// <summary>
	/// Grants (or, when <paramref name="revoke"/> is true, revokes) the given <paramref name="operations"/> for
	/// role <paramref name="grantee"/> on <paramref name="schemaName"/>. Turns on operation permissions for the
	/// object when granting to an object that does not yet use them. Returns whether a change was written.
	/// </summary>
	ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Client for the native Creatio <c>RightManagementService</c>. Resolves an entity schema name to its UId via
/// DataService (the clio name→UId convention) and reads/writes the object's per-role operation rights.
/// </summary>
public class RightManagementServiceClient : CreatioServiceClient, IObjectRightsReader, IObjectRightsWriter {

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	public RightManagementServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	public ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions) {
		JsonObject node;
		string error;
		bool anyCleanAnswer;
		try {
			(node, anyCleanAnswer, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) {
			return new ObjectRightsInfo(true, schemaName, null, false, Array.Empty<RoleOperationRights>(),
				ReadError: ex.Message);
		}
		if (node is null) {
			// No candidate described an administered object: not found at all, a read failure, or simply not
			// administered by operation permissions (available to all).
			if (error is not null && !anyCleanAnswer) {
				return new ObjectRightsInfo(true, schemaName, null, false, Array.Empty<RoleOperationRights>(),
					ReadError: error);
			}
			return anyCleanAnswer
				? new ObjectRightsInfo(true, schemaName, null, false, Array.Empty<RoleOperationRights>())
				: new ObjectRightsInfo(false, schemaName, null, false, Array.Empty<RoleOperationRights>());
		}

		List<RoleOperationRights> roles = ReadOperationRows(node)
			.Select(row => new RoleOperationRights(
				GranteeId(row), (row["sysAdminUnit"]?["name"]?.GetValue<string>()) ?? "(unknown)",
				Flag(row, "canRead"), Flag(row, "canAppend"), Flag(row, "canEdit"), Flag(row, "canDelete")))
			.ToList();
		return new ObjectRightsInfo(true, node["name"]?.GetValue<string>() ?? schemaName,
			node["caption"]?.GetValue<string>(), Flag(node, "administratedByOperations"), roles);
	}

	public ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, CreatioRequestOptions requestOptions) {
		JsonObject node;
		bool anyCleanAnswer;
		string error;
		try {
			(node, anyCleanAnswer, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) {
			return new ObjectRightsChange(true, false, ex.Message);
		}
		if (node is null) {
			// No usable object: schema not found (0 candidates, error null) or every candidate faulted (error set).
			return new ObjectRightsChange(anyCleanAnswer, false, error);
		}

		bool changed = MutateOperationRow(node, grantee, operations, revoke);
		if (!changed) {
			return new ObjectRightsChange(true, false);
		}
		return Save(node, requestOptions);
	}

	// Applies the grant/revoke to the object node's operation-rights rows in place; returns whether anything
	// actually changed. All other fields of the node are left untouched so the save round-trips faithfully.
	private static bool MutateOperationRow(JsonObject node, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke) {
		JsonArray rows = node["entitySchemaOperationsRights"] as JsonArray;
		if (rows is null) {
			rows = new JsonArray();
			node["entitySchemaOperationsRights"] = rows;
		}
		JsonObject existing = rows.OfType<JsonObject>()
			.FirstOrDefault(row => GranteeId(row) == grantee);

		if (revoke) {
			if (existing is null) {
				return false;
			}
			foreach (ObjectOperation op in operations) {
				existing[FieldOf(op)] = false;
			}
			// A row with no remaining rights is removed, mirroring the designer's "remove role".
			if (!Flag(existing, "canRead") && !Flag(existing, "canAppend")
				&& !Flag(existing, "canEdit") && !Flag(existing, "canDelete")) {
				rows.Remove(existing);
			}
			return true;
		}

		node["administratedByOperations"] = true;
		if (existing is null) {
			JsonObject row = new() {
				["sysAdminUnit"] = new JsonObject { ["id"] = grantee.ToString() },
				["canRead"] = false, ["canAppend"] = false, ["canEdit"] = false, ["canDelete"] = false,
				["position"] = rows.Count
			};
			foreach (ObjectOperation op in operations) {
				row[FieldOf(op)] = true;
			}
			rows.Add(row);
			return true;
		}
		foreach (ObjectOperation op in operations) {
			existing[FieldOf(op)] = true;
		}
		return true;
	}

	private ObjectRightsChange Save(JsonObject node, CreatioRequestOptions requestOptions) {
		SaveAdministratedObjectResponse response = PostAndDeserialize<SaveAdministratedObjectResponse>(
			ServiceUrlBuilder.KnownRoute.SaveAdministratedObject,
			new JsonObject { ["administratedObject"] = node.DeepClone() },
			requestOptions);
		return response is { Success: true }
			? new ObjectRightsChange(true, true)
			: new ObjectRightsChange(true, false, response?.ErrorInfo?.Message ?? "SaveAdministratedObject reported failure.");
	}

	// Returns the first candidate UId that describes an administered object as a mutable JSON node, or (null,
	// anyCleanAnswer, lastError): anyCleanAnswer true means a candidate answered but the object is not
	// administered by operations; lastError set with anyCleanAnswer false means every candidate faulted.
	private (JsonObject node, bool anyCleanAnswer, string error) TryGetAdministratedObject(
		string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<Guid> candidates = ResolveEntitySchemaUIds(schemaName, requestOptions);
		if (candidates.Count == 0) {
			return (null, false, null);
		}
		bool anyCleanAnswer = false;
		string lastError = null;
		foreach (Guid candidate in candidates) {
			JsonObject node = TryFetchNode(candidate, requestOptions, out string error);
			if (error is not null) {
				lastError = error;
				continue;
			}
			anyCleanAnswer = true;
			if (node is not null) {
				return (node, true, null);
			}
		}
		return (null, anyCleanAnswer, lastError);
	}

	// Fetches GetAdministratedObject for one UId. Returns the administratedObject node when the response
	// describes an administered object; null with error set when the candidate faulted (wrong UId); null with
	// error null when the response was clean but the object is not administered by operations.
	private JsonObject TryFetchNode(Guid schemaUId, CreatioRequestOptions requestOptions, out string error) {
		error = null;
		GetAdministratedObjectNodeResponse response;
		try {
			response = PostAndDeserialize<GetAdministratedObjectNodeResponse>(
				ServiceUrlBuilder.KnownRoute.GetAdministratedObject,
				new GetAdministratedObjectRequest { SchemaUId = schemaUId },
				requestOptions);
		}
		catch (Exception ex) {
			error = ex.Message;
			return null;
		}
		// A clean success with an object describes the right schema — whether or not it is administered by
		// operations yet (a not-yet-administered object comes back with administratedByOperations=false, and a
		// grant enables it). A wrong candidate UId faults above and is skipped via error.
		JsonObject node = response?.AdministratedObject;
		if (response is { Success: true } && node is not null) {
			return node;
		}
		return null;
	}

	private static IEnumerable<JsonObject> ReadOperationRows(JsonObject node) =>
		(node["entitySchemaOperationsRights"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

	private static Guid GranteeId(JsonObject row) =>
		Guid.TryParse(row["sysAdminUnit"]?["id"]?.GetValue<string>(), out Guid id) ? id : Guid.Empty;

	private static bool Flag(JsonObject node, string name) =>
		node[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

	private static string FieldOf(ObjectOperation operation) => operation switch {
		ObjectOperation.Read => "canRead",
		ObjectOperation.Create => "canAppend",
		ObjectOperation.Edit => "canEdit",
		ObjectOperation.Delete => "canDelete",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	// Resolves an entity schema name to its candidate UId(s) via a DataService SelectQuery over SysSchema,
	// filtered to the EntitySchemaManager layer. A granted/extended schema has more than one row (base +
	// replacing schema); all are returned as candidates.
	private IReadOnlyList<Guid> ResolveEntitySchemaUIds(string schemaName, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysSchema",
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId") },
			new[] {
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "EntitySchemaManager", SelectQueryHelper.TextDataValueType)
			},
			20);

		SchemaUIdSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<SchemaUIdSelectResponse>(
			_applicationClient, _urlBuilder, query, requestOptions.TimeOut, requestOptions.MaxAttempts,
			requestOptions.RetryDelay);

		return response.Rows is null
			? Array.Empty<Guid>()
			: response.Rows.Select(row => row.UId).Where(uId => uId != Guid.Empty).Distinct().ToList();
	}

	private sealed class GetAdministratedObjectNodeResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("administratedObject")]
		public JsonObject AdministratedObject { get; set; }
	}

	private sealed class SaveAdministratedObjectResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public SaveErrorInfo ErrorInfo { get; set; }
	}

	private sealed class SaveErrorInfo
	{
		[JsonPropertyName("message")]
		public string Message { get; set; }
	}

	private sealed class SchemaUIdSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public List<SchemaUIdRow> Rows { get; set; }
	}

	private sealed class SchemaUIdRow
	{
		[JsonPropertyName("UId")]
		public Guid UId { get; set; }
	}
}
