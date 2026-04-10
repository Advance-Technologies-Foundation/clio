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

		List<SimilarTableResult> similarTables = [];
		List<string> tableTerms = NormalizeTerms(request.CandidateTerms, request.RequirementSummary);
		foreach (string term in tableTerms) {
			try {
				similarTables.AddRange(await dataForgeClient.FindSimilarTablesAsync(term, null, configRequest, cancellationToken));
			}
			catch (Exception ex) {
				warnings.Add($"tables:{term}:{ex.Message}");
			}
		}

		List<SimilarLookupResult> similarLookups = [];
		List<string> lookupTerms = NormalizeTerms(request.LookupHints, null);
		foreach (string hint in lookupTerms) {
			try {
				similarLookups.AddRange(await dataForgeClient.FindSimilarLookupsAsync(hint, null, null, configRequest, cancellationToken));
			}
			catch (Exception ex) {
				warnings.Add($"lookups:{hint}:{ex.Message}");
			}
		}

		Dictionary<string, IReadOnlyList<string>> relations = new(StringComparer.OrdinalIgnoreCase);
		foreach (DataForgeRelationPair pair in request.RelationPairs ?? []) {
			if (string.IsNullOrWhiteSpace(pair.SourceTable) || string.IsNullOrWhiteSpace(pair.TargetTable)) {
				continue;
			}

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

		List<SimilarTableResult> distinctTables = similarTables
			.GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		Dictionary<string, IReadOnlyList<DataForgeColumnResult>> columns = new(StringComparer.OrdinalIgnoreCase);
		foreach (SimilarTableResult table in distinctTables) {
			try {
				RuntimeEntitySchemaResult runtimeSchema = runtimeEntitySchemaReader.GetByName(table.Name);
				columns[table.Name] = DataForgeRuntimeSchemaMapper.MapColumns(runtimeSchema);
			}
			catch (Exception ex) {
				warnings.Add($"columns:{table.Name}:{ex.Message}");
			}
		}

		List<SimilarLookupResult> distinctLookups = similarLookups
			.GroupBy(lookup => $"{lookup.SchemaName}:{lookup.Value}", StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(lookup => lookup.SchemaName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(lookup => lookup.Value, StringComparer.OrdinalIgnoreCase)
			.ToList();

		DataForgeCoverage coverage = new(
			Health: true,
			Tables: distinctTables.Count > 0 || tableTerms.Count == 0,
			Lookups: distinctLookups.Count > 0 || lookupTerms.Count == 0,
			Relations: relations.Count > 0 || !(request.RelationPairs?.Any() ?? false),
			Columns: columns.Count == distinctTables.Count);

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
}
