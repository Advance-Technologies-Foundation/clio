using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Ms = System.IO.Abstractions;

namespace Clio.Common;

#region Class: FileSystem

public class FileSystem(Ms.IFileSystem msFileSystem) : IFileSystem {
	#region Class: Nested

	public enum Algorithm{
		SHA1,
		SHA256,
		SHA384,
		SHA512,
		MD5
	}

	#endregion

	internal static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

	/// <summary>
	/// How long <see cref="WriteOwnerOnlyTextToFileAtomic"/> keeps retrying a publish that a contending
	/// handle is refusing. See the remarks on <c>PublishAtomicReplacement</c> for the measurement.
	/// </summary>
	internal static readonly TimeSpan DefaultAtomicPublishRetryWindow = TimeSpan.FromSeconds(2.5);

	// Stated rather than read from configuration so a test can bound a contended publish without
	// waiting out the production window or mutating process-wide state — the same reasoning
	// WorkerProcessSupervisor gives for its explicit queue-wait bound.
	private readonly TimeSpan _atomicPublishRetryWindow = DefaultAtomicPublishRetryWindow;

	/// <summary>
	/// Initializes a file system whose atomic-publish retry window is set explicitly, so a test can
	/// observe the contended path without waiting out the production deadline.
	/// </summary>
	/// <param name="fileSystem">The underlying file system abstraction.</param>
	/// <param name="atomicPublishRetryWindow">How long a refused publish keeps retrying.</param>
	internal FileSystem(Ms.IFileSystem fileSystem, TimeSpan atomicPublishRetryWindow)
		: this(fileSystem) {
		_atomicPublishRetryWindow = atomicPublishRetryWindow;
	}

	public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

	#region Methods: Public

	public void CreateLink(string link, string target) {
		msFileSystem.Directory.CreateSymbolicLink(link, target);
	}

	public void AppendTextToFile(string filePath, string contents, Encoding encoding = null) {
		msFileSystem.File.AppendAllText(filePath, contents, encoding ?? Utf8NoBom);
	}

	public void CheckOrDeleteExistsFile(string filePath, bool delete) {
		if (!msFileSystem.File.Exists(filePath)) {
			return;
		}

		if (delete) {
			DeleteFile(filePath);
		}
		else {
			throw new Exception($"The file {filePath} already exist");
		}
	}

	public void ClearDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		string[] files = GetFiles(directoryPath);
		foreach (string filePath in files) {
			DeleteFileIfExists(filePath);
		}

