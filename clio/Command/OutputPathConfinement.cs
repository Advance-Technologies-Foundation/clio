namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using IoFileSystem = System.IO.Abstractions.IFileSystem;
using IoFileSystemInfo = System.IO.Abstractions.IFileSystemInfo;

/// <summary>
/// Shared guard for the <c>--output-file</c> write path of the MCP-callable schema-writing tools
/// (<c>get-classic-page-sources</c>, <c>get-client-unit-schema</c>, <c>get-schema</c>, <c>get-sql-schema</c>).
/// Those tools can be invoked over MCP, so the output path may be supplied by an agent rather than typed at a
/// shell. Writing an unconstrained path verbatim would let a <c>..</c> traversal, an absolute system path, or a
/// symlink overwrite an arbitrary file, so an explicit output-file is confined to the workspace anchor OR the OS
/// temp directory — the two locations the migration flow legitimately writes to.
/// </summary>
internal static class OutputPathConfinement {

	/// <summary>
	/// Resolves <paramref name="outputFile"/> to an absolute path and confirms it stays inside a trusted
	/// workspace anchor or the OS temp directory. Symlinks are resolved before the check so a link cannot smuggle
	/// the write outside the allowed zones, and an anchor that is a filesystem root or an ancestor of the user's
	/// home directory is not trusted as a confinement boundary.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for path resolution and the workspace-marker probe.</param>
	/// <param name="outputFile">The caller-supplied output path (may be relative).</param>
	/// <returns>
	/// The resolved absolute path with a <c>null</c> error when allowed; <c>(null, error)</c> when the path
	/// escapes the allowed locations or already exists (an explicit output-file is additive; a target that
		/// already exists is never overwritten, keeping every routing tool's Destructive=false honest).
	/// </returns>
	internal static (string path, string error) Resolve(IoFileSystem fileSystem, string outputFile) {
		// H1: reading the process-global cwd (for the anchor) must serialize against the MCP workspace tools that
		// PIN cwd. In the MCP path this runs under the shared tool lock; in the single-threaded CLI path the lock
		// is uncontended. lock is reentrant, so a caller already holding it (the bundle command) is unaffected.
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				fileSystem,
				fileSystem.Directory.GetCurrentDirectory(),
				home,
				ClioRuntimePaths.Home,
				null);
			string full = fileSystem.Path.GetFullPath(outputFile);
			string tempRoot = fileSystem.Path.GetFullPath(fileSystem.Path.GetTempPath());

			// Confine the REAL (symlink-followed) path, not just the lexical one: Path.GetFullPath collapses `..`
			// but never resolves a symlink, and the later write follows links. A link planted under an allowed
			// root (the classic world-writable /tmp attack) could otherwise land the write on an arbitrary file.
			// BOTH bounds are resolved the same way so a symlinked temp/home root (e.g. macOS /var -> /private/var)
			// does not cause a false rejection of an in-bounds path.
			string real = ResolveRealPath(fileSystem, full);
			string realTempRoot = ResolveRealPath(fileSystem, tempRoot);
			string realAnchor = ResolveRealPath(fileSystem, anchor);
			string realHome = string.IsNullOrEmpty(home) ? home : ResolveRealPath(fileSystem, home);

			// A filesystem root ('/', 'C:\') or an ancestor of the user's home directory ('/Users', '/home',
			// 'C:\Users') is too broad to be a write boundary — an MCP host launched with such a cwd (Claude
			// Desktop has historically used '/') would otherwise confine to the whole volume. Drop an untrusted
			// anchor so only the OS temp root remains allowed.
			string trustedAnchor = IsTrustedAnchor(fileSystem, realAnchor, realHome) ? realAnchor : null;

			if (!IsPathConfined(real, trustedAnchor, realTempRoot)) {
				return (null,
					$"output-file '{outputFile}' resolves outside the allowed locations; it must be inside the " +
					"workspace or the OS temp directory.");
			}

			// Keep the Destructive=false classification honest: an explicit output-file must not silently
			// overwrite an existing file. Confinement bounds WHERE the write lands, not WHETHER it destroys
			// existing content. The tool-owned default output path does not flow through here, so re-runs to it
			// still overwrite their own output.
			if (fileSystem.File.Exists(full) || fileSystem.Directory.Exists(full)) {
				return (null,
					$"output-file '{outputFile}' already exists; refusing to overwrite it. Choose a different " +
					"path or remove the existing file.");
			}

