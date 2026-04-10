using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
public sealed class ApplicationCreateEnrichmentService(IDataForgeEnrichmentBuilder enrichmentBuilder)
	: IApplicationCreateEnrichmentService {
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

		return await enrichmentBuilder.BuildAsync(
			new DataForgeEnrichmentRequest(
				EnvironmentName: args.EnvironmentName,
				RequirementSummary: requirementSummary,
				CandidateTerms: candidateTerms,
				LookupHints: lookupHints),
			cancellationToken);
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
