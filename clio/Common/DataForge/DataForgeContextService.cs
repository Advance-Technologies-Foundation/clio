using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

internal sealed class DataForgeContextService(
	IDataForgeReadClient readClient,
	IDataForgeMaintenanceClient maintenanceClient)
	: IDataForgeContextService {
	public DataForgeContextAggregationResult GetContext(
		DataForgeContextRequest request,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		List<string> warnings = [];
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = maintenanceClient.GetFullStatus();

		List<string> tableTerms = NormalizeTerms(request.CandidateTerms, request.RequirementSummary);
		List<SimilarTableResult> similarTables = FindSimilarTables(tableTerms, warnings);

		List<string> lookupTerms = NormalizeTerms(request.LookupHints, null);
		List<SimilarLookupResult> similarLookups = FindSimilarLookups(lookupTerms, warnings);

		Dictionary<string, IReadOnlyList<string>> relations = GetRelations(
			request.RelationPairs,
			warnings);

		List<SimilarTableResult> distinctTables = GetDistinctTables(similarTables);

		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = GetColumns(distinctTables, warnings);

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
		List<string> warnings) {
		List<SimilarTableResult> similarTables = [];
		foreach (string term in tableTerms) {
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
		List<string> warnings) {
		List<SimilarLookupResult> similarLookups = [];
		foreach (string hint in lookupTerms) {
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
		List<string> warnings) {
		Dictionary<string, IReadOnlyList<string>> relations = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataForgeRelationPair pair in relationPairs?.Where(HasRelationTables) ?? []) {
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
		List<string> warnings) {
		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = new(StringComparer.OrdinalIgnoreCase);
		foreach (string tableName in distinctTables.Select(table => table.Name)) {
			try {
				columns[tableName] = readClient.GetTableColumnsDetails(tableName);
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
