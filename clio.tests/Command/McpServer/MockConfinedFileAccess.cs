using System;
using System.IO;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// In-memory stand-in for <see cref="IConfinedFileAccess"/>, backed by a mock file system.
/// </summary>
/// <remarks>
/// The real implementation binds each operation to directory handles and refuses to follow a symbolic link
/// at any path component - none of which a mock file system HAS. That guarantee is therefore proven where it
/// is real, by the integration cases in <c>ConfinedFileAccessTests</c> and
/// <c>OutputPathConfinementTests</c> against the host file system; the tool-level unit tests use this double
/// to exercise everything else without touching the host.
/// </remarks>
internal sealed class MockConfinedFileAccess(IFileSystem fileSystem) : IConfinedFileAccess {

	private readonly IFileSystem _fileSystem =
		fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath, long maxBytes) {
		byte[] content = _fileSystem.File.ReadAllBytes(canonicalPath);
		if (content.LongLength > maxBytes) {
			throw new InputFileTooLargeException(content.LongLength, maxBytes);
		}
		return new MemoryStream(content, writable: false);
	}

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) {
		string directory = _fileSystem.Path.GetDirectoryName(canonicalPath);
		if (!string.IsNullOrEmpty(directory) && !_fileSystem.Directory.Exists(directory)) {
			_fileSystem.Directory.CreateDirectory(directory);
		}
		if (_fileSystem.File.Exists(canonicalPath)) {
			throw new IOException(
				$"output-file '{canonicalPath}' already exists; refusing to overwrite it. Choose a different "
				+ "path or remove the existing file.");
		}
		_fileSystem.File.WriteAllBytes(canonicalPath, content);
	}
}
