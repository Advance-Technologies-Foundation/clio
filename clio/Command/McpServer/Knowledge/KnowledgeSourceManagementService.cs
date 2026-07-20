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
	private readonly ISettingsRepository _settingsRepository;
	private readonly IKnowledgeSourceInstallationStore _store;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeGitRepositoryReader _gitReader;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeArtifactTransport> _artifactTransports;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeRepositoryTransport> _repositoryTransports;
	private readonly IFileSystem _fileSystem;

	public KnowledgeSourceManagementService(
		ISettingsRepository settingsRepository,
		IKnowledgeSourceInstallationStore store,
		IKnowledgeBundleRuntime runtime,
		IKnowledgeGitRepositoryReader gitReader,
		IEnumerable<IKnowledgeArtifactTransport> artifactTransports,
		IEnumerable<IKnowledgeRepositoryTransport> repositoryTransports,
		IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_gitReader = gitReader ?? throw new ArgumentNullException(nameof(gitReader));
		_artifactTransports = IndexTransports(artifactTransports);
		_repositoryTransports = IndexTransports(repositoryTransports);
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
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

	public KnowledgeSourceBatchResult Install(string? sourceAlias, CancellationToken cancellationToken = default) => ExecuteLifecycle(
		sourceAlias,
		includeDisabledWhenExplicit: false,
		(alias, source, deadlineMilliseconds) => InstallOrUpdate(
			alias, source, isUpdate: false, deadlineMilliseconds),
		cancellationToken);

	public KnowledgeSourceBatchResult Update(string? sourceAlias, CancellationToken cancellationToken = default) => ExecuteLifecycle(
		sourceAlias,
		includeDisabledWhenExplicit: false,
		(alias, source, deadlineMilliseconds) => InstallOrUpdate(
			alias, source, isUpdate: true, deadlineMilliseconds),
		cancellationToken);

	public KnowledgeSourceInfoResult GetInfo(
		string? sourceAlias,
		bool checkUpdates,
		CancellationToken cancellationToken = default) {
		KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
		if (!TrySelect(configuration, sourceAlias, includeDisabledWhenExplicit: true,
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
		includeDisabledWhenExplicit: true,
		(alias, _, _) => ToOperation(alias, _store.Delete(alias, confirmed)),
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
				Branch = request.Branch,
				Tag = request.Tag,
				Commit = request.Commit,
				Enabled = request.Enabled,
				Priority = request.Priority,
				Participation = ParseParticipation(request.Participation)
			};
			KnowledgeSourceConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(source);
			if (validated.Type == KnowledgeSourceType.NuGet
					&& !EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
						validated.TrustedPublicKeyPath!,
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
		KnowledgeSourceCurrentState? current = _store.ReadCurrent(alias, out string? diagnostic);
		if (diagnostic is not null) {
			return FailedOperation(alias, diagnostic);
		}
		if (isUpdate && current is null) {
			return FailedOperation(alias, $"Knowledge source '{alias}' is not installed; use install-knowledge.");
		}
		bool repair = false;
		if (!isUpdate && current is not null && IsCurrentValid(alias, source, current, out diagnostic)) {
			return new KnowledgeSourceOperationResult(alias, true, "already-installed",
				$"Knowledge source '{alias}' sequence {current.Active.Sequence} is already installed.");
		}
		if (!isUpdate && current is not null) {
			repair = true;
		}
		if (!_artifactTransports.TryGetValue(source.Type, out IKnowledgeArtifactTransport? transport)) {
			return FailedOperation(alias, $"Knowledge transport '{source.Type}' is not registered.");
		}

		string staging = CreateTransportStaging(alias);
		try {
			HashSet<string> rejected = new(StringComparer.OrdinalIgnoreCase);
			string? highestObserved = null;
			string? fallbackCeiling = null;
			string? catalogFingerprint = null;
			string? lastRejectedRevision = null;
			string? lastDiagnostic = diagnostic;
			Stopwatch operation = Stopwatch.StartNew();
			for (int attempt = 0; attempt < MaxCandidateAttempts; attempt++) {
				int remainingMilliseconds = deadlineMilliseconds - (int)Math.Min(
					operation.ElapsedMilliseconds,
					deadlineMilliseconds);
				if (remainingMilliseconds <= 0) {
					lastDiagnostic = "The operation-wide knowledge retrieval deadline elapsed.";
					break;
				}
				KnowledgeTransportResult retrieved = transport.Retrieve(new KnowledgeTransportRequest(
					alias,
					source,
					rejected,
					repair ? null : current?.Active.ResolvedRevision,
					highestObserved,
					fallbackCeiling,
					catalogFingerprint,
					staging,
					remainingMilliseconds,
					ExactRevision: repair ? current?.Active.ResolvedRevision : null));
				catalogFingerprint = retrieved.CatalogFingerprint ?? catalogFingerprint;
				if (retrieved.Status == KnowledgeTransportStatus.NoCandidate) {
					break;
				}
				if (retrieved.Status == KnowledgeTransportStatus.Failed) {
					return FailedOperation(
						alias,
						retrieved.Diagnostic ?? $"Knowledge source '{alias}' could not be retrieved.");
				}
				if (string.IsNullOrWhiteSpace(retrieved.ResolvedRevision)) {
					lastDiagnostic = retrieved.Diagnostic ?? $"Knowledge source '{alias}' returned no usable candidate.";
					break;
				}
				string revision = retrieved.ResolvedRevision;
				if (rejected.Contains(revision)) {
					lastDiagnostic = retrieved.Diagnostic
						?? $"Knowledge transport repeated rejected revision '{revision}'.";
					break;
				}
				highestObserved = KnowledgeBundleNuGetClient.GreaterVersion(highestObserved, revision);
				fallbackCeiling = revision;
				lastRejectedRevision = revision;
				if (retrieved.Status != KnowledgeTransportStatus.Downloaded) {
					lastDiagnostic = retrieved.Diagnostic ?? "The transport rejected the candidate.";
					if (!rejected.Add(revision)) {
						break;
					}
					continue;
				}
				byte[] bytes = ReadCandidate(retrieved);
				using MemoryStream validationStream = new(bytes, writable: false);
				KnowledgeBundleValidationResult validation = _runtime.Validate(
					validationStream,
					expectedBundleVersion: revision,
					expectedLibraryId: source.LibraryId);
				if (validation.Status != KnowledgeBundleActivationStatus.Activated
						|| validation.CandidateSequence is null
						|| string.IsNullOrWhiteSpace(validation.CandidateLibraryId)
						|| string.IsNullOrWhiteSpace(validation.CandidateLibraryVersion)
						|| !string.Equals(validation.CandidateLibraryId, source.LibraryId, StringComparison.Ordinal)) {
					lastDiagnostic = validation.Diagnostic ?? "The downloaded knowledge bundle was rejected.";
					if (!rejected.Add(revision)) {
						break;
					}
					continue;
				}
				KnowledgeInstallationResult published = _store.Publish(
					alias,
					source.LibraryId,
					validation.CandidateLibraryVersion,
					validation.CandidateSequence.Value,
					source.Type.ToString().ToLowerInvariant(),
					source.Location,
					revision,
					bytes,
					isUpdate: current is not null,
					expectedActive: current?.Active,
					allowRepair: repair);
				return ToOperation(alias, published);
			}
			if (lastRejectedRevision is not null) {
				return FailedOperation(alias,
					$"No compatible knowledge candidate was found after rejecting {lastRejectedRevision}: {lastDiagnostic}",
					status: "rejected");
			}
			if (current is not null && !repair) {
				return new KnowledgeSourceOperationResult(alias, true, "up-to-date",
					$"Knowledge source '{alias}' is up to date at {current.Active.ResolvedRevision}.");
			}
			return FailedOperation(alias,
				lastDiagnostic ?? $"Knowledge source '{alias}' returned no installable candidate.");
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
				status: result.Status == KnowledgeTransportStatus.Rejected ? "rejected" : "failed");
		}
		if (!_gitReader.TryRead(repositoryPath, source.LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
				out string? diagnostic)) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"{diagnostic ?? "Git knowledge repository is invalid."} {rollback}".Trim());
		}
		if (previousSnapshot is not null
				&& (snapshot!.Sequence < previousSnapshot.Sequence
					|| (snapshot.Sequence == previousSnapshot.Sequence
						&& !string.Equals(snapshot.ContentDigest, previousSnapshot.ContentDigest,
							StringComparison.Ordinal)))) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"Git knowledge source '{alias}' rejected sequence {snapshot.Sequence}; "
				+ $"the previously validated sequence is {previousSnapshot.Sequence}. {rollback}",
				status: "rejected");
		}
		KnowledgeBundleActivationResult activation = _runtime.ActivateGitRepository(
			alias,
			source.Priority,
			source.Participation,
			snapshot!);
		if (activation.Status != KnowledgeBundleActivationStatus.Activated) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"{activation.Diagnostic ?? "Git knowledge repository activation was rejected."} {rollback}".Trim(),
				status: "rejected");
		}
		if (source.Branch is null && source.Tag is null && source.Commit is null
				&& !string.IsNullOrWhiteSpace(result.ResolvedBranch)
				&& !_settingsRepository.TrySetKnowledgeSourceBranch(alias, source, result.ResolvedBranch)) {
			string rollback = RollbackRepository(alias, source, repositoryPath, previousRevision, transport);
			return FailedOperation(alias,
				$"Knowledge source '{alias}' changed while its discovered branch was being persisted; retry. {rollback}".Trim());
		}
		string status = (result.Status, isUpdate) switch {
			(KnowledgeTransportStatus.NoCandidate, true) => "up-to-date",
			(KnowledgeTransportStatus.NoCandidate, false) => "already-installed",
			(_, true) => "updated",
			_ => "installed"
		};
		return new KnowledgeSourceOperationResult(alias, true, status,
			$"Git knowledge source '{alias}' is {status} at {result.ResolvedCommit} in {repositoryPath}.");
	}

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
				restored!);
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
		using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
		KnowledgeBundleValidationResult validation = _runtime.Validate(
			stream,
			current.Active.LibraryVersion,
			source.LibraryId);
		diagnostic = validation.Diagnostic;
		return validation.Status == KnowledgeBundleActivationStatus.Activated
			&& validation.CandidateSequence == current.Active.Sequence
			&& string.Equals(validation.CandidateLibraryId, source.LibraryId, StringComparison.Ordinal);
	}

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
			using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
			KnowledgeBundleValidationResult validation = _runtime.Validate(
				stream,
				current.Active.LibraryVersion,
				source.LibraryId);
			valid = validation.Status == KnowledgeBundleActivationStatus.Activated
				&& validation.CandidateSequence == current.Active.Sequence
				&& string.Equals(validation.CandidateLibraryId, source.LibraryId, StringComparison.Ordinal);
			activePath = candidate.ContentRoot;
			diagnostic ??= validation.Diagnostic;
		}
		string update = current is null ? "not-installed" : "unknown";
		string? resolvedRevision = current?.Active.ResolvedRevision;
		if (checkUpdates && source.Enabled
				&& _artifactTransports.TryGetValue(source.Type, out IKnowledgeArtifactTransport? transport)) {
			string staging = CreateTransportStaging(alias);
			try {
				KnowledgeTransportResult remoteCandidate = transport.Retrieve(new KnowledgeTransportRequest(
					alias, source, new HashSet<string>(), current?.Active.ResolvedRevision, null, null, null, staging,
					TransportDeadlineMilliseconds: deadlineMilliseconds));
				if (remoteCandidate.Status == KnowledgeTransportStatus.Downloaded) {
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
					update = trustedCandidate ? "available" : "rejected";
					diagnostic ??= trustedCandidate
						? null
						: validation.Diagnostic ?? "The remote candidate failed verification.";
				}
				else {
					update = remoteCandidate.Status == KnowledgeTransportStatus.NoCandidate ? "up-to-date" : "unknown";
					diagnostic ??= remoteCandidate.Status is KnowledgeTransportStatus.Rejected
						or KnowledgeTransportStatus.Failed
						? Safe(remoteCandidate.Diagnostic ?? "The remote knowledge source could not be checked.")
						: null;
				}
			} catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException) {
				diagnostic ??= Safe(exception.Message);
			} finally {
				DeleteTransportStaging(staging);
			}
		}
		return new KnowledgeSourceInfo(
			alias,
			source.LibraryId,
			source.Type.ToString().ToLowerInvariant(),
			source.Location,
			source.TrustedKeyId,
			source.TrustedPublicKeyPath,
			source.Enabled,
			source.Priority,
			source.Participation.ToString().ToLowerInvariant(),
			source.PackageId,
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

	private KnowledgeSourceInfo BuildRepositoryInfo(
		string alias,
		KnowledgeSourceConfiguration source,
		bool checkUpdates,
		int deadlineMilliseconds,
		IKnowledgeRepositoryTransport transport) {
		string repositoryPath = _store.GetGitRepositoryPath(alias, createSourceRoot: false);
		bool installed = _fileSystem.Directory.Exists(_fileSystem.Path.Combine(repositoryPath, ".git"));
		KnowledgeGitRepositorySnapshot? snapshot = null;
		string? diagnostic = null;
		bool valid = false;
		string? revision = null;
		string update = installed ? "unknown" : "not-installed";
		if (installed) {
			try {
				bool acquired = _store.TryExecuteWithSourceMutationLock(alias, () => {
					transport.ValidateInstalledCheckout(source, repositoryPath);
					valid = _gitReader.TryRead(repositoryPath, source.LibraryId, out snapshot, out diagnostic);
					revision = transport.GetCurrentRevision(repositoryPath);
				});
				if (!acquired) {
					update = "synchronizing";
					diagnostic = $"Git knowledge source '{alias}' is synchronizing; retry the information request.";
				}
				else if (checkUpdates && source.Enabled) {
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
					update = remote.Status switch {
						KnowledgeTransportStatus.Downloaded => "available",
						KnowledgeTransportStatus.NoCandidate => "up-to-date",
						_ => "unknown"
					};
					if (remote.Status is KnowledgeTransportStatus.Failed or KnowledgeTransportStatus.Rejected) {
						diagnostic ??= Safe(remote.Diagnostic
							?? "The remote Git knowledge source could not be checked.");
					}
				}
			} catch (Exception exception) when (exception is IOException
					or UnauthorizedAccessException
					or InvalidOperationException
					or InvalidDataException
					or ArgumentException
					or TimeoutException) {
				diagnostic = Safe(exception.Message);
			}
		}
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
			source.Branch,
			source.Tag,
			source.Commit,
			installed,
			valid,
			valid ? snapshot!.LibraryVersion : null,
			valid ? snapshot!.Sequence : null,
			valid ? snapshot!.ContentDigest : null,
			revision,
			installed ? repositoryPath : null,
			update,
			diagnostic);
	}

	private KnowledgeSourceBatchResult ExecuteLifecycle(
		string? sourceAlias,
		bool includeDisabledWhenExplicit,
		Func<string, KnowledgeSourceConfiguration, int, KnowledgeSourceOperationResult> operation,
		CancellationToken cancellationToken) {
		try {
			KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
			if (!TrySelect(configuration, sourceAlias, includeDisabledWhenExplicit,
					out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
					out string? diagnostic)) {
				return new KnowledgeSourceBatchResult(false, diagnostic!, Array.Empty<KnowledgeSourceOperationResult>());
			}
			KnowledgeSourceOperationResult[] results = ExecuteBounded(
				selected,
				(pair, deadlineMilliseconds) => operation(pair.Key, pair.Value, deadlineMilliseconds),
				pair => FailedOperation(pair.Key,
					"Knowledge operation timed out before this source was processed."),
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
		CancellationToken cancellationToken) where TResult : class {
		cancellationToken.ThrowIfCancellationRequested();
		if (selected.Count <= 1) {
			return selected.Count == 0 ? [] : [operation(selected[0], OperationDeadlineMilliseconds)];
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
				int operationDeadlineMilliseconds = Math.Min(
					OperationDeadlineMilliseconds,
					remainingBatchMilliseconds);
				results[index] = operation(selected[index], operationDeadlineMilliseconds);
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
		source.Type.ToString().ToLowerInvariant(),
		source.Location,
		source.TrustedKeyId,
		source.TrustedPublicKeyPath,
		source.Enabled,
		source.Priority,
		source.Participation.ToString().ToLowerInvariant(),
		source.PackageId,
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
		"unknown",
		diagnostic);

	private static KnowledgeSourceInfo ConfiguredInfo(
		string alias,
		KnowledgeSourceConfiguration source) => UnavailableInfo(alias, source, diagnostic: null) with {
		UpdateAvailability = null
	};

	private static bool TrySelect(
		KnowledgeConfiguration configuration,
		string? sourceAlias,
		bool includeDisabledWhenExplicit,
		out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
		out string? diagnostic) {
		if (sourceAlias is not null) {
			if (!configuration.Sources.TryGetValue(sourceAlias, out KnowledgeSourceConfiguration? source)) {
				selected = Array.Empty<KeyValuePair<string, KnowledgeSourceConfiguration>>();
				diagnostic = $"Knowledge source '{sourceAlias}' is not configured.";
				return false;
			}
			if (!source.Enabled && !includeDisabledWhenExplicit) {
				selected = Array.Empty<KeyValuePair<string, KnowledgeSourceConfiguration>>();
				diagnostic = $"Knowledge source '{sourceAlias}' is disabled.";
				return false;
			}
			selected = [new KeyValuePair<string, KnowledgeSourceConfiguration>(sourceAlias, source)];
			diagnostic = null;
			return true;
		}
		selected = configuration.Sources
			.Where(pair => pair.Value.Enabled)
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (selected.Count == 0) {
			diagnostic = "No enabled knowledge sources are configured.";
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
		_ => throw new ArgumentException("Knowledge source type must be 'git' or 'nuget'.", nameof(value))
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
}
