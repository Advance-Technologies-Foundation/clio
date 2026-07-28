using System;
using System.Linq;
using Clio.Common;
using Clio.Common.DbHub;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for manual dbHub source reconciliation.</summary>
[Verb("sync-dbhub", HelpText = "Reconcile clio-owned dbHub sources with local Creatio environments")]
public sealed class SyncDbHubOptions {
	/// <summary>Gets or sets an optional single environment to reconcile.</summary>
	[Option("environment", Required = false, HelpText = "Optional local clio environment name; all eligible environments are used when omitted.")]
	public string Environment { get; set; }
}

/// <summary>Manually reconciles dbHub sources.</summary>
public sealed class SyncDbHubCommand(IDbHubSynchronizationService synchronizationService, ILogger logger)
	: Command<SyncDbHubOptions> {
	private readonly IDbHubSynchronizationService _synchronizationService = synchronizationService;
	private readonly ILogger _logger = logger;

	/// <inheritdoc />
	public override int Execute(SyncDbHubOptions options) {
		DbHubSyncSummary summary = _synchronizationService.Synchronize(options.Environment);
		foreach (DbHubWarning warning in summary.Warnings) {
			_logger.WriteWarning(string.IsNullOrWhiteSpace(warning.Detail)
				? warning.Message
				: $"{warning.Message} {warning.Detail}");
		}
		_logger.WriteInfo($"dbHub synchronization completed: {summary.Changed} changed, {summary.Unchanged} unchanged, {summary.Skipped} skipped.");
		return summary.Warnings.Any(warning => warning.ErrorCode is "DBHUB_NOT_CONFIGURED"
			or "DBHUB_UNSAFE_ENDPOINT" or "DBHUB_SYNC_FAILED" or "DBHUB_ENVIRONMENT_NOT_FOUND"
			or "DBHUB_CONFIG_UPDATE_FAILED") ? 1 : 0;
	}
}
