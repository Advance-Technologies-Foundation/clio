namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Clio.Common;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>
/// Shared guard for the <c>--output-file</c> write path of the MCP-callable schema-writing tools
/// (<c>get-classic-page-sources</c>, <c>get-client-unit-schema</c>, <c>get-schema</c>, <c>get-sql-schema</c>).
/// Those tools can be invoked over MCP, so the output path may be supplied by an agent rather than typed at a
/// shell. Writing an unconstrained path verbatim would let a <c>..</c> traversal, an absolute system path, or a
/// symlink overwrite an arbitrary file, so an explicit output-file is confined to the workspace anchor OR the OS
/// temp directory — the two locations the migration flow legitimately writes to.
/// </summary>
internal static class OutputPathConfinement {

	// Owner read/write only. Applied at creation time to the temporary file the final output is renamed from,
	// so the payload is never momentarily readable by other local users of a shared temp root.
	internal const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

	// Upper bound on how many links a single path component may chain through before it is treated as a cycle.
	// A legitimate link chain is a handful deep; anything beyond this is pathological and fails closed.
	private const int MaxLinkResolutionDepth = 40;

	/// <summary>
	/// Raised internally when a component is a CONFIRMED symbolic link whose chain cannot be resolved (a cycle or
	/// a chain deeper than <see cref="MaxLinkResolutionDepth"/>). It forces <see cref="Resolve"/> to fail closed
	/// rather than degrade to a lexical path that would slip past confinement.
	/// </summary>
	public sealed class UnresolvableLinkException : Exception { }

	/// <summary>
	/// Read-side counterpart of <see cref="Resolve"/>: confines a caller-supplied INPUT path to the same
	/// workspace anchor / OS temp directory, then requires the file to exist. Without it a file-backed
	/// payload argument is an arbitrary file reader — a prompt-injection payload could point it at clio's
	/// own credentials store and have the contents forwarded to a remote endpoint. The existence rule is
	/// inverted relative to <see cref="Resolve"/>, which refuses a path that already exists; everything
	/// before that rule is shared.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for path resolution and the workspace-marker probe.</param>
	/// <param name="inputFile">The caller-supplied input path (may be relative).</param>
	/// <param name="optionName">Argument name used in the caller-facing messages.</param>
	/// <returns>The resolved absolute path with a <c>null</c> error, or <c>(null, error)</c>.</returns>
	internal static (string path, string error) ResolveForRead(
		IoFileSystem fileSystem, string inputFile, string optionName) {
		(string _, string real, string error) = ResolveConfined(fileSystem, inputFile, optionName);
		if (error is not null) {
			return (null, error);
		}
		if (!fileSystem.File.Exists(real)) {
			return (null, $"{optionName} file was not found.");
		}
		// The CANONICAL path is returned, not the lexical one. Returning the lexical form meant the check ran
		// on the symlink-resolved path while the caller opened the unresolved one, so an intermediate link
		// swapped after validation redirected the read outside the allowed roots.
		return (real, null);
	}

	/// <summary>
	/// Output-path counterpart of <see cref="ResolveForRead"/> that returns the CANONICAL (symlink-followed)
	/// path rather than the lexical one, so the create runs against the same path confinement approved.
	/// Used by the OData file contract, whose payload boundary is caller-supplied on both directions.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for path resolution and the workspace-marker probe.</param>
	/// <param name="outputFile">The caller-supplied output path (may be relative).</param>
	/// <returns>The canonical absolute path with a <c>null</c> error, or <c>(null, error)</c>.</returns>
	internal static (string path, string error) ResolveCanonicalOutput(IoFileSystem fileSystem, string outputFile) {
		(string _, string real, string error) = ResolveConfined(fileSystem, outputFile, "output-file");
		if (error is not null) {
			return (null, error);
		}
		string existsError = RejectExistingTarget(fileSystem, real, outputFile);
		return existsError is not null ? (null, existsError) : (real, null);
	}

