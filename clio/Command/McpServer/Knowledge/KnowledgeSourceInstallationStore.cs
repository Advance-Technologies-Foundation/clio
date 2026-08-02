using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

internal sealed record KnowledgeInstallationStoreOptions(int LockTimeoutMilliseconds);

internal sealed record KnowledgeSourceGenerationPointer(
	string LibraryId,
	string LibraryVersion,
	ulong Sequence,
	string RelativePath,
	string BundleDigest,
	string ResolvedRevision,
	DateTimeOffset ActivatedAtUtc);

internal sealed record KnowledgeSourceCurrentState(
	int SchemaVersion,
	string SourceAlias,
	KnowledgeSourceGenerationPointer Active,
	KnowledgeSourceGenerationPointer? Previous);

internal sealed record KnowledgeSourceInstallMetadata(
	int SchemaVersion,
	string SourceAlias,
	string LibraryId,
	string LibraryVersion,
	ulong Sequence,
	string TransportType,
	string Location,
	string ResolvedRevision,
	string BundleDigest,
	DateTimeOffset InstalledAtUtc);

internal sealed record InstalledKnowledgeSourceCandidate(
	KnowledgeSourceGenerationPointer Pointer,
	string ContentRoot,
	byte[] BundleBytes);

internal sealed record KnowledgeLibraryHighWaterMark(
	int SchemaVersion,
	string LibraryId,
	ulong Sequence,
	string BundleDigest);

internal interface IKnowledgeSourceInstallationStore {
	string GetRootPath();

	string GetGitRepositoryPath(string sourceAlias, bool createSourceRoot);

	/// <summary>
	/// Returns the git repository path for <paramref name="sourceAlias"/> only when that directory
	/// has already been materialized on disk, and <see langword="null"/> otherwise.
	/// </summary>
	/// <remarks>
	/// This is the "is the checkout there at all" probe used by activation, which must skip a source
	/// whose directory is absent. It is deliberately weaker than the installation probe in
	/// <c>KnowledgeSourceManagementService</c>, which additionally requires a <c>.git</c> marker
	/// before it will treat a checkout as installed.
	/// </remarks>
	/// <param name="sourceAlias">The configured knowledge source alias.</param>
	/// <returns>The repository path when it exists; otherwise <see langword="null"/>.</returns>
	string? GetInstalledGitRepositoryPath(string sourceAlias);

	/// <summary>
	/// Reports whether <paramref name="sourceAlias"/> already has a materialized Git checkout.
	/// </summary>
	/// <remarks>
	/// A directory-marker probe only: it spawns no Git process and reads no history, so it is safe
	/// on a bounded startup path where a full inspection is not. It answers "is there something to
	/// activate", not "is that checkout valid" — validation belongs to activation, which runs
	/// without blocking on the source mutation lock and falls back when a checkout is unusable.
	/// </remarks>
	/// <param name="sourceAlias">The configured knowledge source alias.</param>
	/// <returns><see langword="true"/> when a Git checkout is present.</returns>
	bool IsGitRepositoryInstalled(string sourceAlias);

	bool TryMigrateGitRepository(string sourceAlias, string targetAlias);

	bool MigrateGitRepository(string sourceAlias, string targetAlias);

	T ExecuteWithSourceMutationLock<T>(string sourceAlias, Func<T> action);

	bool TryExecuteWithSourceMutationLock(string sourceAlias, Action action);

	KnowledgeSourceCurrentState? ReadCurrent(string sourceAlias, out string? diagnostic);

	bool TryReadCandidate(
		string sourceAlias,
		KnowledgeSourceGenerationPointer pointer,
		out InstalledKnowledgeSourceCandidate? candidate,
		out string? diagnostic);

	KnowledgeInstallationResult Publish(KnowledgeGenerationPublication publication);

	KnowledgeInstallationResult Delete(string sourceAlias, bool confirmed);

	KnowledgeSourceInstallMetadata? ReadMetadata(
		string sourceAlias,
		KnowledgeSourceCurrentState state,
		out string? diagnostic);
}

internal sealed class KnowledgeSourceInstallationStore : IKnowledgeSourceInstallationStore {
	// Everything one publication attempt needs. Private on purpose: this is the store's internal calling
	// convention, not part of IKnowledgeSourceInstallationStore, whose signature is fixed by its callers.
	private sealed record KnowledgePublicationRequest(
		string SourceRoot,
		string SourceAlias,
		string LibraryId,
		string LibraryVersion,
		ulong Sequence,
		string TransportType,
		string Location,
		string ResolvedRevision,
		byte[] BundleBytes,
		bool IsUpdate,
		KnowledgeSourceGenerationPointer? ExpectedActive,
		bool AllowRepair);

	// Where a single immutable generation lives on disk. Resolved once by the caller so that the
	// containment-checked generation root is never derived twice from different inputs.
	private sealed record KnowledgeGenerationLocation(
		string GenerationsRoot,
		string GenerationRoot,
		string Name);

	// What an already-present generation must match before it may be treated as a recoverable duplicate.
	private sealed record KnowledgeGenerationIdentity(
		string SourceAlias,
		string LibraryId,
		ulong Sequence,
		string Digest);

	private const int SchemaVersion = 1;
	private const int MaxMarkerBytes = 64 * 1024;
	private const int MaxBundleBytes = 40 * 1024 * 1024;
	private const int MaxArchiveEntries = 1024;
	private const string RootOwnerFileName = ".clio-knowledge-root";
	private const string RootOwnerContent = "clio-knowledge-store-v1\n";
	private const string SourceOwnerFileName = ".clio-knowledge-source";
	private const string CurrentFileName = "current.json";
	private const string LocksDirectoryName = ".locks";
	private const string HistoryDirectoryName = ".history";
	private const string BundleFileName = "bundle.zip";
	private const string MetadataFileName = "install.json";
	private const string SourcesDirectoryName = "sources";
	private const string GenerationsDirectoryName = "generations";
	private const string StagingDirectoryName = "staging";
	private static readonly ConcurrentDictionary<string, object> ProcessLocks = new(
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	private readonly IKnowledgeRootPathProvider _rootPathProvider;
	private readonly IFileSystem _fileSystem;
	private readonly KnowledgeInstallationStoreOptions _options;

	public KnowledgeSourceInstallationStore(
		IKnowledgeRootPathProvider rootPathProvider,
		IFileSystem fileSystem,
		KnowledgeInstallationStoreOptions options) {
		_rootPathProvider = rootPathProvider ?? throw new ArgumentNullException(nameof(rootPathProvider));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.LockTimeoutMilliseconds);
	}

