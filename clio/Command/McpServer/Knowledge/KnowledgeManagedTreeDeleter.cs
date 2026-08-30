using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Removes a directory tree that Clio owns under the knowledge root.
/// </summary>
internal interface IKnowledgeManagedTreeDeleter {

	/// <summary>
	/// Removes the tree rooted at <paramref name="root"/>, and does nothing when it does not exist.
	/// <para>The tree is first renamed to a sibling scratch name, and only then emptied. A recursive delete is
	/// not atomic, and the ownership marker <c>.clio-knowledge-source</c> is a dot-prefixed direct child, so it
	/// sorts first on NTFS and is removed before the payload. A delete that fails half way therefore used to
	/// leave a source root with no marker — after which every command is refused with "not owned by Clio" and
	/// nothing can re-create the marker, because it is written only when the directory does not exist.</para>
	/// <para>Renaming first narrows the failure modes to two, and neither is that dead end. A failed rename
	/// leaves the tree exactly as it was, so the caller's retry is meaningful. A failed <em>empty</em> leaves it
	/// renamed but complete, which the next call to this method sweeps up before doing anything else — so the
	/// scratch tree is not a leak, but do not describe the outcome as "nothing happened": between the two steps
	/// the alias is already free, and a caller that reports failure has in fact detached the cache.</para>
	/// <para>Read-only attributes are cleared before the delete. Git marks pack files (<c>*.pack</c>,
	/// <c>*.idx</c>) read-only on creation and Windows refuses to delete a read-only file, so every Git
	/// knowledge checkout contains files a plain recursive delete cannot remove.</para>
	/// <para>The attribute walk does not descend into a directory symlink or junction, and skips a file that is
	/// one. <see cref="System.IO.Directory.Delete(string, bool)"/> unlinks a name-surrogate reparse point rather
	/// than emptying its target, so a walk that descended would clear read-only bits on files outside the
	/// managed root that are never deleted — including on a checkout Clio has just rejected as untrusted.</para>
	/// <para>Clearing is best effort: a file that cannot be reset is left for the delete itself to report, so a
	/// genuine permission problem still surfaces as one rather than being masked, or — worse — thrown from the
	/// enumerator before the delete that would have succeeded is ever reached.</para>
	/// </summary>
	/// <param name="root">Path of the tree to remove.</param>
	void Delete(string root);
}

/// <inheritdoc cref="IKnowledgeManagedTreeDeleter"/>
internal sealed class KnowledgeManagedTreeDeleter : IKnowledgeManagedTreeDeleter {

	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="KnowledgeManagedTreeDeleter"/> class.
	/// </summary>
	/// <param name="fileSystem">File system the managed knowledge tree lives on.</param>
	public KnowledgeManagedTreeDeleter(IFileSystem fileSystem) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <inheritdoc />
	public void Delete(string root) {
		ArgumentException.ThrowIfNullOrWhiteSpace(root);
		// Finish what a previous call started. If a Move succeeded and the Delete then failed, the tree is
		// sitting under a scratch name that nothing else in the knowledge subsystem ever enumerates - so
		// without this it is stranded forever, and under generations/ each publish would re-quarantine it and
		// append another 42 characters until the name exceeds the NTFS component limit.
		SweepAbandonedQuarantines(root);
		if (!_fileSystem.Directory.Exists(root)) {
			return;
		}
		if (IsQuarantine(root)) {
			// Already a scratch tree - empty it where it stands. KnowledgeSourceInstallationStore.Prune hands
			// us exactly this: it enumerates everything under generations/ that is not retained, so a scratch
			// tree left by a failed delete arrives here as if it were a generation. Renaming it again would
			// nest one scratch name inside another for no gain.
			ClearReadOnlyAttributes(root);
			_fileSystem.Directory.Delete(root, recursive: true);
			return;
		}
		// A sibling, so the rename stays on one volume and is a metadata operation. If it fails - Windows
		// refuses to rename a directory whose descendant is held open - nothing has been destroyed yet and the
		// caller sees a failure it can retry, which is the whole point of doing it first.
		string quarantine = _fileSystem.Path.Combine(
			ParentOf(root) ?? string.Empty,
			$"{QuarantinePrefix}{Guid.NewGuid():N}");
		_fileSystem.Directory.Move(root, quarantine);
		ClearReadOnlyAttributes(quarantine);
		_fileSystem.Directory.Delete(quarantine, recursive: true);
	}

