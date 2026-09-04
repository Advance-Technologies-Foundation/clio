using System;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Resolves the anchor directory under which <c>get-page</c> / <c>sync-pages</c> write their
/// <c>.clio-pages/{schema}/</c> output.
/// <para>
/// The MCP server is frequently launched with <c>$HOME</c> as its working directory (a common
/// host default — Claude Code starts <c>clio mcp-server</c> without an explicit cwd). Anchoring
/// the output at the raw current directory therefore dumps page artifacts straight into the
/// user's home folder. This resolver prefers the workspace root instead. With no workspace marker
/// it falls back to the current directory itself, and only when THAT is the bare home directory
/// does it use the managed clio home root — so output never litters <c>$HOME</c>, but in a plain
/// checkout it does land under cwd rather than under a workspace root.
/// </para>
/// <para>
/// An EXPLICIT caller-supplied directory is honored verbatim and is not confined: unlike the
/// <c>--output-file</c> path of the sibling schema-writing tools, it does not go through
/// <see cref="OutputPathConfinement"/>. Confining it needs a variant of that guard which permits an
/// already-existing anchor (<c>Resolve</c> refuses an existing target on purpose), so the change is
/// deliberately left out of the #1185 documentation pass rather than improvised here — the contract
/// field for <c>output-directory</c> states the missing boundary instead.
/// </para>
/// <para>See <c>docs/architecture/clio-pages-workspace-binding.md</c>.</para>
/// </summary>
internal static class PageOutputDirectoryResolver {

	private const string ClioDirectoryName = ".clio";
	private const string WorkspaceSettingsFileName = "workspaceSettings.json";

	/// <summary>
	/// Resolves the base directory under which the <c>.clio-pages</c> tree is created.
	/// Resolution order:
	/// <list type="number">
	/// <item>an explicit caller-supplied directory — honored regardless of cwd;</item>
	/// <item>the nearest ancestor of <paramref name="currentDirectory"/> containing
	/// <c>.clio/workspaceSettings.json</c> (the workspace marker);</item>
	/// <item><paramref name="currentDirectory"/> itself, when it is not the user's home directory;</item>
	/// <item><paramref name="homeFallbackAnchor"/> (the managed clio home root) when the current
	/// directory is the bare home directory — so output never litters <c>$HOME</c> and the tool
	/// never fails for lack of a workspace.</item>
	/// </list>
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to probe for the workspace marker.</param>
	/// <param name="currentDirectory">The process current working directory.</param>
	/// <param name="homeDirectory">The user's home directory (<c>SpecialFolder.UserProfile</c>).</param>
	/// <param name="homeFallbackAnchor">Anchor used instead of the bare home directory (clio home root), or
	/// <see langword="null"/> to yield NO anchor in that case - what a caller-supplied path is confined with, so
	/// clio's own configuration directory never becomes its boundary.</param>
	/// <param name="explicitDirectory">Optional caller-pinned output directory.</param>
	public static string? ResolveAnchor(
		IFileSystem fileSystem,
		string currentDirectory,
		string homeDirectory,
		string? homeFallbackAnchor,
		string? explicitDirectory) {
		if (!string.IsNullOrWhiteSpace(explicitDirectory)) {
			return fileSystem.Path.GetFullPath(explicitDirectory);
		}
		string? workspaceRoot = FindWorkspaceRoot(fileSystem, currentDirectory);
		if (workspaceRoot is not null) {
			return workspaceRoot;
		}
		return IsSameDirectory(fileSystem, currentDirectory, homeDirectory)
			? homeFallbackAnchor
			: currentDirectory;
	}

	/// <summary>
	/// Resolves the TOOL-OWNED default anchor: the same resolution as <see cref="ResolveAnchor"/>, but reading
	/// the process current directory and the user's home directory itself instead of taking them as parameters.
	/// </summary>
	/// <remarks>
	/// The cwd read happens under <see cref="McpServer.Tools.McpToolExecutionLock.CwdLock"/> because the MCP
	/// workspace tools PIN the process current directory; an unsynchronized read could anchor one tenant's
	/// default output inside another tenant's pinned directory. Callers — CLI commands and MCP tools alike —
	/// go through this method rather than taking that lock themselves, so the synchronization stays owned by
	/// the shared resolver and a CLI command never has to reach into the MCP layer for it. In the
	/// single-threaded CLI path the lock is uncontended.
	/// <para>
	/// This is for the default only. An EXPLICIT caller-supplied path goes through
	/// <see cref="OutputPathConfinement.Resolve"/>, which resolves its own anchor under the same lock.
	/// </para>
	/// </remarks>
	/// <param name="fileSystem">File-system abstraction used to read the cwd and probe for the workspace marker.</param>
	/// <returns>The workspace root above the current directory, the current directory itself, or the managed
	/// clio home root when the current directory is the bare home directory.</returns>
	public static string ResolveDefaultAnchor(IFileSystem fileSystem) {
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			return ResolveAnchor(
				fileSystem,
				fileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null)!; // non-null fallback, so the bare-home branch cannot yield null
		}
	}

	/// <summary>
	/// Walks up from <paramref name="startDirectory"/> looking for a directory that contains
	/// <c>.clio/workspaceSettings.json</c>. Matches the workspace marker <em>file</em> — not the
	/// bare <c>.clio</c> directory — so an orphaned <c>~/.clio</c> (e.g. a pre-consolidation cache
	/// folder) above a plain project directory does not masquerade as a workspace root.
	/// </summary>
	private static string? FindWorkspaceRoot(IFileSystem fileSystem, string startDirectory) {
		var directory = fileSystem.DirectoryInfo.New(startDirectory);
		while (directory is not null) {
			string marker = fileSystem.Path.Combine(directory.FullName, ClioDirectoryName, WorkspaceSettingsFileName);
			if (fileSystem.File.Exists(marker)) {
				return directory.FullName;
			}
			var parent = directory.Parent;
			// Stop at the filesystem root. On a real filesystem Parent is null at the root; the
			// FullName-equality guard is belt-and-suspenders against a pathological IFileSystem
			// whose Parent never returns null (e.g. an under-specified test substitute), which
			// would otherwise loop forever and exhaust memory.
			if (parent is null || string.Equals(parent.FullName, directory.FullName, StringComparison.Ordinal)) {
				break;
			}
			directory = parent;
		}
		return null;
	}

	private static bool IsSameDirectory(IFileSystem fileSystem, string left, string right) {
		if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) {
			return false;
		}
		string normalizedLeft = Normalize(fileSystem, left);
		string normalizedRight = Normalize(fileSystem, right);
		// macOS and Windows file systems are case-insensitive; OrdinalIgnoreCase is the safe
		// comparison for the home-directory guard across the platforms clio runs on.
		return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
	}

	private static string Normalize(IFileSystem fileSystem, string path) =>
		fileSystem.Path.GetFullPath(path)
			.TrimEnd(fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar);
}
