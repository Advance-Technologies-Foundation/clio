using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="IJsonConfigEditor"/> backed by <see cref="IFileSystem"/> and
/// <see cref="JsonNode"/> in-place edits (unrelated keys are preserved).
/// </summary>
public sealed class JsonConfigEditor(IFileSystem fileSystem) : IJsonConfigEditor {
	private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

	private readonly IFileSystem _fileSystem = fileSystem;

	/// <inheritdoc />
	public void EnableMarketplaceAutoUpdate(string settingsPath, string marketplaceName) {
		JsonObject settings = ReadObject(settingsPath) ?? [];
		if (settings["extraKnownMarketplaces"] is not JsonObject extra) {
			extra = [];
			settings["extraKnownMarketplaces"] = extra;
		}

		if (extra[marketplaceName] is not JsonObject entry) {
			entry = [];
			extra[marketplaceName] = entry;
		}

		// Drop a stale directory-source entry left by a previous local-install run.
		if (entry["source"] is JsonObject source
			&& string.Equals(AsString(source["source"]), "directory", StringComparison.Ordinal)) {
			entry.Remove("source");
		}

		entry["autoUpdate"] = true;
		Write(settingsPath, settings);
	}

	/// <inheritdoc />
	public void RemoveMarketplaceFromSettings(string settingsPath, string marketplaceName) {
		JsonObject settings = ReadObject(settingsPath);
		if (settings?["extraKnownMarketplaces"] is not JsonObject extra || !extra.ContainsKey(marketplaceName)) {
			return;
		}

		extra.Remove(marketplaceName);
		Write(settingsPath, settings);
	}

	/// <inheritdoc />
	public void MergeClioMcpServer(string mcpJsonPath) {
		JsonObject root = ReadObject(mcpJsonPath) ?? [];
		if (root["mcpServers"] is not JsonObject servers) {
			servers = [];
			root["mcpServers"] = servers;
		}

		if (servers.ContainsKey("clio")) {
			return;
		}

		servers["clio"] = new JsonObject {
			["command"] = "clio",
			["args"] = new JsonArray("mcp-server")
		};
		Write(mcpJsonPath, root);
	}

	/// <inheritdoc />
	public void RemovePersonalMarketplacePluginEntry(string catalogPath, string marketplaceName, string pluginName) {
		if (!_fileSystem.ExistsFile(catalogPath)) {
			return;
		}

		JsonObject catalog = ReadObject(catalogPath);
		if (catalog is null) {
			return;
		}

		if (catalog["plugins"] is not JsonArray plugins) {
			// Not a plugin list: drop only an installer-owned catalog that self-identifies as this marketplace.
			if (string.Equals(AsString(catalog["name"]), marketplaceName, StringComparison.Ordinal)) {
				_fileSystem.DeleteFileIfExists(catalogPath);
			}

			return;
		}

		bool changed = false;
		for (int i = plugins.Count - 1; i >= 0; i--) {
			if (plugins[i] is JsonObject plugin
				&& string.Equals(AsString(plugin["name"]), pluginName, StringComparison.Ordinal)) {
				plugins.RemoveAt(i);
				changed = true;
			}
		}

		if (!changed) {
			return;
		}

		if (plugins.Count == 0 && string.Equals(AsString(catalog["name"]), marketplaceName, StringComparison.Ordinal)) {
			_fileSystem.DeleteFileIfExists(catalogPath);
			return;
		}

		Write(catalogPath, catalog);
	}

	/// <summary>
	/// Reads a JSON node as a string only when it is genuinely a string value;
	/// returns <c>null</c> for missing keys or non-string values (never throws).
	/// </summary>
	private static string AsString(JsonNode node) =>
		node is JsonValue value && value.TryGetValue(out string text) ? text : null;

	private JsonObject ReadObject(string path) {
		if (!_fileSystem.ExistsFile(path)) {
			return null;
		}

		string content = _fileSystem.ReadAllText(path);
		if (string.IsNullOrWhiteSpace(content)) {
			return [];
		}

		try {
			return JsonNode.Parse(content) as JsonObject ?? [];
		}
		catch (JsonException error) {
			throw new InvalidOperationException(
				$"Could not parse JSON in {path}: {error.Message}. Fix or remove the file and retry.", error);
		}
	}

	private void Write(string path, JsonNode node) {
		string directory = _fileSystem.GetDirectoryInfo(path).Parent?.FullName;
		if (!string.IsNullOrEmpty(directory)) {
			_fileSystem.CreateDirectoryIfNotExists(directory);
		}

		_fileSystem.WriteAllTextToFile(path, node.ToJsonString(WriteOptions) + "\n");
	}
}