	public string GetRootPath() => _rootPathProvider.GetOrCreateRoot();

	public string GetGitRepositoryPath(string sourceAlias, bool createSourceRoot) {
		KnowledgeSourceConfigurationValidator.ValidateAlias(sourceAlias);
		string sourceRoot = ResolveSourceRoot(sourceAlias, createSourceRoot);
		string repositoryPath = ResolveChild(sourceRoot, "repository");
		if (_fileSystem.Directory.Exists(repositoryPath)) {
			EnsureNoReparsePoint(sourceRoot, repositoryPath);
		}
		return repositoryPath;
	}

	public string? GetInstalledGitRepositoryPath(string sourceAlias) {
		string repositoryPath = GetGitRepositoryPath(sourceAlias, createSourceRoot: false);
		return _fileSystem.Directory.Exists(repositoryPath) ? repositoryPath : null;
	}

	public bool IsGitRepositoryInstalled(string sourceAlias) {
		string repositoryPath = GetGitRepositoryPath(sourceAlias, createSourceRoot: false);
		return _fileSystem.Directory.Exists(_fileSystem.Path.Combine(repositoryPath, ".git"));
	}

	public bool TryMigrateGitRepository(string sourceAlias, string targetAlias) {
		return MigrateGitRepositoryWithLocks(sourceAlias, targetAlias, waitForLocks: false);
	}

	public bool MigrateGitRepository(string sourceAlias, string targetAlias) {
		return MigrateGitRepositoryWithLocks(sourceAlias, targetAlias, waitForLocks: true);
	}

