using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ClioRing.Ipc;

/// <summary>A discovered Creatio build archive (from list-creatio-builds). Its full-path is the zipFile.</summary>
public sealed record CreatioBuild(string FileName, string FullPath, long SizeBytes, string? ModifiedOnUtc);

/// <summary>Result of list-creatio-builds: products-folder status + newest-first builds.</summary>
public sealed record BuildsResult(
	string Status,
	string ProductsFolder,
	bool ProductsFolderExists,
	string Message,
	IReadOnlyList<CreatioBuild> Builds);

/// <summary>A passing local database choice (from show-passing-infrastructure).</summary>
public sealed record InfraDb(string DbServerName, string Engine, string Host, int Port);

/// <summary>A passing local Redis choice (from show-passing-infrastructure).</summary>
public sealed record InfraRedis(string RedisServerName, string Host, int Port);

/// <summary>Passing infrastructure with the recommended local db/redis bundle.</summary>
public sealed record PassingInfra(
	string Status,
	IReadOnlyList<InfraDb> Databases,
	IReadOnlyList<InfraRedis> RedisServers,
	string? RecommendedDbServerName,
	string? RecommendedRedisServerName);

/// <summary>Result of find-empty-iis-port.</summary>
public sealed record PortScan(string Status, int? FirstAvailablePort);

/// <summary>
/// Result of assert-infrastructure: overall status (pass/partial/fail) plus each section's status
/// (k8, local, filesystem). Used to gate a real deploy on the required checks for the chosen infra mode.
/// </summary>
public sealed record AssertResult(string Status, IReadOnlyDictionary<string, string> Sections) {
	/// <summary>Status of a named section (k8/local/filesystem), or "unknown" when absent.</summary>
	public string SectionStatus(string name) => Sections.TryGetValue(name, out string? s) ? s : "unknown";

	/// <summary>True when the named section reported "pass".</summary>
	public bool SectionPasses(string name) => string.Equals(SectionStatus(name), "pass", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tolerant parsers for the deploy-wizard data sources. Each takes the tool's JSON text and returns a
/// typed model, never throwing on shape drift (returns empty/default). Uses <see cref="JsonDocument"/>
/// (AOT-safe, no reflection).
/// </summary>
public static class DeployDiscovery {
	/// <summary>Parses list-creatio-builds output.</summary>
	public static BuildsResult ParseBuilds(string? json) {
		var builds = new List<CreatioBuild>();
		string status = "unknown", folder = string.Empty, message = string.Empty;
		bool exists = false;
		if (TryRoot(json, out JsonElement root)) {
			status = Str(root, "status") ?? status;
			folder = Str(root, "products-folder") ?? folder;
			exists = Bool(root, "products-folder-exists");
			message = Str(root, "message") ?? message;
			if (root.TryGetProperty("builds", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement b in arr.EnumerateArray()) {
					if (b.ValueKind != JsonValueKind.Object) {
						continue;
					}
					builds.Add(new CreatioBuild(
						Str(b, "file-name") ?? string.Empty,
						Str(b, "full-path") ?? string.Empty,
						Long(b, "size-bytes"),
						Str(b, "modified-on-utc")));
				}
			}
		}
		return new BuildsResult(status, folder, exists, message, builds);
	}

	/// <summary>Parses find-empty-iis-port output.</summary>
	public static PortScan ParsePortScan(string? json) {
		if (TryRoot(json, out JsonElement root)) {
			int? port = null;
			if (root.TryGetProperty("firstAvailablePort", out JsonElement p) && p.ValueKind == JsonValueKind.Number) {
				port = p.GetInt32();
			}
			return new PortScan(Str(root, "status") ?? "unknown", port);
		}
		return new PortScan("unknown", null);
	}

	/// <summary>Parses show-passing-infrastructure output (passing local db/redis + recommended bundle).</summary>
	public static PassingInfra ParsePassingInfra(string? json) {
		var dbs = new List<InfraDb>();
		var redis = new List<InfraRedis>();
		string status = "unknown";
		string? recDb = null, recRedis = null;
		if (TryRoot(json, out JsonElement root)) {
			status = Str(root, "status") ?? status;
			if (root.TryGetProperty("local", out JsonElement local) && local.ValueKind == JsonValueKind.Object) {
				ParseDatabases(local, dbs);
				ParseRedisServers(local, redis);
			}
			if (root.TryGetProperty("recommendedDeployment", out JsonElement rec) && rec.ValueKind == JsonValueKind.Object) {
				recDb = Str(rec, "dbServerName");
				recRedis = Str(rec, "redisServerName");
			}
		}
		return new PassingInfra(status, dbs, redis, recDb, recRedis);
	}

	private static void ParseDatabases(JsonElement local, ICollection<InfraDb> databases) {
		if (!local.TryGetProperty("databases", out JsonElement items) || items.ValueKind != JsonValueKind.Array) {
			return;
		}
		foreach (JsonElement item in items.EnumerateArray()) {
			string? name = Str(item, "dbServerName") ?? Str(item, "name");
			if (!string.IsNullOrEmpty(name)) {
				databases.Add(new InfraDb(name, Str(item, "engine") ?? string.Empty,
					Str(item, "host") ?? string.Empty, Int(item, "port")));
			}
		}
	}

	private static void ParseRedisServers(JsonElement local, ICollection<InfraRedis> servers) {
		if (!local.TryGetProperty("redisServers", out JsonElement items) || items.ValueKind != JsonValueKind.Array) {
			return;
		}
		foreach (JsonElement item in items.EnumerateArray()) {
			string? name = Str(item, "redisServerName") ?? Str(item, "name");
			if (!string.IsNullOrEmpty(name)) {
				servers.Add(new InfraRedis(name, Str(item, "host") ?? string.Empty, Int(item, "port")));
			}
		}
	}

	/// <summary>Parses assert-infrastructure output (overall + per-section statuses).</summary>
	public static AssertResult ParseAssert(string? json) {
		var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string status = "unknown";
		if (TryRoot(json, out JsonElement root)) {
			status = Str(root, "status") ?? status;
			if (root.TryGetProperty("sections", out JsonElement secs) && secs.ValueKind == JsonValueKind.Object) {
				foreach (JsonProperty sec in secs.EnumerateObject()) {
					if (sec.Value.ValueKind == JsonValueKind.Object) {
						sections[sec.Name] = Str(sec.Value, "status") ?? "unknown";
					}
				}
			}
		}
		return new AssertResult(status, sections);
	}

	private static bool TryRoot(string? json, out JsonElement root) {
		root = default;
		if (string.IsNullOrWhiteSpace(json)) {
			return false;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind == JsonValueKind.Object) {
				root = doc.RootElement.Clone();
				return true;
			}
		}
		catch (JsonException) {
		}
		return false;
	}

	private static string? Str(JsonElement o, string name) =>
		o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

	private static bool Bool(JsonElement o, string name) =>
		o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.True;

	private static int Int(JsonElement o, string name) =>
		o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

	private static long Long(JsonElement o, string name) =>
		o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : 0L;
}
