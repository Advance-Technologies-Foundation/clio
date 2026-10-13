using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.EntitySchema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Deletes single records through OData and explains a refusal: the server's own inner message, the rows that
/// still reference the record, its binding to a running process, and whether it is visible at all.
/// </summary>
public interface IRecordDeletionService {

	/// <summary>Sends <c>DELETE odata/{entity}({id})</c> once and reads the answer.</summary>
	/// <param name="entity">Entity schema name; the caller has validated it.</param>
	/// <param name="id">Record id; the caller has validated it.</param>
	/// <param name="timeoutMs">Bound of the request, in milliseconds.</param>
	/// <returns>The outcome; a refusal carries the validated headline and the fenced inner message.</returns>
	RecordDeleteOutcome Delete(string entity, string id, int timeoutMs);

	/// <summary>
	/// Finds why a record may not be deleted: whether it is visible, which rows reference it (through the
	/// <c>SysEntitySchemaReference</c> metadata, audit columns excluded), and whether a process element holds it.
	/// </summary>
	/// <param name="entity">Entity schema name.</param>
	/// <param name="id">Record id.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <param name="stopAt">No request is started after this instant; the result then says it is partial.</param>
	/// <returns>The facts found.</returns>
	RecordDeleteDiagnosis Diagnose(string entity, string id, int timeoutMs, DateTimeOffset stopAt);

	/// <summary>Reads whether the record exists and this user can see it.</summary>
	/// <param name="entity">Entity schema name.</param>
	/// <param name="id">Record id.</param>
	/// <param name="timeoutMs">Bound of the request, in milliseconds.</param>
	/// <returns><see langword="false"/> when no row came back; <see langword="null"/> when it could not be read.</returns>
	bool? Exists(string entity, string id, int timeoutMs);

	/// <summary>
	/// Whether the entity is a lookup (dictionary): registered in <c>SysLookup</c>, or derived from a base lookup schema.
	/// A row that requires a lookup value only refers to it, so a cascade never treats such rows as owned.
	/// </summary>
	/// <param name="entity">Entity schema name.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <returns><see langword="true"/> when it is one, and also when that could not be established.</returns>
	bool IsLookup(string entity, int timeoutMs);
}

/// <summary>The outcome of one DELETE.</summary>
/// <param name="Success">Whether the record is gone.</param>
/// <param name="Error">Validated, redacted refusal; <see langword="null"/> on success.</param>
/// <param name="ServerDetail">The server's inner message, fenced as untrusted text; often the real reason.</param>
/// <param name="Attempted">Whether the request entered the transport.</param>
/// <param name="Received">Whether a response came back.</param>
public sealed record RecordDeleteOutcome(bool Success, string Error, string ServerDetail, bool Attempted, bool Received);

/// <summary>Rows of one entity that reference a record through one lookup column.</summary>
public sealed record ReferencingRows(
	[property: JsonProperty("entity")]
	[property: System.Text.Json.Serialization.JsonPropertyName("entity")]
	string Entity,
	[property: JsonProperty("column")]
	[property: System.Text.Json.Serialization.JsonPropertyName("column")]
	string Column,
	[property: JsonProperty("ids")]
	[property: System.Text.Json.Serialization.JsonPropertyName("ids")]
	IReadOnlyList<string> Ids,
	[property: JsonProperty("more")]
	[property: System.Text.Json.Serialization.JsonPropertyName("more")]
	bool More,
	[property: JsonProperty("kind")]
	[property: System.Text.Json.Serialization.JsonPropertyName("kind")]
	string Kind) {

	/// <summary>Deleted by the platform together with the referenced record.</summary>
	public const string CascadeKind = "cascade";

	/// <summary>A required lookup: the row cannot exist without the record, so cascade deletes it.</summary>
	public const string OwnedKind = "owned";

	/// <summary>An optional lookup: the row is only linked, so cascade never deletes it; re-point or clear it.</summary>
	public const string LinkedKind = "linked";
}

