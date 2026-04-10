using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
	/// <param name="configRequest">Resolved Data Forge configuration request.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>An aggregated Data Forge context payload prior to MCP envelope mapping.</returns>
	Task<DataForgeContextAggregationResult> GetContextAsync(
		DataForgeContextRequest request,
		DataForgeConfigRequest configRequest,
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
	IDataForgeClient dataForgeClient,
	IDataForgeMaintenanceClient maintenanceClient,
	IRuntimeEntitySchemaReader runtimeEntitySchemaReader)
	: IDataForgeContextService {
	public async Task<DataForgeContextAggregationResult> GetContextAsync(
		DataForgeContextRequest request,
		DataForgeConfigRequest configRequest,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(configRequest);

		List<string> warnings = [];
		DataForgeHealthResult health = await dataForgeClient.CheckHealthAsync(configRequest, cancellationToken);
		DataForgeMaintenanceStatusResult status = maintenanceClient.GetStatus();

		List<string> tableTerms = NormalizeTerms(request.CandidateTerms, request.RequirementSummary);
		List<SimilarTableResult> similarTables = await FindSimilarTablesAsync(tableTerms, configRequest, warnings, cancellationToken);

		List<string> lookupTerms = NormalizeTerms(request.LookupHints, null);
		List<SimilarLookupResult> similarLookups = await FindSimilarLookupsAsync(lookupTerms, configRequest, warnings, cancellationToken);

		Dictionary<string, IReadOnlyList<string>> relations = await GetRelationsAsync(
			request.RelationPairs,
			configRequest,
			warnings,
			cancellationToken);

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

	private async Task<List<SimilarTableResult>> FindSimilarTablesAsync(
		IReadOnlyList<string> tableTerms,
		DataForgeConfigRequest configRequest,
		List<string> warnings,
		CancellationToken cancellationToken) {
		List<SimilarTableResult> similarTables = [];
		foreach (string term in tableTerms) {
			try {
				similarTables.AddRange(await dataForgeClient.FindSimilarTablesAsync(term, null, configRequest, cancellationToken));
			}
			catch (Exception ex) {
				warnings.Add($"tables:{term}:{ex.Message}");
			}
		}

		return similarTables;
	}

	private async Task<List<SimilarLookupResult>> FindSimilarLookupsAsync(
		IReadOnlyList<string> lookupTerms,
		DataForgeConfigRequest configRequest,
		List<string> warnings,
		CancellationToken cancellationToken) {
		List<SimilarLookupResult> similarLookups = [];
		foreach (string hint in lookupTerms) {
			try {
				similarLookups.AddRange(await dataForgeClient.FindSimilarLookupsAsync(hint, null, null, configRequest, cancellationToken));
			}
			catch (Exception ex) {
				warnings.Add($"lookups:{hint}:{ex.Message}");
			}
		}

		return similarLookups;
	}

	private async Task<Dictionary<string, IReadOnlyList<string>>> GetRelationsAsync(
		IReadOnlyList<DataForgeRelationPair>? relationPairs,
		DataForgeConfigRequest configRequest,
		List<string> warnings,
		CancellationToken cancellationToken) {
		Dictionary<string, IReadOnlyList<string>> relations = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataForgeRelationPair pair in relationPairs?.Where(HasRelationTables) ?? []) {
			string key = $"{pair.SourceTable}->{pair.TargetTable}";
			try {
				relations[key] = await dataForgeClient.GetTableRelationshipsAsync(
					pair.SourceTable,
					pair.TargetTable,
					null,
					configRequest,
					cancellationToken);
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
