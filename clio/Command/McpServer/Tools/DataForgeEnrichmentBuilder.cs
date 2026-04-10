using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.DataForge;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Normalized input for building compact Data Forge enrichment diagnostics.
/// </summary>
/// <param name="EnvironmentName">Target Creatio environment name.</param>
/// <param name="RequirementSummary">Optional fallback summary for Data Forge context aggregation.</param>
/// <param name="CandidateTerms">Candidate semantic search terms for similar table discovery.</param>
/// <param name="LookupHints">Optional semantic search terms for lookup discovery.</param>
public sealed record DataForgeEnrichmentRequest(
	string? EnvironmentName,
	string? RequirementSummary,
	IReadOnlyList<string> CandidateTerms,
	IReadOnlyList<string>? LookupHints = null
);

/// <summary>
/// Builds compact Data Forge diagnostics and summary payloads for MCP mutation tools.
/// </summary>
public interface IDataForgeEnrichmentBuilder {
	/// <summary>
	/// Executes a best-effort Data Forge context aggregation and returns a compact MCP-facing result.
	/// </summary>
	/// <param name="request">Normalized Data Forge enrichment request.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Structured Data Forge diagnostics and compact context summary.</returns>
	Task<ApplicationDataForgeResult> BuildAsync(
		DataForgeEnrichmentRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Default builder for compact Data Forge enrichment diagnostics shared by MCP mutation tools.
/// </summary>
public sealed class DataForgeEnrichmentBuilder(IToolCommandResolver commandResolver) : IDataForgeEnrichmentBuilder {
	private const string DefaultScope = "use_enrichment";

	/// <inheritdoc />
	public async Task<ApplicationDataForgeResult> BuildAsync(
		DataForgeEnrichmentRequest request,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.CandidateTerms);

		try {
			DataForgeTargetOptions options = new() {
				Environment = request.EnvironmentName,
				AllowSysSettingsAuthFallback = true,
				Scope = DefaultScope
			};
			IDataForgeContextService contextService = commandResolver.Resolve<IDataForgeContextService>(options);
			DataForgeContextAggregationResult context = await contextService.GetContextAsync(
				new DataForgeContextRequest(
					request.RequirementSummary,
					request.CandidateTerms,
					request.LookupHints ?? [],
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
