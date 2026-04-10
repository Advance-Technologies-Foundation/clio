using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
public sealed class SchemaEnrichmentService(IDataForgeEnrichmentBuilder enrichmentBuilder) : ISchemaEnrichmentService {
	/// <inheritdoc />
	public async Task<ApplicationDataForgeResult> EnrichAsync(
		string? environmentName,
		IReadOnlyList<string> candidateTerms,
		IReadOnlyList<string>? lookupHints = null,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(candidateTerms);

		return await enrichmentBuilder.BuildAsync(
			new DataForgeEnrichmentRequest(
				EnvironmentName: environmentName,
				RequirementSummary: candidateTerms.FirstOrDefault(),
				CandidateTerms: candidateTerms,
				LookupHints: lookupHints ?? []),
			cancellationToken);
	}
}
