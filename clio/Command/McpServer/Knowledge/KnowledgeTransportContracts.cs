using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer.Knowledge;

internal enum KnowledgeTransportStatus {
	NoCandidate,
	Failed,
	Rejected,
	Downloaded
}

internal sealed record KnowledgeTransportRequest(
	string SourceAlias,
	KnowledgeSourceConfiguration Source,
	IReadOnlySet<string> RejectedRevisions,
	string? ActiveRevision,
	string? HighestObservedRevision,
	string? FallbackCeilingRevision,
	string? CatalogFingerprint,
	string StagingDirectory,
	int? TransportDeadlineMilliseconds = null,
	string? ExactRevision = null);

internal sealed record KnowledgeTransportResult(
	KnowledgeTransportStatus Status,
	string? ResolvedRevision,
	byte[]? CandidateBytes,
	string? CandidatePath,
	string? CatalogFingerprint = null,
	string? ResolvedBranch = null,
	string? ResolvedTag = null,
	string? ResolvedCommit = null,
	string? Diagnostic = null);

internal interface IKnowledgeSourceTransport {
	KnowledgeSourceType Type { get; }
}

internal interface IKnowledgeArtifactTransport : IKnowledgeSourceTransport {

	KnowledgeTransportResult Retrieve(KnowledgeTransportRequest request);

	/// <summary>
	/// Returns whichever of two revisions this transport considers the later one.
	/// </summary>
	/// <remarks>
	/// Revision progression is transport-specific — a NuGet package version orders differently from
	/// a repository revision — so the orchestrator asks the transport instead of assuming one
	/// scheme. Either argument may be absent or unparsable; the result is <see langword="null"/>
	/// only when neither is a revision this transport can order.
	/// </remarks>
	/// <param name="left">The first revision, or <see langword="null"/>.</param>
	/// <param name="right">The second revision, or <see langword="null"/>.</param>
	/// <returns>The later revision, or <see langword="null"/>.</returns>
	string? GreaterRevision(string? left, string? right);
}

internal interface IKnowledgeRepositoryTransport : IKnowledgeSourceTransport {
	KnowledgeTransportResult Synchronize(KnowledgeTransportRequest request, string repositoryPath);

	KnowledgeTransportResult CheckForUpdates(KnowledgeTransportRequest request, string repositoryPath);

	void ValidateInstalledCheckout(KnowledgeSourceConfiguration source, string repositoryPath);

	void ValidateCheckoutForSynchronization(KnowledgeSourceConfiguration source, string repositoryPath);

	string? GetCurrentRevision(string repositoryPath);

	void Restore(string repositoryPath, string revision);
}
