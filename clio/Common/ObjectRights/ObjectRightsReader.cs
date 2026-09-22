using System;
using System.Collections.Generic;
using System.Linq;
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
/// <param name="OperationPermissionsDisabled">
/// The write removed the object's last operation-rights row AND turned <c>administratedByOperations</c> off,
/// so the object is now available to every internal user. Only ever true when the caller asked for it.
/// </param>
/// <param name="RefusedLastRowRemoval">
/// The revoke would have removed the object's LAST operation-rights row and the caller did not ask to turn
/// operation permissions off, so nothing was written.
/// </param>
public sealed record ObjectRightsChange(
	bool Found,
	bool Changed,
	string Error = null,
	bool OperationPermissionsDisabled = false,
	bool RefusedLastRowRemoval = false);

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
	/// <param name="disableOperationPermissions">
	/// Allows a revoke to remove the object's LAST operation-rights row, which turns
	/// <c>administratedByOperations</c> off and thereby makes the object available to EVERY internal user.
	/// That is an access WIDENING, so it is never done implicitly: with this false such a revoke writes nothing
	/// and reports <see cref="ObjectRightsChange.RefusedLastRowRemoval"/> instead.
	/// </param>
	ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions,
		CreatioRequestOptions requestOptions);
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
		try {
			(node, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) {
			return new ObjectRightsInfo(true, schemaName, null, false, Array.Empty<RoleOperationRights>(),
				ReadError: ex.Message);
		}
		if (node is null) {
			// error set = the object exists but could not be read (a service fault); error null = the schema
			// name resolved to no candidate at all (not found). Never report a failed read as "available".
			return error is not null
				? new ObjectRightsInfo(true, schemaName, null, false, Array.Empty<RoleOperationRights>(), ReadError: error)
				: new ObjectRightsInfo(false, schemaName, null, false, Array.Empty<RoleOperationRights>());
		}

		List<RoleOperationRights> roles = ReadOperationRows(node)
			.Select(row => new RoleOperationRights(
				GranteeId(row), Str(row["sysAdminUnit"]?["name"]) ?? "(unknown)",
				Flag(row, "canRead"), Flag(row, "canAppend"), Flag(row, "canEdit"), Flag(row, "canDelete")))
			.ToList();
		return new ObjectRightsInfo(true, Str(node["name"]) ?? schemaName,
			Str(node["caption"]), Flag(node, "administratedByOperations"), roles);
	}

	public ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions,
		CreatioRequestOptions requestOptions) {
		JsonObject node;
		string error;
		try {
			(node, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) {
			return new ObjectRightsChange(true, false, ex.Message);
		}
		if (node is null) {
			// error set = a real read failure (do not silently no-op); error null = schema not found.
			return error is not null
				? new ObjectRightsChange(true, false, error)
				: new ObjectRightsChange(false, false);
		}

		MutationOutcome outcome = MutateOperationRow(node, grantee, operations, revoke, disableOperationPermissions);
		if (outcome == MutationOutcome.RefusedLastRowRemoval) {
			return new ObjectRightsChange(true, false, RefusedLastRowRemoval: true);
		}
		if (outcome == MutationOutcome.NoChange) {
			return new ObjectRightsChange(true, false);
		}
		ObjectRightsChange saved = Save(node, requestOptions);
		return outcome == MutationOutcome.ChangedAndDisabled && saved.Error is null
			? saved with { OperationPermissionsDisabled = true }
			: saved;
	}

	/// <summary>The end state <see cref="MutateOperationRow"/> left the object node in.</summary>
	private enum MutationOutcome {
		NoChange,
		Changed,
		ChangedAndDisabled,
		RefusedLastRowRemoval
	}

	// Applies the grant/revoke to the object node's operation-rights rows in place; reports whether anything
	// ACTUALLY changed (so an unchanged re-run reports "no change" and skips the save). All other fields of the
	// node are left untouched so the save round-trips faithfully.
	private static MutationOutcome MutateOperationRow(JsonObject node, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions) {
		JsonArray rows = node["entitySchemaOperationsRights"] as JsonArray;
		if (rows is null) {
			rows = new JsonArray();
			node["entitySchemaOperationsRights"] = rows;
		}
		JsonObject existing = rows.OfType<JsonObject>()
			.FirstOrDefault(row => GranteeId(row) == grantee);

		if (revoke) {
			if (existing is null) {
				return MutationOutcome.NoChange;
			}
			(bool, bool, bool, bool) before = Snapshot(existing);
			// Removing the object's LAST rights row leaves only two possible end states, and neither may happen as
			// a side effect of a per-grantee revoke: keep administratedByOperations on and the object is reachable
			// by NOBODY, or turn it off and the object becomes reachable by EVERY internal user — an access
			// WIDENING, the opposite of what a revoke asks for. The caller has to choose the second one explicitly.
			// Decided before the flags are cleared, so a refusal leaves the row exactly as it was.
			bool lastRow = rows.Count == 1 && ReferenceEquals(rows[0], existing);
			if (lastRow && !disableOperationPermissions && WouldEmptyRow(existing, operations)) {
				return MutationOutcome.RefusedLastRowRemoval;
			}
			foreach (ObjectOperation op in operations) {
				existing[FieldOf(op)] = false;
			}
			// A row with no remaining rights is removed, mirroring the designer's "remove role".
			if (!Flag(existing, "canRead") && !Flag(existing, "canAppend")
				&& !Flag(existing, "canEdit") && !Flag(existing, "canDelete")) {
				rows.Remove(existing);
				// Reached only with disableOperationPermissions: the caller asked for the object to return to
				// "available to all" rather than be left administered with zero grants.
				if (rows.Count == 0 && Flag(node, "administratedByOperations")) {
					node["administratedByOperations"] = false;
					return MutationOutcome.ChangedAndDisabled;
				}
				return MutationOutcome.Changed;
			}
			return before != Snapshot(existing) ? MutationOutcome.Changed : MutationOutcome.NoChange;
		}

		// Enabling operation permissions is itself a change; track it so a re-grant that alters nothing is a no-op.
		bool enabledNow = !Flag(node, "administratedByOperations");
		node["administratedByOperations"] = true;
		if (existing is null) {
			JsonObject row = new() {
				["sysAdminUnit"] = new JsonObject { ["id"] = grantee.ToString() },
				["canRead"] = false, ["canAppend"] = false, ["canEdit"] = false, ["canDelete"] = false,
				["position"] = NextPosition(rows)
			};
			foreach (ObjectOperation op in operations) {
				row[FieldOf(op)] = true;
			}
			rows.Add(row);
			return MutationOutcome.Changed;
		}
		(bool, bool, bool, bool) grantBefore = Snapshot(existing);
		foreach (ObjectOperation op in operations) {
			existing[FieldOf(op)] = true;
		}
		return enabledNow || grantBefore != Snapshot(existing)
			? MutationOutcome.Changed
			: MutationOutcome.NoChange;
	}

	// Asked BEFORE the revoke clears anything, because the last-row refusal has to leave the row untouched.
	private static bool WouldEmptyRow(JsonObject row, IReadOnlyCollection<ObjectOperation> operations) {
		foreach (string field in new[] { "canRead", "canAppend", "canEdit", "canDelete" }) {
			if (Flag(row, field) && !operations.Any(op => FieldOf(op) == field)) {
				return false;
			}
		}
		return true;
	}

	// The four operation flags of a row, for change detection.
	private static (bool read, bool append, bool edit, bool delete) Snapshot(JsonObject row) =>
		(Flag(row, "canRead"), Flag(row, "canAppend"), Flag(row, "canEdit"), Flag(row, "canDelete"));

	// One past the highest existing position, so a new row never collides with an existing one even when the
	// existing positions are non-contiguous (a prior designer-side removal can leave gaps).
	private static int NextPosition(JsonArray rows) {
		int max = -1;
		foreach (JsonObject row in rows.OfType<JsonObject>()) {
			if (row["position"] is JsonValue value && value.TryGetValue(out int position) && position > max) {
				max = position;
			}
		}
		return max + 1;
	}

	private ObjectRightsChange Save(JsonObject node, CreatioRequestOptions requestOptions) {
		JsonObject payload = node.DeepClone().AsObject();
		// Mirror the platform client: only the collection we changed (operation rights) is sent; the record,
		// column and entity-operation collections are sent as null ("leave untouched") so the save neither
		// re-processes nor risks clobbering them. We only ever mutate entitySchemaOperationsRights.
		payload["entitySchemaRecordDefRights"] = null;
		payload["entitySchemaColumnsRights"] = null;
		payload["entityOperationGrantees"] = null;
		GetAdministratedObjectNodeResponse response = PostAndDeserialize<GetAdministratedObjectNodeResponse>(
			ServiceUrlBuilder.KnownRoute.SaveAdministratedObject,
			new JsonObject { ["administratedObject"] = payload },
			requestOptions);
		return response is { Success: true }
			? new ObjectRightsChange(true, true)
			: new ObjectRightsChange(true, false, response?.ErrorInfo?.Message ?? "SaveAdministratedObject reported failure.");
	}

	// Returns the first candidate UId that describes the object as a mutable JSON node, or (null, error):
	// error null means the schema name resolved to no candidate (not found); error set means every candidate
	// answered but with a fault (a wrong replacing-schema UId, or an in-band success:false).
	private (JsonObject node, string error) TryGetAdministratedObject(
		string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<Guid> candidates = ResolveEntitySchemaUIds(schemaName, requestOptions);
		if (candidates.Count == 0) {
			return (null, null);
		}
		string lastError = null;
		foreach (Guid candidate in candidates) {
			JsonObject node = TryFetchNode(candidate, requestOptions, out string error);
			if (node is not null) {
				return (node, null);
			}
			lastError = error;
		}
		return (null, lastError);
	}

	// Fetches GetAdministratedObject for one UId. Returns the administratedObject node on a clean success
	// (whether or not the object is administered by operations yet — a not-yet-administered object comes back
	// with administratedByOperations=false and a grant enables it). Otherwise returns null with `error` set:
	// an HTTP fault (a wrong replacing-schema UId returns a non-JSON error page), an in-band success:false
	// (the .svc reports logical failures — permission, unknown schema, licensing — as HTTP 200 + errorInfo),
	// or an unexpected empty body. A failure is NEVER reported as "not administered / available".
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
		if (response is { Success: true } && response.AdministratedObject is not null) {
			return response.AdministratedObject;
		}
		error = response is { Success: false }
			? response.ErrorInfo?.Message ?? "GetAdministratedObject reported failure."
			: "GetAdministratedObject returned no administrated object.";
		return null;
	}

	private static IEnumerable<JsonObject> ReadOperationRows(JsonObject node) =>
		(node["entitySchemaOperationsRights"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

	private static Guid GranteeId(JsonObject row) =>
		Guid.TryParse(Str(row["sysAdminUnit"]?["id"]), out Guid id) ? id : Guid.Empty;

	private static bool Flag(JsonObject node, string name) =>
		node[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

	// Reads a JSON node as a string, or null if it is absent or not a string value (never throws on a
	// non-string value the platform might return).
	private static string Str(JsonNode node) =>
		node is JsonValue value && value.TryGetValue(out string text) ? text : null;

	private static string FieldOf(ObjectOperation operation) => operation switch {
		ObjectOperation.Read => "canRead",
		ObjectOperation.Create => "canAppend",
		ObjectOperation.Edit => "canEdit",
		ObjectOperation.Delete => "canDelete",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	// Resolves an entity schema name to its candidate UId(s) via a DataService SelectQuery over SysSchema,
	// filtered to the EntitySchemaManager layer. A granted/extended schema has more than one row (base +
	// replacing schema); all are returned as candidates, in a deterministic order so which one is used does
	// not vary run to run.
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
			: response.Rows.Select(row => row.UId).Where(uId => uId != Guid.Empty).Distinct().OrderBy(uId => uId).ToList();
	}

	private sealed class GetAdministratedObjectNodeResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ObjectRightsErrorInfo ErrorInfo { get; set; }

		[JsonPropertyName("administratedObject")]
		public JsonObject AdministratedObject { get; set; }
	}

	private sealed class ObjectRightsErrorInfo
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
