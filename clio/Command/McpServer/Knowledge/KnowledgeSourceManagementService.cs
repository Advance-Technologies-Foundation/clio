using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeSourceManagementService : IKnowledgeSourceManagementService {
	private const int MaxCandidateAttempts = 64;
	private const int OperationDeadlineMilliseconds = 30_000;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IKnowledgeSourceInstallationStore _store;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeTransport> _transports;
	private readonly IFileSystem _fileSystem;

	public KnowledgeSourceManagementService(
		ISettingsRepository settingsRepository,
		IKnowledgeSourceInstallationStore store,
		IKnowledgeBundleRuntime runtime,
		IEnumerable<IKnowledgeTransport> transports,
		IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		ArgumentNullException.ThrowIfNull(transports);
		_transports = transports
			.GroupBy(transport => transport.Type)
			.ToDictionary(
				group => group.Key,
				group => {
					IKnowledgeTransport[] implementations = group
						.GroupBy(transport => transport.GetType())
						.Select(candidates => candidates.First())
						.ToArray();
					return implementations.Length == 1
						? implementations[0]
						: throw new InvalidOperationException(
							$"Multiple knowledge transports are registered for '{group.Key}'.");
				});
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public KnowledgeSourceBatchResult Install(string? sourceAlias) => ExecuteLifecycle(
		sourceAlias,
		includeDisabledWhenExplicit: false,
		(alias, source) => InstallOrUpdate(alias, source, isUpdate: false));

	public KnowledgeSourceBatchResult Update(string? sourceAlias) => ExecuteLifecycle(
		sourceAlias,
		includeDisabledWhenExplicit: false,
		(alias, source) => InstallOrUpdate(alias, source, isUpdate: true));

	public KnowledgeSourceInfoResult GetInfo(string? sourceAlias, bool checkUpdates) {
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
		List<KnowledgeSourceInfo> sources = [];
		foreach ((string alias, KnowledgeSourceConfiguration source) in selected) {
			sources.Add(BuildInfo(alias, source, checkUpdates));
		}
		return new KnowledgeSourceInfoResult(
			true,
			_settingsRepository.AppSettingsFilePath,
			_store.GetRootPath(),
			sources,
			null);
	}

	public KnowledgeSourceBatchResult Delete(string? sourceAlias, bool confirmed) => ExecuteLifecycle(
		sourceAlias,
		includeDisabledWhenExplicit: true,
		(alias, _) => ToOperation(alias, _store.Delete(alias, confirmed)));

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
				ArtifactPath = request.ArtifactPath,
				Enabled = request.Enabled,
				Priority = request.Priority,
				Participation = ParseParticipation(request.Participation)
			};
			KnowledgeSourceConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(source);
			if (!EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
					validated.TrustedPublicKeyPath,
					out _)) {
				return Failed(request.Alias,
					"Knowledge trusted-public-key-path must identify an existing bounded local regular file "
					+ "containing one P-256 PUBLIC KEY PEM; network, device, reparse, and private-key files are refused.");
			}
			KnowledgeConfiguration existing = _settingsRepository.GetKnowledgeConfiguration();
			if (existing.Sources.ContainsKey(request.Alias)) {
				return Failed(request.Alias, $"Knowledge source alias '{request.Alias}' is already configured.");
			}
			if (existing.Sources.Values.Any(candidate =>
					string.Equals(candidate.LibraryId, validated.LibraryId, StringComparison.OrdinalIgnoreCase))) {
				return Failed(request.Alias,
					$"Knowledge library '{validated.LibraryId}' is already configured under another alias.");
			}
			_settingsRepository.UpsertKnowledgeSource(request.Alias, validated);
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
		if (!_settingsRepository.TryRemoveKnowledgeSource(sourceAlias, expected)) {
			return Failed(sourceAlias, $"Knowledge source '{sourceAlias}' changed while it was being removed; retry.");
		}
		_runtime.DeactivateLibrary(sourceAlias);
		KnowledgeInstallationResult deletion = _store.Delete(sourceAlias, confirmed: true);
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
				.Select(pair => BuildInfo(pair.Key, pair.Value, checkUpdates: false))
				.ToArray();
			return new KnowledgeSourceListResult(true, sources);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException) {
			return new KnowledgeSourceListResult(false, Array.Empty<KnowledgeSourceInfo>(), Safe(exception.Message));
		}
	}

	private KnowledgeSourceOperationResult InstallOrUpdate(
		string alias,
		KnowledgeSourceConfiguration source,
		bool isUpdate) {
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
		if (!_transports.TryGetValue(source.Type, out IKnowledgeTransport? transport)) {
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
				int remainingMilliseconds = OperationDeadlineMilliseconds - (int)Math.Min(
					operation.ElapsedMilliseconds,
					OperationDeadlineMilliseconds);
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
				highestObserved = source.Type == KnowledgeSourceType.NuGet
					? KnowledgeBundleNuGetClient.GreaterVersion(highestObserved, revision)
					: highestObserved ?? revision;
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
					expectedBundleVersion: source.Type == KnowledgeSourceType.NuGet ? revision : null,
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
				if (published.IsSuccess
						&& source.Type == KnowledgeSourceType.Git
						&& source.Branch is null
						&& !string.IsNullOrWhiteSpace(retrieved.ResolvedBranch)
						&& !_settingsRepository.TrySetKnowledgeSourceBranch(alias, source, retrieved.ResolvedBranch)) {
					return FailedOperation(alias,
						$"Knowledge source '{alias}' changed while its discovered branch was being persisted; retry.");
				}
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
		bool checkUpdates) {
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
		if (checkUpdates && source.Enabled && _transports.TryGetValue(source.Type, out IKnowledgeTransport? transport)) {
			string staging = CreateTransportStaging(alias);
			try {
				KnowledgeTransportResult remoteCandidate = transport.Retrieve(new KnowledgeTransportRequest(
					alias, source, new HashSet<string>(), current?.Active.ResolvedRevision, null, null, null, staging,
					TransportDeadlineMilliseconds: OperationDeadlineMilliseconds));
				if (remoteCandidate.Status == KnowledgeTransportStatus.Downloaded) {
					byte[] candidateBytes = ReadCandidate(remoteCandidate);
					using MemoryStream stream = new(candidateBytes, writable: false);
					KnowledgeBundleValidationResult validation = _runtime.Validate(
						stream,
						expectedBundleVersion: source.Type == KnowledgeSourceType.NuGet
							? remoteCandidate.ResolvedRevision
							: null,
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

	private KnowledgeSourceBatchResult ExecuteLifecycle(
		string? sourceAlias,
		bool includeDisabledWhenExplicit,
		Func<string, KnowledgeSourceConfiguration, KnowledgeSourceOperationResult> operation) {
		try {
			KnowledgeConfiguration configuration = _settingsRepository.GetKnowledgeConfiguration();
			if (!TrySelect(configuration, sourceAlias, includeDisabledWhenExplicit,
					out IReadOnlyList<KeyValuePair<string, KnowledgeSourceConfiguration>> selected,
					out string? diagnostic)) {
				return new KnowledgeSourceBatchResult(false, diagnostic!, Array.Empty<KnowledgeSourceOperationResult>());
			}
			KnowledgeSourceOperationResult[] results = selected
				.Select(pair => operation(pair.Key, pair.Value))
				.ToArray();
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
}
