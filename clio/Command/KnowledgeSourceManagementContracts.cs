using System.Collections.Generic;
using System.Threading;

namespace Clio.Command;

/// <summary>
/// Describes one configured knowledge source requested by the CLI.
/// </summary>
internal sealed record KnowledgeSourceAddRequest(
	string Alias,
	string LibraryId,
	string TransportType,
	string Location,
	string? TrustedKeyId,
	string? TrustedPublicKeyPath,
	string? PackageId,
	string? Branch,
	string? Tag,
	string? Commit,
	bool Enabled,
	int Priority,
	string Participation);

/// <summary>
/// Describes the result of one source-management operation.
/// </summary>
internal sealed record KnowledgeSourceCommandResult(
	bool Success,
	string Message,
	string? SourceAlias = null);

/// <summary>
/// Describes one source-specific result within a lifecycle operation.
/// </summary>
internal sealed record KnowledgeSourceOperationResult(
	string SourceAlias,
	bool Success,
	string Status,
	string Message);

/// <summary>
/// Describes a lifecycle operation across one source or all enabled sources.
/// </summary>
internal sealed record KnowledgeSourceBatchResult(
	bool Success,
	string Message,
	IReadOnlyList<KnowledgeSourceOperationResult> Sources);

/// <summary>
/// Describes safe, user-visible configuration and installation state for one knowledge source.
/// </summary>
/// <remarks>
/// Values returned in <see cref="Location"/> and <see cref="ResolvedRevision"/> must not contain
/// credentials, access tokens, authorization headers, or other secrets.
/// </remarks>
internal sealed record KnowledgeSourceInfo(
	string Alias,
	string LibraryId,
	string TransportType,
	string Location,
	string? TrustedKeyId,
	string? TrustedPublicKeyPath,
	bool Enabled,
	int Priority,
	string Participation,
	string? PackageId,
	string? Branch,
	string? Tag,
	string? Commit,
	bool IsInstalled,
	bool IsValid,
	string? ActiveLibraryVersion,
	ulong? ActiveSequence,
	string? BundleDigest,
	string? ResolvedRevision,
	string? ActiveContentPath,
	string? UpdateAvailability,
	string? Diagnostic);

/// <summary>
/// Describes the configured knowledge sources without performing lifecycle work.
/// </summary>
internal sealed record KnowledgeSourceListResult(
	bool Success,
	IReadOnlyList<KnowledgeSourceInfo> Sources,
	string? Diagnostic = null);

/// <summary>
/// Describes detailed knowledge state for one source or all configured sources.
/// </summary>
internal sealed record KnowledgeSourceInfoResult(
	bool Success,
	string SettingsFilePath,
	string RootPath,
	IReadOnlyList<KnowledgeSourceInfo> Sources,
	string? Diagnostic = null);

/// <summary>
/// Provides the command-layer orchestration boundary for multi-source knowledge management.
/// </summary>
/// <remarks>
/// Implementations own validation and atomic persistence. A <see langword="null"/> source alias means
/// all enabled sources for lifecycle operations; a non-null alias selects exactly that configured source.
/// Source locations and returned diagnostics must be safe for user-visible output and must never expose
/// transport credentials.
/// </remarks>
internal interface IKnowledgeSourceManagementService {
	/// <summary>
	/// Installs knowledge for one configured source or all enabled sources.
	/// </summary>
	KnowledgeSourceBatchResult Install(string? sourceAlias, CancellationToken cancellationToken = default);
	KnowledgeSourceBatchResult Install(
		string? sourceAlias,
		int operationDeadlineMilliseconds,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates knowledge for one configured source or all enabled sources.
	/// </summary>
	KnowledgeSourceBatchResult Update(string? sourceAlias, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reports knowledge configuration and installation state.
	/// </summary>
	KnowledgeSourceInfoResult GetInfo(
		string? sourceAlias,
		bool checkUpdates,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes installed knowledge for one configured source or all enabled sources after confirmation.
	/// </summary>
	KnowledgeSourceBatchResult Delete(
		string? sourceAlias,
		bool confirmed,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Validates and atomically adds one configured knowledge source.
	/// </summary>
	KnowledgeSourceCommandResult Add(KnowledgeSourceAddRequest request);

	/// <summary>
	/// Atomically removes one configured source and its managed cache after confirmation.
	/// </summary>
	KnowledgeSourceCommandResult Remove(string sourceAlias, bool confirmed);

	/// <summary>
	/// Enables one configured source without changing its installed cache.
	/// </summary>
	KnowledgeSourceCommandResult Enable(string sourceAlias);

	/// <summary>
	/// Disables one configured source without deleting its installed cache.
	/// </summary>
	KnowledgeSourceCommandResult Disable(string sourceAlias);

	/// <summary>
	/// Lists every configured source, including disabled sources.
	/// </summary>
	KnowledgeSourceListResult List();
}
