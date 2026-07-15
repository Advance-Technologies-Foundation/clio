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
	/// <summary>Reads the environment identities of every clio-owned source block.</summary>
	DbHubOwnedSourcesResult GetOwnedSources(string configPath);

	/// <summary>Adds or updates one clio-owned source.</summary>
	DbHubSyncResult Upsert(string configPath, DbHubSourceDefinition source);

	/// <summary>Removes the exact source owned for an environment.</summary>
	DbHubSyncResult Remove(string configPath, string environmentName);
}

/// <inheritdoc />
public sealed class DbHubTomlStore : IDbHubTomlStore {
	private const string ConflictCode = "DBHUB_SOURCE_OWNERSHIP_CONFLICT";
	private const string FileUpdateCode = "DBHUB_CONFIG_UPDATE_FAILED";
	private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
	private static readonly Regex ManagedEnvironmentRegex = new(
		@"(?m)^# clio-managed-source begin environment=(?<environment>[A-Za-z0-9_-]+) source=[a-z0-9_]+\s*$",
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
	public DbHubOwnedSourcesResult GetOwnedSources(string configPath) {
		if (string.IsNullOrWhiteSpace(configPath)) {
			return OwnedSourcesFailure("The dbHub config path is not configured.");
		}
		try {
			string fullPath = Path.GetFullPath(configPath);
			using FileStream lockStream = AcquireLock(fullPath + ".clio.lock");
			RefuseUnsafeTarget(fullPath);
			string content = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
			ValidateToml(content);
			List<string> environments = [];
			foreach (Match match in ManagedEnvironmentRegex.Matches(content)) {
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
			Directory.CreateDirectory(directory);
			using FileStream lockStream = AcquireLock(fullPath + ".clio.lock");
			RefuseUnsafeTarget(fullPath);
			string current = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
			ValidateToml(current);
			(string candidate, DbHubSyncResult result) = mutation(current);
			if (!result.Changed) {
				return result;
			}
			ValidateToml(candidate);
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
		Match existingManaged = managedRegex.Match(content);
		if (!existingManaged.Success && FindSourceIds(content).Contains(source.SourceId, StringComparer.Ordinal)) {
			return (content, new DbHubSyncResult(false, true,
				new DbHubWarning($"dbHub source '{source.SourceId}' was not changed.",
					"The source id already exists and is not owned by clio.", ConflictCode)));
		}

		string block = RenderManagedBlock(source, encodedEnvironment, newline);
		string candidate;
		if (existingManaged.Success) {
			candidate = content.Remove(existingManaged.Index, existingManaged.Length)
				.Insert(existingManaged.Index, block);
		} else {
			string separator = content.Length == 0 ? string.Empty : content.EndsWith(newline, StringComparison.Ordinal)
				? newline : newline + newline;
			candidate = content + separator + block;
		}
		return string.Equals(candidate, content, StringComparison.Ordinal)
			? (content, DbHubSyncResult.Unchanged())
			: (candidate, new DbHubSyncResult(true, false));
	}

	private static (string Content, DbHubSyncResult Result) RemoveCore(string content, string environmentName) {
		Match match = ManagedBlockRegex(EncodeEnvironment(environmentName)).Match(content);
		if (!match.Success) {
			return (content, DbHubSyncResult.Unchanged());
		}
		string candidate = content.Remove(match.Index, match.Length);
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
		Line($"password = {Quote(source.Password)}");
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
		Line($"# clio-managed-source end environment={encodedEnvironment}");
		return block.ToString();
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

	private static void ValidateToml(string content) {
		if (string.IsNullOrWhiteSpace(content)) {
			return;
		}
		try {
			_ = TomlSerializer.Deserialize<TomlTable>(content);
		}
		catch (Exception exception) when (exception is TomlException or InvalidOperationException) {
			throw new InvalidDataException("Invalid TOML.");
		}
	}

	private static void RefuseUnsafeTarget(string path) {
		if (!File.Exists(path)) {
			return;
		}
		FileInfo info = new(path);
		if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
			throw new InvalidDataException("Unsafe TOML target.");
		}
	}

	private static Regex ManagedBlockRegex(string encodedEnvironment) => new(
		$@"(?ms)^# clio-managed-source begin environment={Regex.Escape(encodedEnvironment)} source=[a-z0-9_]+\r?\n.*?^# clio-managed-source end environment={Regex.Escape(encodedEnvironment)}\r?\n?",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

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
