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

internal interface IKnowledgeTransport {
	KnowledgeSourceType Type { get; }

	KnowledgeTransportResult Retrieve(KnowledgeTransportRequest request);
}