		string[] directories = GetDirectories(directoryPath);
		foreach (string childDirectoryPath in directories) {
			SafeDeleteDirectory(childDirectoryPath);
		}
	}

	public void ClearOrCreateDirectory(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		}

		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public bool CompareFiles(string first, string second) {
		return CompareFiles(Algorithm.MD5, first, second);
	}


	public bool CompareFiles(Algorithm algorithm, string first, string second) {
		if (!msFileSystem.File.Exists(first) || !msFileSystem.File.Exists(second)) {
			return false;
		}

		return GetFileHash(algorithm, first) == GetFileHash(algorithm, second);
	}

	public string Combine(params string[] paths) {
		return msFileSystem.Path.Combine(paths);
	}
	public string Combine(string path1, string path2) => 
		msFileSystem.Path.Combine(path1, path2);
	public string Combine(string path1, string path2, string path3) => msFileSystem.Path.Combine(path1, path2, path3);
	public string Combine(string path1, string path2, string path3, string path4) => msFileSystem.Path.Combine(path1, path2, path3, path4);

	
	public string ConvertToRelativePath(string path, string rootDirectoryPath) {
		rootDirectoryPath = rootDirectoryPath.TrimEnd(msFileSystem.Path.DirectorySeparatorChar);
		int rootDirectoryPathLength = rootDirectoryPath.Length;
		string relativePath = path[rootDirectoryPathLength..];
		return relativePath.TrimStart(msFileSystem.Path.DirectorySeparatorChar);
	}

	public string GetFullPath(string path) {
		return msFileSystem.Path.GetFullPath(path);
	}

	public void CopyDirectory(string source, string destination, bool overwrite) {
		source.CheckArgumentNullOrWhiteSpace(nameof(source));
		destination.CheckArgumentNullOrWhiteSpace(nameof(destination));
		CreateOrOverwriteExistsDirectoryIfNeeded(destination, overwrite);
		foreach (string filePath in msFileSystem.Directory.GetFiles(source)) {
			msFileSystem.File.Copy(filePath, msFileSystem.Path.Combine(destination, msFileSystem.Path.GetFileName(filePath)), true);
		}

		foreach (string directoryPath in msFileSystem.Directory.GetDirectories(source)) {
			CopyDirectory(directoryPath, msFileSystem.Path.Combine(destination, msFileSystem.Path.GetFileName(directoryPath)), overwrite);
		}
	}

	public void CopyDirectoryWithFilter(string source, string destination, bool overwrite, Func<string, bool> filter) {
		source.CheckArgumentNullOrWhiteSpace(nameof(source));
		destination.CheckArgumentNullOrWhiteSpace(nameof(destination));
		CreateOrOverwriteExistsDirectoryIfNeeded(destination, overwrite);
		foreach (string filePath in msFileSystem.Directory.GetFiles(source)) {
			if (!filter(filePath)) {
				msFileSystem.File.Copy(filePath, msFileSystem.Path.Combine(destination, msFileSystem.Path.GetFileName(filePath)), true);
			}
		}

		foreach (string directoryPath in msFileSystem.Directory.GetDirectories(source)) {
			if (!filter(directoryPath)) {
				CopyDirectory(directoryPath, msFileSystem.Path.Combine(destination, msFileSystem.Path.GetFileName(directoryPath)), overwrite);
			}
		}
	}

	public void CopyFile(string from, string to, bool overwrite) {
		msFileSystem.File.Copy(from, to, overwrite);
	}

	public void CopyFiles(IEnumerable<string> filesPaths, string destinationDirectory, bool overwrite) {
		filesPaths.CheckArgumentNull(nameof(filesPaths));
		destinationDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationDirectory));
		foreach (string sourceFilePath in filesPaths) {
			Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
			Ms.IFileInfo sourceFileInfo = fileInfoFactory.New(sourceFilePath);
			string destinationFilePath = msFileSystem.Path.Combine(destinationDirectory, sourceFileInfo.Name);
			msFileSystem.File.Copy(sourceFilePath, destinationFilePath, overwrite);
		}
	}


	public Ms.IDirectoryInfo CreateDirectory(string directoryPath, bool throwWhenExists = false) {
		if (throwWhenExists && ExistsDirectory(directoryPath)) {
			throw new ArgumentException($"Directory {directoryPath} already exists");
		}

		return msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public void CreateDirectoryIfNotExists(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}

		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public Ms.IFileSystemInfo CreateDirectorySymLink(string path, string pathToTarget) {
		return msFileSystem.Directory.CreateSymbolicLink(path, pathToTarget);
	}

	public Ms.FileSystemStream CreateFile(string filePath) {
		return msFileSystem.File.Create(filePath);
	}

	public Ms.IFileSystemInfo CreateFileSymLink(string path, string pathToTarget) {
		return msFileSystem.File.CreateSymbolicLink(path, pathToTarget);
	}

	public void CreateOrClearDirectory(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		}
		else {
			msFileSystem.Directory.CreateDirectory(directoryPath);
		}
	}

	public void CreateOrOverwriteExistsDirectoryIfNeeded(string directoryPath, bool overwrite) {
		if (!msFileSystem.Directory.Exists(directoryPath)) {
			msFileSystem.Directory.CreateDirectory(directoryPath);
			return;
		}

		if (!overwrite) {
			return;
		}

		ClearDirectory(directoryPath);
	}


	public Ms.IFileSystemInfo CreateSymLink(string path, string pathToTarget) {
		path.CheckArgumentNullOrWhiteSpace(nameof(path));
		pathToTarget.CheckArgumentNullOrWhiteSpace(nameof(pathToTarget));
		if (msFileSystem.File.Exists(path)) {
			return CreateFileSymLink(path, pathToTarget);
		}

		if (msFileSystem.Directory.Exists(path)) {
			return CreateDirectorySymLink(path, pathToTarget);
		}

		throw new ArgumentOutOfRangeException(nameof(path), $"Path {path} does not exist");
	}

	public void DeleteDirectory(string directoryPath) {
		DeleteDirectory(directoryPath, false);
	}

	public void DeleteDirectory(string directoryPath, bool recursive) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		msFileSystem.Directory.Delete(directoryPath, recursive);
	}

	public void DeleteDirectoryIfExists(string directoryPath) {
		directoryPath.CheckArgumentNull(nameof(directoryPath));
		if (msFileSystem.Directory.Exists(directoryPath)) {
			msFileSystem.Directory.Delete(directoryPath, true);
		}
	}

	public bool DeleteFile(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		if (IsReadOnlyFile(filePath)) {
			ResetFileReadOnlyAttribute(filePath);
		}

		msFileSystem.File.Delete(filePath);

		//TODO: Discuss with P.Makarchuk
		//why return type is bool when always true
		return true;
	}


	public bool DeleteFileIfExists(string filePath) {
		return msFileSystem.File.Exists(filePath) && DeleteFile(filePath);
	}

	/// <summary>
	///     Checks if directory exists
	/// </summary>
	/// <param name="directoryPath"></param>
	/// <returns></returns>
	public bool ExistsDirectory(string directoryPath) {
		return msFileSystem.Directory.Exists(directoryPath);
	}

	public bool ExistsFile(string filePath) {
		return msFileSystem.File.Exists(filePath);
	}

	public string ExtractFileExtensionFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));

		//var fileInfo = new FileInfo(filePath);
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo fileInfo = fileInfoFactory.New(filePath);
		return fileInfo.Extension;
	}

	public string ExtractFileNameFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		return GetFileNameWithoutExtension(GetFilesInfos(filePath));
	}

	public Ms.FileSystemStream FileOpenStream(string filePath, FileMode mode, FileAccess access, FileShare share) {
		return msFileSystem.File.Open(filePath, mode, access, share);
	}

	public string GetCurrentDirectoryIfEmpty(string directoryPath) {
		return string.IsNullOrWhiteSpace(directoryPath)
			? msFileSystem.Directory.GetCurrentDirectory()
			: directoryPath;
	}

	public string GetDestinationFileDirectory(string filePath, string destinationPath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
		string fileName = ExtractFileNameFromPath(filePath);
		return msFileSystem.Path.Combine(destinationPath, fileName);
	}

	public bool IsPathRooted(string path) {
		return msFileSystem.Path.IsPathRooted(path);
	}

	public string[] GetDirectories(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetDirectories(directoryPath);
	}

	public string[] GetDirectories(string directoryPath, string patternt, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetDirectories(directoryPath, patternt, searchOption);
	}

	public string[] GetDirectories() {
		return GetDirectories(msFileSystem.Directory.GetCurrentDirectory());
	}

	public string GetDirectoryHash(string directoryPath, Algorithm algorithm = Algorithm.SHA256) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));

		if (!msFileSystem.Directory.Exists(directoryPath)) {
			throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
		}

		// Get all files in directory and subdirectories
		List<string> files = msFileSystem.Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
										 .OrderBy(f =>
											 f) // Sort for consistent hashing regardless of directory enumeration order
										 .ToList();

		if (files.Count == 0) {
			return string.Empty;
		}

		// Create the appropriate hash algorithm
		HashAlgorithm hashAlgorithm = algorithm switch {
										  Algorithm.SHA1 => SHA1.Create(),
										  Algorithm.SHA256 => SHA256.Create(),
										  Algorithm.SHA384 => SHA384.Create(),
										  Algorithm.SHA512 => SHA512.Create(),
										  Algorithm.MD5 => MD5.Create(),
										  var _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
											  null)
									  };

		using HashAlgorithm algorithm1 = hashAlgorithm;
		using MemoryStream ms = new();
		foreach (string file in files) {
			// Calculate file hash
			string fileHash = GetFileHash(algorithm, file);

			// Get relative path to include directory structure in the hash
			string relativePath = ConvertToRelativePath(file, directoryPath);

			// Combine file path and hash in a deterministic way
			byte[] fileData = Encoding.UTF8.GetBytes($"{relativePath}:{fileHash}");
			ms.Write(fileData, 0, fileData.Length);
		}

		// Reset stream position and calculate final hash
		ms.Position = 0;
		byte[] directoryHash = hashAlgorithm.ComputeHash(ms);
		return BitConverter.ToString(directoryHash).Replace("-", string.Empty);
	}

	public Ms.IDirectoryInfo GetDirectoryInfo(string path) {
		Ms.IDirectoryInfoFactory dirInfoFactory = msFileSystem.DirectoryInfo;
		return dirInfoFactory.New(path);
	}

	public string GetFileHash(Algorithm algorithm, string fileName) {
		HashAlgorithm hashAlgorithm = algorithm switch {
										  Algorithm.SHA1 => SHA1.Create(),
										  Algorithm.SHA256 => SHA256.Create(),
										  Algorithm.SHA384 => SHA384.Create(),
										  Algorithm.SHA512 => SHA512.Create(),
										  Algorithm.MD5 => MD5.Create(),
										  var _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
											  null)
									  };

		using Ms.FileSystemStream stream = msFileSystem.File.OpenRead(fileName);
		byte[] hash = hashAlgorithm.ComputeHash(stream);
		return BitConverter.ToString(hash).Replace("-", string.Empty);
	}

	public string GetFileNameWithoutExtension(Ms.IFileInfo fileInfo) {
		fileInfo.CheckArgumentNull(nameof(fileInfo));
		return fileInfo.Name[..^fileInfo.Extension.Length];
	}

	public string[] GetFiles(string directoryPath) {
		//TODO: Should probably be IEnumerable<string> instead of string[]
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetFiles(directoryPath);
	}

	public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetFiles(directoryPath, searchPattern, searchOption);
	}

	public Ms.IFileInfo[] GetFilesInfos(string directoryPath, string searchPattern, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));

		var directoryInfo= msFileSystem.DirectoryInfo.New(directoryPath);

		//TODO: Discuss with P.Makarchuk
		//directoryInfo.GetFiles causes System.IO.DirectoryNotFoundException when Schemas does not exist 
		return directoryInfo.Exists 
			? directoryInfo.GetFiles(searchPattern, searchOption) 
			: [];
	}

	public Ms.IFileInfo GetFilesInfos(string filePath) {
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo fileInfo = fileInfoFactory.New(filePath);
		return fileInfo;
	}

	public long GetFileSize(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo ff = fileInfoFactory.New(filePath);
		return ff.Length;
	}

	public long GetFileSize(Ms.IFileInfo fileInfo) {
		return fileInfo.Length;
	}

	public bool IsEmptyDirectory() {
		return !msFileSystem.Directory.GetFileSystemEntries(msFileSystem.Directory.GetCurrentDirectory()).Any();
	}

	public bool IsEmptyDirectory(string path) {
		return !msFileSystem.Directory.GetFileSystemEntries(path).Any();
	}

	public bool IsReadOnlyFile(string filePath) {
		if (!msFileSystem.File.Exists(filePath)) {
			return false;
		}

		return (msFileSystem.File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0;
	}

	public void MoveFile(string oldFilePath, string newFilePath) {
		msFileSystem.File.Move(oldFilePath, newFilePath);
	}

	public string NormalizeFilePathByPlatform(string filePath) {
		if (string.IsNullOrWhiteSpace(filePath)) {
			return filePath;
		}

		string[] filePathItem = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
		string result = Path.Combine(filePathItem);

		// Path.Combine loses the root separator on Unix; restore it for absolute paths
		if (Path.IsPathRooted(filePath) && !Path.IsPathRooted(result)) {
			result = Path.DirectorySeparatorChar + result;
		}

		return result;
	}

	public void OverwriteExistsDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		if (!msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}

		msFileSystem.Directory.Delete(directoryPath, true);
		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public byte[] ReadAllBytes(string filePath) {
		return msFileSystem.File.ReadAllBytes(filePath);
	}

	public System.IO.Stream OpenReadStream(string filePath) {
		return msFileSystem.File.OpenRead(filePath);
	}

	public string ReadAllText(string filePath) {
		return msFileSystem.File.ReadAllText(filePath, Utf8NoBom);
	}

	public void ResetFileReadOnlyAttribute(string filePath) {
		if (!msFileSystem.File.Exists(filePath)) {
			return;
		}

		if (IsReadOnlyFile(filePath)) {
			msFileSystem.File.SetAttributes(filePath,
				msFileSystem.File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
		}
	}

	public void SafeDeleteDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		ClearDirectory(directoryPath);
		DeleteDirectory(directoryPath, false);
		while (ExistsDirectory(directoryPath)) {
			Thread.Sleep(0);
		}
	}


	public void WriteAllTextToFile(string filePath, string contents) {
		WriteAllTextToFile(filePath, contents, Utf8NoBom);
	}

	public void WriteAllTextToFile(string filePath, string contents, Encoding encoding) {
		msFileSystem.File.WriteAllText(filePath, contents, encoding);
	}

	public void WriteOwnerOnlyTextToFile(string filePath, string contents) {
		if (OperatingSystem.IsWindows()) {
			msFileSystem.File.WriteAllText(filePath, contents, Utf8NoBom);
			return;
		}
		var opts = new FileStreamOptions {
			Mode = FileMode.Create,
			Access = FileAccess.Write,
			UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
		};
		// Routed through the injected abstraction rather than a direct System.IO.FileStream so a
		// substituted file system can observe the owner-only write; the Windows branch above already went
		// through msFileSystem, and the asymmetry made this branch provable only end to end.
		using var fs = msFileSystem.FileStream.New(filePath, opts);
		using var w = new StreamWriter(fs, Utf8NoBom);
		w.Write(contents);
	}

	public void WriteOwnerOnlyTextToFileAtomic(string filePath, string contents) {
		string directory = Path.GetDirectoryName(filePath);
		string temporary = Path.Combine(
			string.IsNullOrEmpty(directory) ? "." : directory,
			$".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
		try {
			var opts = new FileStreamOptions {
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			if (!OperatingSystem.IsWindows()) {
				// Mandatory: without this the temp file is created world-readable and the move publishes
				// those permissions, reopening the window WriteOwnerOnlyTextToFile exists to close.
				// The property is Unix-only and throws when set on Windows, where the per-user profile ACL
				// is the security boundary instead.
				opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
			}
			using (var stream = msFileSystem.FileStream.New(temporary, opts)) {
				using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true);
				writer.Write(contents);
				writer.Flush();
				stream.Flush();
			}
			PublishAtomicReplacement(temporary, filePath);
		}
		finally {
			if (msFileSystem.File.Exists(temporary)) {
				msFileSystem.File.Delete(temporary);
			}
		}
	}

	// Publishes the finished temporary file over the destination.
	//
	// On Unix this is rename(2): atomic, and indifferent to any reader that already holds the
	// destination open. On WINDOWS the same call is MoveFileEx(MOVEFILE_REPLACE_EXISTING), which needs
	// DELETE access on the destination and is refused while ANOTHER HANDLE IS OPEN ON IT — including an
	// ordinary reader, because File.ReadAllText opens with FileShare.Read and that share mode denies the
	// rename. The write is still atomic in the sense that matters (a reader never sees a partial
	// document); what fails is the publish itself, with UnauthorizedAccessException or IOException.
	//
	// That is not hypothetical and not a test artifact: the consumer this method exists for is Playwright
	// loading a cached storageState while clio refreshes it. Measured on a Windows build agent, six
	// concurrent writers against one reader produced six UnauthorizedAccessExceptions; the same run on
	// macOS passes, which is exactly the asymmetry rename(2) predicts.
	//
	// MEASURED on Windows 11 (a_kravchuk2, .NET 8), six concurrent writers against one reader loop —
	// the shape of TC-E-902 — publishing 180 times per arm:
	//
	//   bare File.Move          108-113 of 180 publishes FAILED, all UnauthorizedAccessException
	//   deadline-bounded retry  0 of 180 failed over four runs, worst case 16 attempts, deadline never hit
	//
	// The same probe on macOS fails 0 of 180 with the bare move, which is rename(2) behaving as
	// specified — and is exactly why a green macOS suite said nothing about this.
	//
	// The reader's window is short and it is not ours to widen — a third-party consumer opens the file
	// however it likes. So the writer retries the publish over a bounded window instead. The bound is
	// deliberate: a genuine ACL or read-only-file error must still surface rather than being spun on,
	// so the final attempt's exception propagates unchanged.
	private void PublishAtomicReplacement(string temporary, string filePath) {
		const int backoffStepMilliseconds = 15;
		const int backoffCapMilliseconds = 100;
		long startedAt = Stopwatch.GetTimestamp();
		for (int attempt = 1; ; attempt++) {
			try {
				msFileSystem.File.Move(temporary, filePath, overwrite: true);
				return;
			}
			catch (Exception e) when (Stopwatch.GetElapsedTime(startedAt) < _atomicPublishRetryWindow
				&& IsTransientReplacementFailure(e)) {
				// Capped linear backoff. The bound is a DEADLINE rather than an attempt count, and that
				// distinction is measured rather than stylistic: an earlier 12-attempt version was
				// observed needing 13, 15 and 16 attempts under the load below, so a count that looked
				// generous was already failing. A deadline states the guarantee directly and does not
				// have to be re-tuned whenever the backoff curve changes.
				Thread.Sleep(Math.Min(backoffStepMilliseconds * attempt, backoffCapMilliseconds));
			}
		}
	}

	// Only the two shapes a contending handle produces. Anything else — a missing temporary file, a
	// denied directory — is a real error and must not be retried into a timeout.
	private static bool IsTransientReplacementFailure(Exception e) =>
		e is UnauthorizedAccessException || (e is IOException && e is not FileNotFoundException
			&& e is not DirectoryNotFoundException);

	#endregion
}

#endregion
