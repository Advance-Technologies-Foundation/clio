using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Common.EntitySchema;

namespace Clio.Common.DataForge;

/// <summary>
/// Aggregates Data Forge context reads across service health, similar tables, lookups, relations, and runtime columns.
/// </summary>
public interface IDataForgeContextService {
	/// <summary>
	/// Builds an aggregated Data Forge context payload for the requested search terms and relation pairs.
	/// </summary>
	/// <param name="request">Context aggregation request.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>An aggregated Data Forge context payload prior to MCP envelope mapping.</returns>
	DataForgeContextAggregationResult GetContext(
		DataForgeContextRequest request,
		CancellationToken cancellationToken = default);
}

internal static class DataForgeRuntimeSchemaMapper {
	private static readonly IReadOnlyDictionary<int, string> DataValueTypeNames = new Dictionary<int, string> {
		[0] = "Guid",
		[1] = "Text",
		[4] = "Integer",
		[5] = "Float",
		[6] = "Money",
		[7] = "DateTime",
		[8] = "Date",
		[9] = "Time",
		[10] = "Lookup",
		[11] = "Enum",
		[12] = "Boolean",
		[13] = "Blob",
		[18] = "Color",
		[23] = "HASH_TEXT",
		[24] = "SECURE_TEXT",
		[27] = "SHORT_TEXT",
		[28] = "MEDIUM_TEXT",
		[29] = "MAXSIZE_TEXT",
		[30] = "LONG_TEXT",
		[42] = "PHONE_TEXT",
		[43] = "RICH_TEXT",
		[44] = "WEB_TEXT",
		[45] = "EMAIL_TEXT"
	};

