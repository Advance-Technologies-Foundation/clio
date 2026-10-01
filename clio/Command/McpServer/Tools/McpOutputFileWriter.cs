using System;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Confines and writes the local file of a <c>*-to-file</c> MCP tool: the twin of a read tool that puts a
/// large part of its result on disk instead of into the model context.
/// </summary>
public interface IMcpOutputFileWriter {

	/// <summary>
	/// Confines <paramref name="outputFile"/> to the workspace or the OS temp directory and returns its
	/// canonical absolute form, WITHOUT writing anything. Callers resolve before the remote call so a rejected
	/// path does not cost a full fetch first.
	/// </summary>
	/// <param name="outputFile">Caller-supplied output path.</param>
	/// <param name="resolvedPath">Canonical absolute path when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	/// <returns><see langword="true"/> when the path is allowed and does not exist yet.</returns>
	bool TryResolve(string outputFile, out string resolvedPath, out string error);

	/// <summary>
	/// Creates the file at a path returned by <see cref="TryResolve"/>. The write refuses an existing target,
	/// so a file named by two concurrent calls is written once and the other call fails.
	/// </summary>
	/// <param name="resolvedPath">Path previously returned by <see cref="TryResolve"/>.</param>
	/// <param name="content">Bytes to write.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	/// <returns><see langword="true"/> when the file was created.</returns>
	bool TryWriteNew(string resolvedPath, byte[] content, out string error);
}

/// <inheritdoc cref="IMcpOutputFileWriter"/>
public sealed class McpOutputFileWriter(IFileSystem fileSystem, IConfinedFileAccess confinedFileAccess)
	: IMcpOutputFileWriter {

	private readonly IFileSystem _fileSystem =
		fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	// The confinement DECISION is made against IFileSystem; the create is made through this, which binds the
	// operation to directory handles so a component swapped after the decision cannot redirect it.
	private readonly IConfinedFileAccess _confinedFileAccess =
		confinedFileAccess ?? throw new ArgumentNullException(nameof(confinedFileAccess));

	/// <inheritdoc/>
	public bool TryResolve(string outputFile, out string resolvedPath, out string error) {
		resolvedPath = null;
		error = null;
		if (string.IsNullOrWhiteSpace(outputFile)) {
			error = "output-file is required.";
			return false;
		}
		try {
			(string path, string pathError) = OutputPathConfinement.ResolveCanonicalOutput(_fileSystem, outputFile);
			if (pathError is not null) {
				error = pathError;
				return false;
			}
			resolvedPath = path;
			return true;
		} catch (Exception ex) {
			error = SensitiveErrorTextRedactor.Redact($"Failed to resolve output-file: {ex.Message}");
			return false;
		}
	}

	/// <inheritdoc/>
	public bool TryWriteNew(string resolvedPath, byte[] content, out string error) {
		error = null;
		try {
			_confinedFileAccess.WriteNew(resolvedPath, content ?? []);
			return true;
		} catch (OutputFileAlreadyExistsException alreadyExists) {
			error = SensitiveErrorTextRedactor.Redact(alreadyExists.Message);
			return false;
		} catch (Exception ex) {
			error = SensitiveErrorTextRedactor.Redact($"Failed to write output-file: {ex.Message}");
			return false;
		}
	}
}