			return (full, null);
		}
	}

	/// <summary>
	/// True when <paramref name="fullCandidate"/> (an already-resolved absolute path) lies within the workspace
	/// anchor OR the OS temp root. Both bounds are the two locations the schema-writing tools write to; everything
	/// else — parent-traversal escapes, absolute system paths, other volumes — is out of bounds. A <c>null</c> or
	/// empty <paramref name="workspaceAnchor"/> disables the workspace bound (temp-only), used when the resolved
	/// anchor is not trustworthy as a confinement boundary.
	/// </summary>
	internal static bool IsPathConfined(string fullCandidate, string workspaceAnchor, string tempRoot) =>
		IsWithinDirectory(workspaceAnchor, fullCandidate) || IsWithinDirectory(tempRoot, fullCandidate);

	/// <summary>
	/// Resolves the real (symlink-followed) form of <paramref name="fullPath"/> by resolving the link target of
	/// its deepest existing ancestor and re-appending the not-yet-existing tail. Best-effort: any file-system or
	/// platform limitation (e.g. a test mock or a filesystem without link support) falls back to the lexical
	/// path, which is no weaker than the pre-symlink-aware behavior.
	/// </summary>
	private static string ResolveRealPath(IoFileSystem fileSystem, string fullPath) {
		try {
			var tail = new List<string>();
			string current = fullPath;
			while (!string.IsNullOrEmpty(current)
				&& !fileSystem.Directory.Exists(current)
				&& !fileSystem.File.Exists(current)) {
				tail.Add(fileSystem.Path.GetFileName(current));
				string parent = fileSystem.Path.GetDirectoryName(current);
				if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal)) {
					return fullPath; // reached the root without finding an existing ancestor
				}
				current = parent;
			}
			// Canonicalize EVERY component of the deepest existing ancestor, not just its terminal component:
			// ResolveLinkTarget does not follow symlinks in a path's PARENT chain, so a link one level up
			// (e.g. /tmp/link -> /etc, output /tmp/link/existing-dir/x) would otherwise keep its lexical prefix,
			// slip past the confinement check, and let the write follow the link out of the allowed zone.
			string realBase = CanonicalizeExisting(fileSystem, current);
			tail.Reverse();
			foreach (string segment in tail) {
				realBase = fileSystem.Path.Combine(realBase, segment);
			}
			return fileSystem.Path.GetFullPath(realBase);
		}
		catch (Exception) {
			// Link resolution is unavailable (a file system without symlink support, e.g. a unit-test mock) or
			// failed (a symlink cycle / unreadable link metadata). Degrade to the lexical path. This does NOT
			// reopen the intermediate-symlink escape: on a real file system where links exist, per-component
			// canonicalization above resolves them WITHOUT throwing, so a genuine escape is still caught. A cycle
			// or access error that lands here also fails the subsequent write itself (the OS follows the same
			// broken link), so no unverified path is actually written.
			return fullPath;
		}
	}

	// Resolves an existing path to its real location by resolving symlinks at EVERY component, parent-first,
	// so a symlink anywhere in the chain is followed before the confinement check runs.
	private static string CanonicalizeExisting(IoFileSystem fileSystem, string existingPath) {
		string parent = fileSystem.Path.GetDirectoryName(existingPath);
		if (string.IsNullOrEmpty(parent) || string.Equals(parent, existingPath, StringComparison.Ordinal)) {
			// Filesystem root — never a symlink, and ResolveLinkTarget throws DirectoryNotFoundException on a
			// drive root on Windows, so return it unresolved.
			return existingPath;
		}
		string realParent = CanonicalizeExisting(fileSystem, parent);
		string combined = fileSystem.Path.Combine(realParent, fileSystem.Path.GetFileName(existingPath));
		return ResolveSymlink(fileSystem, combined);
	}

	// Returns the final link target of <paramref name="path"/> when it is a symlink; otherwise the path itself.
	private static string ResolveSymlink(IoFileSystem fileSystem, string path) {
		bool isFile = fileSystem.File.Exists(path);
		if (!isFile && !fileSystem.Directory.Exists(path)) {
			return path; // nothing at this component to resolve (also avoids ResolveLinkTarget on a missing path)
		}
		IoFileSystemInfo info = isFile ? fileSystem.FileInfo.New(path) : fileSystem.DirectoryInfo.New(path);
		return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
	}

	/// <summary>
	/// True when <paramref name="anchor"/> is safe to use as a write-confinement boundary: not empty, not a
	/// filesystem root, and not an ancestor (or equal) of the user's home directory.
	/// </summary>
	internal static bool IsTrustedAnchor(IoFileSystem fileSystem, string anchor, string homeDirectory) {
		if (string.IsNullOrEmpty(anchor)) {
			return false;
		}
		// Do NOT trim a trailing separator here: on Windows 'C:\' trimmed to 'C:' is a *different*, drive-relative
		// path (the current directory on C:), not the drive root. Compare with a normalized single trailing
		// separator so a filesystem root is detected as such.
		string fullAnchor = fileSystem.Path.GetFullPath(anchor);
		string root = fileSystem.Path.GetPathRoot(fullAnchor);
		if (!string.IsNullOrEmpty(root)
			&& string.Equals(WithTrailingSeparator(fullAnchor), WithTrailingSeparator(root), StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		if (!string.IsNullOrEmpty(homeDirectory) && IsWithinDirectory(fullAnchor, fileSystem.Path.GetFullPath(homeDirectory))) {
			// home is inside (or equal to) the anchor → the anchor is an ancestor of home → too broad.
			return false;
		}
		return true;
	}

	private static string WithTrailingSeparator(string path) {
		if (string.IsNullOrEmpty(path)) {
			return path;
		}
		char last = path[path.Length - 1];
		return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
			? path
			: path + Path.DirectorySeparatorChar;
	}

	// True when <paramref name="target"/> is <paramref name="baseDirectory"/> itself or a descendant of it.
	// Uses GetRelativePath so the comparison honors the platform's own case rules: a relative result that stays
	// put (".") or descends is inside; one that starts with ".." (escape) or is rooted (different volume) is not.
	private static bool IsWithinDirectory(string baseDirectory, string target) {
		if (string.IsNullOrEmpty(baseDirectory)) {
			return false;
		}
		string relative = Path.GetRelativePath(baseDirectory, target);
		if (relative == "..") {
			return false;
		}
		return !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
			&& !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
			&& !Path.IsPathRooted(relative);
	}
}