	private bool MigrateGitRepositoryWithLocks(string sourceAlias, string targetAlias, bool waitForLocks) {
		KnowledgeSourceConfigurationValidator.ValidateAlias(sourceAlias);
		KnowledgeSourceConfigurationValidator.ValidateAlias(targetAlias);
		if (string.Equals(sourceAlias, targetAlias, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		string firstAlias = string.Compare(sourceAlias, targetAlias, StringComparison.OrdinalIgnoreCase) < 0
			? sourceAlias
			: targetAlias;
		string secondAlias = string.Equals(firstAlias, sourceAlias, StringComparison.OrdinalIgnoreCase)
			? targetAlias
			: sourceAlias;
		if (waitForLocks) {
			return ExecuteWithSourceMutationLock(firstAlias, () =>
				ExecuteWithSourceMutationLock(secondAlias, () => MigrateGitRepositoryCore(sourceAlias, targetAlias)));
		}
		bool migrated = false;
		bool secondLockAcquired = false;
		bool firstLockAcquired = TryExecuteWithSourceMutationLock(firstAlias, () => {
			secondLockAcquired = TryExecuteWithSourceMutationLock(secondAlias, () =>
				migrated = MigrateGitRepositoryCore(sourceAlias, targetAlias));
		});
		return firstLockAcquired && secondLockAcquired && migrated;
	}

	private bool MigrateGitRepositoryCore(string sourceAlias, string targetAlias) {
		string sourceRepository = GetGitRepositoryPath(sourceAlias, createSourceRoot: false);
		string targetRepository = GetGitRepositoryPath(targetAlias, createSourceRoot: true);
		if (_fileSystem.Directory.Exists(_fileSystem.Path.Combine(targetRepository, ".git"))) {
			return true;
		}
		if (!_fileSystem.Directory.Exists(_fileSystem.Path.Combine(sourceRepository, ".git"))) {
			return false;
		}
		if (_fileSystem.Directory.Exists(targetRepository)) {
			if (_fileSystem.Directory.EnumerateFileSystemEntries(targetRepository).Any()) {
				throw new InvalidOperationException(
					$"Knowledge repository target '{targetAlias}' is not empty.");
			}
			_fileSystem.Directory.Delete(targetRepository);
		}
		_fileSystem.Directory.Move(sourceRepository, targetRepository);
		return true;
	}

	public T ExecuteWithSourceMutationLock<T>(string sourceAlias, Func<T> action) {
		ArgumentNullException.ThrowIfNull(action);
		string sourceRoot = ResolveSourceRoot(sourceAlias, create: true);
		return WithMutationLock(sourceRoot, action);
	}

	public bool TryExecuteWithSourceMutationLock(string sourceAlias, Action action) {
		ArgumentNullException.ThrowIfNull(action);
		string sourceRoot = ResolveSourceRoot(sourceAlias, create: true);
		return TryWithMutationLock(sourceRoot, action);
	}

	public KnowledgeSourceCurrentState? ReadCurrent(string sourceAlias, out string? diagnostic) {
		try {
			string sourceRoot = ResolveSourceRoot(sourceAlias, create: false);
			KnowledgeSourceCurrentState? state = ReadCurrentMarker(sourceAlias, sourceRoot, out diagnostic);
			if (state is null || diagnostic is not null) {
				return null;
			}
			KnowledgeLibraryHighWaterMark? highWater = ReadHighWater(sourceRoot, state.Active.LibraryId);
			if (!ConflictsWithHighWater(state.Active, highWater)) {
				diagnostic = null;
				return state;
			}
			KnowledgeSourceCurrentState? recovered = null;
			string? recoveryDiagnostic = null;
			WithMutationLock(sourceRoot, () => WithLibraryMutationLock(sourceRoot, state.Active.LibraryId, () => {
				recovered = ReconcileInterruptedPublication(sourceRoot, sourceAlias, out recoveryDiagnostic);
				return true;
			}));
			diagnostic = recoveryDiagnostic;
			return recovered;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Knowledge source '{sourceAlias}' activation marker could not be read: {exception.Message}";
			return null;
		}
	}

	private KnowledgeSourceCurrentState? ReadCurrentMarker(
		string sourceAlias,
		string sourceRoot,
		out string? diagnostic) {
		if (!_fileSystem.Directory.Exists(sourceRoot)) {
			diagnostic = null;
			return null;
		}
		ValidateSourceRoot(sourceAlias, sourceRoot);
		string markerPath = ResolveChild(sourceRoot, CurrentFileName);
		if (!_fileSystem.File.Exists(markerPath)) {
			diagnostic = null;
			return null;
		}
		EnsureNoReparsePoint(sourceRoot, markerPath);
		KnowledgeSourceCurrentState? state = JsonSerializer.Deserialize(
			ReadBoundedFile(markerPath, MaxMarkerBytes),
			KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState);
		if (state is null
				|| state.SchemaVersion != SchemaVersion
				|| !string.Equals(state.SourceAlias, sourceAlias, StringComparison.OrdinalIgnoreCase)
				|| !IsValidPointer(state.Active)) {
			diagnostic = $"Knowledge source '{sourceAlias}' activation marker is invalid.";
			return null;
		}
		diagnostic = null;
		return state;
	}

	private KnowledgeSourceCurrentState? ReconcileInterruptedPublication(
		string sourceRoot,
		string sourceAlias,
		out string? diagnostic) {
		KnowledgeSourceCurrentState? current = ReadCurrentMarker(sourceAlias, sourceRoot, out diagnostic);
		if (current is null || diagnostic is not null) {
			return null;
		}
		KnowledgeLibraryHighWaterMark? highWater = ReadHighWater(sourceRoot, current.Active.LibraryId);
		if (!ConflictsWithHighWater(current.Active, highWater)) {
			diagnostic = null;
			return current;
		}
		if (highWater.Sequence <= current.Active.Sequence) {
			diagnostic = $"Knowledge source '{sourceAlias}' activation marker conflicts with accepted library sequence "
				+ $"{highWater.Sequence} and cannot be recovered automatically.";
			return null;
		}

		string generationsRoot = ResolveChild(sourceRoot, GenerationsDirectoryName);
		string generationName = $"{highWater.Sequence}-{highWater.BundleDigest[..12]}";
		string generationRoot = ResolveChild(generationsRoot, generationName);
		if (!_fileSystem.Directory.Exists(generationRoot)) {
			diagnostic = $"Knowledge source '{sourceAlias}' accepted generation '{generationName}' is missing; "
				+ "activation cannot be recovered automatically.";
			return null;
		}
		KnowledgeGenerationLocation location = new(generationsRoot, generationRoot, generationName);
		KnowledgeGenerationIdentity expected = new(
			sourceAlias,
			current.Active.LibraryId,
			highWater.Sequence,
			highWater.BundleDigest);
		if (!TryReadRecoverableGeneration(
				location,
				expected,
				out KnowledgeSourceInstallMetadata? metadata,
				out diagnostic)) {
			return null;
		}

		KnowledgeSourceGenerationPointer active = new(
			metadata.LibraryId,
			metadata.LibraryVersion,
			metadata.Sequence,
			$"{GenerationsDirectoryName}/{generationName}",
			metadata.BundleDigest,
			metadata.ResolvedRevision,
			DateTimeOffset.UtcNow);
		KnowledgeSourceCurrentState recovered = new(
			SchemaVersion,
			sourceAlias,
			active,
			current.Active);
		WriteAtomicJson(sourceRoot, CurrentFileName, JsonSerializer.SerializeToUtf8Bytes(
			recovered,
			KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));
		Prune(generationsRoot, recovered);
		diagnostic = null;
		return recovered;
	}

	private static bool ConflictsWithHighWater(
		KnowledgeSourceGenerationPointer pointer,
		KnowledgeLibraryHighWaterMark? highWater) => highWater is not null
		&& ConflictsWithAccepted(pointer.Sequence, pointer.BundleDigest, highWater.Sequence, highWater.BundleDigest);

	// A candidate conflicts when it moves the sequence backwards, or replays an already accepted
	// sequence with different content; the same sequence with the same digest is an idempotent retry.
	private static bool ConflictsWithAccepted(
		ulong sequence,
		string digest,
		ulong acceptedSequence,
		string acceptedDigest) => sequence < acceptedSequence
		|| (sequence == acceptedSequence && !string.Equals(digest, acceptedDigest, StringComparison.Ordinal));

	private static bool IsValidPointer(KnowledgeSourceGenerationPointer pointer) =>
		!string.IsNullOrWhiteSpace(pointer.LibraryId)
		&& !string.IsNullOrWhiteSpace(pointer.LibraryVersion)
		&& pointer.Sequence > 0
		&& !string.IsNullOrWhiteSpace(pointer.RelativePath)
		&& !string.IsNullOrWhiteSpace(pointer.BundleDigest)
		&& pointer.BundleDigest.Length == 64
		&& pointer.BundleDigest.All(Uri.IsHexDigit)
		&& !string.IsNullOrWhiteSpace(pointer.ResolvedRevision);

	public bool TryReadCandidate(
		string sourceAlias,
		KnowledgeSourceGenerationPointer pointer,
		out InstalledKnowledgeSourceCandidate? candidate,
		out string? diagnostic) {
		candidate = null;
		try {
			string sourceRoot = ResolveSourceRoot(sourceAlias, create: false);
			ValidateSourceRoot(sourceAlias, sourceRoot);
			string generationRoot = ResolveRelative(sourceRoot, pointer.RelativePath);
			EnsureNoReparsePoint(sourceRoot, generationRoot);
			byte[] bytes = ReadBoundedFile(ResolveChild(generationRoot, BundleFileName), MaxBundleBytes);
			if (!string.Equals(ComputeDigest(bytes), pointer.BundleDigest, StringComparison.Ordinal)) {
				diagnostic = $"Installed knowledge source '{sourceAlias}' does not match its activation digest.";
				return false;
			}
			candidate = new InstalledKnowledgeSourceCandidate(pointer, generationRoot, bytes);
			diagnostic = null;
			return true;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Installed knowledge source '{sourceAlias}' could not be read: {exception.Message}";
			return false;
		}
	}

	public KnowledgeInstallationResult Publish(KnowledgeGenerationPublication publication) {
		ArgumentNullException.ThrowIfNull(publication);
		string sourceAlias = publication.SourceAlias;
		string libraryId = publication.LibraryId;
		string libraryVersion = publication.LibraryVersion;
		ulong sequence = publication.Sequence;
		string transportType = publication.TransportType;
		string location = publication.Location;
		string resolvedRevision = publication.ResolvedRevision;
		byte[] bundleBytes = publication.BundleBytes;
		bool isUpdate = publication.IsUpdate;
		KnowledgeSourceGenerationPointer? expectedActive = publication.ExpectedActive;
		bool allowRepair = publication.AllowRepair;
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryVersion);
		ArgumentException.ThrowIfNullOrWhiteSpace(transportType);
		ArgumentException.ThrowIfNullOrWhiteSpace(location);
		ArgumentException.ThrowIfNullOrWhiteSpace(resolvedRevision);
		ArgumentNullException.ThrowIfNull(bundleBytes);
		if (sequence == 0 || bundleBytes.Length == 0 || bundleBytes.Length > MaxBundleBytes) {
			return Failed("Knowledge generation is outside supported bounds.");
		}

		string sourceRoot = ResolveSourceRoot(sourceAlias, create: true);
		KnowledgePublicationRequest request = new(
			sourceRoot,
			sourceAlias,
			libraryId,
			libraryVersion,
			sequence,
			transportType,
			location,
			resolvedRevision,
			bundleBytes,
			isUpdate,
			expectedActive,
			allowRepair);
		return WithMutationLock(sourceRoot,
			() => WithLibraryMutationLock(sourceRoot, libraryId, () => PublishLocked(request)));
	}

	public KnowledgeInstallationResult Delete(string sourceAlias, bool confirmed) {
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
		if (!confirmed) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.ConfirmationRequired,
				$"Deleting installed knowledge for source '{sourceAlias}' requires explicit confirmation.",
				RootPath: GetRootPath());
		}
		string sourceRoot = ResolveSourceRoot(sourceAlias, create: false);
		if (!_fileSystem.Directory.Exists(sourceRoot)) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.NotInstalled,
				$"Knowledge source '{sourceAlias}' is not installed.",
				RootPath: GetRootPath());
		}
		ValidateSourceRoot(sourceAlias, sourceRoot);
		return WithMutationLock(sourceRoot, () => {
			ValidateSourceRoot(sourceAlias, sourceRoot);
			_fileSystem.Directory.Delete(sourceRoot, recursive: true);
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Deleted,
				$"Installed knowledge for source '{sourceAlias}' was deleted.",
				RootPath: GetRootPath());
		});
	}

	public KnowledgeSourceInstallMetadata? ReadMetadata(
		string sourceAlias,
		KnowledgeSourceCurrentState state,
		out string? diagnostic) {
		if (!TryReadCandidate(sourceAlias, state.Active, out InstalledKnowledgeSourceCandidate? candidate,
				out diagnostic)) {
			return null;
		}
		try {
			KnowledgeSourceInstallMetadata? metadata = JsonSerializer.Deserialize(
				ReadBoundedFile(ResolveChild(candidate.ContentRoot, MetadataFileName), MaxMarkerBytes),
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceInstallMetadata);
			if (metadata is null
					|| metadata.SchemaVersion != SchemaVersion
					|| metadata.Sequence != state.Active.Sequence
					|| !string.Equals(metadata.LibraryId, state.Active.LibraryId, StringComparison.Ordinal)
					|| !string.Equals(metadata.BundleDigest, state.Active.BundleDigest, StringComparison.Ordinal)) {
				diagnostic = $"Knowledge source '{sourceAlias}' metadata does not match its activation marker.";
				return null;
			}
			diagnostic = null;
			return metadata;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Knowledge source '{sourceAlias}' metadata could not be read: {exception.Message}";
			return null;
		}
	}

	private KnowledgeInstallationResult PublishLocked(KnowledgePublicationRequest request) {
		ValidateSourceRoot(request.SourceAlias, request.SourceRoot);
		KnowledgeSourceCurrentState? current =
			ReadCurrentMarker(request.SourceAlias, request.SourceRoot, out string? diagnostic);
		if (diagnostic is not null) {
			return Failed(diagnostic);
		}
		if (request.IsUpdate
				&& (current is null || request.ExpectedActive is null || current.Active != request.ExpectedActive)) {
			return Failed(
				$"Knowledge source '{request.SourceAlias}' changed while the operation was in progress; retry.");
		}
		string digest = ComputeDigest(request.BundleBytes);
		KnowledgeInstallationResult? refusal = EvaluateSequenceAcceptance(request, current, digest);
		if (refusal is not null) {
			return refusal;
		}

		bool repairingActive = current is not null && request.Sequence == current.Active.Sequence;
		string generationName = repairingActive
			? $"{request.Sequence}-{digest[..12]}-repair-{Guid.NewGuid():N}"
			: $"{request.Sequence}-{digest[..12]}";
		string generationsRoot = EnsureDirectory(request.SourceRoot, GenerationsDirectoryName);
		KnowledgeGenerationLocation location = new(
			generationsRoot,
			ResolveChild(generationsRoot, generationName),
			generationName);
		if (!TryMaterializeGeneration(request, current, digest, location, out string? materializationDiagnostic)) {
			return Failed(materializationDiagnostic);
		}

		KnowledgeSourceGenerationPointer active = new(
			request.LibraryId,
			request.LibraryVersion,
			request.Sequence,
			$"{GenerationsDirectoryName}/{generationName}",
			digest,
			request.ResolvedRevision,
			DateTimeOffset.UtcNow);
		KnowledgeSourceCurrentState next = new(
			SchemaVersion,
			request.SourceAlias,
			active,
			// A repair replaces the active generation in place, so the rollback target must stay the one
			// behind it. repairingActive is only ever true when current is not null.
			repairingActive ? current.Previous : current?.Active);
		WriteAtomicJson(request.SourceRoot, CurrentFileName, JsonSerializer.SerializeToUtf8Bytes(
			next, KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));
		Prune(location.GenerationsRoot, next);
		return new KnowledgeInstallationResult(
			request.IsUpdate ? KnowledgeInstallationStatus.Updated : KnowledgeInstallationStatus.Installed,
			$"Knowledge source '{request.SourceAlias}' sequence {request.Sequence} was installed at {location.GenerationRoot}.",
			request.LibraryVersion,
			GetRootPath());
	}

	// Decides whether the requested sequence may be installed at all. Returns the terminal outcome to
	// hand back to the caller (replay rejection, idempotent hit, or misuse), or null to keep going.
	private KnowledgeInstallationResult? EvaluateSequenceAcceptance(
		KnowledgePublicationRequest request,
		KnowledgeSourceCurrentState? current,
		string digest) {
		KnowledgeLibraryHighWaterMark? highWater = ReadHighWater(request.SourceRoot, request.LibraryId);
		if (highWater is not null
				&& ConflictsWithAccepted(request.Sequence, digest, highWater.Sequence, highWater.BundleDigest)) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Rejected,
				$"Knowledge library '{request.LibraryId}' rejected sequence {request.Sequence}; highest accepted sequence is {highWater.Sequence}.",
				request.LibraryVersion,
				GetRootPath());
		}
		if (current is null) {
			return null;
		}
		if (ConflictsWithAccepted(request.Sequence, digest, current.Active.Sequence, current.Active.BundleDigest)) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Rejected,
				$"Knowledge source '{request.SourceAlias}' rejected sequence {request.Sequence}; active sequence is {current.Active.Sequence}.",
				request.LibraryVersion,
				GetRootPath());
		}
		if (request.Sequence == current.Active.Sequence && !request.AllowRepair) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.AlreadyInstalled,
				$"Knowledge source '{request.SourceAlias}' sequence {request.Sequence} is already installed.",
				request.LibraryVersion,
				GetRootPath());
		}
		if (!request.IsUpdate) {
			return Failed($"Knowledge source '{request.SourceAlias}' is already installed; use update-knowledge.");
		}
		return null;
	}

	// Writes the generation into staging and promotes it with a single directory move, so the
	// generations directory only ever sees a complete generation.
	private bool TryMaterializeGeneration(
		KnowledgePublicationRequest request,
		KnowledgeSourceCurrentState? current,
		string digest,
		KnowledgeGenerationLocation location,
		out string? diagnostic) {
		string staging = EnsureDirectory(request.SourceRoot, StagingDirectoryName);
		if (_fileSystem.Directory.Exists(location.GenerationRoot)
				&& !TryRemoveRecoverableOrphan(location, current, request, digest, out diagnostic)) {
			return false;
		}
		string stagingRoot = ResolveChild(staging, $"{location.Name}-{Guid.NewGuid():N}");
		_fileSystem.Directory.CreateDirectory(stagingRoot);
		try {
			WriteGeneration(stagingRoot, request, digest);
			// The high-water mark has to be durable before the generation becomes visible: a crash in
			// that window is what ReconcileInterruptedPublication repairs. The reverse order is not
			// recoverable, so these three calls must keep this exact sequence.
			WriteHighWater(request.SourceRoot, new KnowledgeLibraryHighWaterMark(
				SchemaVersion,
				request.LibraryId,
				request.Sequence,
				digest));
			_fileSystem.Directory.Move(stagingRoot, location.GenerationRoot);
		} finally {
			if (_fileSystem.Directory.Exists(stagingRoot)) {
				_fileSystem.Directory.Delete(stagingRoot, recursive: true);
			}
		}
		diagnostic = null;
		return true;
	}

	private bool TryRemoveRecoverableOrphan(
		KnowledgeGenerationLocation location,
		KnowledgeSourceCurrentState? current,
		KnowledgePublicationRequest request,
		string digest,
		out string? diagnostic) {
		string relativePath = $"{GenerationsDirectoryName}/{location.Name}";
		if (current is not null
				&& (string.Equals(current.Active.RelativePath, relativePath, StringComparison.Ordinal)
					|| string.Equals(current.Previous?.RelativePath, relativePath, StringComparison.Ordinal))) {
			diagnostic = $"Immutable knowledge generation '{location.Name}' is already referenced by the activation marker.";
			return false;
		}
		try {
			KnowledgeGenerationIdentity expected = new(
				request.SourceAlias,
				request.LibraryId,
				request.Sequence,
				digest);
			if (!TryReadRecoverableGeneration(
					location,
					expected,
					out KnowledgeSourceInstallMetadata? metadata,
					out diagnostic)) {
				return false;
			}
			bool exactOrphan =
				string.Equals(metadata.LibraryVersion, request.LibraryVersion, StringComparison.Ordinal)
				&& string.Equals(metadata.TransportType, request.TransportType, StringComparison.Ordinal)
				&& string.Equals(metadata.Location, request.Location, StringComparison.Ordinal)
				&& string.Equals(metadata.ResolvedRevision, request.ResolvedRevision, StringComparison.Ordinal);
			if (!exactOrphan) {
				diagnostic = $"Immutable knowledge generation '{location.Name}' already exists with unexpected content.";
				return false;
			}
			_fileSystem.Directory.Delete(location.GenerationRoot, recursive: true);
			diagnostic = null;
			return true;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Immutable knowledge generation '{location.Name}' could not be recovered: {exception.Message}";
			return false;
		}
	}

	private bool TryReadRecoverableGeneration(
		KnowledgeGenerationLocation location,
		KnowledgeGenerationIdentity expected,
		out KnowledgeSourceInstallMetadata? metadata,
		out string? diagnostic) {
		metadata = null;
		try {
			EnsureTreeContainsNoReparsePoints(location.GenerationsRoot, location.GenerationRoot);
			byte[] bundle = ReadBoundedFile(ResolveChild(location.GenerationRoot, BundleFileName), MaxBundleBytes);
			metadata = JsonSerializer.Deserialize(
				ReadBoundedFile(ResolveChild(location.GenerationRoot, MetadataFileName), MaxMarkerBytes),
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceInstallMetadata);
			bool exactGeneration = string.Equals(ComputeDigest(bundle), expected.Digest, StringComparison.Ordinal)
				&& metadata is not null
				&& metadata.SchemaVersion == SchemaVersion
				&& string.Equals(metadata.SourceAlias, expected.SourceAlias, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(metadata.LibraryId, expected.LibraryId, StringComparison.Ordinal)
				&& metadata.Sequence == expected.Sequence
				&& string.Equals(metadata.BundleDigest, expected.Digest, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(metadata.LibraryVersion)
				&& !string.IsNullOrWhiteSpace(metadata.TransportType)
				&& !string.IsNullOrWhiteSpace(metadata.Location)
				&& !string.IsNullOrWhiteSpace(metadata.ResolvedRevision);
			if (!exactGeneration) {
				diagnostic = $"Immutable knowledge generation '{location.Name}' exists with unexpected content.";
				return false;
			}
			diagnostic = null;
			return true;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Immutable knowledge generation '{location.Name}' could not be recovered: {exception.Message}";
			return false;
		}
	}

	private void EnsureTreeContainsNoReparsePoints(string root, string directory) {
		EnsureNoReparsePoint(root, directory);
		Stack<string> pending = new();
		pending.Push(directory);
		while (pending.Count > 0) {
			foreach (string entry in _fileSystem.Directory.EnumerateFileSystemEntries(pending.Pop())) {
				FileAttributes attributes = _fileSystem.File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) != 0) {
					throw new InvalidOperationException(
						"Knowledge generation recovery cannot remove symbolic links or junctions.");
				}
				if ((attributes & FileAttributes.Directory) != 0) {
					pending.Push(entry);
				}
			}
		}
	}

	private void WriteGeneration(string stagingRoot, KnowledgePublicationRequest request, string digest) {
		_fileSystem.File.WriteAllBytes(ResolveChild(stagingRoot, BundleFileName), request.BundleBytes);
		using MemoryStream input = new(request.BundleBytes, writable: false);
		using ZipArchive archive = new(input, ZipArchiveMode.Read);
		if (archive.Entries.Count > MaxArchiveEntries) {
			throw new InvalidDataException("Knowledge archive contains too many entries.");
		}
		long extracted = 0;
		foreach (ZipArchiveEntry entry in archive.Entries) {
			if (IsSymbolicLink(entry)) {
				throw new InvalidDataException("Knowledge archive contains a symbolic-link entry.");
			}
			string relative = entry.FullName.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
			string destination = ResolveRelative(stagingRoot, relative);
			if (string.IsNullOrEmpty(entry.Name)) {
				_fileSystem.Directory.CreateDirectory(destination);
				continue;
			}
			if (entry.Length < 0 || extracted > MaxBundleBytes - entry.Length) {
				throw new InvalidDataException("Knowledge archive exceeds the extracted-size limit.");
			}
			extracted += entry.Length;
			string? parent = _fileSystem.Path.GetDirectoryName(destination);
			if (!string.IsNullOrWhiteSpace(parent)) {
				_fileSystem.Directory.CreateDirectory(parent);
			}
			using Stream source = entry.Open();
			using Stream target = _fileSystem.File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			source.CopyTo(target);
		}
		KnowledgeSourceInstallMetadata metadata = new(
			SchemaVersion, request.SourceAlias, request.LibraryId, request.LibraryVersion, request.Sequence,
			request.TransportType, request.Location, request.ResolvedRevision, digest, DateTimeOffset.UtcNow);
		_fileSystem.File.WriteAllBytes(ResolveChild(stagingRoot, MetadataFileName),
			JsonSerializer.SerializeToUtf8Bytes(
				metadata, KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceInstallMetadata));
	}

	private string ResolveSourceRoot(string sourceAlias, bool create) {
		KnowledgeSourceConfigurationValidator.ValidateAlias(sourceAlias);
		string root = GetRootPath();
		if (create) {
			EnsureOwnedRoot(root);
		}
		string sources = ResolveChild(root, SourcesDirectoryName);
		if (create) {
			_fileSystem.Directory.CreateDirectory(sources);
		}
		string sourceRoot = ResolveChild(sources, SourceKey(sourceAlias));
		if (create && !_fileSystem.Directory.Exists(sourceRoot)) {
			_fileSystem.Directory.CreateDirectory(sourceRoot);
			_fileSystem.File.WriteAllText(ResolveChild(sourceRoot, SourceOwnerFileName), sourceAlias + "\n");
		} else if (create) {
			ValidateSourceRoot(sourceAlias, sourceRoot);
		}
		return sourceRoot;
	}

	private void EnsureOwnedRoot(string root) {
		if (!_fileSystem.Directory.Exists(root)) {
			_fileSystem.Directory.CreateDirectory(root);
		}
		EnsureNoReparsePoint(root, root);
		string owner = ResolveChild(root, RootOwnerFileName);
		if (_fileSystem.File.Exists(owner)) {
			if (!string.Equals(_fileSystem.File.ReadAllText(owner), RootOwnerContent, StringComparison.Ordinal)) {
				throw new InvalidOperationException("Knowledge root ownership marker is invalid.");
			}
			return;
		}
		if (_fileSystem.Directory.EnumerateFileSystemEntries(root).Any()) {
			throw new InvalidOperationException("Knowledge root is non-empty and is not owned by Clio.");
		}
		_fileSystem.File.WriteAllText(owner, RootOwnerContent);
	}

	private void ValidateSourceRoot(string sourceAlias, string sourceRoot) {
		string root = GetRootPath();
		EnsureNoReparsePoint(root, sourceRoot);
		string marker = ResolveChild(sourceRoot, SourceOwnerFileName);
		if (!_fileSystem.File.Exists(marker)
				|| !string.Equals(_fileSystem.File.ReadAllText(marker), sourceAlias + "\n", StringComparison.Ordinal)) {
			throw new InvalidOperationException($"Knowledge source root '{sourceAlias}' is not owned by Clio.");
		}
	}

	private string EnsureDirectory(string root, string name) {
		string path = ResolveChild(root, name);
		_fileSystem.Directory.CreateDirectory(path);
		EnsureNoReparsePoint(root, path);
		return path;
	}

	private void Prune(string generationsRoot, KnowledgeSourceCurrentState state) {
		string[] retained = new[] { state.Active.RelativePath, state.Previous?.RelativePath }
			.Where(value => value is not null)
			.Select(value => _fileSystem.Path.GetFileName(value))
			.ToArray();
		// Materialize the victims before the first delete so the directory is never mutated mid-enumeration.
		string[] obsolete = _fileSystem.Directory.EnumerateDirectories(generationsRoot)
			.Where(directory => !retained.Contains(_fileSystem.Path.GetFileName(directory), StringComparer.Ordinal))
			.ToArray();
		foreach (string directory in obsolete) {
			EnsureNoReparsePoint(generationsRoot, directory);
			_fileSystem.Directory.Delete(directory, recursive: true);
		}
	}

	private void WriteAtomicJson(string root, string fileName, byte[] bytes) {
		string target = ResolveChild(root, fileName);
		string temporary = ResolveChild(root, $".{fileName}.{Guid.NewGuid():N}.tmp");
		try {
			_fileSystem.File.WriteAllBytes(temporary, bytes);
			_fileSystem.File.Move(temporary, target, overwrite: true);
		} finally {
			if (_fileSystem.File.Exists(temporary)) {
				_fileSystem.File.Delete(temporary);
			}
		}
	}

	// Locks and library history live next to the per-source directories, so every one of them is
	// anchored on the shared "sources" parent rather than on the source root itself.
	private string ResolveSourcesRoot(string sourceRoot) => _fileSystem.Path.GetDirectoryName(sourceRoot)
		?? throw new InvalidOperationException("Knowledge source root has no parent directory.");

	private T WithMutationLock<T>(string sourceRoot, Func<T> action) {
		string sourcesRoot = ResolveSourcesRoot(sourceRoot);
		string locksRoot = EnsureDirectory(sourcesRoot, LocksDirectoryName);
		string lockPath = ResolveChild(locksRoot, $"{_fileSystem.Path.GetFileName(sourceRoot)}.lock");
		object processLock = ProcessLocks.GetOrAdd(lockPath, _ => new object());
		if (!Monitor.TryEnter(processLock, _options.LockTimeoutMilliseconds)) {
			throw new TimeoutException("Timed out waiting for the knowledge source mutation lock.");
		}
		try {
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.LockTimeoutMilliseconds);
			FileSystemStream? stream = null;
			while (true) {
				try {
					stream = _fileSystem.File.Open(
						lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
					break;
				} catch (IOException) when (DateTime.UtcNow < deadline) {
					Thread.Sleep(25);
				}
			}
			using (stream) {
				return action();
			}
		} finally {
			Monitor.Exit(processLock);
		}
	}

	private bool TryWithMutationLock(string sourceRoot, Action action) {
		string sourcesRoot = ResolveSourcesRoot(sourceRoot);
		string locksRoot = EnsureDirectory(sourcesRoot, LocksDirectoryName);
		string lockPath = ResolveChild(locksRoot, $"{_fileSystem.Path.GetFileName(sourceRoot)}.lock");
		object processLock = ProcessLocks.GetOrAdd(lockPath, _ => new object());
		if (!Monitor.TryEnter(processLock)) {
			return false;
		}
		try {
			FileSystemStream? stream;
			try {
				stream = _fileSystem.File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			} catch (IOException) {
				return false;
			}
			using (stream) {
				action();
				return true;
			}
		} finally {
			Monitor.Exit(processLock);
		}
	}

	private T WithLibraryMutationLock<T>(string sourceRoot, string libraryId, Func<T> action) {
		string sourcesRoot = ResolveSourcesRoot(sourceRoot);
		string locksRoot = EnsureDirectory(sourcesRoot, LocksDirectoryName);
		string lockPath = ResolveChild(locksRoot, $"library-{SourceKey(libraryId)}.lock");
		object processLock = ProcessLocks.GetOrAdd(lockPath, _ => new object());
		if (!Monitor.TryEnter(processLock, _options.LockTimeoutMilliseconds)) {
			throw new TimeoutException("Timed out waiting for the knowledge library mutation lock.");
		}
		try {
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(_options.LockTimeoutMilliseconds);
			FileSystemStream? stream = null;
			while (true) {
				try {
					stream = _fileSystem.File.Open(
						lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
					break;
				} catch (IOException) when (DateTime.UtcNow < deadline) {
					Thread.Sleep(25);
				}
			}
			using (stream) {
				return action();
			}
		} finally {
			Monitor.Exit(processLock);
		}
	}

	private KnowledgeLibraryHighWaterMark? ReadHighWater(string sourceRoot, string libraryId) {
		string sourcesRoot = ResolveSourcesRoot(sourceRoot);
		string historyRoot = EnsureDirectory(sourcesRoot, HistoryDirectoryName);
		string path = ResolveChild(historyRoot, $"{SourceKey(libraryId)}.json");
		if (!_fileSystem.File.Exists(path)) {
			return null;
		}
		EnsureNoReparsePoint(historyRoot, path);
		KnowledgeLibraryHighWaterMark? mark = JsonSerializer.Deserialize(
			ReadBoundedFile(path, MaxMarkerBytes),
			KnowledgeSourceInstallationJsonContext.Default.KnowledgeLibraryHighWaterMark);
		if (mark is null
				|| mark.SchemaVersion != SchemaVersion
				|| mark.Sequence == 0
				|| !string.Equals(mark.LibraryId, libraryId, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(mark.BundleDigest)
				|| mark.BundleDigest.Length != 64
				|| !mark.BundleDigest.All(Uri.IsHexDigit)) {
			throw new InvalidDataException($"Knowledge library '{libraryId}' replay marker is invalid.");
		}
		return mark;
	}

	private void WriteHighWater(string sourceRoot, KnowledgeLibraryHighWaterMark mark) {
		string sourcesRoot = ResolveSourcesRoot(sourceRoot);
		string historyRoot = EnsureDirectory(sourcesRoot, HistoryDirectoryName);
		WriteAtomicJson(historyRoot, $"{SourceKey(mark.LibraryId)}.json", JsonSerializer.SerializeToUtf8Bytes(
			mark,
			KnowledgeSourceInstallationJsonContext.Default.KnowledgeLibraryHighWaterMark));
	}

	private string ResolveChild(string parent, string child) => ResolveRelative(parent, child);

	private string ResolveRelative(string parent, string relative) {
		string fullParent = _fileSystem.Path.GetFullPath(parent);
		string candidate = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(fullParent, relative));
		string prefix = fullParent.TrimEnd(_fileSystem.Path.DirectorySeparatorChar,
			_fileSystem.Path.AltDirectorySeparatorChar) + _fileSystem.Path.DirectorySeparatorChar;
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		if (!candidate.StartsWith(prefix, comparison)) {
			throw new InvalidDataException("Knowledge path escapes its managed root.");
		}
		return candidate;
	}

	private void EnsureNoReparsePoint(string root, string path) {
		string fullRoot = _fileSystem.Path.GetFullPath(root);
		string current = _fileSystem.Path.GetFullPath(path);
		while (current.StartsWith(fullRoot, OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal)) {
			if ((_fileSystem.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
				throw new InvalidOperationException("Knowledge storage paths cannot contain symbolic links or junctions.");
			}
			if (string.Equals(current, fullRoot, OperatingSystem.IsWindows()
					? StringComparison.OrdinalIgnoreCase
					: StringComparison.Ordinal)) {
				break;
			}
			current = _fileSystem.Path.GetDirectoryName(current);
		}
	}

	private byte[] ReadBoundedFile(string path, int maximumBytes) {
		using FileSystemStream stream = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length <= 0 || stream.Length > maximumBytes) {
			throw new IOException($"Knowledge file '{_fileSystem.Path.GetFileName(path)}' is outside supported bounds.");
		}
		byte[] bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly(bytes);
		return bytes;
	}

	private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
		((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

	private static string SourceKey(string sourceAlias) {
		byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(sourceAlias.ToLowerInvariant()));
		return Convert.ToHexString(digest).ToLowerInvariant()[..24];
	}

	private static string ComputeDigest(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private KnowledgeInstallationResult Failed(string message) => new(
		KnowledgeInstallationStatus.Failed,
		message,
		RootPath: GetRootPath());

	private static bool IsStorageException(Exception exception) => exception is IOException
		or UnauthorizedAccessException
		or InvalidOperationException
		or InvalidDataException
		or JsonException
		or NotSupportedException
		or TimeoutException;
}

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	WriteIndented = true,
	UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(KnowledgeSourceCurrentState))]
