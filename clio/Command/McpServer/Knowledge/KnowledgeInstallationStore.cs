using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeInstallationStore {
	string GetRootPath();

	KnowledgeCurrentState? ReadCurrent(out string? diagnostic);

	bool TryReadCandidate(
		KnowledgeVersionPointer pointer,
		out InstalledKnowledgeCandidate? candidate,
		out string? diagnostic);

	KnowledgeInstallationResult Publish(
		string packageId,
		string packageVersion,
		ulong sequence,
		string source,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeVersionPointer? expectedActive);

	KnowledgeInstallationResult Delete(bool confirmed);

	KnowledgeInstallMetadata? ReadActiveMetadata(KnowledgeCurrentState state, out string? diagnostic);

	bool TryValidateInstallation(KnowledgeCurrentState state, out string? diagnostic);
}

internal sealed record KnowledgeInstallationStoreOptions(int LockTimeoutMilliseconds);

internal sealed class KnowledgeInstallationStore : IKnowledgeInstallationStore {
	private const int CurrentSchemaVersion = 1;
	private const int MaxMarkerBytes = 64 * 1024;
	private const int MaxBundleBytes = 40 * 1024 * 1024;
	private const string CurrentFileName = "current.json";
	private const string DeletingMarkerFileName = ".current.deleting.json";
	private const string LockFileName = "knowledge.lock";
	private const string OwnerFileName = ".clio-knowledge-root";
	private const string OwnerFileContent = "clio-knowledge-store-v1\n";
	private const string BundleFileName = "bundle.zip";
	private const string MetadataFileName = "install.json";
	private const string VersionsDirectoryName = "versions";
	private const string StagingDirectoryName = "staging";
	private const string ExamplesDirectoryName = "examples";
	private static readonly ConcurrentDictionary<string, object> ProcessLocks = new(
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	private readonly IKnowledgeRootPathProvider _rootPathProvider;
	private readonly IFileSystem _fileSystem;
	private readonly KnowledgeInstallationStoreOptions _options;

	public KnowledgeInstallationStore(
		IKnowledgeRootPathProvider rootPathProvider,
		IFileSystem fileSystem,
		KnowledgeInstallationStoreOptions options) {
		_rootPathProvider = rootPathProvider ?? throw new ArgumentNullException(nameof(rootPathProvider));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.LockTimeoutMilliseconds);
	}

	public string GetRootPath() => _rootPathProvider.GetOrCreateRoot();

	public KnowledgeCurrentState? ReadCurrent(out string? diagnostic) {
		try {
			string root = GetRootPath();
			string markerPath = _fileSystem.Path.Combine(root, CurrentFileName);
			if (!_fileSystem.File.Exists(markerPath)) {
				diagnostic = null;
				return null;
			}
			ValidateOwnedRoot(root);
			byte[] bytes = ReadBoundedFile(markerPath, MaxMarkerBytes);
			KnowledgeCurrentState? state = JsonSerializer.Deserialize(
				bytes,
				KnowledgeInstallationJsonContext.Default.KnowledgeCurrentState);
			if (state is null || state.SchemaVersion != CurrentSchemaVersion || state.Active is null) {
				diagnostic = "Knowledge activation marker is empty or unsupported.";
				return null;
			}
			diagnostic = null;
			return state;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or InvalidDataException
				or JsonException
				or NotSupportedException) {
			diagnostic = $"Knowledge activation marker could not be read: {exception.Message}";
			return null;
		}
	}

	public bool TryReadCandidate(
		KnowledgeVersionPointer pointer,
		out InstalledKnowledgeCandidate? candidate,
		out string? diagnostic) {
		candidate = null;
		try {
			if (!TryResolveVersionDirectory(pointer, out string? versionDirectory, out diagnostic)) {
				return false;
			}
			string bundlePath = _fileSystem.Path.Combine(versionDirectory!, BundleFileName);
			byte[] bytes = ReadBoundedFile(bundlePath, MaxBundleBytes);
			string digest = ComputeDigest(bytes);
			if (!string.Equals(digest, pointer.BundleDigest, StringComparison.Ordinal)) {
				diagnostic = "Installed knowledge bundle does not match the active marker digest.";
				return false;
			}
			candidate = new InstalledKnowledgeCandidate(pointer, bundlePath, bytes);
			diagnostic = null;
			return true;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or InvalidDataException
				or NotSupportedException) {
			diagnostic = $"Installed knowledge bundle could not be read: {exception.Message}";
			return false;
		}
	}

