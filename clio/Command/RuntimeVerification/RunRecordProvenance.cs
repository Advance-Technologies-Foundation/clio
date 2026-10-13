using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.EntitySchema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Ties records that appeared in a time window to the action that caused them. Time alone ties nothing: another
/// session, the same login included, can create records in the same window, so a cleanup deletes only what this
/// service can tie.
/// </summary>
public interface IRunRecordProvenance {

	/// <summary>
	/// Returns which of <paramref name="candidates"/> provably belong to the action: the <paramref name="seedIds"/>, a
	/// record whose <c>ProcessElementId</c> names an element of one of <paramref name="runIds"/> (or of their
	/// sub-processes), and any record whose lookup points at a record already tied.
	/// </summary>
	/// <param name="candidates">The records to classify; only these are read.</param>
	/// <param name="seedIds">Ids already known to come from the action (for example a record it created itself).</param>
	/// <param name="runIds">Process runs (SysProcessLog ids) the action started.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <param name="stopAt">No request starts past this instant; what is not read by then stays untied.</param>
	/// <returns>The tied ids, the run holding each record that names one, and why some records could not be checked.</returns>
	RecordProvenance Resolve(IReadOnlyList<CreatedRecord> candidates, IReadOnlyCollection<string> seedIds,
		IReadOnlyCollection<string> runIds, int timeoutMs, DateTimeOffset? stopAt = null);
}

/// <summary>Outcome of <see cref="IRunRecordProvenance.Resolve"/>.</summary>
public sealed class RecordProvenance {