[JsonSerializable(typeof(KnowledgeSourceInstallMetadata))]
[JsonSerializable(typeof(KnowledgeLibraryHighWaterMark))]
internal sealed partial class KnowledgeSourceInstallationJsonContext : JsonSerializerContext;

/// <summary>
/// One generation being written to the local installation store: what is published, and how the
/// write relates to the generation already installed.
/// </summary>
internal sealed record KnowledgeGenerationPublication {
	/// <summary>Alias of the source the generation belongs to.</summary>
	internal required string SourceAlias { get; init; }

	/// <summary>Reverse-DNS library identity carried by the bundle.</summary>
	internal required string LibraryId { get; init; }

	/// <summary>Publisher-facing release label.</summary>
	internal required string LibraryVersion { get; init; }

	/// <summary>Monotonic generation counter; part of the canonical identity.</summary>
	internal required ulong Sequence { get; init; }

	/// <summary>Transport that retrieved the bundle, lowercased.</summary>
	internal required string TransportType { get; init; }

	/// <summary>Configured source location the bundle came from.</summary>
	internal required string Location { get; init; }

	/// <summary>Immutable revision the transport resolved.</summary>
	internal required string ResolvedRevision { get; init; }

	/// <summary>Bundle payload to persist.</summary>
	internal required byte[] BundleBytes { get; init; }

	/// <summary>Whether this replaces an installed generation rather than being a first install.</summary>
	internal required bool IsUpdate { get; init; }

	/// <summary>Generation expected to be active, used to detect a concurrent write.</summary>
	internal KnowledgeSourceGenerationPointer? ExpectedActive { get; init; }

	/// <summary>Whether the write may repair a checkout left inconsistent by an earlier failure.</summary>
	internal bool AllowRepair { get; init; }
}
