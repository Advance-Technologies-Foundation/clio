using System.Collections.Generic;

namespace Clio.Command;

/// <summary>
/// Selects reference examples from active, locally cached knowledge catalogs.
/// </summary>
internal sealed record KnowledgeReferenceExampleQuery(
	string? SourceAlias,
	string? SearchText,
	string? Capability,
	string? Status);

/// <summary>
/// Describes the primary use case of a registered reference example.
/// </summary>
internal sealed record KnowledgeReferenceExampleUseCase(string Id, string Summary);

/// <summary>
/// Describes immutable source coordinates for a registered reference example.
/// </summary>
internal sealed record KnowledgeReferenceExampleSource(
	string Repository,
	string Revision,
	string DefaultBranch);

/// <summary>
/// Describes compatibility information declared by a reference example publisher.
/// </summary>
internal sealed record KnowledgeReferenceExampleCompatibility(string Status, string Details);

/// <summary>
/// Describes the publisher trust label carried by a reference example catalog entry.
/// </summary>
internal sealed record KnowledgeReferenceExampleTrust(string Publisher, string Level);

/// <summary>
/// Describes one discoverable reference example without cloning its repository.
/// </summary>
internal sealed record KnowledgeReferenceExample(
	string SourceAlias,
	string LibraryId,
	int SourcePriority,
	string SourceParticipation,
	ulong BundleSequence,
	string BundleDigest,
	string CatalogItemId,
	int SchemaVersion,
	string Id,
	string Title,
	string Status,
	KnowledgeReferenceExampleUseCase PrimaryUseCase,
	KnowledgeReferenceExampleSource Source,
	IReadOnlyDictionary<string, string> EntryPoints,
	IReadOnlyList<string> SupportingCapabilities,
	KnowledgeReferenceExampleCompatibility Compatibility,
	KnowledgeReferenceExampleTrust Trust,
	IReadOnlyList<string> Notes);

/// <summary>
/// Describes reference examples discovered from active trusted knowledge catalogs.
/// </summary>
internal sealed record KnowledgeReferenceExampleListResult(
	bool Success,
	IReadOnlyList<KnowledgeReferenceExample> Examples,
	IReadOnlyList<string> Diagnostics);

/// <summary>
/// Discovers registered reference examples from active local knowledge catalogs.
/// </summary>
internal interface IKnowledgeReferenceExampleService {
	/// <summary>
	/// Lists matching catalog entries without contacting or cloning their repositories.
	/// </summary>
	KnowledgeReferenceExampleListResult List(KnowledgeReferenceExampleQuery query);
}