	/// <summary>
	/// Re-runs confinement on an ALREADY-canonical path and confirms it still canonicalizes to itself. Called
	/// while the caller holds the file open, so a component swapped between resolution and the open is caught
	/// before the handle's contents are used.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for path resolution.</param>
	/// <param name="resolvedPath">A path previously returned by <see cref="ResolveForRead"/>.</param>
	/// <param name="optionName">Argument name used in the caller-facing message.</param>
	/// <returns><c>null</c> when the path is unchanged and still confined, otherwise the caller-facing error.</returns>
	internal static string RevalidateResolved(IoFileSystem fileSystem, string resolvedPath, string optionName) {
		(string _, string real, string error) = ResolveConfined(fileSystem, resolvedPath, optionName);
		if (error is not null) {
			return error;
		}
		return string.Equals(real, resolvedPath, StringComparison.Ordinal)
			? null
			: $"{optionName} changed on disk while it was being read; refusing to continue.";
	}

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
		(string full, string _, string error) = ResolveConfined(fileSystem, outputFile, "output-file");
		if (error is not null) {
			return (null, error);
		}
		string existsError = RejectExistingTarget(fileSystem, full, outputFile);
		return existsError is not null ? (null, existsError) : (full, null);
	}

	// Keep the Destructive=false classification honest: an explicit output-file must not silently
	// overwrite an existing file. Confinement bounds WHERE the write lands, not WHETHER it destroys
	// existing content. The tool-owned default output path does not flow through here, so re-runs to it
	// still overwrite their own output.
	private static string RejectExistingTarget(IoFileSystem fileSystem, string resolved, string requested) =>
		fileSystem.File.Exists(resolved) || fileSystem.Directory.Exists(resolved)
			? $"output-file '{requested}' already exists; refusing to overwrite it. Choose a different " +
				"path or remove the existing file."
			: null;

	// Everything both directions share: resolve to an absolute, symlink-followed path and confirm it stays
	// inside a trusted workspace anchor or the OS temp root. The existence rule is the caller's, because the
	// two directions want opposite answers.
	private static (string path, string realPath, string error) ResolveConfined(
		IoFileSystem fileSystem, string candidatePath, string optionName) {
		// H1: reading the process-global cwd (for the anchor) must serialize against the MCP workspace tools that
		// PIN cwd. In the MCP path this runs under the shared tool lock; in the single-threaded CLI path the lock
		// is uncontended. Callers that resolve a tool-owned DEFAULT anchor instead go through
		// PageOutputDirectoryResolver.ResolveDefaultAnchor, which takes the same lock — the two are alternative
		// branches of one decision (explicit path vs default), so they never nest.
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				fileSystem,
				fileSystem.Directory.GetCurrentDirectory(),
				home,
				ClioRuntimePaths.Home,
				null);
			string full = fileSystem.Path.GetFullPath(candidatePath);
			string tempRoot = fileSystem.Path.GetFullPath(fileSystem.Path.GetTempPath());

			// Confine the REAL (symlink-followed) path, not just the lexical one: Path.GetFullPath collapses `..`
			// but never resolves a symlink, and the later write follows links. A link planted under an allowed
			// root (the classic world-writable /tmp attack) could otherwise land the write on an arbitrary file.
			// BOTH bounds are resolved the same way so a symlinked temp/home root (e.g. macOS /var -> /private/var)
			// does not cause a false rejection of an in-bounds path.
			string real, realTempRoot, realAnchor, realHome;
			try {
				real = ResolveRealPath(fileSystem, full);
				// Resolve the bounds the same way — including these system paths — so an unresolvable link
				// anywhere in the comparison fails CLOSED with the friendly message rather than escaping Resolve
				// as an opaque exception.
				realTempRoot = ResolveRealPath(fileSystem, tempRoot);
				realAnchor = ResolveRealPath(fileSystem, anchor);
				realHome = string.IsNullOrEmpty(home) ? home : ResolveRealPath(fileSystem, home);
			}
			catch (UnresolvableLinkException) {
				// A confirmed symlink whose chain could not be resolved (cycle / pathological depth / a target
				// that cannot be normalized). Fail CLOSED: never fall back to a lexical path that would slip past
				// confinement (see ResolveRealPath / ResolveSymlink).
				return (null, null,
					$"{optionName} '{candidatePath}' resolves through an unresolvable symbolic link; refusing to continue.");
			}

			// A filesystem root ('/', 'C:\') or an ancestor of the user's home directory ('/Users', '/home',
			// 'C:\Users') is too broad to be a write boundary — an MCP host launched with such a cwd (Claude
			// Desktop has historically used '/') would otherwise confine to the whole volume. Drop an untrusted
			// anchor so only the OS temp root remains allowed.
			string trustedAnchor = IsTrustedAnchor(fileSystem, realAnchor, realHome) ? realAnchor : null;

			if (!IsPathConfined(real, trustedAnchor, realTempRoot)) {
				return (null, null,
					$"{optionName} '{candidatePath}' resolves outside the allowed locations; it must be inside the " +
					"workspace or the OS temp directory.");
			}

			return (full, real, null);
		}
	}

	/// <summary>
	/// Atomically writes <paramref name="content"/> to <paramref name="resolvedPath"/> — a path already returned
	/// by <see cref="Resolve"/> — creating the parent directory if needed. The create itself is the gate:
	/// <see cref="FileMode.CreateNew"/> fails if the target exists, so it (a) keeps the additive Destructive=false
	/// contract honest even against a target that appeared after <see cref="Resolve"/> checked, and (b) collapses
	/// the resolve→write TOCTOU window; on POSIX its <c>O_EXCL</c> also refuses to follow a symlink at the final
	/// component. Throws <see cref="IOException"/> with a caller-facing message when the target already exists.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for the directory probe and the atomic write.</param>
	/// <param name="resolvedPath">The confined absolute path returned by <see cref="Resolve"/>.</param>
	/// <param name="content">The text to write.</param>
	internal static void WriteAtomic(IoFileSystem fileSystem, string resolvedPath, string content) =>
		WriteThroughTemporaryFile(fileSystem, resolvedPath, stream => {
			using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
			writer.Write(content);
			writer.Flush();
		});

	/// <summary>
	/// Byte-exact counterpart of <see cref="WriteAtomic(IoFileSystem, string, string)"/>, for a payload that must
	/// land on disk exactly as it arrived on the wire. The string overload round-trips through a decoder and a
	/// <see cref="StreamWriter"/>, which normalises the encoding (a BOM on the source is dropped, an invalid
	/// sequence becomes U+FFFD); a caller that advertises a byte-faithful copy has to bypass that.
	/// </summary>
	internal static void WriteAtomic(IoFileSystem fileSystem, string resolvedPath, byte[] content) =>
		WriteThroughTemporaryFile(fileSystem, resolvedPath, stream => stream.Write(content, 0, content.Length));

	/// <summary>
	/// Lowest-level of the three <see cref="WriteAtomic(IoFileSystem, string, string)"/> overloads: hands the
	/// open temporary-file stream to <paramref name="writeContent"/> so a caller that produces its payload
	/// incrementally does not have to materialize it first.
	/// </summary>
	/// <remarks>
	/// The 0600 guarantee is about the window in which the temporary file EXISTS, so an assertion on the
	/// renamed final file alone cannot tell a creation-time mode from an unsafe create-then-chmod. Only a
	/// writer that observes the open temporary file can, which is why this overload is part of the API rather
	/// than an implementation detail.
	/// </remarks>
	/// <param name="fileSystem">File-system abstraction used for the directory probe and the atomic write.</param>
	/// <param name="resolvedPath">The confined absolute path returned by <see cref="Resolve"/>.</param>
	/// <param name="writeContent">Writes the payload into the open temporary-file stream.</param>
	internal static void WriteAtomic(IoFileSystem fileSystem, string resolvedPath,
		Action<Stream> writeContent) =>
		WriteThroughTemporaryFile(fileSystem, resolvedPath, writeContent);

	/// <summary>
	/// Completes the content in a sibling temporary file and only then moves it onto the final name, without
	/// replacing an existing file.
	/// </summary>
	/// <remarks>
	/// <see cref="FileMode.CreateNew"/> reserves the NAME atomically, not the CONTENT. Writing straight into
	/// the final path therefore left a truncated file behind whenever the write failed part-way — a full disk,
	/// say — while the call reported failure, and the no-overwrite guard then refused every retry against the
	/// wreckage. The temporary file is removed on every failure path, so a failed write leaves nothing at all.
	/// <para>
	/// On Unix the temporary file is created 0600 rather than at whatever the process umask allows. An output
	/// file is legitimately permitted under the SHARED OS temp root, and the payload is a raw service response —
	/// business data, often personal data — so a default 0644 would leave it readable by every other local user
	/// for as long as it sits there, and a failed cleanup would leave the sibling temporary copy the same way.
	/// The mode is set at CREATION, not afterwards: a chmod after the fact still leaves a window in which the
	/// file exists world-readable. The final name inherits it, because the move renames the same inode.
	/// </para>
	/// </remarks>
	private static void WriteThroughTemporaryFile(
		IoFileSystem fileSystem, string resolvedPath, Action<Stream> writeContent) {
		string directory = fileSystem.Path.GetDirectoryName(resolvedPath);
		if (!string.IsNullOrEmpty(directory) && !fileSystem.Directory.Exists(directory)) {
			fileSystem.Directory.CreateDirectory(directory);
		}
		string temporaryPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
		try {
			FileStreamOptions options = new() {
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				options.UnixCreateMode = OwnerOnlyFile;
			}
			using (Stream stream = fileSystem.File.Open(temporaryPath, options)) {
				writeContent(stream);
				stream.Flush();
			}
			fileSystem.File.Move(temporaryPath, resolvedPath, overwrite: false);
		}
		catch (IOException) when (fileSystem.File.Exists(resolvedPath)) {
			DeleteTemporaryFile(fileSystem, temporaryPath);
			throw new IOException(
				$"output-file '{resolvedPath}' already exists; refusing to overwrite it. Choose a different " +
				"path or remove the existing file.");
		}
		catch {
			DeleteTemporaryFile(fileSystem, temporaryPath);
			throw;
		}
	}

	private static void DeleteTemporaryFile(IoFileSystem fileSystem, string temporaryPath) {
		try {
			if (fileSystem.File.Exists(temporaryPath)) {
				fileSystem.File.Delete(temporaryPath);
			}
		}
		catch (Exception) {
			// A leftover temporary file is not worth replacing the real failure with a second exception.
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
			// Directory.Exists / File.Exists FOLLOW a symlink and both report false for a DANGLING link (target
			// absent), so a dangling symlink would otherwise be treated as an ordinary not-yet-created tail
			// segment and appended lexically — never canonicalized. The later write follows the link at the OS
			// level and lands OUTSIDE the allowed zone (the terminal-/intermediate-symlink escape). Stop the walk
			// at a reparse point too, so its own component is canonicalized (and thus confinement-checked)
			// regardless of whether its target exists yet.
			while (!string.IsNullOrEmpty(current)
				&& !fileSystem.Directory.Exists(current)
				&& !fileSystem.File.Exists(current)
				&& !IsReparsePoint(fileSystem, current)) {
				tail.Add(fileSystem.Path.GetFileName(current));
				string parent = fileSystem.Path.GetDirectoryName(current);
				if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal)) {
					return fullPath; // reached the root without finding an existing ancestor
				}
				current = parent;
			}
			// Canonicalize EVERY component of the deepest existing ancestor (or dangling reparse point), not just
			// its terminal component: a link one level up (e.g. /tmp/link -> /etc, output /tmp/link/existing-dir/x)
			// would otherwise keep its lexical prefix, slip past the confinement check, and let the write follow
			// the link out of the allowed zone.
			string realBase = CanonicalizeExisting(fileSystem, current);
			tail.Reverse();
			foreach (string segment in tail) {
				realBase = fileSystem.Path.Combine(realBase, segment);
			}
			return fileSystem.Path.GetFullPath(realBase);
		}
		catch (UnresolvableLinkException) {
			// A CONFIRMED symlink whose chain cannot be resolved (cycle / pathological depth). Propagate so
			// Resolve fails CLOSED — never degrade to the lexical fallback below, which would let a link slip
			// past confinement.
			throw;
		}
		catch (Exception) {
			// Link INSPECTION is unavailable (a file system without symlink support, e.g. a unit-test mock) or
			// failed on a path that is not a confirmed link. Degrade to the lexical path. This does NOT reopen the
			// intermediate-symlink escape: on a real file system where links exist, per-component canonicalization
			// above resolves them WITHOUT throwing, so a genuine escape is still caught; and a confirmed-but-
			// unresolvable link takes the fail-closed branch above.
			return fullPath;
		}
	}

	// Resolves an existing path (or a dangling reparse point) to its real location by resolving symlinks at EVERY
	// component, parent-first, so a symlink anywhere in the chain is followed before the confinement check runs.
	private static string CanonicalizeExisting(IoFileSystem fileSystem, string existingPath) {
		string parent = fileSystem.Path.GetDirectoryName(existingPath);
		if (string.IsNullOrEmpty(parent) || string.Equals(parent, existingPath, StringComparison.Ordinal)) {
			// Filesystem root — never a symlink, and ResolveLinkTarget throws DirectoryNotFoundException on a
			// drive root on Windows, so return it unresolved.
			return existingPath;
		}
		string realParent = CanonicalizeExisting(fileSystem, parent);
		string combined = fileSystem.Path.Combine(realParent, fileSystem.Path.GetFileName(existingPath));
		return ResolveSymlink(fileSystem, combined, 0);
	}

	// Returns the real target of <paramref name="path"/> when it is a symlink (following the link chain, bounded),
	// otherwise the path itself. Unlike ResolveLinkTarget(returnFinalTarget:true), this reads the link target via
	// LinkTarget so a DANGLING link (target not yet created) is still resolved rather than left lexical.
	private static string ResolveSymlink(IoFileSystem fileSystem, string path, int depth) {
		if (!TryReadLinkTarget(fileSystem, path, out string target)) {
			return path; // not a symlink — nothing to resolve
		}
		if (depth >= MaxLinkResolutionDepth) {
			// A link chain this long is a cycle or pathological. Fail CLOSED rather than trust a lexical path.
			throw new UnresolvableLinkException();
		}
		// A link target may be relative to the link's own directory; resolve it there, then collapse it.
		string resolved;
		try {
			resolved = fileSystem.Path.IsPathRooted(target)
				? target
				: fileSystem.Path.Combine(fileSystem.Path.GetDirectoryName(path) ?? string.Empty, target);
			resolved = fileSystem.Path.GetFullPath(resolved);
		}
		catch (Exception) {
			// The node IS a confirmed link but its target cannot be normalized (e.g. a malformed target). Fail
			// CLOSED so ResolveRealPath's broad catch cannot degrade a real link to its lexical path — that broad
			// catch is reserved strictly for filesystems that do not support link inspection at all.
			throw new UnresolvableLinkException();
		}
		// The target may itself be a symlink — follow the chain (bounded by MaxLinkResolutionDepth).
		return ResolveSymlink(fileSystem, resolved, depth + 1);
	}

	// True when <paramref name="path"/> is a symbolic link / reparse point, EVEN when its target does not exist
	// (a dangling link). Distinct from File.Exists / Directory.Exists, which follow the link and report false for
	// a dangling one. Returns false — never throws — when link metadata cannot be read (mock / unsupported FS),
	// so a filesystem without link support degrades to the lexical fallback in ResolveRealPath.
	private static bool IsReparsePoint(IoFileSystem fileSystem, string path) =>
		TryReadLinkTarget(fileSystem, path, out _);

	private static bool TryReadLinkTarget(IoFileSystem fileSystem, string path, out string target) {
		// Probe the two info kinds INDEPENDENTLY: a `??` would skip the DirectoryInfo fallback if reading
		// FileInfo.LinkTarget threw, so a directory symlink whose FileInfo probe throws would be misread as
		// not-a-link (a security softening). Read each under its own guard instead.
		target = ReadLinkTargetOrNull(() => fileSystem.FileInfo.New(path).LinkTarget)
			?? ReadLinkTargetOrNull(() => fileSystem.DirectoryInfo.New(path).LinkTarget);
		return target != null;
	}

	private static string ReadLinkTargetOrNull(Func<string> read) {
		try {
			return read();
		}
		catch (Exception) {
			return null;
		}
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
