using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClioRing.Services;

/// <summary>
/// Default <see cref="IClioSettingsStore"/> backed by <c>app-settings.json</c> beside the executable.
/// Reads/writes through <see cref="JsonNode"/> (no reflection-based serialization) so it stays AOT-safe
/// and preserves any settings the strongly-typed model does not know about. A read-modify-write updates
/// only the <c>DevClioPath</c> field; every other key is left byte-for-byte in place.
/// </summary>
public sealed class ClioSettingsStore : IClioSettingsStore {
	private const string DevClioPathKey = "DevClioPath";
	private const string RuntimeModeKey = "ClioRuntimeMode";
	private const string ReleaseMode = "release";
	private const string DevelopmentMode = "development";

	private readonly string _settingsPath;

	/// <summary>Creates a store over the default <c>app-settings.json</c> beside the executable.</summary>
	public ClioSettingsStore() : this(AppSettingsReader.SettingsPath) { }

	/// <summary>Creates a store over an explicit settings file (test seam).</summary>
	/// <param name="settingsPath">Absolute path to the <c>app-settings.json</c> file to read/write.</param>
	public ClioSettingsStore(string settingsPath) {
		_settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
	}

	/// <inheritdoc />
	public string SettingsPath => _settingsPath;

	/// <inheritdoc />
	public string? ReadDevClioPath() {
		JsonObject? root = TryReadRoot();
		string? value = ReadString(root?[DevClioPathKey]);
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <inheritdoc />
	public void SaveDevClioPath(string? path) {
		JsonObject root = TryReadRoot() ?? new JsonObject();
		if (string.IsNullOrWhiteSpace(path)) {
			root.Remove(DevClioPathKey);
		}
		else {
			root[DevClioPathKey] = path.Trim();
		}

		string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(_settingsPath, json);
	}

	/// <inheritdoc />
	public string? ReadRuntimeMode() {
		JsonObject? root = TryReadRoot();
		string? value = ReadString(root?[RuntimeModeKey]);
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
	}

	/// <inheritdoc />
	public void SaveRuntimeMode(string mode) {
		ArgumentException.ThrowIfNullOrWhiteSpace(mode);
		string normalized = mode.Trim().ToLowerInvariant();
		if (normalized is not ReleaseMode and not DevelopmentMode) {
			throw new ArgumentOutOfRangeException(nameof(mode), mode,
				"Clio runtime mode must be release or development.");
		}
		JsonObject root = TryReadRoot() ?? new JsonObject();
		root[RuntimeModeKey] = normalized;
		string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(_settingsPath, json);
	}

	/// <inheritdoc />
	public bool HasDevelopmentTarget() {
		JsonObject? root = TryReadRoot();
		string? devPath = ReadString(root?[DevClioPathKey]);
		if (!string.IsNullOrWhiteSpace(devPath) && DevClioLaunch.Validate(devPath).IsValid) {
			return true;
		}

		JsonObject? ipc = root?["ClioIpc"] as JsonObject;
		string? command = ReadString(ipc?["Command"]);
		JsonArray? args = ipc?["Args"] as JsonArray;
		return !string.IsNullOrWhiteSpace(command) && args is { Count: > 0 }
			&& args.All(value => !string.IsNullOrWhiteSpace(ReadString(value)));
	}

	private static string? ReadString(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? result) ? result : null;

	private JsonObject? TryReadRoot() {
		try {
			if (!File.Exists(_settingsPath)) {
				return null;
			}
			return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject;
		}
		catch (Exception) {
			// A missing/unreadable/invalid file behaves like "no override"; a subsequent save rewrites it.
			return null;
		}
	}
}
