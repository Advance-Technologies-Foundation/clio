using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeSourceManagementService : IKnowledgeSourceManagementService {
	private const int MaxCandidateAttempts = 64;
	private const int OperationDeadlineMilliseconds = 30_000;
	private const int BatchDeadlineMilliseconds = 120_000;
	private const int MaximumConcurrentSourceOperations = 8;
	// The lifecycle-status and update-availability vocabularies deliberately share these tokens so the
	// same observed state is spelled identically in lifecycle results and information reports.
	private const string RejectedState = "rejected";
	private const string UpToDateState = "up-to-date";
	private const string UnknownState = "unknown";
	private readonly ISettingsRepository _settingsRepository;
	private readonly IKnowledgeSourceInstallationStore _store;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeGitRepositoryReader _gitReader;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeArtifactTransport> _artifactTransports;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeRepositoryTransport> _repositoryTransports;
	private readonly IFileSystem _fileSystem;
	private readonly KnowledgeBundleClientCapabilities _capabilities;

	public KnowledgeSourceManagementService(
		ISettingsRepository settingsRepository,
		IKnowledgeSourceInstallationStore store,
		IKnowledgeBundleRuntime runtime,
		IKnowledgeGitRepositoryReader gitReader,
		IEnumerable<IKnowledgeArtifactTransport> artifactTransports,
		IEnumerable<IKnowledgeRepositoryTransport> repositoryTransports,
		IFileSystem fileSystem,
		KnowledgeBundleClientCapabilities capabilities) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_gitReader = gitReader ?? throw new ArgumentNullException(nameof(gitReader));
		_artifactTransports = IndexTransports(artifactTransports);
		_repositoryTransports = IndexTransports(repositoryTransports);
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
	}

	private static IReadOnlyDictionary<KnowledgeSourceType, TTransport> IndexTransports<TTransport>(
		IEnumerable<TTransport> transports) where TTransport : class, IKnowledgeSourceTransport {
		ArgumentNullException.ThrowIfNull(transports);
		return transports
			.GroupBy(transport => transport.Type)
			.ToDictionary(
				group => group.Key,
				group => {
					TTransport[] implementations = group.ToArray();
					return implementations.Length == 1
						? implementations[0]
						: throw new InvalidOperationException(
							$"Multiple knowledge transports are registered for '{group.Key}'.");
				});
	}

	public KnowledgeSourceBatchResult Install(string? sourceAlias, CancellationToken cancellationToken = default) =>
		Install(sourceAlias, OperationDeadlineMilliseconds, cancellationToken);

	public KnowledgeSourceBatchResult Install(
		string? sourceAlias,
		int operationDeadlineMilliseconds,
		CancellationToken cancellationToken = default) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operationDeadlineMilliseconds);
		return ExecuteLifecycle(
			sourceAlias,
			KnowledgeSourceSelection.EnabledOnly,
			(alias, source, deadlineMilliseconds) => InstallOrUpdate(
				alias, source, isUpdate: false, deadlineMilliseconds),
			operationDeadlineMilliseconds,
			cancellationToken);
	}

	public KnowledgeSourceBatchResult Update(string? sourceAlias, CancellationToken cancellationToken = default) => ExecuteLifecycle(
		sourceAlias,
		KnowledgeSourceSelection.EnabledOnly,
		(alias, source, deadlineMilliseconds) => InstallOrUpdate(
			alias, source, isUpdate: true, deadlineMilliseconds),
		OperationDeadlineMilliseconds,
		cancellationToken);

	public KnowledgeSourceInfoResult GetInfo(
		string? sourceAlias,
		bool checkUpdates,
		CancellationToken cancellationToken = default) {
		KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
		// Information is a report, not a lifecycle operation: a disabled source must stay visible with its
		// retained cache and its disabled state, exactly as info-knowledge documents.
		if (!TrySelect(configuration, sourceAlias, KnowledgeSourceSelection.AllConfigured,
				out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
				out string? diagnostic)) {
			return new KnowledgeSourceInfoResult(
				false,
				_settingsRepository.AppSettingsFilePath,
				_store.GetRootPath(),
				Array.Empty<KnowledgeSourceInfo>(),
				diagnostic);
		}
		KnowledgeSourceInfo[] sources = ExecuteBounded(
			selected,
			(pair, deadlineMilliseconds) => BuildInfo(
				pair.Key, pair.Value, checkUpdates, deadlineMilliseconds),
			pair => UnavailableInfo(pair.Key, pair.Value, "Knowledge information request timed out before this source was inspected."),
			OperationDeadlineMilliseconds,
			cancellationToken);
		return new KnowledgeSourceInfoResult(
			true,
			_settingsRepository.AppSettingsFilePath,
			_store.GetRootPath(),
			sources,
			null);
	}

	public KnowledgeSourceBatchResult Delete(
		string? sourceAlias,
		bool confirmed,
		CancellationToken cancellationToken = default) => ExecuteLifecycle(
		sourceAlias,
		KnowledgeSourceSelection.ExplicitDisabled,
		(alias, _, _) => ToOperation(alias, _store.Delete(alias, confirmed)),
		OperationDeadlineMilliseconds,
		cancellationToken);

	public KnowledgeSourceCommandResult Add(KnowledgeSourceAddRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		try {
			KnowledgeSourceConfiguration source = new() {
				LibraryId = request.LibraryId,
				Type = ParseType(request.TransportType),
				Location = request.Location,
				TrustedKeyId = request.TrustedKeyId,
				TrustedPublicKeyPath = request.TrustedPublicKeyPath,
				PackageId = request.PackageId,
				RepositoryOwner = request.RepositoryOwner,
				RepositoryName = request.RepositoryName,
				AssetName = request.AssetName,
				Branch = request.Branch,
				Tag = request.Tag,
				Commit = request.Commit,
				Enabled = request.Enabled,
				Priority = request.Priority,
				Participation = ParseParticipation(request.Participation)
			};
			KnowledgeSourceConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(source);
			// A GitHub release source may omit key material entirely and rely on Clio's pinned built-in
			// trust; when it does declare a key file, that file is checked exactly as a NuGet one is.
			if (validated.TrustedPublicKeyPath is not null
					&& !EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
						validated.TrustedPublicKeyPath,
						out _)) {
				return Failed(request.Alias,
					"Knowledge trusted-public-key-path must identify an existing bounded local regular file "
					+ "containing one P-256 PUBLIC KEY PEM; network, device, reparse, and private-key files are refused.");
			}
			if (!_settingsRepository.TryAddKnowledgeSource(request.Alias, validated)) {
				return Failed(request.Alias,
					$"Knowledge source alias '{request.Alias}' or library '{validated.LibraryId}' is already configured.");
			}
			return new KnowledgeSourceCommandResult(
				true,
				$"Knowledge source '{request.Alias}' was added. Run install-knowledge --source {request.Alias} to install it.",
				request.Alias);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
			return Failed(request.Alias, Safe(exception.Message));
		}
	}

	public KnowledgeSourceCommandResult Remove(string sourceAlias, bool confirmed) {
		if (!confirmed) {
			return Failed(sourceAlias, "Removing a knowledge source requires explicit confirmation.");
		}
		KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
		if (!configuration.Sources.ContainsKey(sourceAlias)) {
			return Failed(sourceAlias, $"Knowledge source '{sourceAlias}' is not configured.");
		}
		KnowledgeSourceConfiguration expected = configuration.Sources[sourceAlias];
		if (string.Equals(sourceAlias, CuratedKnowledgeSourceDefaults.Alias, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(
				expected.LibraryId,
				CuratedKnowledgeSourceDefaults.LibraryId,
				StringComparison.OrdinalIgnoreCase)) {
			return Failed(sourceAlias,
				$"Built-in knowledge source '{sourceAlias}' cannot be removed. "
				+ $"Use disable-knowledge-source --alias {sourceAlias} to stop serving it while retaining its cache.");
		}
		if (!_settingsRepository.TryRemoveKnowledgeSource(sourceAlias, expected)) {
			return Failed(sourceAlias, $"Knowledge source '{sourceAlias}' changed while it was being removed; retry.");
		}
		_runtime.DeactivateLibrary(sourceAlias);
		KnowledgeInstallationResult deletion;
		try {
			deletion = _store.Delete(sourceAlias, confirmed: true);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or TimeoutException) {
			return Failed(sourceAlias,
				$"Knowledge source '{sourceAlias}' was removed and deactivated, but its orphaned cache could not be deleted: "
				+ Safe(exception.Message));
		}
		if (deletion.Status is not (KnowledgeInstallationStatus.Deleted or KnowledgeInstallationStatus.NotInstalled)) {
			return Failed(sourceAlias,
				$"Knowledge source '{sourceAlias}' was removed and deactivated, but its orphaned cache could not be deleted: "
				+ deletion.Message);
		}
		return new KnowledgeSourceCommandResult(true, $"Knowledge source '{sourceAlias}' was removed.", sourceAlias);
	}

	public KnowledgeSourceCommandResult Enable(string sourceAlias) => SetEnabled(sourceAlias, enabled: true);

	public KnowledgeSourceCommandResult Disable(string sourceAlias) => SetEnabled(sourceAlias, enabled: false);

	public KnowledgeSourceListResult List() {
		try {
			KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
			IReadOnlyList<KnowledgeSourceInfo> sources = configuration.Sources
				.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
				.Select(pair => ConfiguredInfo(pair.Key, pair.Value))
				.ToArray();
			return new KnowledgeSourceListResult(true, sources);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException) {
			return new KnowledgeSourceListResult(false, Array.Empty<KnowledgeSourceInfo>(), Safe(exception.Message));
		}
	}

	private KnowledgeSourceOperationResult InstallOrUpdate(
		string alias,
		KnowledgeSourceConfiguration source,
		bool isUpdate,
		int deadlineMilliseconds) {
		if (_repositoryTransports.TryGetValue(source.Type, out IKnowledgeRepositoryTransport? repositoryTransport)) {
			return InstallOrUpdateRepository(alias, source, isUpdate, deadlineMilliseconds, repositoryTransport);
		}
		ArtifactInstallPreflight preflight = InspectArtifactInstallState(alias, source, isUpdate);
		if (preflight.Completed is not null) {
			return preflight.Completed;
		}
		if (!_artifactTransports.TryGetValue(source.Type, out IKnowledgeArtifactTransport? transport)) {
			return FailedOperation(alias, $"Knowledge transport '{source.Type}' is not registered.");
		}

		string staging = CreateTransportStaging(alias);
		try {
			return SearchInstallableCandidate(new ArtifactInstallContext(
				alias,
				source,
				preflight.Current,
				preflight.Repair,
				transport,
				staging,
				deadlineMilliseconds,
				preflight.Diagnostic));
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or ArgumentException
				or TimeoutException) {
			return FailedOperation(alias, Safe(exception.Message));
		} finally {
			DeleteTransportStaging(staging);
		}
	}

	/// <summary>
	/// Decides, before any transport work, whether the caller must stop with an early result, repair an
	/// installed generation that no longer validates, or continue to candidate retrieval.
	/// </summary>
	private ArtifactInstallPreflight InspectArtifactInstallState(
		string alias,
		KnowledgeSourceConfiguration source,
		bool isUpdate) {
		KnowledgeSourceCurrentState? current = _store.ReadCurrent(alias, out string? diagnostic);
		if (diagnostic is not null) {
			return new ArtifactInstallPreflight(FailedOperation(alias, diagnostic), current, false, diagnostic);
		}
		if (isUpdate && current is null) {
			return new ArtifactInstallPreflight(
				FailedOperation(alias, $"Knowledge source '{alias}' is not installed; use install-knowledge."),
				current,
				false,
				diagnostic);
		}
		if (!isUpdate && current is not null && IsCurrentValid(alias, source, current, out diagnostic)) {
			return new ArtifactInstallPreflight(
				new KnowledgeSourceOperationResult(alias, true, "already-installed",
					$"Knowledge source '{alias}' sequence {current.Active.Sequence} is already installed."),
				current,
				false,
				diagnostic);
		}
		// An install over a generation that failed validation must republish rather than refuse, while an
		// update never repairs: it advances from the recorded active generation.
		return new ArtifactInstallPreflight(null, current, !isUpdate && current is not null, diagnostic);
	}

	/// <summary>
	/// Retrieves candidates until one publishes, the transport stops offering candidates, or the
	/// operation-wide deadline elapses.
	/// </summary>
	private KnowledgeSourceOperationResult SearchInstallableCandidate(ArtifactInstallContext context) {
		ArtifactCandidateSearch search = new() { LastDiagnostic = context.InitialDiagnostic };
		Stopwatch operation = Stopwatch.StartNew();
		for (int attempt = 0; attempt < MaxCandidateAttempts; attempt++) {
			int remainingMilliseconds = context.DeadlineMilliseconds - (int)Math.Min(
				operation.ElapsedMilliseconds,
				context.DeadlineMilliseconds);
			if (remainingMilliseconds <= 0) {
				search.LastDiagnostic = "The operation-wide knowledge retrieval deadline elapsed.";
				break;
			}
			ArtifactCandidateAttempt attempted = TryInstallNextCandidate(context, search, remainingMilliseconds);
			if (attempted.Completed is not null) {
				return attempted.Completed;
			}
			if (attempted.StopSearch) {
				break;
			}
		}
		return CompleteCandidateSearch(context, search);
	}

	/// <summary>
	/// Retrieves and evaluates one candidate, publishing it only after the runtime verified it against
	/// the configured library identity.
	/// </summary>
	private ArtifactCandidateAttempt TryInstallNextCandidate(
		ArtifactInstallContext context,
		ArtifactCandidateSearch search,
		int remainingMilliseconds) {
		KnowledgeTransportResult retrieved = context.Transport.Retrieve(new KnowledgeTransportRequest(
			context.Alias,
			context.Source,
			search.Rejected,
			context.Repair ? null : context.Current?.Active.ResolvedRevision,
			search.HighestObservedRevision,
			search.FallbackCeilingRevision,
			search.CatalogFingerprint,
			context.StagingDirectory,
			remainingMilliseconds,
			ExactRevision: context.Repair ? context.Current?.Active.ResolvedRevision : null));
		search.CatalogFingerprint = retrieved.CatalogFingerprint ?? search.CatalogFingerprint;
		if (retrieved.Status == KnowledgeTransportStatus.NoCandidate) {
			if (context.Current is not null && !context.Repair && search.Rejected.Count == 0) {
				_store.TryRecordPublisherCheck(context.Alias, context.Current.Active);
			}
			return new ArtifactCandidateAttempt(null, StopSearch: true);
		}
		if (retrieved.Status == KnowledgeTransportStatus.Failed) {
			return new ArtifactCandidateAttempt(
				FailedOperation(
					context.Alias,
					retrieved.Diagnostic ?? $"Knowledge source '{context.Alias}' could not be retrieved."),
				StopSearch: true);
		}
		if (string.IsNullOrWhiteSpace(retrieved.ResolvedRevision)) {
			search.LastDiagnostic = retrieved.Diagnostic
				?? $"Knowledge source '{context.Alias}' returned no usable candidate.";
			return new ArtifactCandidateAttempt(null, StopSearch: true);
		}
		string revision = retrieved.ResolvedRevision;
		if (search.Rejected.Contains(revision)) {
			search.LastDiagnostic = retrieved.Diagnostic
				?? $"Knowledge transport repeated rejected revision '{revision}'.";
			return new ArtifactCandidateAttempt(null, StopSearch: true);
		}
		search.HighestObservedRevision = context.Transport.GreaterRevision(
			search.HighestObservedRevision, revision);
		search.FallbackCeilingRevision = revision;
		search.LastRejectedRevision = revision;
		if (retrieved.Status != KnowledgeTransportStatus.Downloaded) {
			search.LastDiagnostic = retrieved.Diagnostic ?? "The transport rejected the candidate.";
			return RejectCandidate(search, revision);
		}
		byte[] bytes = ReadCandidate(retrieved);
		using MemoryStream validationStream = new(bytes, writable: false);
		KnowledgeBundleValidationResult validation = _runtime.Validate(
			validationStream,
			expectedBundleVersion: revision,
			expectedLibraryId: context.Source.LibraryId);
		if (ShouldRejectCandidate(validation, context.Source.LibraryId)) {
			search.LastDiagnostic = validation.Diagnostic ?? "The downloaded knowledge bundle was rejected.";
			return RejectCandidate(search, revision);
		}
		KnowledgeInstallationResult published = _store.Publish(new KnowledgeGenerationPublication {
			SourceAlias = context.Alias,
			LibraryId = context.Source.LibraryId,
			LibraryVersion = validation.CandidateLibraryVersion,
			Sequence = validation.CandidateSequence.Value,
			TransportType = KnowledgeSourceTypeNames.Format(context.Source.Type),
			Location = context.Source.Location,
			ResolvedRevision = revision,
			BundleBytes = bytes,
			IsUpdate = context.Current is not null,
			ExpectedActive = context.Current?.Active,
			AllowRepair = context.Repair
		});
		return new ArtifactCandidateAttempt(
			WithTransportAdvisory(ToOperation(context.Alias, published), retrieved.Diagnostic),
			StopSearch: true);
	}

	/// <summary>
	/// Carries a transport advisory into a successful lifecycle result.
	/// </summary>
	/// <remarks>
	/// A transport can accept a candidate and still have something the operator must know — a GitHub
	/// release that is not marked immutable, for instance, whose assets could still be replaced
	/// upstream. Failure diagnostics already surface on the refusal branches; without this, an
	/// advisory attached to an accepted candidate would be silently discarded.
	/// </remarks>
	/// <param name="operation">The publication outcome.</param>
	/// <param name="advisory">The transport diagnostic, or <see langword="null"/>.</param>
	/// <returns>The outcome, with the advisory appended when there is one to report.</returns>
	private static KnowledgeSourceOperationResult WithTransportAdvisory(
		KnowledgeSourceOperationResult operation,
		string? advisory) => operation.Success && !string.IsNullOrWhiteSpace(advisory)
		? operation with { Message = $"{operation.Message} {Safe(advisory)}" }
		: operation;

	/// <summary>
	/// Records a refused revision. A revision the search already refused means the transport is
	/// repeating itself, so the search stops instead of retrying the same candidate.
	/// </summary>
	private static ArtifactCandidateAttempt RejectCandidate(ArtifactCandidateSearch search, string revision) =>
		new(null, StopSearch: !search.Rejected.Add(revision));

	/// <summary>
	/// Refuses a downloaded candidate that failed verification or that does not carry the configured
	/// library identity, so an unverified or mislabeled bundle can never be published.
	/// </summary>
	private static bool ShouldRejectCandidate(KnowledgeBundleValidationResult validation, string libraryId) =>
		validation.Status != KnowledgeBundleActivationStatus.Activated
		|| validation.CandidateSequence is null
		|| string.IsNullOrWhiteSpace(validation.CandidateLibraryId)
		|| string.IsNullOrWhiteSpace(validation.CandidateLibraryVersion)
		|| !string.Equals(validation.CandidateLibraryId, libraryId, StringComparison.Ordinal);

	private static KnowledgeSourceOperationResult CompleteCandidateSearch(
		ArtifactInstallContext context,
		ArtifactCandidateSearch search) {
		if (search.LastRejectedRevision is not null) {
			return FailedOperation(context.Alias,
				$"No compatible knowledge candidate was found after rejecting {search.LastRejectedRevision}: {search.LastDiagnostic}",
				status: RejectedState);
		}
		if (context.Current is not null && !context.Repair) {
			return new KnowledgeSourceOperationResult(context.Alias, true, UpToDateState,
				$"Knowledge source '{context.Alias}' is up to date at {context.Current.Active.ResolvedRevision}.");
		}
		return FailedOperation(context.Alias,
			search.LastDiagnostic ?? $"Knowledge source '{context.Alias}' returned no installable candidate.");
	}

	private KnowledgeSourceOperationResult InstallOrUpdateRepository(
		string alias,
		KnowledgeSourceConfiguration source,
		bool isUpdate,
		int deadlineMilliseconds,
		IKnowledgeRepositoryTransport transport) {
		try {
			return _store.ExecuteWithSourceMutationLock(
				alias,
				() => InstallOrUpdateRepositoryLocked(
					alias, source, isUpdate, deadlineMilliseconds, transport));
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or ArgumentException
				or TimeoutException) {
			return FailedOperation(alias, Safe(exception.Message));
		}
	}

	private KnowledgeSourceOperationResult InstallOrUpdateRepositoryLocked(
		string alias,
		KnowledgeSourceConfiguration source,
		bool isUpdate,
		int deadlineMilliseconds,
		IKnowledgeRepositoryTransport transport) {
		string repositoryPath = _store.GetGitRepositoryPath(alias, createSourceRoot: true);
		bool installed = _fileSystem.Directory.Exists(_fileSystem.Path.Combine(repositoryPath, ".git"));
		if (isUpdate && !installed) {
			return FailedOperation(alias, $"Knowledge source '{alias}' is not installed; use install-knowledge.");
		}
		string? previousRevision = installed ? transport.GetCurrentRevision(repositoryPath) : null;
		KnowledgeGitRepositorySnapshot? previousSnapshot = null;
		if (installed) {
			transport.ValidateCheckoutForSynchronization(source, repositoryPath);
			_gitReader.TryRead(
				repositoryPath,
				source.LibraryId,
				out previousSnapshot,
				out _);
		}
		KnowledgeTransportResult result = transport.Synchronize(new KnowledgeTransportRequest(
			alias,
			source,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			previousRevision,
			null,
			null,
			null,
			repositoryPath,
			deadlineMilliseconds), repositoryPath);
		if (result.Status is KnowledgeTransportStatus.Failed or KnowledgeTransportStatus.Rejected) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"{result.Diagnostic ?? "Git knowledge synchronization failed."} {rollback}".Trim(),
				status: ResolveRepositoryStatus(result.Status, isUpdate));
		}
		if (!_gitReader.TryRead(repositoryPath, source.LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
				out string? diagnostic)) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"{diagnostic ?? "Git knowledge repository is invalid."} {rollback}".Trim());
		}
		// LOCAL DEV TOGGLE (knowledge-allow-unsequenced): the forward-only sequence guard blocks
		// re-testing a branch (same libraryVersion -> same synthesized sequence, new content) or
		// switching branches. When the flag is on, skip it so a Git candidate always installs.
		if (previousSnapshot is not null && !_capabilities.AllowUnsequencedGitBundles
				&& IsSequenceRegression(snapshot, previousSnapshot)) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"Git knowledge source '{alias}' rejected sequence {snapshot.Sequence}; "
				+ $"the previously validated sequence is {previousSnapshot.Sequence}. {rollback}",
				status: RejectedState);
		}
		KnowledgeBundleActivationResult activation = _runtime.ActivateGitRepository(
			alias,
			source.Priority,
			source.Participation,
			snapshot);
		if (activation.Status != KnowledgeBundleActivationStatus.Activated) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"{activation.Diagnostic ?? "Git knowledge repository activation was rejected."} {rollback}".Trim(),
				status: RejectedState);
		}
		if (source.Branch is null && source.Tag is null && source.Commit is null
				&& !string.IsNullOrWhiteSpace(result.ResolvedBranch)
				&& !_settingsRepository.TrySetKnowledgeSourceBranch(alias, source, result.ResolvedBranch)) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"Knowledge source '{alias}' changed while its discovered branch was being persisted; retry. {rollback}".Trim());
		}
		string status = ResolveRepositoryStatus(result.Status, isUpdate);
		return new KnowledgeSourceOperationResult(alias, true, status,
			$"Git knowledge source '{alias}' is {status} at {result.ResolvedCommit} in {repositoryPath}.");
	}

	/// <summary>
	/// Detects a downgrade or rewritten history: a sequence may never move backwards, and the same
	/// sequence must keep the same content digest.
	/// </summary>
	private static bool IsSequenceRegression(
		KnowledgeGitRepositorySnapshot snapshot,
		KnowledgeGitRepositorySnapshot previousSnapshot) =>
		snapshot.Sequence < previousSnapshot.Sequence
		|| (snapshot.Sequence == previousSnapshot.Sequence
			&& !string.Equals(snapshot.ContentDigest, previousSnapshot.ContentDigest, StringComparison.Ordinal));

	/// <summary>
	/// Maps a Git synchronization outcome to the reported lifecycle status. A refused synchronization
	/// keeps the rejected/failed distinction; an accepted one distinguishes update from first install.
	/// </summary>
	private static string ResolveRepositoryStatus(KnowledgeTransportStatus status, bool isUpdate) => status switch {
		KnowledgeTransportStatus.Rejected => RejectedState,
		KnowledgeTransportStatus.Failed => "failed",
		KnowledgeTransportStatus.NoCandidate => isUpdate ? UpToDateState : "already-installed",
		_ => isUpdate ? "updated" : "installed"
	};

	private string RollbackRepository(
		string alias,
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		string? previousRevision,
		IKnowledgeRepositoryTransport transport) {
		if (previousRevision is null) {
			_runtime.DeactivateLibrary(alias);
			try {
				string expectedPath = _fileSystem.Path.GetFullPath(
					_store.GetGitRepositoryPath(alias, createSourceRoot: true));
				string actualPath = _fileSystem.Path.GetFullPath(repositoryPath);
				if (!string.Equals(expectedPath, actualPath, PathComparison)) {
					return "The rejected checkout was left inactive because its managed path could not be verified.";
				}
				if (_fileSystem.Directory.Exists(actualPath)) {
					FileAttributes attributes = _fileSystem.File.GetAttributes(actualPath);
					if ((attributes & FileAttributes.ReparsePoint) != 0) {
						return "The rejected checkout was left inactive because its root is a reparse point.";
					}
					_fileSystem.Directory.Delete(actualPath, recursive: true);
				}
				return "The rejected first checkout was discarded so installation can be retried.";
			} catch (Exception exception) when (exception is IOException
					or UnauthorizedAccessException
					or InvalidOperationException
					or ArgumentException
					or NotSupportedException) {
				return $"The rejected checkout was left inactive because cleanup failed: {Safe(exception.Message)}";
			}
		}
		try {
			transport.Restore(repositoryPath, previousRevision);
			if (!_gitReader.TryRead(repositoryPath, source.LibraryId, out KnowledgeGitRepositorySnapshot? restored,
					out string? diagnostic)) {
				_runtime.DeactivateLibrary(alias);
				return $"The previous checkout was restored but could not be reactivated: {diagnostic}";
			}
			KnowledgeBundleActivationResult activation = _runtime.ActivateGitRepository(
				alias,
				source.Priority,
				source.Participation,
				restored);
			if (activation.Status != KnowledgeBundleActivationStatus.Activated) {
				_runtime.DeactivateLibrary(alias);
				return $"The previous checkout was restored but could not be reactivated: {activation.Diagnostic}";
			}
			return $"The previous revision {previousRevision} was restored.";
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or ArgumentException
				or TimeoutException) {
			_runtime.DeactivateLibrary(alias);
			return $"Rollback to revision {previousRevision} failed: {Safe(exception.Message)}";
		}
	}

	private bool IsCurrentValid(
		string alias,
		KnowledgeSourceConfiguration source,
		KnowledgeSourceCurrentState current,
		out string? diagnostic) {
		if (!_store.TryReadCandidate(alias, current.Active, out InstalledKnowledgeSourceCandidate? candidate,
				out diagnostic)) {
			return false;
		}
		using MemoryStream stream = new(candidate.BundleBytes, writable: false);
		KnowledgeBundleValidationResult validation = _runtime.Validate(
			stream,
			current.Active.LibraryVersion,
			source.LibraryId);
		diagnostic = validation.Diagnostic;
		return MatchesActiveGeneration(validation, current.Active, source.LibraryId);
	}

	/// <summary>
	/// Confirms a validated bundle is exactly the generation recorded as active, so a cache entry that
	/// was swapped or rewritten under the same version is never reported as valid.
	/// </summary>
	private static bool MatchesActiveGeneration(
		KnowledgeBundleValidationResult validation,
		KnowledgeSourceGenerationPointer active,
		string libraryId) =>
		validation.Status == KnowledgeBundleActivationStatus.Activated
		&& validation.CandidateSequence == active.Sequence
		&& string.Equals(validation.CandidateLibraryId, libraryId, StringComparison.Ordinal);

	private KnowledgeSourceInfo BuildInfo(
		string alias,
		KnowledgeSourceConfiguration source,
		bool checkUpdates,
		int deadlineMilliseconds) {
		if (_repositoryTransports.TryGetValue(source.Type, out IKnowledgeRepositoryTransport? repositoryTransport)) {
			return BuildRepositoryInfo(alias, source, checkUpdates, deadlineMilliseconds, repositoryTransport);
		}
		KnowledgeSourceCurrentState? current = _store.ReadCurrent(alias, out string? diagnostic);
		KnowledgeSourceInstallMetadata? metadata = current is null
			? null
			: _store.ReadMetadata(alias, current, out diagnostic);
		bool valid = false;
		string? activePath = null;
		if (current is not null
				&& _store.TryReadCandidate(alias, current.Active, out InstalledKnowledgeSourceCandidate? candidate,
					out diagnostic)) {
			using MemoryStream stream = new(candidate.BundleBytes, writable: false);
			KnowledgeBundleValidationResult validation = _runtime.Validate(
				stream,
				current.Active.LibraryVersion,
				source.LibraryId);
			valid = MatchesActiveGeneration(validation, current.Active, source.LibraryId);
			activePath = candidate.ContentRoot;
			diagnostic ??= validation.Diagnostic;
		}
		string update = current is null ? "not-installed" : UnknownState;
		string? resolvedRevision = current?.Active.ResolvedRevision;
		// A disabled source is reported from its retained local cache only; it never reaches a transport.
		if (checkUpdates && source.Enabled
				&& _artifactTransports.TryGetValue(source.Type, out IKnowledgeArtifactTransport? transport)) {
			ArtifactUpdateProbe probe = ProbeArtifactUpdate(
				alias, source, transport, current, deadlineMilliseconds, update);
			update = probe.Availability;
			diagnostic ??= probe.Diagnostic;
		}
		return new KnowledgeSourceInfo(
			alias,
			source.LibraryId,
			KnowledgeSourceTypeNames.Format(source.Type),
			source.Location,
			source.TrustedKeyId,
			source.TrustedPublicKeyPath,
			source.Enabled,
			source.Priority,
			source.Participation.ToString().ToLowerInvariant(),
			source.PackageId,
			source.RepositoryOwner,
			source.RepositoryName,
			source.AssetName,
			source.Branch,
			source.Tag,
			source.Commit,
			current is not null,
			valid,
			current?.Active.LibraryVersion,
			current?.Active.Sequence,
			current?.Active.BundleDigest,
			resolvedRevision ?? metadata?.ResolvedRevision,
			activePath,
			update,
			diagnostic);
	}

	/// <summary>
	/// Contacts the artifact transport once to classify remote update availability. The probe reports
	/// only: it never publishes or activates a candidate, and an unverified candidate reads as rejected.
	/// </summary>
	/// <param name="fallbackAvailability">
	/// Availability reported when the probe cannot classify the remote at all, so a failed probe never
	/// overstates what is known about the source.
	/// </param>
	private ArtifactUpdateProbe ProbeArtifactUpdate(
		string alias,
		KnowledgeSourceConfiguration source,
		IKnowledgeArtifactTransport transport,
		KnowledgeSourceCurrentState? current,
		int deadlineMilliseconds,
		string fallbackAvailability) {
		string staging = CreateTransportStaging(alias);
		try {
			KnowledgeTransportResult remoteCandidate = transport.Retrieve(new KnowledgeTransportRequest(
				alias, source, new HashSet<string>(), current?.Active.ResolvedRevision, null, null, null, staging,
				TransportDeadlineMilliseconds: deadlineMilliseconds));
			if (remoteCandidate.Status != KnowledgeTransportStatus.Downloaded) {
				return new ArtifactUpdateProbe(
					remoteCandidate.Status == KnowledgeTransportStatus.NoCandidate ? UpToDateState : UnknownState,
					remoteCandidate.Status is KnowledgeTransportStatus.Rejected
						or KnowledgeTransportStatus.Failed
						? Safe(remoteCandidate.Diagnostic ?? "The remote knowledge source could not be checked.")
						: null);
			}
			byte[] candidateBytes = ReadCandidate(remoteCandidate);
			using MemoryStream stream = new(candidateBytes, writable: false);
			KnowledgeBundleValidationResult validation = _runtime.Validate(
				stream,
				expectedBundleVersion: remoteCandidate.ResolvedRevision,
				expectedLibraryId: source.LibraryId);
			bool trustedCandidate = validation.Status == KnowledgeBundleActivationStatus.Activated
				&& string.Equals(
					validation.CandidateLibraryId,
					source.LibraryId,
					StringComparison.Ordinal);
			return new ArtifactUpdateProbe(
				trustedCandidate ? "available" : RejectedState,
				trustedCandidate
					? null
					: validation.Diagnostic ?? "The remote candidate failed verification.");
		} catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException) {
			return new ArtifactUpdateProbe(fallbackAvailability, Safe(exception.Message));
		} finally {
			DeleteTransportStaging(staging);
		}
	}

	private KnowledgeSourceInfo BuildRepositoryInfo(
		string alias,
		KnowledgeSourceConfiguration source,
		bool checkUpdates,
		int deadlineMilliseconds,
		IKnowledgeRepositoryTransport transport) {
		string repositoryPath = _store.GetGitRepositoryPath(alias, createSourceRoot: false);
		bool installed = _fileSystem.Directory.Exists(_fileSystem.Path.Combine(repositoryPath, ".git"));
		RepositoryInspection inspection = installed
			? InspectRepositoryCheckout(alias, source, checkUpdates, deadlineMilliseconds, transport, repositoryPath)
			: new RepositoryInspection(false, "not-installed", null, null, null);
		return new KnowledgeSourceInfo(
			alias,
			source.LibraryId,
			"git",
			source.Location,
			null,
			null,
			source.Enabled,
			source.Priority,
			source.Participation.ToString().ToLowerInvariant(),
			null,
			null,
			null,
			null,
			source.Branch,
			source.Tag,
			source.Commit,
			installed,
			inspection.Valid,
			inspection.Valid ? inspection.Snapshot.LibraryVersion : null,
			inspection.Valid ? inspection.Snapshot.Sequence : null,
			inspection.Valid ? inspection.Snapshot.ContentDigest : null,
			inspection.Revision,
			installed ? repositoryPath : null,
			inspection.Availability,
			inspection.Diagnostic);
	}

	/// <summary>
	/// Reads one installed Git checkout under the source mutation lock and, when requested, probes the
	/// remote. A checkout already being synchronized is reported as such instead of waiting for the lock.
	/// </summary>
	private RepositoryInspection InspectRepositoryCheckout(
		string alias,
		KnowledgeSourceConfiguration source,
		bool checkUpdates,
		int deadlineMilliseconds,
		IKnowledgeRepositoryTransport transport,
		string repositoryPath) {
		KnowledgeGitRepositorySnapshot? snapshot = null;
		string? diagnostic = null;
		bool valid = false;
		string? revision = null;
		try {
			bool acquired = _store.TryExecuteWithSourceMutationLock(alias, () => {
				transport.ValidateInstalledCheckout(source, repositoryPath);
				valid = _gitReader.TryRead(repositoryPath, source.LibraryId, out snapshot, out diagnostic);
				revision = transport.GetCurrentRevision(repositoryPath);
			});
			if (!acquired) {
				return new RepositoryInspection(false, "synchronizing", null, null,
					$"Git knowledge source '{alias}' is synchronizing; retry the information request.");
			}
			string availability = UnknownState;
			// A disabled source is reported from its retained local checkout only; it never reaches a remote.
			if (checkUpdates && source.Enabled) {
				KnowledgeTransportResult remote = transport.CheckForUpdates(new KnowledgeTransportRequest(
					alias,
					source,
					new HashSet<string>(StringComparer.OrdinalIgnoreCase),
					revision,
					null,
					null,
					null,
					repositoryPath,
					deadlineMilliseconds), repositoryPath);
				availability = remote.Status switch {
					KnowledgeTransportStatus.Downloaded => "available",
					KnowledgeTransportStatus.NoCandidate => UpToDateState,
					_ => UnknownState
				};
				if (remote.Status is KnowledgeTransportStatus.Failed or KnowledgeTransportStatus.Rejected) {
					diagnostic ??= Safe(remote.Diagnostic
						?? "The remote Git knowledge source could not be checked.");
				}
			}
			return new RepositoryInspection(valid, availability, revision, snapshot, diagnostic);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or InvalidDataException
				or ArgumentException
				or TimeoutException) {
			// Whatever the locked read established before the failure stays reported; availability does not.
			return new RepositoryInspection(valid, UnknownState, revision, snapshot, Safe(exception.Message));
		}
	}

	private KnowledgeSourceBatchResult ExecuteLifecycle(
		string? sourceAlias,
		KnowledgeSourceSelection selection,
		Func<string, KnowledgeSourceConfiguration, int, KnowledgeSourceOperationResult> operation,
		int operationDeadlineMilliseconds,
		CancellationToken cancellationToken) {
		try {
			KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
			if (!TrySelect(configuration, sourceAlias, selection,
					out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
					out string? diagnostic)) {
				return new KnowledgeSourceBatchResult(false, diagnostic, Array.Empty<KnowledgeSourceOperationResult>());
			}
			KnowledgeSourceOperationResult[] results = ExecuteBounded(
				selected,
				(pair, deadlineMilliseconds) => operation(pair.Key, pair.Value, deadlineMilliseconds),
				pair => FailedOperation(pair.Key,
					"Knowledge operation timed out before this source was processed."),
				operationDeadlineMilliseconds,
				cancellationToken);
			bool success = results.All(result => result.Success);
			return new KnowledgeSourceBatchResult(
				success,
				success ? $"Knowledge operation completed for {results.Length} source(s)."
					: "Knowledge operation failed for one or more sources; successful sources remain active.",
				results);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException) {
			return new KnowledgeSourceBatchResult(false, Safe(exception.Message), Array.Empty<KnowledgeSourceOperationResult>());
		}
	}

	private static TResult[] ExecuteBounded<TResult>(
		IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
		Func<KeyValuePair<string, KnowledgeSourceConfiguration>, int, TResult> operation,
		Func<KeyValuePair<string, KnowledgeSourceConfiguration>, TResult> timeoutResult,
		int operationDeadlineMilliseconds,
		CancellationToken cancellationToken) where TResult : class {
		cancellationToken.ThrowIfCancellationRequested();
		if (selected.Count <= 1) {
			return selected.Count == 0 ? [] : [operation(selected[0], operationDeadlineMilliseconds)];
		}
		TResult?[] results = new TResult?[selected.Count];
		Stopwatch batch = Stopwatch.StartNew();
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(BatchDeadlineMilliseconds);
		try {
			Parallel.For(0, selected.Count, new ParallelOptions {
				CancellationToken = deadline.Token,
				MaxDegreeOfParallelism = MaximumConcurrentSourceOperations
			}, index => {
				int remainingBatchMilliseconds = BatchDeadlineMilliseconds - (int)Math.Min(
					batch.ElapsedMilliseconds,
					BatchDeadlineMilliseconds);
				if (remainingBatchMilliseconds <= 0) {
					return;
				}
				int sourceDeadlineMilliseconds = Math.Min(
					operationDeadlineMilliseconds,
					remainingBatchMilliseconds);
				results[index] = operation(selected[index], sourceDeadlineMilliseconds);
			});
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			// A bounded batch returns explicit per-source timeout results for work that was not started.
		}
		cancellationToken.ThrowIfCancellationRequested();
		return results.Select((result, index) => result ?? timeoutResult(selected[index])).ToArray();
	}

	private static KnowledgeSourceInfo UnavailableInfo(
		string alias,
		KnowledgeSourceConfiguration source,
		string diagnostic) => new(
		alias,
		source.LibraryId,
		KnowledgeSourceTypeNames.Format(source.Type),
		source.Location,
		source.TrustedKeyId,
		source.TrustedPublicKeyPath,
		source.Enabled,
		source.Priority,
		source.Participation.ToString().ToLowerInvariant(),
		source.PackageId,
		source.RepositoryOwner,
		source.RepositoryName,
		source.AssetName,
		source.Branch,
		source.Tag,
		source.Commit,
		false,
		false,
		null,
		null,
		null,
		null,
		null,
		UnknownState,
		diagnostic);

	private static KnowledgeSourceInfo ConfiguredInfo(
		string alias,
		KnowledgeSourceConfiguration source) => UnavailableInfo(alias, source, diagnostic: null) with {
		UpdateAvailability = null
	};

	private static bool TrySelect(
		KnowledgeConfiguration configuration,
		string? sourceAlias,
		KnowledgeSourceSelection selection,
		out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
		out string? diagnostic) {
		if (sourceAlias is not null) {
			if (!configuration.Sources.TryGetValue(sourceAlias, out KnowledgeSourceConfiguration? source)) {
				selected = Array.Empty<KeyValuePair<string, KnowledgeSourceConfiguration>>();
				diagnostic = $"Knowledge source '{sourceAlias}' is not configured.";
				return false;
			}
			if (!source.Enabled && selection == KnowledgeSourceSelection.EnabledOnly) {
				selected = Array.Empty<KeyValuePair<string, KnowledgeSourceConfiguration>>();
				diagnostic = $"Knowledge source '{sourceAlias}' is disabled.";
				return false;
			}
			string canonicalAlias = configuration.Sources.Keys.Single(key =>
				string.Equals(key, sourceAlias, StringComparison.OrdinalIgnoreCase));
			selected = [new KeyValuePair<string, KnowledgeSourceConfiguration>(canonicalAlias, source)];
			diagnostic = null;
			return true;
		}
		bool includeDisabled = selection == KnowledgeSourceSelection.AllConfigured;
		selected = configuration.Sources
			.Where(pair => includeDisabled || pair.Value.Enabled)
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (selected.Count == 0) {
			diagnostic = includeDisabled
				? "No knowledge sources are configured."
				: "No enabled knowledge sources are configured.";
			return false;
		}
		diagnostic = null;
		return true;
	}

	private KnowledgeSourceCommandResult SetEnabled(string sourceAlias, bool enabled) {
		try {
			_settingsRepository.SetKnowledgeSourceEnabled(sourceAlias, enabled);
			if (!enabled) {
				_runtime.DeactivateLibrary(sourceAlias);
			}
			return new KnowledgeSourceCommandResult(
				true,
				$"Knowledge source '{sourceAlias}' was {(enabled ? "enabled" : "disabled")}; its cache was retained.",
				sourceAlias);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException) {
			return Failed(sourceAlias, Safe(exception.Message));
		}
	}

	private byte[] ReadCandidate(KnowledgeTransportResult result) {
		if (result.CandidateBytes is { Length: > 0 } bytes) {
			return bytes;
		}
		if (string.IsNullOrWhiteSpace(result.CandidatePath)) {
			throw new InvalidDataException("Knowledge transport returned no candidate bytes or path.");
		}
		using Stream input = _fileSystem.File.OpenRead(result.CandidatePath);
		if (input.Length <= 0 || input.Length > 40 * 1024 * 1024) {
			throw new InvalidDataException("Knowledge transport candidate is outside supported bounds.");
		}
		byte[] candidate = new byte[checked((int)input.Length)];
		input.ReadExactly(candidate);
		return candidate;
	}

	private string CreateTransportStaging(string alias) {
		string root = _fileSystem.Path.Combine(
			_fileSystem.Path.GetTempPath(),
			"clio-knowledge-transport",
			$"{alias}-{Guid.NewGuid():N}");
		_fileSystem.Directory.CreateDirectory(root);
		return _fileSystem.Path.GetFullPath(root);
	}

	private void DeleteTransportStaging(string path) {
		try {
			if (_fileSystem.Directory.Exists(path)) {
				_fileSystem.Directory.Delete(path, recursive: true);
			}
		} catch (IOException) {
			// Best-effort cleanup of non-active transport staging; a later OS temp cleanup can remove it.
		} catch (UnauthorizedAccessException) {
			// Best-effort cleanup of non-active transport staging; a later OS temp cleanup can remove it.
		}
	}

	private static KnowledgeSourceType ParseType(string value) => value.ToLowerInvariant() switch {
		"git" => KnowledgeSourceType.Git,
		"nuget" => KnowledgeSourceType.NuGet,
		"github-release" => KnowledgeSourceType.GitHubRelease,
		_ => throw new ArgumentException(
			"Knowledge source type must be 'github-release', 'git', or 'nuget'.", nameof(value))
	};

	private static KnowledgeSourceParticipation ParseParticipation(string value) => value.ToLowerInvariant() switch {
		"isolated" => KnowledgeSourceParticipation.Isolated,
		"supplement" => KnowledgeSourceParticipation.Supplement,
		"authoritative" => KnowledgeSourceParticipation.Authoritative,
		_ => throw new ArgumentException(
			"Knowledge source participation must be isolated, supplement, or authoritative.", nameof(value))
	};

	private static KnowledgeSourceOperationResult ToOperation(string alias, KnowledgeInstallationResult result) =>
		new(alias, result.IsSuccess, result.Status.ToString().ToLowerInvariant(), result.Message);

	private static KnowledgeSourceOperationResult FailedOperation(
		string alias,
		string message,
		string status = "failed") => new(alias, false, status, Safe(message));

	private static KnowledgeSourceCommandResult Failed(string alias, string message) =>
		new(false, Safe(message), alias);

	private static string Safe(string message) => SensitiveErrorTextRedactor.Redact(message);

	private static StringComparison PathComparison => OperatingSystem.IsWindows()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;

	/// <summary>
	/// Controls which configured sources one operation is allowed to act on.
	/// </summary>
	private enum KnowledgeSourceSelection {
		/// <summary>Only enabled sources participate, even when an alias is requested explicitly.</summary>
		EnabledOnly,

		/// <summary>A disabled source participates only when its alias is requested explicitly.</summary>
		ExplicitDisabled,

		/// <summary>Disabled sources participate in explicit and all-source selection.</summary>
		AllConfigured
	}

	/// <summary>
	/// Pre-retrieval decision for one artifact source: an early result that ends the operation, the
	/// installed generation, whether a cache that failed validation must be republished, and the
	/// diagnostic observed so far.
	/// </summary>
	private sealed record ArtifactInstallPreflight(
		KnowledgeSourceOperationResult? Completed,
		KnowledgeSourceCurrentState? Current,
		bool Repair,
		string? Diagnostic);

	/// <summary>
	/// Immutable inputs of one bounded candidate search for an artifact source.
	/// </summary>
	private sealed record ArtifactInstallContext(
		string Alias,
		KnowledgeSourceConfiguration Source,
		KnowledgeSourceCurrentState? Current,
		bool Repair,
		IKnowledgeArtifactTransport Transport,
		string StagingDirectory,
		int DeadlineMilliseconds,
		string? InitialDiagnostic);

	/// <summary>
	/// Mutable state carried across the retrieval attempts of one candidate search.
	/// </summary>
	private sealed record ArtifactCandidateSearch {
		/// <summary>Revisions this search already refused; the transport must not offer them again.</summary>
		internal HashSet<string> Rejected { get; } = new(StringComparer.OrdinalIgnoreCase);

		internal string? HighestObservedRevision { get; set; }

		internal string? FallbackCeilingRevision { get; set; }

		internal string? CatalogFingerprint { get; set; }

		internal string? LastRejectedRevision { get; set; }

		internal string? LastDiagnostic { get; set; }
	}

	/// <summary>
	/// Outcome of one retrieval attempt: a finished operation, or whether the search must stop.
	/// </summary>
	private sealed record ArtifactCandidateAttempt(KnowledgeSourceOperationResult? Completed, bool StopSearch);

	/// <summary>
	/// Remote update availability observed for one artifact source, with its safe diagnostic.
	/// </summary>
	private sealed record ArtifactUpdateProbe(string Availability, string? Diagnostic);

	/// <summary>
	/// Locally observed state of one installed Git knowledge checkout.
	/// </summary>
	private sealed record RepositoryInspection(
		bool Valid,
		string Availability,
		string? Revision,
		KnowledgeGitRepositorySnapshot? Snapshot,
		string? Diagnostic);
}
