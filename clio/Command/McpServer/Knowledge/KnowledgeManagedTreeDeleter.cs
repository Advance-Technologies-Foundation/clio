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
	/// Removes the tree rooted at <paramref name="root"/> directly, and does nothing when it does not exist.
	/// Read-only attributes are cleared first. Use <see cref="DeleteRecoverably"/> when a partially removed root
	/// would make later retries impossible.
	/// <para>Every directory reparse point inside the tree is <em>unlinked</em> before the delete is attempted —
	/// see <see cref="DeleteRecoverably"/> for why. Unlike that method this one walks the LIVE root with no
	/// rename, so the unlink applies to the caller's own tree and is not undone when the delete then fails: a
	/// reported failure leaves the tree in place minus its links. Every current caller is discarding the tree
	/// outright, which is the only reason that is acceptable.</para>
	/// </summary>
	/// <param name="root">Path of the tree to remove.</param>
	void Delete(string root);

	/// <summary>
	/// Removes the tree rooted at <paramref name="root"/> after renaming it to a source-specific sibling scratch
	/// name, and does nothing when it does not exist.
	/// <para>A recursive delete is
	/// not atomic, and the ownership marker <c>.clio-knowledge-source</c> is a dot-prefixed direct child, so it
	/// sorts first on NTFS and is removed before the payload. A delete that fails half way therefore used to
	/// leave a source root with no marker — after which every command is refused with "not owned by Clio" and
	/// nothing can re-create the marker, because it is written only when the directory does not exist.</para>
	/// <para>Renaming first narrows the failure modes to two, and neither is that dead end. A failed rename
	/// leaves the tree exactly as it was, so the caller's retry is meaningful. A failed <em>empty</em> leaves it
	/// renamed but complete, which the next call for the same root sweeps up before doing anything else — so the
	/// scratch tree is not a leak, but do not describe the outcome as "nothing happened": between the two steps
	/// the alias is already free, and a caller that reports failure has in fact detached the cache.</para>
	/// <para>Read-only attributes are cleared before the delete. Git marks pack files (<c>*.pack</c>,
	/// <c>*.idx</c>) read-only on creation and Windows refuses to delete a read-only file, so every Git
	/// knowledge checkout contains files a plain recursive delete cannot remove.</para>
	/// <para>The walk never descends into a directory reparse point, and skips a file that is one: absent a local
	/// process racing the walk, nothing behind a link is deleted or modified, so clearing read-only bits there
	/// would mutate state outside the managed root — including on a checkout Clio has just rejected as
	/// untrusted. The residual race is accepted: closing it needs handle-relative traversal the framework does
	/// not expose.</para>
	/// <para>Instead of skipping a directory reparse point, the walk <em>unlinks</em> it with a non-recursive
	/// delete, which removes the link and leaves its target untouched. This is not tidiness: a recursive
	/// <see cref="System.IO.Directory.Delete(string, bool)"/> that meets a <b>junction</b> anywhere inside the
	/// tree removes the link and then throws anyway, leaving the tree on disk. It fails elevated as well as
	/// unelevated, and the exception differs by host — <c>IOException</c> "The parameter is incorrect" or
	/// <c>UnauthorizedAccessException</c> "Access to the path is denied" — so neither the privilege level nor
	/// the exception type can be relied on. A directory <em>symlink</em> is handled natively and never triggers
	/// it. A tag that is not a name surrogate at all — a OneDrive Files-On-Demand placeholder, a ProjFS root,
	/// WCI, DFS — is neither: the framework descends into it, so this walk descends too and clears the
	/// read-only bits inside, which is what keeps a read-only pack file behind a placeholder folder from
	/// becoming another undeletable cache.</para>
	/// <para>Clearing is best effort: a file that cannot be reset is left for the delete itself to report, so a
	/// genuine permission problem still surfaces as one rather than being masked, or — worse — thrown from the
	/// enumerator before the delete that would have succeeded is ever reached.</para>
	/// </summary>
	/// <param name="root">Path of the tree to remove.</param>
	void DeleteRecoverably(string root);
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
		if (!_fileSystem.Directory.Exists(root)) {
			return;
		}
		ClearReadOnlyAttributes(root);
		_fileSystem.Directory.Delete(root, recursive: true);
	}

	/// <inheritdoc />
	public void DeleteRecoverably(string root) {
		ArgumentException.ThrowIfNullOrWhiteSpace(root);
		// Finish what a previous call started. If a Move succeeded and the Delete then failed, the tree is
		// sitting under a source-specific scratch name that nothing else enumerates. The source mutation lock
		// serializes calls for the same root, while the source-specific prefix prevents parallel operations for
		// different sources from deleting each other's active quarantine.
		SweepAbandonedQuarantines(root);
		if (!_fileSystem.Directory.Exists(root)) {
			return;
		}
		// A sibling, so the rename stays on one volume and is a metadata operation. If it fails - Windows
		// refuses to rename a directory whose descendant is held open - nothing has been destroyed yet and the
		// caller sees a failure it can retry, which is the whole point of doing it first.
		string quarantine = _fileSystem.Path.Combine(
			ParentOf(root) ?? string.Empty,
			$"{QuarantinePrefix}{RootKey(root)}-{Guid.NewGuid():N}");
		_fileSystem.Directory.Move(root, quarantine);
		ClearReadOnlyAttributes(quarantine);
		_fileSystem.Directory.Delete(quarantine, recursive: true);
	}

	/// <summary>
	/// Fixed prefix for scratch trees owned by Clio.
	/// </summary>
	internal const string QuarantinePrefix = ".clio-deleting-";

	private string RootKey(string path) => _fileSystem.Path.GetFileName(path) ?? string.Empty;

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
					parent, $"{QuarantinePrefix}{RootKey(root)}-*")) {
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
				// LOAD-BEARING, not redundant. Since name-surrogate children are unlinked below and never
				// pushed, the only links that reach this check are the root itself and one swapped in after
				// enumeration - and a failed unlink is silent, so surviving links do occur. Removing this as
				// unreachable would let the walk descend through them.
				if (!directory.Exists || IsLink(directory)) {
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
						case IDirectoryInfo child when IsLink(child):
							// Unlinked HERE, not left for the recursive delete. Directory.Delete(recursive: true)
							// throws when it meets a JUNCTION anywhere inside the tree - it removes the link and
							// then fails anyway, leaving the tree behind. The exception varies by host
							// (IOException "The parameter is incorrect" / UnauthorizedAccessException "Access to
							// the path is denied"), and it happens elevated as well as not, so neither the type
							// nor the privilege level can be relied on.
							//
							// THREE tag classes, not two, and the framework keys on the name-surrogate bit: it
							// unlinks a SYMLINK natively, mishandles a MOUNT POINT (junction) as above, and
							// DESCENDS INTO a non-name-surrogate tag - a OneDrive Files-On-Demand placeholder,
							// a ProjFS/Scalar root, WCI, DFS. IsLink separates them, so this branch takes only
							// the first two and the third falls through to be walked exactly as the framework
							// walks it - otherwise a read-only *.pack behind a placeholder folder would keep
							// its attribute and stay an undeletable cache.
							UnlinkReparsePoint(child);
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

	// TRUE for a link, FALSE for a reparse point that is not one. Measured: ResolveLinkTarget returns a
	// target for a JUNCTION as well as a symbolic link, so this does not accidentally exclude the mount-point
	// tag - which is the only one that actually breaks the recursive delete. A non-name-surrogate tag
	// (OneDrive placeholder, ProjFS, WCI, DFS) returns null and must be descended into rather than unlinked.
	// Throwing counts as "not a link": the walk then leaves it alone, which is the pre-existing behaviour.
	private static bool IsLink(IDirectoryInfo directory) {
		try {
			return (directory.Attributes & FileAttributes.ReparsePoint) != 0
				&& directory.ResolveLinkTarget(returnFinalTarget: false) is not null;
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return false;
		}
	}

	// A NON-recursive delete of a directory reparse point removes the link and never touches its target -
	// verified, including that a read-only payload behind the link keeps its attribute. Best effort, like the
	// rest of the walk: if the link survives, the delete that follows is what reports it.
	//
	// DESTROYING things inside a walk named "clear attributes" is sound for exactly one reason: every caller
	// deletes this same root immediately afterwards (Delete, DeleteRecoverably, SweepAbandonedQuarantines), so
	// nothing unlinked here was going to survive the call. That makes two refactors unsafe even though both
	// look like improvements - reusing this walk in a NON-deleting context ("make a tree writable first"), and
	// moving it BEFORE DeleteRecoverably's Move, which would turn a failed rename from "nothing happened" into
	// an unrecoverable link-stripping of a tree that then survives.
	private void UnlinkReparsePoint(IDirectoryInfo link) {
		try {
			// Re-read the attribute rather than trusting the enumeration's cached FIND_DATA: the parent was
			// re-opened by name after its own fresh check, so a local process can swap a plain directory for a
			// link in between and steer this delete outside the managed root. One stat closes the cheap half of
			// that race; the rest needs handle-relative traversal the framework does not expose.
			IDirectoryInfo current = _fileSystem.DirectoryInfo.New(link.FullName);
			if (!IsLink(current)) {
				return;
			}
			current.Delete(recursive: false);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// Left for the delete to report, exactly as an unresettable read-only file is.
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
