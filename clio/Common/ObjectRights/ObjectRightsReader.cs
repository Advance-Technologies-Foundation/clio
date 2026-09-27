using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
	bool CanDelete) {

	/// <summary>The role holds at least one operation (an all-false row grants nothing).</summary>
	public bool HasAnyOperation => CanRead || CanCreate || CanEdit || CanDelete;

	/// <summary>The operations the role holds, in grid order: read, create, edit, delete.</summary>
	public IReadOnlyList<string> OperationNames() =>
		new[] { CanRead ? "read" : null, CanCreate ? "create" : null, CanEdit ? "edit" : null, CanDelete ? "delete" : null }
			.Where(op => op is not null)
			.ToArray();
}

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

/// <summary>What a set-object-rights write did to one object. Exactly one outcome applies.</summary>
public enum ObjectRightsOutcome {
	/// <summary>The schema name resolved to no object; nothing was written.</summary>
	NotFound,
	/// <summary>The object could not be read or saved; see <see cref="ObjectRightsChange.Error"/>.</summary>
	Failed,
	/// <summary>The object was already in the requested state; nothing was written.</summary>
	NoChange,
	/// <summary>The grantee's rights were changed and saved.</summary>
	Changed,
	/// <summary>
	/// The grant turned <c>administratedByOperations</c> ON for an object that did not use operation permissions
	/// yet. That can NARROW access for every other internal role, so the saved object is read back and the roles
	/// that actually hold rights afterwards are reported in <see cref="ObjectRightsChange.RolesAfterEnable"/>.
	/// </summary>
	ChangedAndEnabled,
	/// <summary>
	/// The revoke removed the object's last rights row AND turned <c>administratedByOperations</c> off, so the
	/// object is now available to every internal user. Only ever reached when the caller asked for it.
	/// </summary>
	ChangedAndDisabled,
	/// <summary>
	/// The revoke would have removed the object's last EFFECTIVE grant and the caller did not ask to turn
	/// operation permissions off; nothing was written.
	/// </summary>
	RefusedLastRowRemoval,
	/// <summary>
	/// A revoke was asked for on an object that is not administered by operation permissions. Every internal user
	/// can reach such an object whatever its rows say, so no per-role revoke can restrict it; nothing was written.
	/// </summary>
	RevokeOnNotAdministered
}

/// <summary>The result of a set-object-rights write for one object.</summary>
/// <param name="Outcome">What happened to the object.</param>
/// <param name="Error">The failure reason when <paramref name="Outcome"/> is <see cref="ObjectRightsOutcome.Failed"/>.</param>
/// <param name="RolesAfterEnable">
/// For <see cref="ObjectRightsOutcome.ChangedAndEnabled"/>: the roles that hold rights after the save, read back
/// from the server. <see langword="null"/> when the read-back itself failed (<see cref="ReadBackError"/>).
/// </param>
/// <param name="ReadBackError">Why the read-back after enabling could not be done.</param>
public sealed record ObjectRightsChange(
	ObjectRightsOutcome Outcome,
	string Error = null,
	IReadOnlyList<RoleOperationRights> RolesAfterEnable = null,
	string ReadBackError = null) {

	/// <summary>A change was saved (<see cref="ObjectRightsOutcome.Changed"/>, <c>ChangedAndEnabled</c> or <c>ChangedAndDisabled</c>).</summary>
	public bool Changed => Outcome is ObjectRightsOutcome.Changed or ObjectRightsOutcome.ChangedAndEnabled
		or ObjectRightsOutcome.ChangedAndDisabled;
}

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
/// object-level analog of set-record-rights; it works for ANY role.
/// </summary>
public interface IObjectRightsWriter {
	/// <summary>
	/// Grants (or, when <paramref name="revoke"/> is true, revokes) the given <paramref name="operations"/> for
	/// role <paramref name="grantee"/> on <paramref name="schemaName"/>. Turns on operation permissions for the
	/// object when granting to an object that does not yet use them, and then reads the object back.
	/// </summary>
	/// <param name="disableOperationPermissions">
	/// Allows a revoke to remove the object's LAST effective grant, which turns <c>administratedByOperations</c>
	/// off and thereby makes the object available to EVERY internal user. That is an access WIDENING, so it is
	/// never done implicitly: with this false such a revoke writes nothing and reports
	/// <see cref="ObjectRightsOutcome.RefusedLastRowRemoval"/> instead.
	/// </param>
	ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions,
		CreatioRequestOptions requestOptions);
}

