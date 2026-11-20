using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Management.Automation;

namespace Clio.Common.Skills;

/// <summary>Reads the installed toolkit metadata owned by each supported coding agent.</summary>
public sealed class InstalledToolkitVersions(IFileSystem fileSystem, IUserHomeProvider homeProvider)
	: IInstalledToolkitVersions {
	private const string Unknown = "unknown (metadata unavailable)";
	private const string Missing = "not installed";
	private const string PluginsDirectory = "plugins";
	private const string ClaudeAgentId = "claude";
	private const string CodexAgentId = "codex";
	private const long MaximumMetadataBytes = 1024 * 1024;

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> Read() {
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		foreach (string agent in new[] { ClaudeAgentId, CodexAgentId, "cursor", "copilot" }) {
			try {
				string home = GetAgentHome(agent);
				result[agent] = agent switch {
					ClaudeAgentId => ReadClaude(home),
					CodexAgentId => ReadCodex(home),
					"cursor" => ReadManifest(fileSystem.Combine(home, PluginsDirectory, "local", ToolkitDistribution.PluginName),
						[".cursor-plugin/plugin.json", ".claude-plugin/plugin.json", "plugin.json"]),
					_ => ReadManifest(fileSystem.Combine(home, "installed-plugins", ToolkitDistribution.MarketplaceName,
						ToolkitDistribution.PluginName), ["plugin.json", ".github/plugin/plugin.json", ".github/plugin.json", ".claude-plugin/plugin.json"])
				};
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
				or JsonException or InvalidOperationException or ArgumentException or System.Security.SecurityException) {
				// Optional agent metadata must never prevent other component versions from being reported.
				result[agent] = Unknown;
			}
		}
		return result;
	}

	/// <inheritdoc />
	public string ReadInstalledCommit(string agentId) {
		try {
			return agentId switch {
				ClaudeAgentId => ReadClaudeCommit(GetAgentHome(ClaudeAgentId)),
				CodexAgentId => ReadGitHeadCommit(SelectCodexPayload(GetAgentHome(CodexAgentId))),
				_ => null
			};
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// Anything unreadable is unknown, never "up to date", and must not fail the update: the caller
			// falls back to a full update.
			return null;
		}
	}

	private string GetAgentHome(string agent) {
		string variable = agent switch {
			CodexAgentId => "CODEX_HOME",
			ClaudeAgentId => "CLAUDE_CONFIG_DIR",
			_ => null
		};
		string customHome = variable is null ? null : Environment.GetEnvironmentVariable(variable);
		return string.IsNullOrWhiteSpace(customHome) ? homeProvider.GetAgentHome(agent) : fileSystem.GetFullPath(customHome);
	}

	private string ReadClaude(string home) {
		string path = fileSystem.Combine(home, PluginsDirectory, "installed_plugins.json");
		if (!fileSystem.ExistsFile(path)) {
			return Missing;
		}
		using JsonDocument document = ReadJson(path);
		if (!document.RootElement.TryGetProperty(PluginsDirectory, out JsonElement plugins)) {
			return Unknown;
		}
		if (!plugins.TryGetProperty(ToolkitDistribution.PluginSource, out JsonElement entries)) {
			return Missing;
		}
		List<string> versions = [];
		foreach (JsonElement entry in entries.EnumerateArray().Where(entry => StringProperty(entry, "scope") == "user")) {
			string installPath = StringProperty(entry, "installPath");
			versions.Add(!string.IsNullOrWhiteSpace(installPath) && fileSystem.ExistsDirectory(installPath)
				? VersionProperty(entry) : Unknown);
		}
		return versions.Count == 0 ? Missing : string.Join(", ", versions.Distinct(StringComparer.Ordinal));
	}

	private string ReadCodex(string home) {
		string active = SelectCodexPayload(home);
		return active is null
			? Missing
			: ReadManifest(active, [".codex-plugin/plugin.json", ".claude-plugin/plugin.json", "plugin.json"]);
	}

	/// <summary>Returns the Codex cache payload Codex itself loads, or <c>null</c> when none is installed.</summary>
	private string SelectCodexPayload(string home) {
		string root = fileSystem.Combine(home, PluginsDirectory, "cache", ToolkitDistribution.MarketplaceName,
			ToolkitDistribution.PluginName);
		if (!fileSystem.ExistsDirectory(root)) {
			return null;
		}
		string[] directories = fileSystem.GetDirectories(root);
		if (directories.Length == 0) {
			return null;
		}
		// Codex prefers "local", otherwise the greatest cache version (semver, with ordinal fallback).
		Array.Sort(directories, (left, right) => CompareCacheVersions(Path.GetFileName(left), Path.GetFileName(right)));
		return directories.FirstOrDefault(path => Path.GetFileName(path) == "local") ?? directories[^1];
	}

	private string ReadClaudeCommit(string home) {
		string path = fileSystem.Combine(home, PluginsDirectory, "installed_plugins.json");
		if (!fileSystem.ExistsFile(path)) {
			return null;
		}
		using JsonDocument document = ReadJson(path);
		if (!document.RootElement.TryGetProperty(PluginsDirectory, out JsonElement plugins)
			|| !plugins.TryGetProperty(ToolkitDistribution.PluginSource, out JsonElement entries)
			|| entries.ValueKind != JsonValueKind.Array) {
			return null;
		}
		List<string> commits = [];
		foreach (JsonElement entry in entries.EnumerateArray().Where(entry => StringProperty(entry, "scope") == "user")) {
			string installPath = StringProperty(entry, "installPath");
			if (string.IsNullOrWhiteSpace(installPath) || !fileSystem.ExistsDirectory(installPath)) {
				// A registry entry whose payload is gone is not an installation that can be up to date.
				return null;
			}
			string commit = GitCommitId.Normalize(StringProperty(entry, "gitCommitSha"));
			if (commit is null) {
				return null;
			}
			commits.Add(commit);
		}
		List<string> distinct = commits.Distinct(StringComparer.Ordinal).ToList();
		return distinct.Count == 1 ? distinct[0] : null;
	}

	/// <summary>
	/// Reads the commit a payload directory's own <c>.git</c> points at, without discovering any parent
	/// repository: <c>HEAD</c> holds either a commit or <c>ref: refs/...</c>, resolved through the loose ref
	/// file and then <c>packed-refs</c>.
	/// </summary>
	private string ReadGitHeadCommit(string payloadDirectory) {
		if (payloadDirectory is null) {
			return null;
		}
		string gitDirectory = fileSystem.Combine(payloadDirectory, ".git");
		string headPath = fileSystem.Combine(gitDirectory, "HEAD");
		if (!fileSystem.ExistsDirectory(gitDirectory) || !fileSystem.ExistsFile(headPath)) {
			return null;
		}
		string head = ReadBoundedText(headPath).Trim();
		const string refPrefix = "ref:";
		if (!head.StartsWith(refPrefix, StringComparison.Ordinal)) {
			return GitCommitId.Normalize(head);
		}
		string refName = head[refPrefix.Length..].Trim();
		if (!refName.StartsWith("refs/", StringComparison.Ordinal) || refName.Contains("..", StringComparison.Ordinal)) {
			return null;
		}
		string loosePath = fileSystem.Combine(gitDirectory, refName.Replace('/', Path.DirectorySeparatorChar));
		if (fileSystem.ExistsFile(loosePath)) {
			return GitCommitId.Normalize(ReadBoundedText(loosePath));
		}
		string packedRefsPath = fileSystem.Combine(gitDirectory, "packed-refs");
		if (!fileSystem.ExistsFile(packedRefsPath)) {
			return null;
		}
		foreach (string line in ReadBoundedText(packedRefsPath).Split('\n')) {
			string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 2 && parts[1] == refName) {
				return GitCommitId.Normalize(parts[0]);
			}
		}
		return null;
	}

	private static int CompareCacheVersions(string left, string right) {
		if (SemanticVersion.TryParse(left, out SemanticVersion leftVersion)
			&& SemanticVersion.TryParse(right, out SemanticVersion rightVersion)) {
			return leftVersion.CompareTo(rightVersion);
		}
		return string.Compare(left, right, StringComparison.Ordinal);
	}

	private string ReadManifest(string root, string[] candidates) {
		if (!fileSystem.ExistsDirectory(root)) {
			return Missing;
		}
		foreach (string relative in candidates) {
			string path = fileSystem.Combine(root, relative);
			if (!fileSystem.ExistsFile(path)) {
				continue;
			}
			using JsonDocument document = ReadJson(path);
			return StringProperty(document.RootElement, "name") == ToolkitDistribution.PluginName
				? VersionProperty(document.RootElement) : Unknown;
		}
		return Unknown;
	}

	private JsonDocument ReadJson(string path) => JsonDocument.Parse(ReadBoundedText(path));

	private string ReadBoundedText(string path) {
		if (fileSystem.GetFilesInfos(path).Length > MaximumMetadataBytes) {
			throw new InvalidOperationException("Toolkit metadata exceeds the read limit.");
		}
		return fileSystem.ReadAllText(path);
	}

	private static string VersionProperty(JsonElement element) {
		string version = StringProperty(element, "version");
		return string.IsNullOrWhiteSpace(version) ? Unknown : version;
	}

	private static string StringProperty(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() : null;
}