/// <summary>What a diagnosis found about one record.</summary>
public sealed class RecordDeleteDiagnosis {

	/// <summary>Whether the record is visible to the caller; <see langword="null"/> when it could not be read.</summary>
	public bool? Visible { get; init; }

	/// <summary>
	/// The live process element the record is bound to: the entity has <c>ProcessElementId</c>, it is set, and that
	/// element's <c>SysProcessElementData</c> row still exists (the row is deleted when the element completes).
	/// </summary>
	public string ProcessElementId { get; init; }

	/// <summary>Referencing rows, cascade ones included.</summary>
	public List<ReferencingRows> Referencing { get; } = [];

	/// <summary>Whether every referencing column was checked.</summary>
	public bool Complete { get; set; } = true;

	/// <summary>Referencing columns that could not be read ("Entity.Column"); each makes the diagnosis incomplete.</summary>
	public List<string> Unchecked { get; } = [];
}

internal sealed class RecordDeletionService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRuntimeEntitySchemaReader schemaReader)
	: IRecordDeletionService {

	/// <summary>Most referencing (entity, column) pairs checked for one record.</summary>
	internal const int MaxReferencePairs = 400;

	/// <summary>Ids listed per referencing pair; more is flagged.</summary>
	internal const int MaxIdsPerPair = 20;

	private const int GuidDataValueType = 0;
	private const int TextDataValueType = 1;
	private const int MaxReferenceMetadataRows = 5_000;
	private const string ProcessElementColumn = "ProcessElementId";
	// The base schemas a lookup is derived from; one that is not registered in SysLookup is still a dictionary.
	private static readonly HashSet<string> LookupBaseSchemas = new(StringComparer.Ordinal) {
		"BaseLookup", "BaseCodeLookup", "BaseImageLookup", "BaseCodeImageLookup"
	};
	private readonly Dictionary<string, bool> _lookups = new(StringComparer.OrdinalIgnoreCase);

	// One service instance serves one tool call; a cascade diagnoses many rows of the same entities.
	private readonly Dictionary<string, (List<(string Entity, string Column)> Pairs, bool Truncated)> _pairsByEntity =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, RuntimeEntitySchemaResult> _schemas = new(StringComparer.Ordinal);

	// Every entity has them, they point at Contact, and they are never the reason a test record cannot go.
	private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal) { "CreatedBy", "ModifiedBy" };

	/// <inheritdoc />
	public RecordDeleteOutcome Delete(string entity, string id, int timeoutMs) {
		bool attempted = false;
		bool received = false;
		try {
			string url = serviceUrlBuilder.Build(ODataKeyFormatter.KeyPath(entity, id));
			attempted = true;
			string response = applicationClient.ExecuteDeleteRequest(url, string.Empty, Math.Max(1, timeoutMs));
			received = true;
			string error = ODataKeyedWrite.ValidateWriteResponse(response);
			return error is null
				? new RecordDeleteOutcome(true, null, null, attempted, received)
				: new RecordDeleteOutcome(false, error, ReadInnerMessage(response), attempted, received);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return new RecordDeleteOutcome(false, SensitiveErrorTextRedactor.Redact(exception.Message), null, attempted,
				received);
		}
	}

	/// <inheritdoc />
	public RecordDeleteDiagnosis Diagnose(string entity, string id, int timeoutMs, DateTimeOffset stopAt) {
		// Every request is bounded by what is left before stopAt, and none starts after it: a request started just
		// before stopAt with its full timeout could otherwise run the whole call past its budget.
		int Budget() => (int)Math.Clamp((stopAt - DateTimeOffset.UtcNow).TotalMilliseconds, 1, Math.Max(1, timeoutMs));
		bool TimeIsUp() => DateTimeOffset.UtcNow >= stopAt;
		if (TimeIsUp()) {
			return new RecordDeleteDiagnosis { Complete = false };
		}
		RecordDeleteDiagnosis diagnosis = new() {
			Visible = ReadVisibility(entity, id, Budget(), out string processElementId),
			ProcessElementId = processElementId
		};
		List<(string Entity, string Column)> pairs;
		try {
			// The truncation is cached with the pairs: every diagnosis over a truncated list is incomplete, not
			// only the first one.
			if (!_pairsByEntity.TryGetValue(entity, out (List<(string Entity, string Column)> Pairs, bool Truncated) cached)) {
				if (TimeIsUp()) {
					diagnosis.Complete = false;
					return diagnosis;
				}
				cached = (ReadReferencePairs(entity, Budget(), out bool truncated), truncated);
				_pairsByEntity[entity] = cached;
			}
			pairs = cached.Pairs;
			if (cached.Truncated) {
				diagnosis.Complete = false;
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			diagnosis.Complete = false;
			return diagnosis;
		}
		if (pairs.Count > MaxReferencePairs) {
			diagnosis.Complete = false;
			pairs = pairs.Take(MaxReferencePairs).ToList();
		}
		// One select per referencing entity, its referencing columns OR-ed, instead of one per (entity, column) pair.
		foreach (IGrouping<string, (string Entity, string Column)> group in pairs.GroupBy(pair => pair.Entity, StringComparer.Ordinal)) {
			if (TimeIsUp()) {
				diagnosis.Complete = false;
				break;
			}
			ReadReferencingEntity(group.Key, group.Select(pair => pair.Column).ToList(), id, Budget, TimeIsUp, diagnosis);
		}
		return diagnosis;
	}

	/// <inheritdoc />
	public bool? Exists(string entity, string id, int timeoutMs) {
		try {
			return Select(ClassicEntitySchemaQuery.Query(entity, new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
				ClassicEntitySchemaQuery.Group(("byId", ClassicEntitySchemaQuery.Eq("Id", id, GuidDataValueType))), 1),
				timeoutMs).Count > 0;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	/// <inheritdoc />
	public bool IsLookup(string entity, int timeoutMs) {
		if (_lookups.TryGetValue(entity, out bool known)) {
			return known;
		}
		bool lookup;
		try {
			RuntimeEntitySchemaResult schema = GetSchema(entity, timeoutMs);
			lookup = schema is null
				|| Select(ClassicEntitySchemaQuery.Query("SysLookup", new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
					ClassicEntitySchemaQuery.Group(("bySchema", ClassicEntitySchemaQuery.Eq("SysEntitySchemaUId",
						schema.UId.ToString("D"), GuidDataValueType))), 1), timeoutMs).Count > 0
				|| (schema.ParentUId is { } parent && parent != Guid.Empty && IsLookupBase(parent, timeoutMs));
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// Not knowing is treated as a lookup: a cascade refused wrongly costs a call, a lookup cascaded costs data.
			lookup = true;
		}
		_lookups[entity] = lookup;
		return lookup;
	}

	private bool IsLookupBase(Guid parentUId, int timeoutMs) =>
		Select(ClassicEntitySchemaQuery.BuildSelectSchemaNamesByUId([parentUId.ToString("D")]), timeoutMs)
			.Any(row => LookupBaseSchemas.Contains(row["Name"]?.ToString() ?? string.Empty));

	// The referencing rows of one entity in a single select; a select that fails or fills its cap falls back to one
	// select per column, which also tells an unreadable column apart.
	private void ReadReferencingEntity(string entity, List<string> columns, string id, Func<int> budget,
		Func<bool> timeIsUp, RecordDeleteDiagnosis diagnosis) {
		if (columns.Count > 1) {
			int cap = (MaxIdsPerPair + 1) * columns.Count;
			JArray rows = TrySelectReferencing(entity, columns, id, cap, budget());
			if (rows is not null && rows.Count < cap) {
				string target = Guid.TryParse(id, out Guid parsed) ? parsed.ToString("D") : id;
				foreach (string column in columns) {
					List<string> ids = rows.Where(row => string.Equals(RunRecordProvenance.LookupValue(row[column]), target,
							StringComparison.OrdinalIgnoreCase))
						.Select(row => row["Id"]?.ToString()).Where(value => value is not null).ToList();
					ReferencingRows found = ids.Count == 0 ? null : Classify(entity, column, ids, budget());
					if (found is not null) {
						diagnosis.Referencing.Add(found);
					}
				}
				return;
			}
		}
		foreach (string column in columns) {
			if (timeIsUp()) {
				diagnosis.Complete = false;
				return;
			}
			ReferencingRows rows = ReadReferencingRows(entity, column, id, budget(), out bool readable);
			if (!readable) {
				diagnosis.Unchecked.Add($"{entity}.{column}");
				diagnosis.Complete = false;
			}
			if (rows is not null) {
				diagnosis.Referencing.Add(rows);
			}
		}
	}

	// Rows of the entity whose any listed column points at id; null when the select fails.
	private JArray TrySelectReferencing(string entity, List<string> columns, string id, int cap, int timeoutMs) {
		try {
			JObject selected = new() { ["Id"] = ClassicEntitySchemaQuery.Column("Id") };
			JObject any = new();
			for (int i = 0; i < columns.Count; i++) {
				selected[columns[i]] = ClassicEntitySchemaQuery.Column(columns[i]);
				any[$"ref{i}"] = ClassicEntitySchemaQuery.Eq(columns[i], id, GuidDataValueType);
			}
			return Select(ClassicEntitySchemaQuery.Query(entity, selected, ClassicEntitySchemaQuery.Group(("any", AnyOf(any))),
				cap), timeoutMs);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	// filterType 6 with logicalOperation 1: an OR group.
	private static JObject AnyOf(JObject items) => new() {
		["filterType"] = 6, ["logicalOperation"] = 1, ["isEnabled"] = true, ["items"] = items
	};

	// The record's own row: absent means missing or not readable by this user. ProcessElementId is read when the
	// entity has it (Activity does): a record a running process element still holds refuses deletion.
	private bool? ReadVisibility(string entity, string id, int timeoutMs, out string processElementId) {
		processElementId = null;
		try {
			bool hasProcessElement = (GetSchema(entity, timeoutMs)?.Columns ?? [])
				.Any(column => string.Equals(column.Name, ProcessElementColumn, StringComparison.Ordinal));
			JObject columns = new() { ["Id"] = ClassicEntitySchemaQuery.Column("Id") };
			if (hasProcessElement) {
				columns[ProcessElementColumn] = ClassicEntitySchemaQuery.Column(ProcessElementColumn);
			}
			JArray rows = Select(ClassicEntitySchemaQuery.Query(entity, columns,
				ClassicEntitySchemaQuery.Group(("byId", ClassicEntitySchemaQuery.Eq("Id", id, GuidDataValueType))), 1),
				timeoutMs);
			JToken row = rows.FirstOrDefault();
			string element = row?[ProcessElementColumn]?.ToString();
			if (Guid.TryParse(element, out Guid elementId) && elementId != Guid.Empty
					&& IsLiveProcessElement(elementId.ToString("D"), timeoutMs)) {
				processElementId = elementId.ToString("D");
			}
			return row is not null;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	// A completed element's data row is deleted, so a row that still exists is an element the run waits on. An
	// unreadable table counts as live: claiming a binding wrongly costs a hint, missing one costs the reason.
	private bool IsLiveProcessElement(string elementId, int timeoutMs) {
		try {
			return Select(ClassicEntitySchemaQuery.Query("SysProcessElementData",
				new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
				ClassicEntitySchemaQuery.Group(("byId", ClassicEntitySchemaQuery.Eq("Id", elementId, GuidDataValueType))), 1),
				timeoutMs).Count > 0;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return true;
		}
	}

	private RuntimeEntitySchemaResult GetSchema(string entity, int timeoutMs) {
		if (!_schemas.TryGetValue(entity, out RuntimeEntitySchemaResult schema)) {
			try {
				schema = schemaReader.GetByName(entity, Math.Max(1, timeoutMs));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				schema = null;
			}
			_schemas[entity] = schema;
		}
		return schema;
	}

	// Distinct (schema, column) pairs whose lookup targets the entity. The metadata keeps a row per package layer,
	// hence the distinct; DB views cannot block a delete and are dropped once their schema is read.
	private List<(string Entity, string Column)> ReadReferencePairs(string entity, int timeoutMs, out bool truncated) {
		JArray rows = Select(ClassicEntitySchemaQuery.Query("SysEntitySchemaReference",
			new JObject {
				["SchemaName"] = ClassicEntitySchemaQuery.Column("SysSchema.Name"),
				["ColumnName"] = ClassicEntitySchemaQuery.Column("ColumnName")
			},
			ClassicEntitySchemaQuery.Group(("target",
				ClassicEntitySchemaQuery.Eq("ReferenceSchema.Name", entity, TextDataValueType))),
			MaxReferenceMetadataRows), timeoutMs);
		truncated = rows.Count >= MaxReferenceMetadataRows;
		return rows
			.Select(row => (Entity: row["SchemaName"]?.ToString(), Column: row["ColumnName"]?.ToString()))
			.Where(pair => CreatedRecordWatcher.IsValidEntityName(pair.Entity)
				&& CreatedRecordWatcher.IsValidEntityName(pair.Column)
				&& !AuditColumns.Contains(pair.Column))
			.Distinct()
			.OrderBy(pair => pair.Entity, StringComparer.Ordinal)
			.ThenBy(pair => pair.Column, StringComparer.Ordinal)
			.ToList();
	}

	// readable is false when the column could not be checked: a timeout, a rights refusal, or a schema with no table
	// of its own. A virtual schema or a DB view cannot block a delete, so it counts as checked.
	private ReferencingRows ReadReferencingRows(string entity, string column, string id, int timeoutMs, out bool readable) {
		readable = true;
		JArray rows;
		try {
			rows = Select(ClassicEntitySchemaQuery.Query(entity,
				new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
				ClassicEntitySchemaQuery.Group(("ref", ClassicEntitySchemaQuery.Eq(column, id, GuidDataValueType))),
				MaxIdsPerPair + 1), timeoutMs);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			RuntimeEntitySchemaResult failed = GetSchema(entity, timeoutMs);
			readable = failed is not null && (failed.IsVirtual || failed.IsDBView);
			return null;
		}
		if (rows.Count == 0) {
			return null;
		}
		return Classify(entity, column, rows.Select(row => row["Id"]?.ToString()).Where(value => value is not null).ToList(),
			timeoutMs);
	}

	// Cascade when the platform deletes the row with the record, owned when its lookup is required, linked otherwise.
	// A DB view cannot block a delete, so it is not reported.
	private ReferencingRows Classify(string entity, string column, List<string> ids, int timeoutMs) {
		RuntimeEntitySchemaResult schema = GetSchema(entity, timeoutMs);
		if (schema?.IsDBView == true) {
			return null;
		}
		RuntimeEntitySchemaColumnResult lookup = schema?.Columns
			.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.Ordinal));
		string kind = ReferencingRows.LinkedKind;
		if (lookup?.IsCascade == true) {
			kind = ReferencingRows.CascadeKind;
		}
		else if (lookup?.IsRequired == true) {
			kind = ReferencingRows.OwnedKind;
		}
		return new ReferencingRows(entity, column, ids.Take(MaxIdsPerPair).ToList(), ids.Count > MaxIdsPerPair, kind);
	}

	private JArray Select(JObject query, int timeoutMs) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string json = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None),
			Math.Max(1, timeoutMs), maxAttempts: 1);
		return DataServiceSelectResponse.ReadRows(json);
	}

	/// <summary>
	/// The deepest <c>innererror.message</c> of an OData error body, fenced. Creatio puts the reason there and only
	/// "An error has occurred." in the headline - for example "Item cannot be deleted because it is being used in a
	/// process." for the activity of a parked Perform task.
	/// </summary>
	internal static string ReadInnerMessage(string response) {
		if (string.IsNullOrWhiteSpace(response)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(response);
			if (!document.RootElement.TryGetProperty("error", out JsonElement error)
					|| error.ValueKind != JsonValueKind.Object) {
				return null;
			}
			string inner = null;
			JsonElement current = error;
			for (int depth = 0; depth < 5; depth++) {
				if (!current.TryGetProperty("innererror", out JsonElement next)
						&& !current.TryGetProperty("internalexception", out next)) {
					break;
				}
				if (next.ValueKind != JsonValueKind.Object) {
					break;
				}
				if (next.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String
						&& !string.IsNullOrWhiteSpace(message.GetString())) {
					inner = message.GetString();
				}
				current = next;
			}
			if (inner is null) {
				return null;
			}
			string firstLine = ProcessRunMonitor.FirstLine(inner);
			return UntrustedText.Fenced(firstLine.Length > 500 ? firstLine[..500] : firstLine);
		}
		catch (System.Text.Json.JsonException) {
			return null;
		}
	}
}

/// <summary>Turns a refused delete and its diagnosis into one explanation an agent can act on.</summary>
internal static class DeleteRefusalText {

	/// <summary>Builds the explanation; it is never empty, because a refusal must always be explained.</summary>
	internal static string Explain(string entity, string id, RecordDeleteOutcome outcome, RecordDeleteDiagnosis diagnosis) {
		List<string> parts = [];
		if (outcome.ServerDetail is not null) {
			parts.Add($"The server's own reason: {outcome.ServerDetail}.");
		}
		if (diagnosis?.Visible == false) {
			parts.Add($"{entity}({id}) is not visible to this user: it does not exist, was already deleted, or "
				+ "the user has no read access to it.");
		}
		if (diagnosis?.ProcessElementId is not null) {
			parts.Add($"It is bound to process element {diagnosis.ProcessElementId} (ProcessElementId): a process "
				+ "run waiting on it - for example a Perform task that created this activity - keeps it from being "
				+ "deleted. Complete the task or cancel that run first; run-process with cleanup does this for the "
				+ "records a run created.");
		}
		List<ReferencingRows> blocking = diagnosis?.Referencing
			.Where(rows => rows.Kind != ReferencingRows.CascadeKind).ToList() ?? [];
		if (blocking.Count > 0) {
			parts.Add("Rows that still reference it: " + string.Join("; ", blocking.Select(Describe)) + ". "
				+ "Owned rows (a required lookup) go with cascade: true - call with dry-run: true first to list them; "
				+ "linked rows (an optional lookup) are never deleted by cascade: re-point or clear that lookup, or "
				+ "delete them separately if the user asks.");
		}
		if (diagnosis is { Complete: false }) {
			parts.Add("Not every referencing table could be checked"
				+ (diagnosis.Unchecked.Count > 0 ? $" ({string.Join(", ", diagnosis.Unchecked.Take(10))})" : string.Empty)
				+ ", so other referencing rows may exist.");
		}
		bool nothingFound = blocking.Count == 0 && diagnosis?.ProcessElementId is null && diagnosis?.Visible != false;
		if (parts.Count == 0 || (nothingFound && diagnosis is not { Complete: false })) {
			parts.Add("No referencing rows and no process binding were found, so the refusal comes from the user's "
				+ "delete rights on this object or record, or from an entity event handler that refuses it"
				+ (outcome.ServerDetail is null ? "; the server gave no reason." : " - the server's reason above says which."));
		}
		return string.Join(" ", parts);
	}

	private static string Describe(ReferencingRows rows) =>
		$"{rows.Entity}.{rows.Column} x{rows.Ids.Count}{(rows.More ? "+" : string.Empty)} ({rows.Kind})";
}
