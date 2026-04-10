using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.DataForge;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Builds Data Forge enrichment metadata for <c>application-create</c> responses.
/// </summary>
public interface IApplicationCreateEnrichmentService {
	/// <summary>
	/// Executes the app-creation Data Forge enrichment flow for the requested application shell.
	/// </summary>
	/// <param name="args">Normalized MCP create arguments.</param>
	/// <param name="optionalTemplateData">Parsed optional template data.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Structured Data Forge diagnostics and compact context summary.</returns>
	Task<ApplicationDataForgeResult> EnrichAsync(
		ApplicationCreateArgs args,
		ApplicationOptionalTemplateData? optionalTemplateData,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Default Data Forge enrichment flow for <c>application-create</c>.
/// </summary>
public sealed class ApplicationCreateEnrichmentService(IToolCommandResolver commandResolver)
	: IApplicationCreateEnrichmentService {
	private const string DefaultScope = "use_enrichment";

	/// <inheritdoc />
	public async Task<ApplicationDataForgeResult> EnrichAsync(
		ApplicationCreateArgs args,
		ApplicationOptionalTemplateData? optionalTemplateData,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(args);

		List<string> candidateTerms = NormalizeTerms(
			args.Name,
			args.Description,
			optionalTemplateData?.AppSectionDescription,
			optionalTemplateData?.EntitySchemaName);
		List<string> lookupHints = NormalizeTerms(
			optionalTemplateData?.EntitySchemaName,
			optionalTemplateData?.AppSectionDescription,
			args.Name);
		string? requirementSummary = FirstNonEmpty(
			args.Description,
			optionalTemplateData?.AppSectionDescription,
			args.Name);

		try {
			DataForgeTargetOptions options = new() {
				Environment = args.EnvironmentName,
				AllowSysSettingsAuthFallback = true,
				Scope = DefaultScope
			};
			IDataForgeContextService contextService = commandResolver.Resolve<IDataForgeContextService>(options);
			DataForgeContextAggregationResult context = await contextService.GetContextAsync(
				new DataForgeContextRequest(
					requirementSummary,
					candidateTerms,
					lookupHints,
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

	private static List<string> NormalizeTerms(params string?[] values) {
		return values
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(value => value!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static string? FirstNonEmpty(params string?[] values) {
		return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
	}
}