	internal static IReadOnlyList<DataForgeColumnResult> MapColumns(RuntimeEntitySchemaResult schema) {
		return schema.Columns
			.Where(column => !column.IsInherited)
			.Select(column => new DataForgeColumnResult(
				column.Name,
				column.Caption,
				column.Description,
				ResolveDataType(column.DataValueType),
				column.IsRequired,
				column.ReferenceSchemaName))
			.OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static string ResolveDataType(int dataValueType) {
		return DataValueTypeNames.TryGetValue(dataValueType, out string? dataTypeName)
			? dataTypeName
			: "Text";
	}
}

internal sealed class DataForgeContextService(
	IDataForgeReadClient readClient,
	IDataForgeMaintenanceClient maintenanceClient,
	IRuntimeEntitySchemaReader runtimeEntitySchemaReader)
	: IDataForgeContextService {
	public DataForgeContextAggregationResult GetContext(
		DataForgeContextRequest request,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		List<string> warnings = [];
		// Tracks the collapsed state per distinct (category, message) cause so repeats collapse into one warning.
		Dictionary<string, CollapsedWarning> firstIndexByCause = new(StringComparer.Ordinal);
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = maintenanceClient.GetFullStatus();
		cancellationToken.ThrowIfCancellationRequested();

		List<string> tableTerms = NormalizeTerms(request.CandidateTerms, request.RequirementSummary);
		List<SimilarTableResult> similarTables = FindSimilarTables(tableTerms, warnings, firstIndexByCause, cancellationToken);

		List<string> lookupTerms = NormalizeTerms(request.LookupHints, null);
		List<SimilarLookupResult> similarLookups = FindSimilarLookups(lookupTerms, warnings, firstIndexByCause, cancellationToken);

		Dictionary<string, IReadOnlyList<string>> relations = GetRelations(
			request.RelationPairs,
			warnings,
			firstIndexByCause,
			cancellationToken);

		List<SimilarTableResult> distinctTables = GetDistinctTables(similarTables);

		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = GetColumns(
			distinctTables,
			warnings,
			firstIndexByCause,
			cancellationToken);

		List<SimilarLookupResult> distinctLookups = GetDistinctLookups(similarLookups);

		DataForgeCoverage coverage = CreateCoverage(
			tableTerms,
			lookupTerms,
			request.RelationPairs,
			distinctTables,
			distinctLookups,
			relations,
			columns);

		return new DataForgeContextAggregationResult(
			health.CorrelationId,
			warnings,
			health,
			status,
			distinctTables,
			distinctLookups,
			relations,
			columns,
			coverage);
	}

	/// <summary>
	/// Records a per-item read failure, collapsing repeats of the SAME underlying error into one warning that
	/// names the affected items instead of emitting one line per item.
	/// </summary>
	/// <remarks>
	/// When the Data Forge subsystem is unconfigured on an environment every read fails identically, so an
	/// N-term request produced N copies of the same message and buried the one fact the caller needed
	/// (issue #948). Collapsing at the reporting layer — rather than skipping the reads on a health probe —
	/// is deliberate: the probe's liveness is NOT a reliable predictor of whether the reads work. The
	/// sandbox proves it, running with liveness false while table-column reads (which go through the
	/// runtime schema reader, not Data Forge) succeed, so short-circuiting on it would discard real results.
	/// </remarks>
	/// <param name="warnings">The accumulating warning list, mutated in place.</param>
	/// <param name="firstIndexByCause">Maps an already-seen (category, message) cause to its collapsed state.</param>
	/// <param name="category">Read category (<c>tables</c>, <c>lookups</c>, <c>relations</c>, <c>columns</c>).</param>
	/// <param name="item">The term, pair key, or table name the read was for.</param>
	/// <param name="message">The failure message used as the dedup key.</param>
	private static void AddDedupedWarning(
		List<string> warnings,
		Dictionary<string, CollapsedWarning> firstIndexByCause,
		string category,
		string item,
		string message) {
		// NUL-joined so a message containing ':' cannot collide with another category's key.
		string causeKey = $"{category}\0{message}";
		if (!firstIndexByCause.TryGetValue(causeKey, out CollapsedWarning? collapsed)) {
			// The first occurrence keeps the ORIGINAL `category:item:message` shape byte for byte, so a
			// single-failure payload is unchanged for existing consumers; only repeats are collapsed.
			collapsed = new CollapsedWarning(warnings.Count, category, item, message);
			firstIndexByCause[causeKey] = collapsed;
			warnings.Add(collapsed.Render());
			return;
		}
		// Collapsed state is tracked STRUCTURALLY and the line re-rendered from it. Inferring "already
		// collapsed" by parsing the emitted line (`EndsWith(")") && Contains(" (also: ")`) could not tell
		// clio's own marker from the same substring occurring inside an exception message, so a cause like
		// `Load failed (also: check config)` had the next item spliced into the message's own parenthetical.
		collapsed.AlsoItems.Add(item);
		warnings[collapsed.Index] = collapsed.Render();
	}

	/// <summary>
	/// Structural state of one collapsed warning: where it sits in the warning list, the cause it reports, and
	/// every item that hit that same cause. The emitted line is always rendered from this, never parsed back.
	/// </summary>
	private sealed class CollapsedWarning(int index, string category, string firstItem, string message) {
		public int Index { get; } = index;

		public List<string> AlsoItems { get; } = [];

		public string Render() {
			string head = $"{category}:{firstItem}:{message}";
			return AlsoItems.Count == 0
				? head
				: $"{head} (also: {string.Join(", ", AlsoItems)})";
		}
	}

	private static DataForgeCoverage CreateCoverage(
		IReadOnlyCollection<string> tableTerms,
		IReadOnlyCollection<string> lookupTerms,
		IReadOnlyList<DataForgeRelationPair>? relationPairs,
		IReadOnlyCollection<SimilarTableResult> distinctTables,
		IReadOnlyCollection<SimilarLookupResult> distinctLookups,
		IReadOnlyDictionary<string, IReadOnlyList<string>> relations,
		IReadOnlyDictionary<string, IReadOnlyList<DataForgeColumnResult>> columns) {
		return new DataForgeCoverage(
			Health: true,
			Tables: HasMatchesOrNoTerms(distinctTables.Count, tableTerms.Count),
			Lookups: HasMatchesOrNoTerms(distinctLookups.Count, lookupTerms.Count),
			Relations: HasResolvedRelationsOrNoPairs(relations.Count, relationPairs),
			Columns: columns.Count == distinctTables.Count);
	}

	private static bool HasMatchesOrNoTerms(int matchCount, int termCount) {
		return matchCount > 0 || termCount == 0;
	}

	private static bool HasResolvedRelationsOrNoPairs(int relationCount, IReadOnlyList<DataForgeRelationPair>? relationPairs) {
		return relationCount > 0 || !(relationPairs?.Any() ?? false);
	}

	private static List<string> NormalizeTerms(IEnumerable<string>? terms, string? fallback) {
		List<string> values = (terms ?? [])
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(value => value.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (values.Count == 0 && !string.IsNullOrWhiteSpace(fallback)) {
			values.Add(fallback.Trim());
		}

		return values;
	}

	private List<SimilarTableResult> FindSimilarTables(
		IReadOnlyList<string> tableTerms,
		List<string> warnings,
		Dictionary<string, CollapsedWarning> firstIndexByCause,
		CancellationToken cancellationToken) {
		List<SimilarTableResult> similarTables = [];
		foreach (string term in tableTerms) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				similarTables.AddRange(readClient.FindSimilarTables(term));
			}
			catch (Exception ex) {
				AddDedupedWarning(warnings, firstIndexByCause, "tables", term, ex.Message);
			}
		}

		return similarTables;
	}