/// <summary>Resolves a SysAdminUnit (role or user) id to its name, to confirm a grantee exists before a write.</summary>
public interface IGranteeLookup {
	/// <summary>
	/// Returns the name of the SysAdminUnit <paramref name="grantee"/>, or <see langword="null"/> when no such
	/// role or user exists.
	/// </summary>
	string ResolveGranteeName(Guid grantee, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Client for the native Creatio <c>RightManagementService</c>. Resolves an entity schema name to its UId via
/// DataService (the clio name→UId convention) and reads/writes the object's per-role operation rights.
/// </summary>
public class RightManagementServiceClient : CreatioServiceClient, IObjectRightsReader, IObjectRightsWriter,
	IGranteeLookup {

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>Creates the client over the given application client and URL builder.</summary>
	public RightManagementServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	public ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions) {
		JsonObject node;
		string error;
		try {
			(node, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) when (IsServiceFailure(ex)) {
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
		return new ObjectRightsInfo(true, Str(node["name"]) ?? schemaName,
			Str(node["caption"]), Flag(node, "administratedByOperations"), ProjectRoles(node));
	}

	/// <inheritdoc />
	public ObjectRightsChange SetObjectRights(string schemaName, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions,
		CreatioRequestOptions requestOptions) {
		JsonObject node;
		string error;
		try {
			(node, error) = TryGetAdministratedObject(schemaName, requestOptions);
		}
		catch (Exception ex) when (IsServiceFailure(ex)) {
			return new ObjectRightsChange(ObjectRightsOutcome.Failed, ex.Message);
		}
		if (node is null) {
			// error set = a real read failure (do not silently no-op); error null = schema not found.
			return error is not null
				? new ObjectRightsChange(ObjectRightsOutcome.Failed, error)
				: new ObjectRightsChange(ObjectRightsOutcome.NotFound);
		}

		ObjectRightsOutcome outcome = MutateOperationRows(node, grantee, operations, revoke, disableOperationPermissions);
		if (outcome is ObjectRightsOutcome.NoChange or ObjectRightsOutcome.RefusedLastRowRemoval
			or ObjectRightsOutcome.RevokeOnNotAdministered) {
			return new ObjectRightsChange(outcome);
		}
		string saveError;
		try {
			saveError = Save(node, requestOptions);
		}
		catch (Exception ex) when (IsServiceFailure(ex)) {
			// An HTTP fault, a non-JSON error page or a timeout on the save: report it against THIS object so a
			// fan-out names where it stopped and carries on with the rest, instead of aborting the whole run.
			return new ObjectRightsChange(ObjectRightsOutcome.Failed, ex.Message);
		}
		if (saveError is not null) {
			return new ObjectRightsChange(ObjectRightsOutcome.Failed, saveError);
		}
		return outcome == ObjectRightsOutcome.ChangedAndEnabled
			? ReadBackAfterEnable(schemaName, requestOptions)
			: new ObjectRightsChange(outcome);
	}

	/// <inheritdoc />
	public string ResolveGranteeName(Guid grantee, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysAdminUnit",
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name") },
			new[] { new SelectQueryHelper.SelectQueryFilterDefinition("Id", grantee, SelectQueryHelper.GuidDataValueType) },
			1);
		GranteeSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<GranteeSelectResponse>(
			_applicationClient, _urlBuilder, query, requestOptions.TimeOut, requestOptions.MaxAttempts,
			requestOptions.RetryDelay);
		return response.Rows?.FirstOrDefault()?.Name;
	}

	// Turning operation permissions on is where access can silently NARROW for every other internal role: on
	// Creatio 8.3.4 the server adds an "All employees" row with full rights at that point, but that is observed,
	// not a documented contract. So the saved object is read back and the roles that actually hold rights are
	// returned, letting the caller tell "internal users kept access" from "only the grantee can reach it now".
	private ObjectRightsChange ReadBackAfterEnable(string schemaName, CreatioRequestOptions requestOptions) {
		ObjectRightsInfo after = GetObjectRights(schemaName, requestOptions);
		return after.ReadError is not null || !after.Found
			? new ObjectRightsChange(ObjectRightsOutcome.ChangedAndEnabled,
				ReadBackError: after.ReadError ?? "the object was not found on read-back")
			: new ObjectRightsChange(ObjectRightsOutcome.ChangedAndEnabled, RolesAfterEnable: after.Roles);
	}

	// Applies the grant/revoke to EVERY row of the grantee (a duplicate row must not keep access a revoke took
	// away) in place; reports what ACTUALLY changed, so an unchanged re-run reports "no change" and skips the
	// save. All other fields of the node are left untouched so the save round-trips faithfully.
	private static ObjectRightsOutcome MutateOperationRows(JsonObject node, Guid grantee,
		IReadOnlyCollection<ObjectOperation> operations, bool revoke, bool disableOperationPermissions) {
		JsonArray rows = node["entitySchemaOperationsRights"] as JsonArray;
		if (rows is null) {
			rows = new JsonArray();
			node["entitySchemaOperationsRights"] = rows;
		}
		List<JsonObject> granteeRows = rows.OfType<JsonObject>().Where(row => GranteeId(row) == grantee).ToList();
		return revoke
			? Revoke(node, rows, granteeRows, operations, disableOperationPermissions)
			: Grant(node, rows, granteeRows, grantee, operations);
	}

	private static ObjectRightsOutcome Revoke(JsonObject node, JsonArray rows, List<JsonObject> granteeRows,
		IReadOnlyCollection<ObjectOperation> operations, bool disableOperationPermissions) {
		// A revoke on an object that is not administered restricts nothing: every internal user reaches it
		// regardless of its rows (a stale row included). Report that instead of "no change" or "revoked", and
		// never let the last-row refusal fire here with its "would open it to all internal users" message —
		// the object already is open to them.
		if (!Flag(node, "administratedByOperations")) {
			return ObjectRightsOutcome.RevokeOnNotAdministered;
		}
		// Revoking operations the grantee does not hold changes nothing — decided before the last-grant check,
		// so a no-op re-run is "no change", not a refusal.
		if (!granteeRows.Any(row => operations.Any(op => Flag(row, FieldOf(op))))) {
			return ObjectRightsOutcome.NoChange;
		}
		// Removing the object's LAST effective grant leaves only two possible end states, and neither may happen as
		// a side effect of a per-grantee revoke: keep administratedByOperations on and the object is reachable by
		// NOBODY, or turn it off and it becomes reachable by EVERY internal user — an access WIDENING. The caller
		// has to choose the second one explicitly. "Last" counts effective grants, not rows: another row whose
		// flags are all false grants nothing and must not let the refusal be skipped.
		bool othersGrantSomething = rows.OfType<JsonObject>()
			.Where(row => !granteeRows.Contains(row))
			.Any(row => OperationFields.Any(field => Flag(row, field)));
		bool leavesGranteeSomething = granteeRows.Any(row => !WouldEmptyRow(row, operations));
		if (!othersGrantSomething && !leavesGranteeSomething && !disableOperationPermissions) {
			return ObjectRightsOutcome.RefusedLastRowRemoval;
		}
		foreach (JsonObject row in granteeRows) {
			foreach (ObjectOperation op in operations) {
				row[FieldOf(op)] = false;
			}
			// A row with no remaining rights is removed, mirroring the designer's "remove role".
			if (!OperationFields.Any(field => Flag(row, field))) {
				rows.Remove(row);
			}
		}
		// Only reachable with disableOperationPermissions: the caller asked for the object to return to "available
		// to all internal users" rather than be left administered with no effective grant.
		bool anyGrantLeft = rows.OfType<JsonObject>().Any(row => OperationFields.Any(field => Flag(row, field)));
		if (!anyGrantLeft) {
			node["administratedByOperations"] = false;
			return ObjectRightsOutcome.ChangedAndDisabled;
		}
		return ObjectRightsOutcome.Changed;
	}

	private static ObjectRightsOutcome Grant(JsonObject node, JsonArray rows, List<JsonObject> granteeRows,
		Guid grantee, IReadOnlyCollection<ObjectOperation> operations) {
		// Enabling operation permissions is itself a change (and possibly an access NARROWING for every other
		// internal role), so it is tracked separately: a re-grant that alters nothing stays a no-op, and a grant
		// that flips the object from "available to all internal users" to "only listed roles" is reported as such.
		bool enabledNow = !Flag(node, "administratedByOperations");
		node["administratedByOperations"] = true;
		ObjectRightsOutcome changed = enabledNow ? ObjectRightsOutcome.ChangedAndEnabled : ObjectRightsOutcome.Changed;
		if (granteeRows.Count == 0) {
			JsonObject row = new() {
				["sysAdminUnit"] = new JsonObject { ["id"] = grantee.ToString() },
				["position"] = NextPosition(rows)
			};
			foreach (string field in OperationFields) {
				row[field] = false;
			}
			foreach (ObjectOperation op in operations) {
				row[FieldOf(op)] = true;
			}
			rows.Add(row);
			return changed;
		}
		bool rowChanged = false;
		foreach (JsonObject row in granteeRows) {
			foreach (ObjectOperation op in operations) {
				rowChanged |= !Flag(row, FieldOf(op));
				row[FieldOf(op)] = true;
			}
		}
		return enabledNow || rowChanged ? changed : ObjectRightsOutcome.NoChange;
	}

	// True when the revoke would leave the row with no operation at all. Asked BEFORE the revoke clears anything,
	// because the last-grant refusal has to leave the rows untouched.
	private static bool WouldEmptyRow(JsonObject row, IReadOnlyCollection<ObjectOperation> operations) =>
		!OperationFields.Any(field => Flag(row, field) && !operations.Any(op => FieldOf(op) == field));

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

	// Read-modify-write is last-writer-wins: SaveAdministratedObject carries no version, so a change another
	// client saves between our read and our save is overwritten. Object permissions are changed rarely and by
	// administrators, so this is accepted; re-read with get-object-rights when concurrent edits are possible.
	// Returns null on success, otherwise the service's failure message.
	private string Save(JsonObject node, CreatioRequestOptions requestOptions) {
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
			? null
			: response?.ErrorInfo?.Message ?? "SaveAdministratedObject reported failure.";
	}

	// Returns the first candidate UId that describes the object as a mutable JSON node, or (null, error):
	// error null means the schema name resolved to no candidate (not found); error set means every candidate
	// answered but with a fault. The FIRST candidate's error is kept: it is the base row, whose real cause (a
	// timeout, a permission error) is what the caller needs, not the opaque "Request Error" page a later
	// replacing layer answers with.
	private (JsonObject node, string error) TryGetAdministratedObject(
		string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<Guid> candidates = ResolveEntitySchemaUIds(schemaName, requestOptions);
		if (candidates.Count == 0) {
			return (null, null);
		}
		string firstError = null;
		foreach (Guid candidate in candidates) {
			JsonObject node = TryFetchNode(candidate, requestOptions, out string error);
			if (node is not null) {
				return (node, null);
			}
			firstError ??= error;
		}
		return (null, firstError);
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
		catch (Exception ex) when (IsServiceFailure(ex)) {
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

	// The failures a call to the Creatio service can produce and that must be attributed to one object rather
	// than end the run: a transport fault, a timeout, a non-JSON or empty body (InvalidOperationException from
	// PostAndDeserialize), an authentication rejection, an oversized response. Programming errors
	// (NullReferenceException, ArgumentException, ...) are deliberately NOT caught here. An HTTP timeout surfaces
	// as TaskCanceledException, hence OperationCanceledException.
	internal static bool IsServiceFailure(Exception exception) =>
		exception is InvalidOperationException or HttpRequestException or TimeoutException or IOException
			or JsonException or UnauthorizedAccessException or ResponseTooLargeException or OperationCanceledException;

	private static List<RoleOperationRights> ProjectRoles(JsonObject node) =>
		ReadOperationRows(node)
			.Select(row => new RoleOperationRights(
				GranteeId(row), Str(row["sysAdminUnit"]?["name"]) ?? "(unknown)",
				Flag(row, FieldOf(ObjectOperation.Read)), Flag(row, FieldOf(ObjectOperation.Create)),
				Flag(row, FieldOf(ObjectOperation.Edit)), Flag(row, FieldOf(ObjectOperation.Delete))))
			.ToList();

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

	// The wire names of the four operation flags, derived from FieldOf so the mapping lives in one place.
	private static readonly string[] OperationFields =
		Enum.GetValues<ObjectOperation>().Select(FieldOf).ToArray();

	private static string FieldOf(ObjectOperation operation) => operation switch {
		ObjectOperation.Read => "canRead",
		ObjectOperation.Create => "canAppend",
		ObjectOperation.Edit => "canEdit",
		ObjectOperation.Delete => "canDelete",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	// Resolves an entity schema name to its candidate UId(s) via a DataService SelectQuery over SysSchema,
	// filtered to the EntitySchemaManager layer. A granted/extended schema has more than one row (base +
	// replacing layers); heavily layered OOTB objects (Contact, Account) can have many. The query is ORDERED
	// server-side by ExtendParent ascending — false first, i.e. the BASE row — BEFORE the row cap applies:
	// rowCount is applied to an otherwise unordered result, so without the order the base (administrable) row
	// can fall outside the window depending on the data (the same trap ClassicEntitySchemaQuery.ColumnOrderedAsc
	// documents). The server order is kept, so the base row is probed first and the replacing layers — which
	// fault on GetAdministratedObject — are only tried if it does not answer. Deterministic run to run.
	private IReadOnlyList<Guid> ResolveEntitySchemaUIds(string schemaName, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysSchema",
			new[] {
				new SelectQueryHelper.SelectQueryColumnDefinition("ExtendParent", "ExtendParent",
					OrderDirection: 1, OrderPosition: 0),
				new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId")
			},
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

	private sealed class GranteeSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public List<GranteeRow> Rows { get; set; }
	}

	private sealed class GranteeRow
	{
		[JsonPropertyName("Name")]
		public string Name { get; set; }
	}
}
