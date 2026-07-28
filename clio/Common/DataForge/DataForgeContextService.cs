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
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = maintenanceClient.GetFullStatus();
		cancellationToken.ThrowIfCancellationRequested();

		// Fail fast when the subsystem is not online. Every read below would throw the SAME underlying error,
		// once per search term, so an environment without Data Forge configured produced a wall of identical
		// warnings that buried the one fact the caller needed — the subsystem is unavailable here (issue #948).
		if (!health.Liveness) {
			return BuildUnavailableResult(health, status);
		}

		List<string> tableTerms = NormalizeTerms(request.CandidateTerms, request.RequirementSummary);
		List<SimilarTableResult> similarTables = FindSimilarTables(tableTerms, warnings, cancellationToken);

		List<string> lookupTerms = NormalizeTerms(request.LookupHints, null);
		List<SimilarLookupResult> similarLookups = FindSimilarLookups(lookupTerms, warnings, cancellationToken);

		Dictionary<string, IReadOnlyList<string>> relations = GetRelations(
			request.RelationPairs,
			warnings,
			cancellationToken);

		List<SimilarTableResult> distinctTables = GetDistinctTables(similarTables);

		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = GetColumns(
			distinctTables,
			warnings,
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
	/// Builds the single, structured "Data Forge is unavailable here" result used instead of fanning out reads
	/// that are all guaranteed to fail the same way.
	/// </summary>
	/// <param name="health">The health probe result that reported the subsystem offline.</param>
	/// <param name="status">The maintenance status accompanying it, whose message carries the platform diagnosis.</param>
	/// <returns>An empty aggregation carrying one actionable warning and honest (all-false) coverage.</returns>
	private static DataForgeContextAggregationResult BuildUnavailableResult(
		DataForgeHealthResult health,
		DataForgeMaintenanceStatusResult status) {
		// The status message is the platform's own diagnosis (for example a missing service base URI on the
		// Creatio side). It is surfaced once, prefixed so the caller can see WHERE the limitation is: Data
		// Forge is an optional Creatio-side subsystem, and clio has no endpoint of its own to configure.
		string diagnosis = string.IsNullOrWhiteSpace(status.Error)
			? $"status {status.Status}"
			: $"status {status.Status}: {status.Error}";
		return new DataForgeContextAggregationResult(
			health.CorrelationId,
			[$"dataforge:unavailable-on-environment:{diagnosis}. Data Forge enrichment is skipped; it is an "
				+ "optional Creatio-side subsystem and is not configured on this environment. This never blocks "
				+ "schema work — use find-entity-schema for discovery and explicit read-back for verification."],
			health,
			status,
			[],
			[],
			new Dictionary<string, IReadOnlyList<string>>(),
			new Dictionary<string, IReadOnlyList<DataForgeColumnResult>>(),
			new DataForgeCoverage(Health: false, Tables: false, Lookups: false, Relations: false, Columns: false));
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
		CancellationToken cancellationToken) {
		List<SimilarTableResult> similarTables = [];
		foreach (string term in tableTerms) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				similarTables.AddRange(readClient.FindSimilarTables(term));
			}
			catch (Exception ex) {
				warnings.Add($"tables:{term}:{ex.Message}");
			}
		}

		return similarTables;
	}

	private List<SimilarLookupResult> FindSimilarLookups(
		IReadOnlyList<string> lookupTerms,
		List<string> warnings,
		CancellationToken cancellationToken) {
		List<SimilarLookupResult> similarLookups = [];
		foreach (string hint in lookupTerms) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				similarLookups.AddRange(readClient.FindSimilarLookups(hint));
			}
			catch (Exception ex) {
				warnings.Add($"lookups:{hint}:{ex.Message}");
			}
		}

		return similarLookups;
	}

	private Dictionary<string, IReadOnlyList<string>> GetRelations(
		IReadOnlyList<DataForgeRelationPair>? relationPairs,
		List<string> warnings,
		CancellationToken cancellationToken) {
		Dictionary<string, IReadOnlyList<string>> relations = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataForgeRelationPair pair in relationPairs?.Where(HasRelationTables) ?? []) {
			cancellationToken.ThrowIfCancellationRequested();
			string key = $"{pair.SourceTable}->{pair.TargetTable}";
			try {
				relations[key] = readClient.GetTableRelationships(pair.SourceTable, pair.TargetTable);
			}
			catch (Exception ex) {
				warnings.Add($"relations:{key}:{ex.Message}");
			}
		}

		return relations;
	}

	private Dictionary<string, IReadOnlyList<DataForgeColumnResult>> GetColumns(
		IReadOnlyList<SimilarTableResult> distinctTables,
		List<string> warnings,
		CancellationToken cancellationToken) {
		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = new(StringComparer.OrdinalIgnoreCase);
		foreach (string tableName in distinctTables.Select(table => table.Name)) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				RuntimeEntitySchemaResult runtimeSchema = runtimeEntitySchemaReader.GetByName(tableName);
				columns[tableName] = DataForgeRuntimeSchemaMapper.MapColumns(runtimeSchema);
			}
			catch (Exception ex) {
				warnings.Add($"columns:{tableName}:{ex.Message}");
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
