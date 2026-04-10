using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.DataForge;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides Data Forge enrichment context for schema creation and sync tools.
/// Collects similar-table, lookup, and column hints from Data Forge before executing
/// schema mutations so the AI has structured context about existing data structures.
/// </summary>
public interface ISchemaEnrichmentService {
	/// <summary>
	/// Builds a best-effort Data Forge enrichment result from the supplied candidate terms.
	/// Always returns a result — degrades gracefully when Data Forge is unavailable.
	/// </summary>
	/// <param name="environmentName">Target Creatio environment name.</param>
	/// <param name="candidateTerms">Schema names and titles used as table-search terms.</param>
	/// <param name="lookupHints">Optional terms used for lookup-value search.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Structured Data Forge enrichment diagnostics and compact context summary.</returns>
	Task<ApplicationDataForgeResult> EnrichAsync(
		string? environmentName,
		IReadOnlyList<string> candidateTerms,
		IReadOnlyList<string>? lookupHints = null,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Default Data Forge enrichment flow for schema creation and sync tools.
/// </summary>
public sealed class SchemaEnrichmentService(IToolCommandResolver commandResolver) : ISchemaEnrichmentService {
	private const string DefaultScope = "use_enrichment";

	/// <inheritdoc />
	public async Task<ApplicationDataForgeResult> EnrichAsync(
		string? environmentName,
		IReadOnlyList<string> candidateTerms,
		IReadOnlyList<string>? lookupHints = null,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(candidateTerms);
		try {
			DataForgeTargetOptions options = new() {
				Environment = environmentName,
				AllowSysSettingsAuthFallback = true,
				Scope = DefaultScope
			};
			IDataForgeContextService contextService = commandResolver.Resolve<IDataForgeContextService>(options);
			DataForgeContextAggregationResult context = await contextService.GetContextAsync(
				new DataForgeContextRequest(
					candidateTerms.FirstOrDefault(),
					candidateTerms,
					lookupHints ?? [],
					[]),
				new DataForgeConfigRequest {
					AllowSysSettingsAuthFallback = true,
					Scope = DefaultScope
				},
				cancellationToken);
			return new ApplicationDataForgeResult(
				Used: true,
				Health: context.Health,
				Status: context.Status,
				Coverage: context.Coverage,
				Warnings: context.Warnings,
				ContextSummary: BuildSummary(context));
		} catch (Exception ex) {
			return new ApplicationDataForgeResult(
				Used: true,
				Health: null,
				Status: null,
				Coverage: new DataForgeCoverage(false, false, false, false, false),
				Warnings: [$"dataforge:{ex.Message}"],
				ContextSummary: new ApplicationDataForgeContextSummary([], [], [], []));
		}
	}

	private static ApplicationDataForgeContextSummary BuildSummary(DataForgeContextAggregationResult context) {
		IReadOnlyList<ApplicationDataForgeColumnHint> columnHints = context.Columns
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(pair => new ApplicationDataForgeColumnHint(
				TableName: pair.Key,
				ColumnCount: pair.Value.Count,
				RequiredColumnCount: pair.Value.Count(column => column.Required),
				LookupColumnCount: pair.Value.Count(column => !string.IsNullOrWhiteSpace(column.ReferenceSchemaName))))
			.ToList();
		return new ApplicationDataForgeContextSummary(
			SimilarTables: context.SimilarTables,
			SimilarLookups: context.SimilarLookups,
			RelationPairs: context.Relations.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
			ColumnHints: columnHints);
	}
}
