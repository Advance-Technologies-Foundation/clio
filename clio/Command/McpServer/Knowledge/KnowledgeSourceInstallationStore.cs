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

	KnowledgeInstallationResult Publish(
		string sourceAlias,
		string libraryId,
		string libraryVersion,
		ulong sequence,
		string transportType,
		string location,
		string resolvedRevision,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeSourceGenerationPointer? expectedActive,
		bool allowRepair = false);

	KnowledgeInstallationResult Delete(string sourceAlias, bool confirmed);

	KnowledgeSourceInstallMetadata? ReadMetadata(
		string sourceAlias,
		KnowledgeSourceCurrentState state,
		out string? diagnostic);
}

internal sealed class KnowledgeSourceInstallationStore : IKnowledgeSourceInstallationStore {
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
		if (highWater!.Sequence <= current.Active.Sequence) {
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
		if (!TryReadRecoverableGeneration(
				generationsRoot,
				generationRoot,
				generationName,
				sourceAlias,
				current.Active.LibraryId,
				highWater.Sequence,
				highWater.BundleDigest,
				out KnowledgeSourceInstallMetadata? metadata,
				out diagnostic)) {
			return null;
		}

		KnowledgeSourceGenerationPointer active = new(
			metadata!.LibraryId,
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
		&& (pointer.Sequence < highWater.Sequence
			|| (pointer.Sequence == highWater.Sequence
				&& !string.Equals(pointer.BundleDigest, highWater.BundleDigest, StringComparison.Ordinal)));

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

	public KnowledgeInstallationResult Publish(
		string sourceAlias,
		string libraryId,
		string libraryVersion,
		ulong sequence,
		string transportType,
		string location,
		string resolvedRevision,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeSourceGenerationPointer? expectedActive,
		bool allowRepair = false) {
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
		return WithMutationLock(sourceRoot, () => WithLibraryMutationLock(sourceRoot, libraryId, () => PublishLocked(
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
			allowRepair)));
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
				ReadBoundedFile(ResolveChild(candidate!.ContentRoot, MetadataFileName), MaxMarkerBytes),
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

	private KnowledgeInstallationResult PublishLocked(
		string sourceRoot,
		string sourceAlias,
		string libraryId,
		string libraryVersion,
		ulong sequence,
		string transportType,
		string location,
		string resolvedRevision,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeSourceGenerationPointer? expectedActive,
		bool allowRepair) {
		ValidateSourceRoot(sourceAlias, sourceRoot);
		KnowledgeSourceCurrentState? current = ReadCurrentMarker(sourceAlias, sourceRoot, out string? diagnostic);
		if (diagnostic is not null) {
			return Failed(diagnostic);
		}
		if (isUpdate && (current is null || expectedActive is null || current.Active != expectedActive)) {
			return Failed($"Knowledge source '{sourceAlias}' changed while the operation was in progress; retry.");
		}
		string digest = ComputeDigest(bundleBytes);
		KnowledgeLibraryHighWaterMark? highWater = ReadHighWater(sourceRoot, libraryId);
		if (highWater is not null
				&& (sequence < highWater.Sequence
					|| (sequence == highWater.Sequence
						&& !string.Equals(digest, highWater.BundleDigest, StringComparison.Ordinal)))) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Rejected,
				$"Knowledge library '{libraryId}' rejected sequence {sequence}; highest accepted sequence is {highWater.Sequence}.",
				libraryVersion,
				GetRootPath());
		}
		if (current is not null) {
			if (sequence < current.Active.Sequence
					|| (sequence == current.Active.Sequence
						&& !string.Equals(digest, current.Active.BundleDigest, StringComparison.Ordinal))) {
				return new KnowledgeInstallationResult(
					KnowledgeInstallationStatus.Rejected,
					$"Knowledge source '{sourceAlias}' rejected sequence {sequence}; active sequence is {current.Active.Sequence}.",
					libraryVersion,
					GetRootPath());
			}
			if (sequence == current.Active.Sequence && !allowRepair) {
				return new KnowledgeInstallationResult(
					KnowledgeInstallationStatus.AlreadyInstalled,
					$"Knowledge source '{sourceAlias}' sequence {sequence} is already installed.",
					libraryVersion,
					GetRootPath());
			}
			if (!isUpdate) {
				return Failed($"Knowledge source '{sourceAlias}' is already installed; use update-knowledge.");
			}
		}

		string generations = EnsureDirectory(sourceRoot, GenerationsDirectoryName);
		string staging = EnsureDirectory(sourceRoot, StagingDirectoryName);
		bool repairingActive = current is not null && sequence == current.Active.Sequence;
		string generationName = repairingActive
			? $"{sequence}-{digest[..12]}-repair-{Guid.NewGuid():N}"
			: $"{sequence}-{digest[..12]}";
		string finalRoot = ResolveChild(generations, generationName);
		if (_fileSystem.Directory.Exists(finalRoot)) {
			if (!TryRemoveRecoverableOrphan(
					generations,
					finalRoot,
					generationName,
					current,
					sourceAlias,
					libraryId,
					libraryVersion,
					sequence,
					transportType,
					location,
					resolvedRevision,
					digest,
					out string? orphanDiagnostic)) {
				return Failed(orphanDiagnostic!);
			}
		}
		string stagingRoot = ResolveChild(staging, $"{generationName}-{Guid.NewGuid():N}");
		_fileSystem.Directory.CreateDirectory(stagingRoot);
		try {
			WriteGeneration(stagingRoot, sourceAlias, libraryId, libraryVersion, sequence, transportType,
				location, resolvedRevision, digest, bundleBytes);
			WriteHighWater(sourceRoot, new KnowledgeLibraryHighWaterMark(
				SchemaVersion,
				libraryId,
				sequence,
				digest));
			_fileSystem.Directory.Move(stagingRoot, finalRoot);
		} finally {
			if (_fileSystem.Directory.Exists(stagingRoot)) {
				_fileSystem.Directory.Delete(stagingRoot, recursive: true);
			}
		}

		KnowledgeSourceGenerationPointer active = new(
			libraryId,
			libraryVersion,
			sequence,
			$"{GenerationsDirectoryName}/{generationName}",
			digest,
			resolvedRevision,
			DateTimeOffset.UtcNow);
		KnowledgeSourceCurrentState next = new(
			SchemaVersion,
			sourceAlias,
			active,
			repairingActive ? current?.Previous : current?.Active);
		WriteAtomicJson(sourceRoot, CurrentFileName, JsonSerializer.SerializeToUtf8Bytes(
			next, KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));
		Prune(generations, next);
		return new KnowledgeInstallationResult(
			isUpdate ? KnowledgeInstallationStatus.Updated : KnowledgeInstallationStatus.Installed,
			$"Knowledge source '{sourceAlias}' sequence {sequence} was installed at {finalRoot}.",
			libraryVersion,
			GetRootPath());
	}

	private bool TryRemoveRecoverableOrphan(
		string generationsRoot,
		string generationRoot,
		string generationName,
		KnowledgeSourceCurrentState? current,
		string sourceAlias,
		string libraryId,
		string libraryVersion,
		ulong sequence,
		string transportType,
		string location,
		string resolvedRevision,
		string digest,
		out string? diagnostic) {
		string relativePath = $"{GenerationsDirectoryName}/{generationName}";
		if (current is not null
				&& (string.Equals(current.Active.RelativePath, relativePath, StringComparison.Ordinal)
					|| string.Equals(current.Previous?.RelativePath, relativePath, StringComparison.Ordinal))) {
			diagnostic = $"Immutable knowledge generation '{generationName}' is already referenced by the activation marker.";
			return false;
		}
		try {
			if (!TryReadRecoverableGeneration(
					generationsRoot,
					generationRoot,
					generationName,
					sourceAlias,
					libraryId,
					sequence,
					digest,
					out KnowledgeSourceInstallMetadata? metadata,
					out diagnostic)) {
				return false;
			}
			bool exactOrphan = string.Equals(metadata!.LibraryVersion, libraryVersion, StringComparison.Ordinal)
				&& string.Equals(metadata.TransportType, transportType, StringComparison.Ordinal)
				&& string.Equals(metadata.Location, location, StringComparison.Ordinal)
				&& string.Equals(metadata.ResolvedRevision, resolvedRevision, StringComparison.Ordinal);
			if (!exactOrphan) {
				diagnostic = $"Immutable knowledge generation '{generationName}' already exists with unexpected content.";
				return false;
			}
			_fileSystem.Directory.Delete(generationRoot, recursive: true);
			diagnostic = null;
			return true;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Immutable knowledge generation '{generationName}' could not be recovered: {exception.Message}";
			return false;
		}
	}

	private bool TryReadRecoverableGeneration(
		string generationsRoot,
		string generationRoot,
		string generationName,
		string sourceAlias,
		string libraryId,
		ulong sequence,
		string digest,
		out KnowledgeSourceInstallMetadata? metadata,
		out string? diagnostic) {
		metadata = null;
		try {
			EnsureTreeContainsNoReparsePoints(generationsRoot, generationRoot);
			byte[] bundle = ReadBoundedFile(ResolveChild(generationRoot, BundleFileName), MaxBundleBytes);
			metadata = JsonSerializer.Deserialize(
				ReadBoundedFile(ResolveChild(generationRoot, MetadataFileName), MaxMarkerBytes),
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceInstallMetadata);
			bool exactGeneration = string.Equals(ComputeDigest(bundle), digest, StringComparison.Ordinal)
				&& metadata is not null
				&& metadata.SchemaVersion == SchemaVersion
				&& string.Equals(metadata.SourceAlias, sourceAlias, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(metadata.LibraryId, libraryId, StringComparison.Ordinal)
				&& metadata.Sequence == sequence
				&& string.Equals(metadata.BundleDigest, digest, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(metadata.LibraryVersion)
				&& !string.IsNullOrWhiteSpace(metadata.TransportType)
				&& !string.IsNullOrWhiteSpace(metadata.Location)
				&& !string.IsNullOrWhiteSpace(metadata.ResolvedRevision);
			if (!exactGeneration) {
				diagnostic = $"Immutable knowledge generation '{generationName}' exists with unexpected content.";
				return false;
			}
			diagnostic = null;
			return true;
		} catch (Exception exception) when (IsStorageException(exception)) {
			diagnostic = $"Immutable knowledge generation '{generationName}' could not be recovered: {exception.Message}";
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

	private void WriteGeneration(
		string stagingRoot,
		string sourceAlias,
		string libraryId,
		string libraryVersion,
		ulong sequence,
		string transportType,
		string location,
		string resolvedRevision,
		string digest,
		byte[] bundleBytes) {
		_fileSystem.File.WriteAllBytes(ResolveChild(stagingRoot, BundleFileName), bundleBytes);
		using MemoryStream input = new(bundleBytes, writable: false);
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
			SchemaVersion, sourceAlias, libraryId, libraryVersion, sequence, transportType, location,
			resolvedRevision, digest, DateTimeOffset.UtcNow);
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
			.Select(value => _fileSystem.Path.GetFileName(value!))
			.ToArray();
		foreach (string directory in _fileSystem.Directory.EnumerateDirectories(generationsRoot).ToArray()) {
			if (!retained.Contains(_fileSystem.Path.GetFileName(directory), StringComparer.Ordinal)) {
				EnsureNoReparsePoint(generationsRoot, directory);
				_fileSystem.Directory.Delete(directory, recursive: true);
			}
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

	private T WithMutationLock<T>(string sourceRoot, Func<T> action) {
		string sourcesRoot = _fileSystem.Path.GetDirectoryName(sourceRoot)
			?? throw new InvalidOperationException("Knowledge source root has no parent directory.");
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
		string sourcesRoot = _fileSystem.Path.GetDirectoryName(sourceRoot)
			?? throw new InvalidOperationException("Knowledge source root has no parent directory.");
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
		string sourcesRoot = _fileSystem.Path.GetDirectoryName(sourceRoot)
			?? throw new InvalidOperationException("Knowledge source root has no parent directory.");
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
		string sourcesRoot = _fileSystem.Path.GetDirectoryName(sourceRoot)
			?? throw new InvalidOperationException("Knowledge source root has no parent directory.");
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
		string sourcesRoot = _fileSystem.Path.GetDirectoryName(sourceRoot)
			?? throw new InvalidOperationException("Knowledge source root has no parent directory.");
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
			current = _fileSystem.Path.GetDirectoryName(current)!;
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