	/// <summary>Ids tied to the action: the seeds, records its runs hold, and records pointing at those.</summary>
	public HashSet<string> Tied { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Per record id, the run whose live element data its <c>ProcessElementId</c> names.</summary>
	public Dictionary<string, string> HoldingRun { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>One line per entity or step that could not be read.</summary>
	public List<string> Warnings { get; } = [];
}

internal sealed class RunRecordProvenance(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRuntimeEntitySchemaReader schemaReader)
	: IRunRecordProvenance {

	private const int GuidDataValueType = 0;
	private const int MaxSubProcesses = 100;
	internal const string ProcessElementColumn = "ProcessElementId";
	// Every row has them, and they point at the user, never at what the action created.
	private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal) { "CreatedBy", "ModifiedBy" };

	/// <inheritdoc />
	public RecordProvenance Resolve(IReadOnlyList<CreatedRecord> candidates, IReadOnlyCollection<string> seedIds,
		IReadOnlyCollection<string> runIds, int timeoutMs, DateTimeOffset? stopAt = null) {
		RecordProvenance provenance = new();
		provenance.Tied.UnionWith(seedIds ?? []);
		List<CreatedRecord> records = (candidates ?? []).Where(record => record?.Id is not null).ToList();
		Dictionary<string, HashSet<string>> pointsAt = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> elementOf = new(StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, CreatedRecord> entity in records.GroupBy(record => record.Entity, StringComparer.OrdinalIgnoreCase)) {
			ReadEntity(entity.Key, entity.Select(record => record.Id).ToList(), timeoutMs, stopAt, pointsAt, elementOf,
				provenance);
		}
		ReadHoldingRuns(elementOf, timeoutMs, stopAt, provenance);
		HashSet<string> runs = ExpandRuns(runIds ?? [], timeoutMs, stopAt, provenance);
		provenance.Tied.UnionWith(provenance.HoldingRun.Where(pair => runs.Contains(pair.Value)).Select(pair => pair.Key));
		List<string> linked;
		do {
			linked = records.Select(record => record.Id)
				.Where(id => !provenance.Tied.Contains(id) && pointsAt.TryGetValue(id, out HashSet<string> values)
					&& values.Overlaps(provenance.Tied))
				.ToList();
			provenance.Tied.UnionWith(linked);
		} while (linked.Count > 0);
		return provenance;
	}

	// One select per entity: its lookup values (audit columns excluded) and, when it has one, its ProcessElementId.
	// An entity that cannot be read ties nothing, so its records stay untied.
	private void ReadEntity(string entity, IReadOnlyList<string> ids, int timeoutMs, DateTimeOffset? stopAt,
		Dictionary<string, HashSet<string>> pointsAt, Dictionary<string, string> elementOf, RecordProvenance provenance) {
		if (TimeIsUp(stopAt)) {
			provenance.Warnings.Add($"The new {entity} records could not be tied to the run: the time ran out first.");
			return;
		}
		try {
			RuntimeEntitySchemaResult schema = schemaReader.GetByName(entity, CreatedRecordWatcher.Budget(timeoutMs, stopAt));
			List<string> lookups = schema.Columns
				.Where(column => !string.IsNullOrWhiteSpace(column.ReferenceSchemaName) && !AuditColumns.Contains(column.Name)
					&& CreatedRecordWatcher.IsValidEntityName(column.Name))
				.Select(column => column.Name)
				.ToList();
			bool hasElement = schema.Columns.Any(column => string.Equals(column.Name, ProcessElementColumn, StringComparison.Ordinal));
			List<string> columns = hasElement ? [.. lookups, ProcessElementColumn] : lookups;
			if (columns.Count == 0) {
				return;
			}
			if (TimeIsUp(stopAt)) {
				provenance.Warnings.Add($"The new {entity} records could not be tied to the run: the time ran out first.");
				return;
			}
			foreach (JToken row in SelectByIds(entity, columns, ids, timeoutMs, stopAt)) {
				string id = row["Id"]?.ToString() ?? string.Empty;
				pointsAt[id] = lookups.Select(column => LookupValue(row[column]))
					.Where(value => value is not null)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				string element = hasElement ? LookupValue(row[ProcessElementColumn]) : null;
				if (element is not null) {
					elementOf[id] = element;
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			provenance.Warnings.Add($"The new {entity} records could not be tied to the run: "
				+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
		}
	}

	// A record's ProcessElementId names an element data row while that element is live; the row names its run. The row
	// is deleted when the element completes, so a run must be read for this before it is cancelled.
	private void ReadHoldingRuns(Dictionary<string, string> elementOf, int timeoutMs, DateTimeOffset? stopAt,
		RecordProvenance provenance) {
		if (elementOf.Count == 0) {
			return;
		}
		if (TimeIsUp(stopAt)) {
			provenance.Warnings.Add("The runs holding the new records could not be read: the time ran out first.");
			return;
		}
		try {
			Dictionary<string, string> runOf = SelectByIds("SysProcessElementData", ["SysProcess"],
					elementOf.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), timeoutMs, stopAt)
				.Select(row => (Element: row["Id"]?.ToString(), Run: LookupValue(row["SysProcess"])))
				.Where(pair => pair.Element is not null && pair.Run is not null)
				.GroupBy(pair => pair.Element, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.First().Run, StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> pair in elementOf) {
				if (runOf.TryGetValue(pair.Value, out string run)) {
					provenance.HoldingRun[pair.Key] = run;
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			provenance.Warnings.Add("The runs holding the new records could not be read: "
				+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
		}
	}

	// The runs and their sub-processes: a Perform task inside a sub-process names the sub-process's run.
	private HashSet<string> ExpandRuns(IReadOnlyCollection<string> runIds, int timeoutMs, DateTimeOffset? stopAt,
		RecordProvenance provenance) {
		HashSet<string> runs = runIds.Where(id => Guid.TryParse(id, out Guid run) && run != Guid.Empty)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (runs.Count == 0 || provenance.HoldingRun.Count == 0
				|| provenance.HoldingRun.Values.All(runs.Contains) || TimeIsUp(stopAt)) {
			return runs;
		}
		try {
			JObject query = ClassicEntitySchemaQuery.Query("SysProcessLog",
				new JObject { ["Id"] = ClassicEntitySchemaQuery.Column("Id") },
				ClassicEntitySchemaQuery.Group(("byRoot", ClassicEntitySchemaQuery.InFilter("RootId", runs, GuidDataValueType))),
				MaxSubProcesses);
			runs.UnionWith(Select(query, timeoutMs, stopAt).Select(row => row["Id"]?.ToString()).Where(id => id is not null));
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			provenance.Warnings.Add("The run's sub-processes could not be read, so a record a sub-process holds is not tied "
				+ "to the run: " + (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
		}
		return runs;
	}

	private static bool TimeIsUp(DateTimeOffset? stopAt) => stopAt is not null && DateTimeOffset.UtcNow >= stopAt;

	private JArray SelectByIds(string entity, IEnumerable<string> columns, IReadOnlyCollection<string> ids, int timeoutMs,
		DateTimeOffset? stopAt) {
		JObject selected = new() { ["Id"] = ClassicEntitySchemaQuery.Column("Id") };
		foreach (string column in columns) {
			selected[column] = ClassicEntitySchemaQuery.Column(column);
		}
		return Select(ClassicEntitySchemaQuery.Query(entity, selected,
			ClassicEntitySchemaQuery.Group(("ids", ClassicEntitySchemaQuery.InFilter("Id", ids, GuidDataValueType))),
			ids.Count), timeoutMs, stopAt);
	}

	private JArray Select(JObject query, int timeoutMs, DateTimeOffset? stopAt) {
		string json = applicationClient.ExecutePostRequest(serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			query.ToString(Formatting.None), CreatedRecordWatcher.Budget(timeoutMs, stopAt), maxAttempts: 1);
		return DataServiceSelectResponse.ReadRows(json);
	}

	internal static string LookupValue(JToken value) {
		string text = value is JObject lookup ? lookup["value"]?.ToString() : value?.ToString();
		return Guid.TryParse(text, out Guid id) && id != Guid.Empty ? id.ToString("D") : null;
	}
}