	public KnowledgeInstallationResult Publish(
		string packageId,
		string packageVersion,
		ulong sequence,
		string source,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeVersionPointer? expectedActive) {
		ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		ArgumentNullException.ThrowIfNull(bundleBytes);
		if (!KnowledgeBundleNuGetClient.IsStableVersion(packageVersion) || sequence == 0) {
			return Failed(KnowledgeInstallationStatus.Rejected,
				"Knowledge package version and signed sequence are invalid.");
		}
		if (bundleBytes.Length == 0 || bundleBytes.Length > MaxBundleBytes) {
			return Failed(KnowledgeInstallationStatus.Rejected, "Knowledge bundle size is outside supported bounds.");
		}

		string root = GetRootPath();
		EnsureOwnedRoot(root);
		return WithMutationLock(root, () => PublishLocked(
			root,
			packageId,
			packageVersion,
			sequence,
			source,
			bundleBytes,
			isUpdate,
			expectedActive));
	}

	public KnowledgeInstallationResult Delete(bool confirmed) {
		string root = GetRootPath();
		if (!confirmed) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.ConfirmationRequired,
				"Deleting installed knowledge requires explicit confirmation.",
				RootPath: root);
		}
		if (!HasOwnedRoot(root)) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.NotInstalled,
				"Knowledge is not installed; the configured root is not owned by Clio.",
				RootPath: root);
		}
		return WithMutationLock(root, () => DeleteLocked(root));
	}

	public KnowledgeInstallMetadata? ReadActiveMetadata(KnowledgeCurrentState state, out string? diagnostic) {
		ArgumentNullException.ThrowIfNull(state);
		try {
			if (!TryResolveVersionDirectory(state.Active, out string? versionDirectory, out diagnostic)) {
				return null;
			}
			string metadataPath = _fileSystem.Path.Combine(versionDirectory!, MetadataFileName);
			KnowledgeInstallMetadata? metadata = JsonSerializer.Deserialize(
				ReadBoundedFile(metadataPath, MaxMarkerBytes),
				KnowledgeInstallationJsonContext.Default.KnowledgeInstallMetadata);
			if (metadata is null
					|| metadata.SchemaVersion != CurrentSchemaVersion
					|| !string.Equals(metadata.PackageVersion, state.Active.PackageVersion, StringComparison.Ordinal)
					|| metadata.Sequence != state.Active.Sequence
					|| !string.Equals(metadata.BundleDigest, state.Active.BundleDigest, StringComparison.Ordinal)) {
				diagnostic = "Knowledge installation metadata does not match the active marker.";
				return null;
			}
			diagnostic = null;
			return metadata;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidOperationException
				or InvalidDataException
				or JsonException
				or NotSupportedException) {
			diagnostic = $"Knowledge installation metadata could not be read: {exception.Message}";
			return null;
		}
	}

	public bool TryValidateInstallation(KnowledgeCurrentState state, out string? diagnostic) =>
		IsPublishedVersionHealthy(state, out diagnostic);

	private KnowledgeInstallationResult PublishLocked(
		string root,
		string packageId,
		string packageVersion,
		ulong sequence,
		string source,
		byte[] bundleBytes,
		bool isUpdate,
		KnowledgeVersionPointer? expectedActive) {
		ValidateOwnedRoot(root);
		KnowledgeCurrentState? current = ReadCurrent(out string? markerDiagnostic);
		if (markerDiagnostic is not null) {
			return Failed(KnowledgeInstallationStatus.Failed, markerDiagnostic, rootPath: root);
		}
		if (isUpdate && (current is null || expectedActive is null || !SameIdentity(current.Active, expectedActive))) {
			return Failed(
				KnowledgeInstallationStatus.Failed,
				"Knowledge changed while the operation was in progress; retry the command.",
				current?.Active.PackageVersion,
				root);
		}
		string digest = ComputeDigest(bundleBytes);
		bool repairActiveVersion = false;
		if (current is not null) {
			if (string.Equals(current.Active.PackageVersion, packageVersion, StringComparison.Ordinal)) {
				if (!string.Equals(current.Active.BundleDigest, digest, StringComparison.Ordinal)) {
					return Failed(
						KnowledgeInstallationStatus.Rejected,
						$"Knowledge {packageVersion} is already installed with different immutable content.",
						packageVersion,
						root);
				}
				if (IsPublishedVersionHealthy(current, out _)) {
					return new KnowledgeInstallationResult(
						KnowledgeInstallationStatus.AlreadyInstalled,
						$"Knowledge {packageVersion} is already installed.",
						packageVersion,
						root);
				}
				repairActiveVersion = true;
			}
			else if (!isUpdate) {
				return Failed(
					KnowledgeInstallationStatus.Failed,
					$"Knowledge {current.Active.PackageVersion} is already installed; use update-knowledge.",
					current.Active.PackageVersion,
					root);
			}
			if (!repairActiveVersion
					&& !KnowledgeBundleNuGetClient.IsVersionGreaterThan(packageVersion, current.Active.PackageVersion)) {
				return Failed(
					KnowledgeInstallationStatus.Rejected,
					$"Knowledge update {packageVersion} must be newer than active version {current.Active.PackageVersion}.",
					current.Active.PackageVersion,
					root);
			}
			if (!repairActiveVersion && sequence <= current.Active.Sequence) {
				return Failed(
					KnowledgeInstallationStatus.Rejected,
					$"Knowledge sequence {sequence} must be greater than active sequence {current.Active.Sequence}.",
					current.Active.PackageVersion,
					root);
			}
		}

		string versionsRoot = EnsureManagedDirectory(root, VersionsDirectoryName);
		string stagingRoot = EnsureManagedDirectory(root, StagingDirectoryName);
		EnsureManagedDirectory(root, ExamplesDirectoryName);
		RemoveStaleStaging(stagingRoot);
		string finalDirectory = ResolveChild(versionsRoot, packageVersion);
		if (_fileSystem.Directory.Exists(finalDirectory)) {
			EnsureNoReparsePoints(root, finalDirectory);
			string existingBundle = _fileSystem.Path.Combine(finalDirectory, BundleFileName);
			if (!repairActiveVersion
					&& (!_fileSystem.File.Exists(existingBundle)
					|| !string.Equals(ComputeDigest(ReadBoundedFile(existingBundle, MaxBundleBytes)), digest,
						StringComparison.Ordinal))) {
				return Failed(KnowledgeInstallationStatus.Rejected,
					$"Existing immutable knowledge directory for {packageVersion} has different content.",
					packageVersion,
					root);
			}
		}
		string stagingDirectory = ResolveChild(stagingRoot, $"{packageVersion}-{Guid.NewGuid():N}");
		string backupDirectory = ResolveChild(stagingRoot, $"backup-{packageVersion}-{Guid.NewGuid():N}");
		_fileSystem.Directory.CreateDirectory(stagingDirectory);
		try {
			EnsureNoReparsePoints(root, stagingDirectory);
			WriteVersion(stagingDirectory, packageId, packageVersion, sequence, source, digest, bundleBytes);
			if (_fileSystem.Directory.Exists(finalDirectory)) {
				_fileSystem.Directory.Move(finalDirectory, backupDirectory);
			}
			try {
				_fileSystem.Directory.Move(stagingDirectory, finalDirectory);
			} catch {
				if (_fileSystem.Directory.Exists(backupDirectory)
						&& !_fileSystem.Directory.Exists(finalDirectory)) {
					_fileSystem.Directory.Move(backupDirectory, finalDirectory);
				}
				throw;
			}
		} finally {
			DeleteOwnedDirectoryIfExists(root, stagingDirectory);
			DeleteOwnedDirectoryIfExists(root, backupDirectory);
		}

		DateTimeOffset activatedAt = DateTimeOffset.UtcNow;
		KnowledgeVersionPointer active = new(
			packageVersion,
			sequence,
			$"{VersionsDirectoryName}/{packageVersion}",
			digest,
			activatedAt);
		KnowledgeCurrentState next = new(CurrentSchemaVersion, active, current?.Active);
		WriteAtomicJson(
			root,
			CurrentFileName,
			JsonSerializer.SerializeToUtf8Bytes(next, KnowledgeInstallationJsonContext.Default.KnowledgeCurrentState));
		PruneUnreferencedVersions(versionsRoot, next);
		return new KnowledgeInstallationResult(
			isUpdate ? KnowledgeInstallationStatus.Updated : KnowledgeInstallationStatus.Installed,
			$"Knowledge {packageVersion} was {(repairActiveVersion ? "repaired" : isUpdate ? "updated" : "installed")} at {finalDirectory}.",
			packageVersion,
			root);
	}

	private KnowledgeInstallationResult DeleteLocked(string root) {
		ValidateOwnedRoot(root);
		KnowledgeCurrentState? current = ReadCurrent(out _);
		string markerPath = ResolveChild(root, CurrentFileName);
		string deletingMarkerPath = ResolveChild(root, DeletingMarkerFileName);
		bool hadMarker = _fileSystem.File.Exists(markerPath) || _fileSystem.File.Exists(deletingMarkerPath);
		if (_fileSystem.File.Exists(markerPath)) {
			_fileSystem.File.Move(markerPath, deletingMarkerPath, overwrite: true);
		}
		DeleteAbandonedRootQuarantines(root);
		foreach (string directoryName in new[] { VersionsDirectoryName, StagingDirectoryName, ExamplesDirectoryName }) {
			string path = ResolveChild(root, directoryName);
			DeleteOwnedDirectoryIfExists(root, path);
		}
		if (_fileSystem.File.Exists(deletingMarkerPath)) {
			_fileSystem.File.Delete(deletingMarkerPath);
		}
		return new KnowledgeInstallationResult(
			hadMarker ? KnowledgeInstallationStatus.Deleted : KnowledgeInstallationStatus.NotInstalled,
			hadMarker ? "Installed knowledge was deleted." : "Knowledge is not installed.",
			current?.Active.PackageVersion,
			root);
	}

	private void DeleteAbandonedRootQuarantines(string root) {
		foreach (string path in _fileSystem.Directory.EnumerateDirectories(root).ToArray()) {
			string name = _fileSystem.Path.GetFileName(path);
			if (IsManagedRootQuarantineName(name)) {
				DeleteQuarantinedTree(root, path);
			}
		}
	}

	private static bool IsManagedRootQuarantineName(string name) {
		foreach (string managedName in new[] { VersionsDirectoryName, StagingDirectoryName, ExamplesDirectoryName }) {
			string prefix = $".{managedName}.delete-";
			if (name.StartsWith(prefix, StringComparison.Ordinal)
					&& name.Length == prefix.Length + 32
					&& Guid.TryParseExact(name[prefix.Length..], "N", out _)) {
				return true;
			}
		}
		return false;
	}

	private void WriteVersion(
		string stagingDirectory,
		string packageId,
		string packageVersion,
		ulong sequence,
		string source,
		string digest,
		byte[] bundleBytes) {
		string bundlePath = ResolveChild(stagingDirectory, BundleFileName);
		_fileSystem.File.WriteAllBytes(bundlePath, bundleBytes);
		using MemoryStream input = new(bundleBytes, writable: false);
		using ZipArchive archive = new(input, ZipArchiveMode.Read);
		long extractedBytes = 0;
		foreach (ZipArchiveEntry entry in archive.Entries) {
			string relative = entry.FullName.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
			string destination = ResolveChild(stagingDirectory, relative);
			if (string.IsNullOrEmpty(entry.Name)) {
				_fileSystem.Directory.CreateDirectory(destination);
				continue;
			}
			if (entry.Length < 0 || entry.Length > MaxBundleBytes
					|| extractedBytes > MaxBundleBytes - entry.Length) {
				throw new InvalidDataException("Knowledge archive exceeds the extracted-size limit.");
			}
			extractedBytes += entry.Length;
			string? directory = _fileSystem.Path.GetDirectoryName(destination);
			if (!string.IsNullOrWhiteSpace(directory)) {
				_fileSystem.Directory.CreateDirectory(directory);
			}
			using Stream sourceStream = entry.Open();
			using Stream target = _fileSystem.File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			sourceStream.CopyTo(target);
		}
		KnowledgeInstallMetadata metadata = new(
			CurrentSchemaVersion,
			packageId,
			packageVersion,
			sequence,
			source,
			digest,
			DateTimeOffset.UtcNow);
		_fileSystem.File.WriteAllBytes(
			ResolveChild(stagingDirectory, MetadataFileName),
			JsonSerializer.SerializeToUtf8Bytes(
				metadata,
				KnowledgeInstallationJsonContext.Default.KnowledgeInstallMetadata));
	}

	private void WriteAtomicJson(string root, string fileName, byte[] content) {
		string destination = ResolveChild(root, fileName);
		string temporary = ResolveChild(root, $".{fileName}.{Guid.NewGuid():N}.tmp");
		try {
			_fileSystem.File.WriteAllBytes(temporary, content);
			_fileSystem.File.Move(temporary, destination, overwrite: true);
		} finally {
			if (_fileSystem.File.Exists(temporary)) {
				_fileSystem.File.Delete(temporary);
			}
		}
	}

	private T WithMutationLock<T>(string root, Func<T> action) {
		ValidateOwnedRoot(root);
		string lockPath = ResolveChild(root, LockFileName);
		object processLock = ProcessLocks.GetOrAdd(lockPath, _ => new object());
		TimeSpan timeout = TimeSpan.FromMilliseconds(_options.LockTimeoutMilliseconds);
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (!Monitor.TryEnter(processLock, timeout)) {
			throw new TimeoutException("Timed out waiting for another knowledge operation in this process.");
		}
		try {
			Stream? lockStream = null;
			while (lockStream is null) {
				try {
					lockStream = _fileSystem.File.Open(
						lockPath,
						FileMode.OpenOrCreate,
						FileAccess.ReadWrite,
						FileShare.None);
				} catch (IOException) when (stopwatch.Elapsed < timeout) {
					Thread.Sleep(50);
				}
			}
			using (lockStream) {
				return action();
			}
		} catch (IOException exception) when (stopwatch.Elapsed >= timeout) {
			throw new TimeoutException("Timed out waiting for another knowledge operation.", exception);
		} finally {
			Monitor.Exit(processLock);
		}
	}

	private bool TryResolveVersionDirectory(
		KnowledgeVersionPointer pointer,
		out string? versionDirectory,
		out string? diagnostic) {
		versionDirectory = null;
		if (pointer is null
				|| !KnowledgeBundleNuGetClient.IsStableVersion(pointer.PackageVersion)
				|| pointer.Sequence == 0
				|| !string.Equals(
					pointer.RelativePath,
					$"{VersionsDirectoryName}/{pointer.PackageVersion}",
					StringComparison.Ordinal)
				|| pointer.BundleDigest is null
				|| pointer.BundleDigest.Length != 64
				|| pointer.BundleDigest.Any(character => !Uri.IsHexDigit(character))) {
			diagnostic = "Knowledge activation marker contains an invalid version pointer.";
			return false;
		}
		string root = GetRootPath();
		versionDirectory = ResolveChild(
			root,
			pointer.RelativePath.Replace('/', _fileSystem.Path.DirectorySeparatorChar));
		diagnostic = null;
		return true;
	}

	private string EnsureManagedDirectory(string root, string name) {
		string path = ResolveChild(root, name);
		_fileSystem.Directory.CreateDirectory(path);
		EnsureNoReparsePoints(root, path);
		return path;
	}

	private string ResolveChild(string root, string relative) {
		string normalizedRoot = _fileSystem.Path.GetFullPath(root);
		string candidate = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(normalizedRoot, relative));
		string rootPrefix = normalizedRoot.TrimEnd(
			_fileSystem.Path.DirectorySeparatorChar,
			_fileSystem.Path.AltDirectorySeparatorChar) + _fileSystem.Path.DirectorySeparatorChar;
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		if (!candidate.StartsWith(rootPrefix, comparison)) {
			throw new InvalidDataException("Knowledge path escapes the configured root.");
		}
		EnsureNoReparsePoints(normalizedRoot, candidate);
		return candidate;
	}

	private byte[] ReadBoundedFile(string path, int maximumBytes) {
		EnsureNoReparsePoints(GetRootPath(), path);
		using Stream input = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		long length = input.Length;
		if (length <= 0 || length > maximumBytes) {
			throw new IOException($"Knowledge file '{_fileSystem.Path.GetFileName(path)}' is outside supported bounds.");
		}
		byte[] bytes = new byte[(int)length];
		input.ReadExactly(bytes);
		if (input.ReadByte() != -1) {
			throw new IOException($"Knowledge file '{_fileSystem.Path.GetFileName(path)}' changed while it was read.");
		}
		return bytes;
	}

	private static string ComputeDigest(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private void RemoveStaleStaging(string stagingRoot) {
		foreach (string directory in _fileSystem.Directory.EnumerateDirectories(stagingRoot).ToArray()) {
			string safe = ResolveChild(stagingRoot, _fileSystem.Path.GetFileName(directory));
			DeleteOwnedDirectoryIfExists(GetRootPath(), safe);
		}
	}

	private void PruneUnreferencedVersions(string versionsRoot, KnowledgeCurrentState state) {
		HashSet<string> retained = new(StringComparer.Ordinal) {
			state.Active.PackageVersion
		};
		if (state.Previous is not null) {
			retained.Add(state.Previous.PackageVersion);
		}
		foreach (string directory in _fileSystem.Directory.EnumerateDirectories(versionsRoot).ToArray()) {
			string name = _fileSystem.Path.GetFileName(directory);
			if (!retained.Contains(name)) {
				string safe = ResolveChild(versionsRoot, name);
				DeleteOwnedDirectoryIfExists(GetRootPath(), safe);
			}
		}
	}

	private bool IsPublishedVersionHealthy(KnowledgeCurrentState current, out string? diagnostic) {
		if (!TryReadCandidate(current.Active, out InstalledKnowledgeCandidate? candidate, out diagnostic)) {
			return false;
		}
		if (ReadActiveMetadata(current, out diagnostic) is null) {
			return false;
		}
		return ValidateExtractedContent(candidate!, out diagnostic);
	}

	private bool ValidateExtractedContent(InstalledKnowledgeCandidate candidate, out string? diagnostic) {
		string versionDirectory = _fileSystem.Path.GetDirectoryName(candidate.BundlePath)!;
		HashSet<string> expectedFiles = new(
			OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		try {
			using MemoryStream input = new(candidate.BundleBytes, writable: false);
			using ZipArchive archive = new(input, ZipArchiveMode.Read);
			foreach (ZipArchiveEntry entry in archive.Entries.Where(item => !string.IsNullOrEmpty(item.Name))) {
				string relative = entry.FullName.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
				string path = ResolveChild(versionDirectory, relative);
				expectedFiles.Add(_fileSystem.Path.GetFullPath(path));
				if (!_fileSystem.File.Exists(path)
						|| _fileSystem.FileInfo.New(path).Length != entry.Length) {
					diagnostic = "Extracted knowledge content is missing or has an unexpected size.";
					return false;
				}
				using Stream expected = entry.Open();
				using Stream actual = _fileSystem.File.OpenRead(path);
				if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual))) {
					diagnostic = "Extracted knowledge content does not match the verified bundle.";
					return false;
				}
			}
			foreach (string file in _fileSystem.Directory.EnumerateFiles(
					versionDirectory,
					"*",
					SearchOption.AllDirectories)) {
				string fullPath = _fileSystem.Path.GetFullPath(file);
				if (string.Equals(fullPath, candidate.BundlePath,
						OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
						|| string.Equals(
							fullPath,
							_fileSystem.Path.Combine(versionDirectory, MetadataFileName),
							OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
					continue;
				}
				if (!expectedFiles.Contains(fullPath)) {
					diagnostic = "Extracted knowledge content contains a file outside the verified bundle.";
					return false;
				}
			}
			diagnostic = null;
			return true;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or NotSupportedException) {
			diagnostic = $"Extracted knowledge content could not be validated: {exception.Message}";
			return false;
		}
	}

	private static bool SameIdentity(KnowledgeVersionPointer left, KnowledgeVersionPointer right) =>
		string.Equals(left.PackageVersion, right.PackageVersion, StringComparison.Ordinal)
		&& left.Sequence == right.Sequence
		&& string.Equals(left.BundleDigest, right.BundleDigest, StringComparison.Ordinal);

	private bool HasOwnedRoot(string root) {
		string ownerPath = _fileSystem.Path.Combine(root, OwnerFileName);
		if (!_fileSystem.File.Exists(ownerPath)) {
			return false;
		}
		ValidateOwnedRoot(root);
		return true;
	}

	private void EnsureOwnedRoot(string root) {
		EnsureNoReparsePoints(root, root);
		string ownerPath = _fileSystem.Path.Combine(root, OwnerFileName);
		if (_fileSystem.File.Exists(ownerPath)) {
			try {
				ValidateOwnedRoot(root);
			} catch (Exception exception) when (exception is IOException or InvalidOperationException) {
				string[] entries = _fileSystem.Directory.EnumerateFileSystemEntries(root).ToArray();
				if (entries.Length != 1 || !string.Equals(
						_fileSystem.Path.GetFullPath(entries[0]),
						_fileSystem.Path.GetFullPath(ownerPath),
						OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
					throw;
				}
				WriteOwnerMarker(root, ownerPath, overwrite: true);
				ValidateOwnedRoot(root);
			}
			return;
		}
		string[] existingEntries = _fileSystem.Directory.EnumerateFileSystemEntries(root).ToArray();
		foreach (string entry in existingEntries.Where(IsAbandonedOwnerTemporaryFile)) {
			EnsureNoReparsePoints(root, entry);
			_fileSystem.File.Delete(entry);
		}
		if (_fileSystem.Directory.EnumerateFileSystemEntries(root).Any()) {
			throw new InvalidOperationException(
				"The configured knowledge-root-path is not empty and has no Clio ownership marker.");
		}
		WriteOwnerMarker(root, ownerPath, overwrite: false);
		ValidateOwnedRoot(root);
	}

	private bool IsAbandonedOwnerTemporaryFile(string path) {
		if (!_fileSystem.File.Exists(path)) {
			return false;
		}
		string name = _fileSystem.Path.GetFileName(path);
		const string prefix = "..clio-knowledge-root.";
		const string suffix = ".tmp";
		return name.StartsWith(prefix, StringComparison.Ordinal)
			&& name.EndsWith(suffix, StringComparison.Ordinal)
			&& Guid.TryParseExact(name[prefix.Length..^suffix.Length], "N", out _);
	}

	private void WriteOwnerMarker(string root, string ownerPath, bool overwrite) {
		string temporary = ResolveChild(root, $".{OwnerFileName}.{Guid.NewGuid():N}.tmp");
		try {
			using (Stream output = _fileSystem.File.Open(
				temporary,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None)) {
				byte[] content = Encoding.UTF8.GetBytes(OwnerFileContent);
				output.Write(content);
				output.Flush();
			}
			try {
				_fileSystem.File.Move(temporary, ownerPath, overwrite);
			} catch (IOException) when (!overwrite && _fileSystem.File.Exists(ownerPath)) {
				// A concurrent initializer atomically published the same ownership marker first.
			}
		} finally {
			if (_fileSystem.File.Exists(temporary)) {
				_fileSystem.File.Delete(temporary);
			}
		}
	}

	private void ValidateOwnedRoot(string root) {
		EnsureNoReparsePoints(root, root);
		string ownerPath = _fileSystem.Path.Combine(root, OwnerFileName);
		if (!_fileSystem.File.Exists(ownerPath)
				|| !string.Equals(
					Encoding.UTF8.GetString(ReadBoundedFile(ownerPath, 128)),
					OwnerFileContent,
					StringComparison.Ordinal)) {
			throw new InvalidOperationException("The configured knowledge-root-path is not owned by Clio.");
		}
		EnsureNoReparsePoints(root, ownerPath);
	}

	private void DeleteOwnedDirectoryIfExists(string root, string path) {
		if (!_fileSystem.Directory.Exists(path)) {
			return;
		}
		EnsureNoReparsePoints(root, path);
		string parent = _fileSystem.Path.GetDirectoryName(path)
			?? throw new InvalidOperationException("Managed knowledge directory has no parent.");
		string quarantine = ResolveChild(parent, $".{_fileSystem.Path.GetFileName(path)}.delete-{Guid.NewGuid():N}");
		_fileSystem.Directory.Move(path, quarantine);
		DeleteQuarantinedTree(root, quarantine);
	}

	private void DeleteQuarantinedTree(string root, string path) {
		EnsureNoReparsePoints(root, path);
		foreach (string file in _fileSystem.Directory.EnumerateFiles(path).ToArray()) {
			EnsureNoReparsePoints(root, file);
			_fileSystem.File.Delete(file);
		}
		foreach (string directory in _fileSystem.Directory.EnumerateDirectories(path).ToArray()) {
			DeleteQuarantinedTree(root, directory);
		}
		_fileSystem.Directory.Delete(path, recursive: false);
	}

	private void EnsureNoReparsePoints(string root, string candidate) {
		string normalizedRoot = _fileSystem.Path.GetFullPath(root);
		string normalizedCandidate = _fileSystem.Path.GetFullPath(candidate);
		ValidateRootAncestors(normalizedRoot);
		string relative = _fileSystem.Path.GetRelativePath(normalizedRoot, normalizedCandidate);
		if (relative == ".") {
			return;
		}
		string current = normalizedRoot;
		foreach (string segment in relative.Split(
				new[] { _fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar },
				StringSplitOptions.RemoveEmptyEntries)) {
			current = _fileSystem.Path.Combine(current, segment);
			RejectReparsePoint(current);
		}
	}

	private void ValidateRootAncestors(string root) {
		Stack<string> ancestors = new();
		string? current = root;
		while (!string.IsNullOrWhiteSpace(current)) {
			ancestors.Push(current);
			string? parent = _fileSystem.Path.GetDirectoryName(current);
			if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.Ordinal)) {
				break;
			}
			current = parent;
		}
		while (ancestors.Count > 0) {
			RejectReparsePoint(ancestors.Pop());
		}
	}

	private void RejectReparsePoint(string path) {
		if ((_fileSystem.File.Exists(path) || _fileSystem.Directory.Exists(path))
				&& (_fileSystem.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) {
			throw new InvalidOperationException("Knowledge storage paths cannot contain symbolic links or junctions.");
		}
	}

	private KnowledgeInstallationResult Failed(
		KnowledgeInstallationStatus status,
		string message,
		string? version = null,
		string? rootPath = null) => new(status, message, version, rootPath ?? GetRootPath());
}
