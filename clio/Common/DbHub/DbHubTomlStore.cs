using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Tomlyn;
using Tomlyn.Model;

namespace Clio.Common.DbHub;

/// <summary>Atomically manages only clio-owned dbHub TOML source blocks.</summary>
public interface IDbHubTomlStore {
	/// <summary>Validates an existing configuration without changing it.</summary>
	DbHubSyncResult ValidateForInstallation(string configPath);

	/// <summary>Creates or repairs the minimum runnable configuration under the shared update lock.</summary>
	DbHubSyncResult EnsureRunnable(string configPath);

	/// <summary>Reads the environment identities of every clio-owned source block.</summary>
	DbHubOwnedSourcesResult GetOwnedSources(string configPath);

	/// <summary>Adds or updates one clio-owned source.</summary>
	DbHubSyncResult Upsert(string configPath, DbHubSourceDefinition source);

	/// <summary>Removes the exact source owned for an environment.</summary>
	DbHubSyncResult Remove(string configPath, string environmentName);
}

/// <inheritdoc />
public sealed class DbHubTomlStore : IDbHubTomlStore {
	internal const string ControlSourceId = "clio_control";
	private const string ConflictCode = "DBHUB_SOURCE_OWNERSHIP_CONFLICT";
	private const string FileUpdateCode = "DBHUB_CONFIG_UPDATE_FAILED";
	private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
	private static readonly Regex ManagedEnvironmentRegex = new(
		@"(?m)^# clio-managed-source begin environment=(?<environment>[A-Za-z0-9_-]+) source=(?<source>[a-z0-9_]+)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	private static readonly Regex ManagedControlRegex = new(
		@"(?m)^# clio-managed-control-source: keeps dbHub configuration valid when no database environments exist\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	private static readonly Regex AnyManagedBlockRegex = new(
		@"(?ms)^# clio-managed-source begin environment=(?<environment>[A-Za-z0-9_-]+) source=(?<source>[a-z0-9_]+)\r?\n.*?^# clio-managed-source end environment=\k<environment>\r?\n?",
		RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	private static readonly Regex ManagedControlBlockRegex = new(
		@"(?m)^# clio-managed-control-source: keeps dbHub configuration valid when no database environments exist\s*\r?\n\[\[sources\]\]\s*\r?\nid\s*=\s*\""clio_control\""\s*\r?\ntype\s*=\s*\""sqlite\""\s*\r?\ndsn\s*=\s*\""sqlite:///:memory:\""\s*\r?\nlazy\s*=\s*true\s*\r?\n\[\[tools\]\]\s*\r?\nname\s*=\s*\""execute_sql\""\s*\r?\nsource\s*=\s*\""clio_control\""\s*\r?\nreadonly\s*=\s*true\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	private readonly TimeSpan _lockTimeout;
	private readonly IDbHubAtomicFileWriter _atomicFileWriter;

	/// <summary>Initializes the store with its atomic file writer and the production lock timeout.</summary>
	public DbHubTomlStore(IDbHubAtomicFileWriter atomicFileWriter) : this(atomicFileWriter, DefaultLockTimeout) { }

	internal DbHubTomlStore(IDbHubAtomicFileWriter atomicFileWriter, TimeSpan lockTimeout) {
		_atomicFileWriter = atomicFileWriter;
		_lockTimeout = lockTimeout;
	}

	/// <inheritdoc />
	public DbHubSyncResult ValidateForInstallation(string configPath) {
		if (string.IsNullOrWhiteSpace(configPath)) {
			return Failure("The dbHub config path is not configured.");
		}
		try {
			string fullPath = Path.GetFullPath(configPath);
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			if (!File.Exists(fullPath) && !Directory.Exists(Path.GetDirectoryName(fullPath))) {
				return DbHubSyncResult.Unchanged();
			}
			using FileStream lockStream = AcquireLock(fullPath + ".clio.lock");
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			_atomicFileWriter.ValidateExistingPermissions(fullPath);
			string content = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
			ValidateToml(content);
			ValidateToolSafety(EnsureRunnableContent(content));
			return DbHubSyncResult.Unchanged();
		}
		catch (TimeoutException) {
			return Failure("Another process is updating the dbHub configuration. Retry after it finishes.");
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException
			or UnauthorizedAccessException or InvalidDataException) {
			return Failure("The dbHub TOML file could not be read or validated safely.");
		}
	}

	/// <inheritdoc />
	public DbHubSyncResult EnsureRunnable(string configPath) => Update(configPath, content => {
		string candidate = EnsureRunnableContent(content);
		return string.Equals(candidate, content, StringComparison.Ordinal)
			? (content, DbHubSyncResult.Unchanged())
			: (candidate, new DbHubSyncResult(true, false));
	});

	/// <inheritdoc />
	public DbHubOwnedSourcesResult GetOwnedSources(string configPath) {
		if (string.IsNullOrWhiteSpace(configPath)) {
			return OwnedSourcesFailure("The dbHub config path is not configured.");
		}
		try {
			string fullPath = Path.GetFullPath(configPath);
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			using FileStream lockStream = AcquireLock(fullPath + ".clio.lock");
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			_atomicFileWriter.ValidateExistingPermissions(fullPath);
			string content = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
			ValidateToml(content);
			List<string> environments = [];
			foreach (Match match in SafeMatches(ManagedEnvironmentRegex, content, requireSafeEnd: false)) {
				environments.Add(DecodeEnvironment(match.Groups["environment"].Value));
			}
			return new DbHubOwnedSourcesResult(environments);
		}
		catch (TimeoutException) {
			return OwnedSourcesFailure("Another process is updating the dbHub configuration. Retry after it finishes.");
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException
			or UnauthorizedAccessException or InvalidDataException or FormatException) {
			return OwnedSourcesFailure("The dbHub configuration could not be read safely.");
		}
	}

	/// <inheritdoc />
	public DbHubSyncResult Upsert(string configPath, DbHubSourceDefinition source) {
		ArgumentNullException.ThrowIfNull(source);
		return Update(configPath, content => UpsertCore(content, source));
	}

	/// <inheritdoc />
	public DbHubSyncResult Remove(string configPath, string environmentName) {
		return Update(configPath, content => RemoveCore(content, environmentName));
	}

	private DbHubSyncResult Update(string configPath, Func<string, (string Content, DbHubSyncResult Result)> mutation) {
		if (string.IsNullOrWhiteSpace(configPath)) {
			return Failure("The dbHub config path is not configured.");
		}
		string fullPath;
		try {
			fullPath = Path.GetFullPath(configPath);
		} catch (Exception exception) when (exception is ArgumentException or NotSupportedException) {
			return Failure("The dbHub config path is invalid.");
		}

		try {
			string directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			Directory.CreateDirectory(directory);
			using FileStream lockStream = AcquireLock(fullPath + ".clio.lock");
			DbHubPathSafety.RefuseUnsafeTarget(fullPath);
			_atomicFileWriter.ValidateExistingPermissions(fullPath);
			string current = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
			ValidateToml(current);
			(string candidate, DbHubSyncResult result) = mutation(current);
			if (result.Warning is not null) {
				return result;
			}
			candidate = EnsureRunnableContent(candidate);
			ValidateToml(candidate, requireSource: true);
			ValidateToolSafety(candidate);
			if (!result.Changed) {
				return result;
			}
			_atomicFileWriter.Commit(fullPath, candidate);
			return result;
		}
		catch (TimeoutException) {
			return Failure("Another process is updating the dbHub configuration. Retry after it finishes.");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) {
			return Failure("The dbHub configuration could not be updated; the original file was left intact.");
		}
	}

	private static (string Content, DbHubSyncResult Result) UpsertCore(string content,
		DbHubSourceDefinition source) {
		string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string encodedEnvironment = EncodeEnvironment(source.EnvironmentName);
		Regex managedRegex = ManagedBlockRegex(encodedEnvironment);
		IReadOnlyList<Match> existingManaged = SafeMatches(managedRegex, content, requireSafeEnd: true);
		int insertAt = existingManaged.Count > 0 ? existingManaged[0].Index : -1;
		string remaining = RemoveMatches(content, existingManaged);
		string targetSuffix = NormalizeDbHubToolSuffix(source.SourceId);
		string collision = FindSourceIds(remaining).FirstOrDefault(id => string.Equals(
			NormalizeDbHubToolSuffix(id), targetSuffix, StringComparison.Ordinal));
		if (collision is not null) {
			Match previousOwnedSource = SafeMatches(ManagedSourceBlockRegex(collision), remaining,
				requireSafeEnd: true).FirstOrDefault();
			string previousEnvironment = previousOwnedSource is not null
				? DecodeEnvironment(previousOwnedSource.Groups["environment"].Value)
				: null;
			if (previousOwnedSource is not null && string.Equals(previousEnvironment, source.EnvironmentName,
					StringComparison.OrdinalIgnoreCase)) {
				if (insertAt < 0) {
					insertAt = previousOwnedSource.Index;
				}
				remaining = remaining.Remove(previousOwnedSource.Index, previousOwnedSource.Length);
			} else {
				return (content, new DbHubSyncResult(false, true,
					new DbHubWarning($"dbHub source '{source.SourceId}' was not changed.",
						"Another source produces the same dbHub MCP tool name and is not owned for this environment.",
						ConflictCode)));
			}
		}

		string block = RenderManagedBlock(source, encodedEnvironment, newline);
		string candidate;
		if (insertAt >= 0) {
			candidate = remaining.Insert(Math.Min(insertAt, remaining.Length), block);
		} else {
			string separator = remaining.Length == 0 ? string.Empty : remaining.EndsWith(newline, StringComparison.Ordinal)
				? newline : newline + newline;
			candidate = remaining + separator + block;
		}
		return string.Equals(candidate, content, StringComparison.Ordinal)
			? (content, DbHubSyncResult.Unchanged())
			: (candidate, new DbHubSyncResult(true, false));
	}

	private static (string Content, DbHubSyncResult Result) RemoveCore(string content, string environmentName) {
		IReadOnlyList<Match> matches = SafeMatches(ManagedBlockRegex(EncodeEnvironment(environmentName)), content,
			requireSafeEnd: true);
		if (matches.Count == 0) {
			return (content, DbHubSyncResult.Unchanged());
		}
		string candidate = content;
		for (int index = matches.Count - 1; index >= 0; index--) {
			candidate = candidate.Remove(matches[index].Index, matches[index].Length);
		}
		return (candidate, new DbHubSyncResult(true, false));
	}

	private static HashSet<string> FindSourceIds(string content) {
		HashSet<string> result = new(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(content)) {
			return result;
		}
		TomlTable document = TomlSerializer.Deserialize<TomlTable>(content);
		if (document.TryGetValue("sources", out object sources) && sources is TomlTableArray sourceArray) {
			foreach (TomlTable source in sourceArray) {
				if (source.TryGetValue("id", out object id) && id is string sourceId) {
					result.Add(sourceId);
				}
			}
		}
		return result;
	}

	private static string RenderManagedBlock(DbHubSourceDefinition source, string encodedEnvironment,
		string newline) {
		StringBuilder block = new();
		void Line(string value) => block.Append(value).Append(newline);
		Line($"# clio-managed-source begin environment={encodedEnvironment} source={source.SourceId}");
		Line("[[sources]]");
		Line($"id = {Quote(source.SourceId)}");
		Line($"type = {Quote(source.Type)}");
		Line($"host = {Quote(source.Host)}");
		Line($"port = {source.Port}");
		Line($"database = {Quote(source.Database)}");
		Line($"user = {Quote(source.User)}");
		Line($"password = {Quote(source.Credential)}");
		if (!string.IsNullOrWhiteSpace(source.InstanceName)) {
			Line($"instanceName = {Quote(source.InstanceName)}");
		}
		if (!string.IsNullOrWhiteSpace(source.SslMode)) {
			Line($"sslmode = {Quote(source.SslMode)}");
		}
		if (!string.IsNullOrWhiteSpace(source.SslRootCertificate)) {
			Line($"sslrootcert = {Quote(source.SslRootCertificate)}");
		}
		Line("lazy = true");
		Line("[[tools]]");
		Line("name = \"execute_sql\"");
		Line($"source = {Quote(source.SourceId)}");
		Line("readonly = true");
		Line($"# clio-managed-source end environment={encodedEnvironment}");
		return block.ToString();
	}

	internal static string EnsureRunnableContent(string content) {
		ValidateToml(content);
		if (FindSourceIds(content).Count > 0) {
			return content;
		}
		string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string separator = content.Length == 0 ? string.Empty : content.EndsWith(newline, StringComparison.Ordinal)
			? newline : newline + newline;
		return content + separator
			+ "# clio-managed-control-source: keeps dbHub configuration valid when no database environments exist" + newline
			+ "[[sources]]" + newline
			+ $"id = {Quote(ControlSourceId)}" + newline
			+ "type = \"sqlite\"" + newline
			+ "dsn = \"sqlite:///:memory:\"" + newline
			+ "lazy = true" + newline
			+ "[[tools]]" + newline
			+ "name = \"execute_sql\"" + newline
			+ $"source = {Quote(ControlSourceId)}" + newline
			+ "readonly = true" + newline;
	}

	private static void ValidateToolSafety(string content) {
		foreach (Match managedBlock in SafeMatches(AnyManagedBlockRegex, content, requireSafeEnd: true)) {
			if (!HasExactlyOneReadonlySqlTool(managedBlock.Value, managedBlock.Groups["source"].Value)) {
				throw new InvalidDataException("Every clio-managed dbHub source must explicitly configure read-only SQL.");
			}
		}
		int controlMarkers = SafeMatches(ManagedControlRegex, content, requireSafeEnd: false).Count;
		int validControlBlocks = SafeMatches(ManagedControlBlockRegex, content, requireSafeEnd: false).Count;
		if (controlMarkers != validControlBlocks) {
			throw new InvalidDataException("The clio dbHub control source must explicitly configure read-only SQL.");
		}
	}

	private static bool HasExactlyOneReadonlySqlTool(string block, string sourceId) {
		if (Regex.Matches(block, @"(?m)^\[\[tools\]\]\s*$", RegexOptions.CultureInvariant,
				TimeSpan.FromSeconds(1)).Count != 1) {
			return false;
		}
		string pattern = $$"""(?ms)^\[\[tools\]\]\s*\r?\nname\s*=\s*"execute_sql"\s*\r?\nsource\s*=\s*{{Regex.Escape(Quote(sourceId))}}\s*\r?\nreadonly\s*=\s*true\s*$""";
		return Regex.IsMatch(block, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	}

	private FileStream AcquireLock(string path) {
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (true) {
			try {
				return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException) when (stopwatch.Elapsed < _lockTimeout) {
				Thread.Sleep(25);
			}
			if (stopwatch.Elapsed >= _lockTimeout) {
				throw new TimeoutException();
			}
		}
	}

	private static void ValidateToml(string content, bool requireSource = false) {
		if (string.IsNullOrWhiteSpace(content)) {
			return;
		}
		try {
			TomlTable document = TomlSerializer.Deserialize<TomlTable>(content);
			if (requireSource && (!document.TryGetValue("sources", out object sources)
					|| sources is not TomlTableArray sourceArray || sourceArray.Count == 0)) {
				throw new InvalidDataException("dbHub requires at least one source.");
			}
		}
		catch (Exception exception) when (exception is TomlException or InvalidOperationException) {
			throw new InvalidDataException("Invalid TOML.");
		}
	}

	private static Regex ManagedBlockRegex(string encodedEnvironment) => new(
		$@"(?ms)^# clio-managed-source begin environment={Regex.Escape(encodedEnvironment)} source=[a-z0-9_]+\r?\n.*?^# clio-managed-source end environment={Regex.Escape(encodedEnvironment)}\r?\n?",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static Regex ManagedSourceBlockRegex(string sourceId) => new(
		$@"(?ms)^# clio-managed-source begin environment=(?<environment>[A-Za-z0-9_-]+) source={Regex.Escape(sourceId)}\r?\n.*?^# clio-managed-source end environment=[A-Za-z0-9_-]+\r?\n?",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static string RemoveMatches(string content, IReadOnlyList<Match> matches) {
		string result = content;
		for (int index = matches.Count - 1; index >= 0; index--) {
			result = result.Remove(matches[index].Index, matches[index].Length);
		}
		return result;
	}

	private static IReadOnlyList<Match> SafeMatches(Regex regex, string content, bool requireSafeEnd) =>
		[.. regex.Matches(content).Cast<Match>().Where(match => !IsInsideTomlString(content, match.Index)
			&& (!requireSafeEnd || IsSafeEndMarker(content, match)))];

	private static bool IsSafeEndMarker(string content, Match match) {
		int relativeEnd = match.Value.LastIndexOf("# clio-managed-source end", StringComparison.Ordinal);
		return relativeEnd >= 0 && !IsInsideTomlString(content, match.Index + relativeEnd);
	}

	private static bool IsInsideTomlString(string content, int position) {
		bool basic = false;
		bool literal = false;
		bool basicMultiline = false;
		bool literalMultiline = false;
		bool comment = false;
		for (int index = 0; index < position; index++) {
			char character = content[index];
			if (comment) {
				comment = character != '\n';
				continue;
			}
			if (basicMultiline) {
				if (StartsWithTriple(content, index, '"')) {
					basicMultiline = false;
					index += 2;
				} else if (character == '\\') {
					index++;
				}
				continue;
			}
			if (literalMultiline) {
				if (StartsWithTriple(content, index, '\'')) {
					literalMultiline = false;
					index += 2;
				}
				continue;
			}
			if (basic) {
				if (character == '\\') {
					index++;
				} else if (character == '"') {
					basic = false;
				}
				continue;
			}
			if (literal) {
				literal = character != '\'';
				continue;
			}
			if (character == '#') {
				comment = true;
			} else if (StartsWithTriple(content, index, '"')) {
				basicMultiline = true;
				index += 2;
			} else if (StartsWithTriple(content, index, '\'')) {
				literalMultiline = true;
				index += 2;
			} else if (character == '"') {
				basic = true;
			} else if (character == '\'') {
				literal = true;
			}
		}
		return basic || literal || basicMultiline || literalMultiline;
	}

	private static bool StartsWithTriple(string content, int index, char quote) => index + 2 < content.Length
		&& content[index] == quote && content[index + 1] == quote && content[index + 2] == quote;

	private static string NormalizeDbHubToolSuffix(string sourceId) => Regex.Replace(sourceId ?? string.Empty,
		"[^a-zA-Z0-9]", "_", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static string EncodeEnvironment(string environmentName) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(environmentName ?? string.Empty))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string DecodeEnvironment(string encodedEnvironment) {
		string base64 = encodedEnvironment.Replace('-', '+').Replace('_', '/');
		base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
		return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
	}

	private static string Quote(string value) => $"\"{(value ?? string.Empty).Replace("\\", "\\\\")
		.Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}\"";

	private static DbHubSyncResult Failure(string detail) => new(false, true,
		new DbHubWarning("The dbHub configuration was not changed.", detail, FileUpdateCode));

	private static DbHubOwnedSourcesResult OwnedSourcesFailure(string detail) => new([],
		new DbHubWarning("The dbHub configuration was not inspected.", detail, FileUpdateCode));
}

internal static class DbHubPathSafety {
	internal static void RefuseUnsafeTarget(string path) {
		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) {
			throw new InvalidDataException("Network and device paths are not supported for dbHub configuration.");
		}
		FileSystemInfo current = new FileInfo(fullPath);
		while (current is not null) {
			if (current.Exists && (current.Attributes.HasFlag(FileAttributes.ReparsePoint)
					|| current.LinkTarget is not null)) {
				throw new InvalidDataException("The dbHub configuration path resolves through a reparse point.");
			}
			current = current switch {
				FileInfo file => file.Directory,
				DirectoryInfo directory => directory.Parent,
				_ => null
			};
		}
	}
}