	/// <summary>
	/// Fixed prefix for a scratch tree, deliberately NOT derived from the name being deleted.
	/// </summary>
	/// <remarks>
	/// A name-derived suffix only reclaims a scratch tree whose originating name recurs, and two of the four
	/// call sites never repeat one: a staging root is <c>&lt;generation&gt;-&lt;fresh guid&gt;</c> per publish,
	/// so a scratch tree left there would be matched by no future pattern and nothing else enumerates
	/// <c>staging/</c> - stranding a whole extracted generation permanently. Owning the prefix means one
	/// pattern reclaims every abandoned tree in a directory regardless of what it was called before.
	/// </remarks>
	internal const string QuarantinePrefix = ".clio-deleting-";

	private bool IsQuarantine(string path) =>
		(_fileSystem.Path.GetFileName(path) ?? string.Empty)
			.StartsWith(QuarantinePrefix, StringComparison.Ordinal);

	private string? ParentOf(string path) => _fileSystem.Path.GetDirectoryName(path);

	// Best effort by design: a scratch tree that still cannot be removed must not fail the delete the caller
	// actually asked for.
	private void SweepAbandonedQuarantines(string root) {
		try {
			string parent = ParentOf(root);
			if (string.IsNullOrEmpty(parent) || !_fileSystem.Directory.Exists(parent)) {
				return;
			}
			foreach (string abandoned in _fileSystem.Directory.EnumerateDirectories(
					parent, QuarantinePrefix + "*")) {
				try {
					ClearReadOnlyAttributes(abandoned);
					_fileSystem.Directory.Delete(abandoned, recursive: true);
				} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
					// A quarantined tree is already detached; leave it for a later best-effort sweep.
				}
			}
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or ArgumentException) {
			// Failure to enumerate abandoned quarantines must not block deletion of the requested tree.
		}
	}

	// Iterative, NOT recursive: the tree can be a Git checkout whose content came from a remote repository, and
	// a StackOverflowException cannot be caught - under mcp-server it would take down the agent's whole session
	// rather than one command. Same shape as ProcessExecutor.GetDirectorySize.
	private void ClearReadOnlyAttributes(string root) {
		Stack<string> pending = new();
		pending.Push(root);
		while (pending.Count > 0) {
			string directoryPath = pending.Pop();
			try {
				IDirectoryInfo directory = _fileSystem.DirectoryInfo.New(directoryPath);
				if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) {
					continue;
				}
				// TopDirectoryOnly, NOT SearchOption.AllDirectories: the framework's recursive enumeration
				// descends through reparse points, and it binds IgnoreInaccessible = false, so an unreadable
				// subtree throws from MoveNext - outside any per-entry try - and fails a delete that would
				// otherwise have succeeded.
				foreach (IFileSystemInfo entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)) {
					switch (entry) {
						case IFileInfo file:
							ClearReadOnlyAttribute(file);
							break;
						case IDirectoryInfo child:
							pending.Push(child.FullName);
							break;
					}
				}
				if ((directory.Attributes & FileAttributes.ReadOnly) != 0) {
					// Decorative on Windows, but on Unix FileAttributes.ReadOnly is the write permission bit,
					// and a directory without it cannot have its entries unlinked.
					directory.Attributes &= ~FileAttributes.ReadOnly;
				}
			} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
				// A concurrent removal or an unreadable subtree. Leave it to the delete to report: clearing
				// attributes must never be the step that fails an otherwise removable tree.
			}
		}
	}

	private static void ClearReadOnlyAttribute(IFileInfo file) {
		try {
			// A file whose ReparsePoint bit is set is a symlink, and both Windows SetFileAttributesW and the
			// Unix chmod behind IsReadOnly follow it - clearing would reach through to a target the delete
			// never touches. Bitmask equality, not a flag test: the file must be read-only AND not a link.
			if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint))
					== FileAttributes.ReadOnly) {
				file.IsReadOnly = false;
			}
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// Same best-effort contract as the directory walk above.
		}
	}
}
