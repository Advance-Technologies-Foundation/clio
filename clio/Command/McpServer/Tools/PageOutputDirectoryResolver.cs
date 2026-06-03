using System;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves the anchor directory under which <c>get-page</c> / <c>sync-pages</c> write their
/// <c>.clio-pages/{schema}/</c> output.
/// <para>
/// The MCP server is frequently launched with <c>$HOME</c> as its working directory (a common
/// host default — Claude Code starts <c>clio mcp-server</c> without an explicit cwd). Anchoring
/// the output at the raw current directory therefore dumps page artifacts straight into the
/// user's home folder. This resolver binds the output to the workspace root instead, and falls
/// back to the managed clio home root (never the bare home directory) when no workspace is found.
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
	/// <param name="homeFallbackAnchor">Anchor used instead of the bare home directory (clio home root).</param>
	/// <param name="explicitDirectory">Optional caller-pinned output directory.</param>
	public static string ResolveAnchor(
		IFileSystem fileSystem,
		string currentDirectory,
		string homeDirectory,
		string homeFallbackAnchor,
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
			directory = directory.Parent;
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