	private List<SimilarLookupResult> FindSimilarLookups(
		IReadOnlyList<string> lookupTerms,
		List<string> warnings,
		Dictionary<string, CollapsedWarning> firstIndexByCause,
		CancellationToken cancellationToken) {
		List<SimilarLookupResult> similarLookups = [];
		foreach (string hint in lookupTerms) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				similarLookups.AddRange(readClient.FindSimilarLookups(hint));
			}
			catch (Exception ex) {
				AddDedupedWarning(warnings, firstIndexByCause, "lookups", hint, ex.Message);
			}
		}

		return similarLookups;
	}

	private Dictionary<string, IReadOnlyList<string>> GetRelations(
		IReadOnlyList<DataForgeRelationPair>? relationPairs,
		List<string> warnings,
		Dictionary<string, CollapsedWarning> firstIndexByCause,
		CancellationToken cancellationToken) {
		Dictionary<string, IReadOnlyList<string>> relations = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataForgeRelationPair pair in relationPairs?.Where(HasRelationTables) ?? []) {
			cancellationToken.ThrowIfCancellationRequested();
			string key = $"{pair.SourceTable}->{pair.TargetTable}";
			try {
				relations[key] = readClient.GetTableRelationships(pair.SourceTable, pair.TargetTable);
			}
			catch (Exception ex) {
				AddDedupedWarning(warnings, firstIndexByCause, "relations", key, ex.Message);
			}
		}

		return relations;
	}

	private Dictionary<string, IReadOnlyList<DataForgeColumnResult>> GetColumns(
		IReadOnlyList<SimilarTableResult> distinctTables,
		List<string> warnings,
		Dictionary<string, CollapsedWarning> firstIndexByCause,
		CancellationToken cancellationToken) {
		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = new(StringComparer.OrdinalIgnoreCase);
		foreach (string tableName in distinctTables.Select(table => table.Name)) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				RuntimeEntitySchemaResult runtimeSchema = runtimeEntitySchemaReader.GetByName(tableName);
				columns[tableName] = DataForgeRuntimeSchemaMapper.MapColumns(runtimeSchema);
			}
			catch (Exception ex) {
				AddDedupedWarning(warnings, firstIndexByCause, "columns", tableName, ex.Message);
			}
		}

		return columns;
	}

	private static List<SimilarTableResult> GetDistinctTables(IEnumerable<SimilarTableResult> similarTables) {
		return similarTables
			.GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<SimilarLookupResult> GetDistinctLookups(IEnumerable<SimilarLookupResult> similarLookups) {
		return similarLookups
			.GroupBy(lookup => $"{lookup.SchemaName}:{lookup.Value}", StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(lookup => lookup.SchemaName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(lookup => lookup.Value, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static bool HasRelationTables(DataForgeRelationPair pair) {
		return !string.IsNullOrWhiteSpace(pair.SourceTable) && !string.IsNullOrWhiteSpace(pair.TargetTable);
	}
}
