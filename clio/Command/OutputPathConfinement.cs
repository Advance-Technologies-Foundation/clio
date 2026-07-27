namespace Clio.Command;

using System;
using System.IO;
using Clio.Common;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>
/// Shared guard for the <c>--output-file</c> write path of the MCP-callable schema-writing tools
/// (<c>get-classic-page-sources</c>, <c>get-client-unit-schema</c>). Those tools are non-destructive, so the MCP
/// host does not prompt on the write, and their output path can be supplied by an agent rather than typed at a
/// shell. Writing an unconstrained path verbatim would let a <c>..</c> traversal or an absolute system path
/// overwrite an arbitrary file, so an explicit output-file is confined to the workspace anchor OR the OS temp
/// directory — the two locations the migration flow legitimately writes to.
/// </summary>
internal static class OutputPathConfinement {

	/// <summary>
	/// Resolves <paramref name="outputFile"/> to an absolute path and confirms it stays inside the workspace
	/// anchor or the OS temp directory.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used for path resolution and the workspace-marker probe.</param>
	/// <param name="outputFile">The caller-supplied output path (may be relative).</param>
	/// <returns>
	/// The resolved absolute path with a <c>null</c> error when allowed; <c>(null, error)</c> when the path
	/// escapes both allowed locations.
	/// </returns>
	internal static (string path, string error) Resolve(IoFileSystem fileSystem, string outputFile) {
		// H1: reading the process-global cwd (for the anchor) must serialize against the MCP workspace tools that
		// PIN cwd. In the MCP path this runs under the shared tool lock; in the single-threaded CLI path the lock
		// is uncontended. lock is reentrant, so a caller already holding it (the bundle command) is unaffected.
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				fileSystem,
				fileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			string full = fileSystem.Path.GetFullPath(outputFile);
			string tempRoot = fileSystem.Path.GetFullPath(fileSystem.Path.GetTempPath());
			if (!IsPathConfined(full, anchor, tempRoot)) {
				return (null,
					$"output-file '{outputFile}' resolves outside the allowed locations; it must be inside the " +
					"workspace or the OS temp directory.");
			}
			return (full, null);
		}
	}

	/// <summary>
	/// True when <paramref name="fullCandidate"/> (an already-resolved absolute path) lies within the workspace
	/// anchor OR the OS temp root. Both bounds are the two locations the schema-writing tools write to; everything
	/// else — parent-traversal escapes, absolute system paths, other volumes — is out of bounds.
	/// </summary>
	internal static bool IsPathConfined(string fullCandidate, string workspaceAnchor, string tempRoot) =>
		IsWithinDirectory(workspaceAnchor, fullCandidate) || IsWithinDirectory(tempRoot, fullCandidate);

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
