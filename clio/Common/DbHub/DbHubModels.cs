using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Clio.Common.DbHub;

/// <summary>
/// Persisted configuration for the local dbHub HTTP MCP server.
/// </summary>
public sealed class DbHubSettings {
	/// <summary>Default HTTP host. dbHub is unauthenticated, so only loopback is safe.</summary>
	public const string DefaultHost = "127.0.0.1";

	/// <summary>Default HTTP port used by clio-managed dbHub installations.</summary>
	public const int DefaultPort = 7999;

	/// <summary>Gets or sets whether dbHub integration is enabled.</summary>
	[JsonProperty("enabled")]
	public bool Enabled { get; set; }

	/// <summary>Gets or sets the explicit dbHub TOML configuration path.</summary>
	[JsonProperty("config-path")]
	public string ConfigPath { get; set; }

	/// <summary>Gets or sets the loopback HTTP host.</summary>
	[JsonProperty("host")]
	public string Host { get; set; } = DefaultHost;

	/// <summary>Gets or sets the HTTP port.</summary>
	[JsonProperty("port")]
	public int Port { get; set; } = DefaultPort;

	/// <summary>Gets or sets whether successful local deploy and uninstall operations synchronize dbHub.</summary>
	[JsonProperty("sync-local-environments")]
	public bool SyncLocalEnvironments { get; set; } = true;

	/// <summary>Creates a detached copy safe for callers to mutate.</summary>
	public DbHubSettings Clone() => new() {
		Enabled = Enabled,
		ConfigPath = ConfigPath,
		Host = Host,
		Port = Port,
		SyncLocalEnvironments = SyncLocalEnvironments
	};
}

/// <summary>Database source definition written to a clio-managed dbHub TOML block.</summary>
public sealed record DbHubSourceDefinition(
	string EnvironmentName,
	string SourceId,
	string Type,
	string Host,
	int Port,
	string Database,
	string User,
	string Password,
	string InstanceName = null,
	string SslMode = null,
	string SslRootCertificate = null) {
	/// <summary>Returns a diagnostic representation with credential fields redacted.</summary>
	public override string ToString() =>
		$"DbHubSourceDefinition {{ EnvironmentName = {EnvironmentName}, SourceId = {SourceId}, Type = {Type}, "
		+ $"Host = {Host}, Port = {Port}, Database = {Database}, User = [redacted], Password = [redacted], "
		+ $"InstanceName = {InstanceName}, SslMode = {SslMode}, SslRootCertificate = {SslRootCertificate} }}";
}

/// <summary>Safe source-discovery outcome.</summary>
public sealed record DbHubSourceDiscoveryResult(DbHubSourceDefinition Source, DbHubWarning Warning = null) {
	/// <summary>Gets whether an eligible source was produced.</summary>
	public bool Success => Source is not null;
}

/// <summary>One safe warning produced by dbHub integration.</summary>
public sealed record DbHubWarning(string Message, string Detail = null, string ErrorCode = null);

/// <summary>Outcome of a single dbHub source operation.</summary>
public sealed record DbHubSyncResult(bool Changed, bool Skipped, DbHubWarning Warning = null) {
	/// <summary>Creates a successful no-op result.</summary>
	public static DbHubSyncResult Unchanged() => new(false, false);

	/// <summary>Creates a skipped result with a safe explanation.</summary>
	public static DbHubSyncResult Skip(DbHubWarning warning = null) => new(false, true, warning);
}

/// <summary>Safe inventory of environment names represented by clio-owned TOML blocks.</summary>
public sealed record DbHubOwnedSourcesResult(IReadOnlyCollection<string> EnvironmentNames,
	DbHubWarning Warning = null);

/// <summary>Aggregate result of manual dbHub reconciliation.</summary>
public sealed record DbHubSyncSummary(int Changed, int Unchanged, int Skipped, IReadOnlyList<DbHubWarning> Warnings) {
	/// <summary>Gets whether at least one warning was produced.</summary>
	public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>Requested dbHub installation configuration.</summary>
public sealed record DbHubInstallRequest(string ConfigPath, string Host, int Port, bool SyncLocalEnvironments);

/// <summary>Result of installing, adopting, or repairing dbHub.</summary>
public sealed record DbHubInstallationResult(bool Success, string Message, DbHubSettings Settings = null);

/// <summary>Safe dbHub HTTP verification outcome.</summary>
public sealed record DbHubVerificationResult(bool Online, bool Verified, DbHubWarning Warning = null);
