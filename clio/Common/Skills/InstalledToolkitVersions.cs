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
	private const long MaximumMetadataBytes = 1024 * 1024;

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> Read() {
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		foreach (string agent in new[] { "claude", "codex", "cursor", "copilot" }) {
			try {
				string home = GetAgentHome(agent);
				result[agent] = agent switch {
					"claude" => ReadClaude(home),
					"codex" => ReadCodex(home),
					"cursor" => ReadManifest(fileSystem.Combine(home, "plugins", "local", ToolkitDistribution.PluginName),
						[".cursor-plugin/plugin.json", ".claude-plugin/plugin.json", "plugin.json"]),
					_ => ReadManifest(fileSystem.Combine(home, "installed-plugins", ToolkitDistribution.MarketplaceName,
						ToolkitDistribution.PluginName), ["plugin.json", ".claude-plugin/plugin.json"])
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

	private string GetAgentHome(string agent) {
		string variable = agent switch {
			"codex" => "CODEX_HOME",
			"claude" => "CLAUDE_CONFIG_DIR",
			_ => null
		};
		string customHome = variable is null ? null : Environment.GetEnvironmentVariable(variable);
		return string.IsNullOrWhiteSpace(customHome) ? homeProvider.GetAgentHome(agent) : fileSystem.GetFullPath(customHome);
	}

	private string ReadClaude(string home) {
		string path = fileSystem.Combine(home, "plugins", "installed_plugins.json");
		if (!fileSystem.ExistsFile(path)) {
			return Missing;
		}
		using JsonDocument document = ReadJson(path);
		if (!document.RootElement.TryGetProperty("plugins", out JsonElement plugins)) {
			return Unknown;
		}
		if (!plugins.TryGetProperty(ToolkitDistribution.PluginSource, out JsonElement entries)) {
			return Missing;
		}
		List<string> versions = [];
		foreach (JsonElement entry in entries.EnumerateArray()) {
			if (StringProperty(entry, "scope") == "user") {
				string installPath = StringProperty(entry, "installPath");
				versions.Add(!string.IsNullOrWhiteSpace(installPath) && fileSystem.ExistsDirectory(installPath)
					? VersionProperty(entry) : Unknown);
			}
		}
		return versions.Count == 0 ? Missing : string.Join(", ", versions.Distinct(StringComparer.Ordinal));
	}

	private string ReadCodex(string home) {
		string root = fileSystem.Combine(home, "plugins", "cache", ToolkitDistribution.MarketplaceName,
			ToolkitDistribution.PluginName);
		if (!fileSystem.ExistsDirectory(root)) {
			return Missing;
		}
		string[] directories = fileSystem.GetDirectories(root);
		if (directories.Length == 0) {
			return Missing;
		}
		// Codex prefers "local", otherwise the greatest cache version (semver, with ordinal fallback).
		Array.Sort(directories, (left, right) => CompareCacheVersions(Path.GetFileName(left), Path.GetFileName(right)));
		string active = directories.FirstOrDefault(path => Path.GetFileName(path) == "local") ?? directories[^1];
		return ReadManifest(active, [".codex-plugin/plugin.json", ".claude-plugin/plugin.json", "plugin.json"]);
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

	private JsonDocument ReadJson(string path) {
		if (fileSystem.GetFilesInfos(path).Length > MaximumMetadataBytes) {
			throw new InvalidOperationException("Toolkit metadata exceeds the read limit.");
		}
		return JsonDocument.Parse(fileSystem.ReadAllText(path));
	}

	private static string VersionProperty(JsonElement element) {
		string version = StringProperty(element, "version");
		return string.IsNullOrWhiteSpace(version) ? Unknown : version;
	}

	private static string StringProperty(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() : null;
}
