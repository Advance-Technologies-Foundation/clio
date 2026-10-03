using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Package;

namespace Clio.Common.ObjectRights;

/// <summary>
/// Reads object operation permissions (the SysEntitySchemaOperationRight layer) for an entity, using the native
/// Creatio <c>RightManagementService.svc/GetAdministratedObject</c> service — the same service the System
/// Designer "Object permissions" section uses. Reports EVERY role's row, in priority order.
/// </summary>
public interface IObjectRightsReader {
	/// <summary>
	/// Reads the operation permissions of the entity schema <paramref name="schemaName"/>: the switch, every role's
	/// row in priority order, and a snapshot a save can write back. A failed read is reported in
	/// <see cref="ObjectRightsInfo.ReadError"/> and never as "not administered".
	/// </summary>
	/// <param name="schemaName">The entity schema name.</param>
	/// <param name="requestOptions">Timeout and retry settings.</param>
	/// <returns>The object's operation permissions, or why they could not be read.</returns>
	ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Saves a planned operation-permissions state through <c>RightManagementService.svc/SaveAdministratedObject</c>.
/// The writer holds no policy: it writes exactly the state it is given (see <see cref="IObjectRightsPlanner"/>).
/// </summary>
public interface IObjectRightsWriter {
	/// <summary>
	/// Writes <paramref name="after"/> onto the object read as <paramref name="snapshot"/>: the switch, and only the rows
	/// the plan changes or adds. A row the plan leaves as it was read is sent exactly as read, including any field the
	/// projection cannot represent. A changed row is found by its grantee AND position, so two rows of one role are never
	/// merged; a planned row with no read row at its grantee and position is added there. No row is ever removed. The
	/// record, column and entity-operation collections are sent as null so the save leaves them alone.
	/// </summary>
	/// <param name="snapshot">The object as it was read.</param>
	/// <param name="after">The planned state.</param>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <returns><see cref="ObjectRightsSaveResult.Saved"/> on success, otherwise why the save failed or was not sent, and
	/// whether no answer came (the save may then still land).</returns>
	ObjectRightsSaveResult Save(ObjectRightsSnapshot snapshot, ObjectRightsState after, CreatioRequestOptions requestOptions);
}

/// <summary>Resolves a SysAdminUnit (role or user) id to its name, to confirm a grantee exists before a write.</summary>
public interface IGranteeLookup {
	/// <summary>
	/// Returns the name of the SysAdminUnit <paramref name="grantee"/>, or <see langword="null"/> when no such
	/// role or user exists.
	/// </summary>
	/// <param name="grantee">The SysAdminUnit id.</param>
	/// <param name="requestOptions">Timeout and retry settings.</param>
	/// <returns>The name, or <see langword="null"/>.</returns>
	string ResolveGranteeName(Guid grantee, CreatioRequestOptions requestOptions);
}

/// <summary>
/// Client for the native Creatio <c>RightManagementService</c>. Resolves an entity schema name to its UId via
/// DataService (the clio name→UId convention), reads the object's operation rows, and saves a planned state.
/// </summary>
public class RightManagementServiceClient : CreatioServiceClient, IObjectRightsReader, IObjectRightsWriter,
	IGranteeLookup {

	private const string AdministratedByOperationsField = "administratedByOperations";
	private const string OperationRowsField = "entitySchemaOperationsRights";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>Creates the client over the given application client and URL builder.</summary>
	/// <param name="applicationClient">The authenticated application client.</param>
	/// <param name="urlBuilder">Builds the service routes.</param>
	public RightManagementServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: base(applicationClient, urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	public ObjectRightsInfo GetObjectRights(string schemaName, CreatioRequestOptions requestOptions) {
		(JsonObject node, string error, bool timedOut) = TryGetAdministratedObject(schemaName, requestOptions);
		if (node is null) {
			// error set = the object exists but could not be read (a service fault); error null = the schema
			// name resolved to no candidate at all (not found). Never report a failed read as "available".
			return error is not null
				? ObjectRightsInfo.ReadFailed(schemaName, error, timedOut)
				: new ObjectRightsInfo(false, schemaName, null, false, Array.Empty<RoleOperationRights>());
		}
		return new ObjectRightsInfo(true, Str(node["name"]) ?? schemaName, Str(node["caption"]),
			Flag(node, AdministratedByOperationsField), ProjectRoles(node), Snapshot: new ObjectRightsSnapshot(node));
	}

	/// <inheritdoc />
	public ObjectRightsSaveResult Save(ObjectRightsSnapshot snapshot, ObjectRightsState after,
		CreatioRequestOptions requestOptions) {
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(after);
		JsonObject payload = snapshot.Node.DeepClone().AsObject();
		if (payload[OperationRowsField] is not JsonArray rows) {
			rows = new JsonArray();
			payload[OperationRowsField] = rows;
		}
		string conflict = ApplyPlannedRows(rows, after.Roles);
		if (conflict is not null) {
			return new ObjectRightsSaveResult(conflict);
		}
		payload[AdministratedByOperationsField] = after.AdministratedByOperations;
		// Mirror the platform client: only the collection that changed (operation rights) is sent; the record,
		// column and entity-operation collections are sent as null ("leave untouched") so the save neither
		// re-processes nor risks clobbering them.
		payload["entitySchemaRecordDefRights"] = null;
		payload["entitySchemaColumnsRights"] = null;
		payload["entityOperationGrantees"] = null;
		// Read-modify-write is last-writer-wins: SaveAdministratedObject carries no version, so a change another
		// client saves between our read and our save is overwritten. The caller reads the object back and reports
		// any difference from the plan.
		// The save is sent exactly once, like the other clio writes (manage-access, the schema designer's save and
		// build): Creatio.Client re-sends a request after ANY exception, a timeout included, and a new row is sent
		// without an id, so a retry of a save the server already committed could add the row a second time.
		// A save the call's deadline leaves no time for is not sent at all, so its outcome is known: nothing changed.
		if (requestOptions.Deadline is { IsSpent: true }) {
			return new ObjectRightsSaveResult("the call's time limit was spent before the save, so it was not sent");
		}
		try {
			GetAdministratedObjectNodeResponse response = PostAndDeserialize<GetAdministratedObjectNodeResponse>(
				ServiceUrlBuilder.KnownRoute.SaveAdministratedObject,
				new JsonObject { ["administratedObject"] = payload },
				requestOptions with { MaxAttempts = 1 });
			return response is { Success: true }
				? ObjectRightsSaveResult.Saved
				: new ObjectRightsSaveResult(ServiceMessage(response?.ErrorInfo?.Message,
					"SaveAdministratedObject reported failure."));
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			// No answer — a hang, or a connection that broke after the request went out — is not a refusal: the server
			// may have committed the save, and may even commit it after the read-back.
			return new ObjectRightsSaveResult(ObjectRightsSupport.DisplayFailure(ex),
				ObjectRightsSupport.LeavesOutcomeUnknown(ex));
		}
	}

	/// <inheritdoc />
	public string ResolveGranteeName(Guid grantee, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysAdminUnit",
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name") },
			new[] { new SelectQueryHelper.SelectQueryFilterDefinition("Id", grantee, SelectQueryHelper.GuidDataValueType) },
			1);
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		GranteeSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<GranteeSelectResponse>(
			_applicationClient, _urlBuilder, query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		return response.Rows?.FirstOrDefault()?.Name;
	}

	// Returns the node of the first candidate UId that describes the object, or (null, error, timedOut): error null
	// means the schema name resolved to no candidate (not found); error set means the UId lookup failed or every
	// candidate answered with a fault. The FIRST candidate's error is kept: it is the base row, whose real cause (a
	// permission error, say) is what the caller needs, not the opaque "Request Error" page a later replacing layer
	// answers with. timedOut says the read ended on a hang; the error then names the hang too.
	private (JsonObject node, string error, bool timedOut) TryGetAdministratedObject(
		string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<Guid> candidates;
		// Only the UId lookup is guarded here: each candidate's own call is guarded in TryFetchNode.
		try {
			candidates = ResolveEntitySchemaUIds(schemaName, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return (null, ObjectRightsSupport.DisplayFailure(ex), ObjectRightsSupport.IsTimeout(ex));
		}
		if (candidates.Count == 0) {
			return (null, null, false);
		}
		string firstError = null;
		foreach (Guid candidate in candidates) {
			if (TryFetchNode(candidate, requestOptions, out JsonObject node, out string error, out bool timedOut)) {
				return (node, null, false);
			}
			if (timedOut) {
				// A hang is not an answer from a wrong layer: the next candidate would only wait as long again. The error
				// names the hang, so a listing that stops on it does not read as stopping on the earlier fault.
				return (null, firstError is null ? error : ObjectRightsSupport.DisplayError($"{firstError}; then {error}"),
					true);
			}
			firstError ??= error;
		}
		return (null, firstError, false);
	}

	// Fetches GetAdministratedObject for one UId. Returns true with the administratedObject node on a clean success
	// (whether or not the object is administered by operations yet). Otherwise returns false with `error` set:
	// an HTTP fault (a wrong replacing-schema UId returns a non-JSON error page), an in-band success:false
	// (the .svc reports logical failures — permission, unknown schema, licensing — as HTTP 200 + errorInfo),
	// or an unexpected empty body. A failure is NEVER reported as "not administered / available".
	private bool TryFetchNode(Guid schemaUId, CreatioRequestOptions requestOptions, out JsonObject node,
		out string error, out bool timedOut) {
		node = null;
		error = null;
		timedOut = false;
		GetAdministratedObjectNodeResponse response;
		try {
			response = PostAndDeserialize<GetAdministratedObjectNodeResponse>(
				ServiceUrlBuilder.KnownRoute.GetAdministratedObject,
				new GetAdministratedObjectRequest { SchemaUId = schemaUId },
				requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			error = ObjectRightsSupport.DisplayFailure(ex);
			timedOut = ObjectRightsSupport.IsTimeout(ex);
			return false;
		}
		if (response is { Success: true } && response.AdministratedObject is not null) {
			node = response.AdministratedObject;
			return true;
		}
		error = response is { Success: false }
			? ServiceMessage(response.ErrorInfo?.Message, "GetAdministratedObject reported failure.")
			: "GetAdministratedObject returned no administrated object.";
		return false;
	}

	// Writes only what the plan changes. The rows read are first matched to the planned rows as a multiset by grantee,
	// position and operations: those are unchanged and stay exactly as read. Each planned row left over is either a
	// change to the one remaining read row of the same grantee at the same position, or a new row. Matching by grantee
	// alone would merge two rows of one role (the last planned one overwriting the first read one), which can widen
	// that role's access on a row nobody named. Returns why nothing may be sent, or null.
	private static string ApplyPlannedRows(JsonArray rows, IReadOnlyList<RoleOperationRights> planned) {
		List<(JsonObject Node, RoleOperationRights Read)> unmatched = rows.OfType<JsonObject>()
			.Select((node, index) => (node, ProjectRow(node, index)))
			.ToList();
		List<RoleOperationRights> changed = new();
		foreach (RoleOperationRights row in planned) {
			int same = unmatched.FindIndex(entry => entry.Read.SameRowAs(row));
			if (same >= 0) {
				unmatched.RemoveAt(same);
			} else {
				changed.Add(row);
			}
		}
		foreach (RoleOperationRights row in changed) {
			List<(JsonObject Node, RoleOperationRights Read)> candidates = unmatched
				.Where(entry => entry.Read.GranteeId == row.GranteeId && entry.Read.Position == row.Position)
				.ToList();
			if (candidates.Count > 1) {
				return $"{candidates.Count} rows of {ObjectRightsSupport.Display(row.GranteeName)} sit at position "
					+ $"{row.Position}, so the row to change is ambiguous; nothing was sent";
			}
			JsonObject node;
			if (candidates.Count == 1) {
				node = candidates[0].Node;
				unmatched.Remove(candidates[0]);
			} else {
				node = new JsonObject {
					["sysAdminUnit"] = new JsonObject { ["id"] = row.GranteeId.ToString() },
					["position"] = row.Position
				};
				rows.Add(node);
			}
			node[FieldOf(ObjectOperation.Read)] = row.CanRead;
			node[FieldOf(ObjectOperation.Create)] = row.CanCreate;
			node[FieldOf(ObjectOperation.Edit)] = row.CanEdit;
			node[FieldOf(ObjectOperation.Delete)] = row.CanDelete;
		}
		return null;
	}

	// A failure is never reported with an empty message: an empty string would read as "no error" to a caller that
	// checks for null, and as nothing at all to the operator. The platform's text is rendered on one line.
	private static string ServiceMessage(string message, string fallback) =>
		string.IsNullOrWhiteSpace(message) ? fallback : ObjectRightsSupport.DisplayError(message);

	// Every row, in priority order.
	private static List<RoleOperationRights> ProjectRoles(JsonObject node) =>
		ReadOperationRows(node)
			.Select(ProjectRow)
			.OrderBy(role => role.Position)
			.ToList();

	// One row, as the planner and the save see it. The service returns every row with its position; a row without a
	// readable one is placed at its index in the array, only so that it stays visible. The save matches rows with the
	// same projection, so it finds such a row again.
	private static RoleOperationRights ProjectRow(JsonObject row, int index) =>
		new(GranteeId(row), Str(row["sysAdminUnit"]?["name"]) ?? "(unknown)", Position(row) ?? index,
			Flag(row, FieldOf(ObjectOperation.Read)), Flag(row, FieldOf(ObjectOperation.Create)),
			Flag(row, FieldOf(ObjectOperation.Edit)), Flag(row, FieldOf(ObjectOperation.Delete)));

	private static IEnumerable<JsonObject> ReadOperationRows(JsonObject node) =>
		(node[OperationRowsField] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

	private static int? Position(JsonObject row) =>
		row["position"] is JsonValue value && value.TryGetValue(out int position) ? position : null;

	private static Guid GranteeId(JsonObject row) =>
		Guid.TryParse(Str(row["sysAdminUnit"]?["id"]), out Guid id) ? id : Guid.Empty;

	private static bool Flag(JsonObject node, string name) =>
		node[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

	// Reads a JSON node as a string, or null if it is absent or not a string value (never throws on a
	// non-string value the platform might return).
	private static string Str(JsonNode node) =>
		node is JsonValue value && value.TryGetValue(out string text) ? text : null;

	// The wire names of the four operation flags.
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
	// The candidate UIds of a schema do not change within one command run, and the write path reads the same object
	// again after the save. The client is transient (resolved per command), so the cache never outlives a call.
	private readonly Dictionary<string, IReadOnlyList<Guid>> _schemaUIds = new(StringComparer.Ordinal);

	private IReadOnlyList<Guid> ResolveEntitySchemaUIds(string schemaName, CreatioRequestOptions requestOptions) {
		if (_schemaUIds.TryGetValue(schemaName, out IReadOnlyList<Guid> cached)) {
			return cached;
		}
		IReadOnlyList<Guid> resolved = QueryEntitySchemaUIds(schemaName, requestOptions);
		_schemaUIds[schemaName] = resolved;
		return resolved;
	}

	private IReadOnlyList<Guid> QueryEntitySchemaUIds(string schemaName, CreatioRequestOptions requestOptions) {
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

		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		SchemaUIdSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<SchemaUIdSelectResponse>(
			_applicationClient, _urlBuilder, query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);

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
