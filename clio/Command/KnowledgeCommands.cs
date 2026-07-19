using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// Options for installing verified knowledge from configured sources.
/// </summary>
[Verb("install-knowledge", HelpText = "Install verified knowledge from one source or all enabled sources")]
public sealed class InstallKnowledgeOptions {
	/// <summary>
	/// Gets or sets the optional configured source alias.
	/// </summary>
	[Option("source", Required = false,
		HelpText = "Install only this configured source alias; omit to install all enabled sources")]
	public string? Source { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for updating verified knowledge from configured sources.
/// </summary>
[Verb("update-knowledge", HelpText = "Update verified knowledge from one source or all enabled sources")]
public sealed class UpdateKnowledgeOptions {
	/// <summary>
	/// Gets or sets the optional configured source alias.
	/// </summary>
	[Option("source", Required = false,
		HelpText = "Update only this configured source alias; omit to update all enabled sources")]
	public string? Source { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for inspecting configured and installed knowledge sources.
/// </summary>
[Verb("info-knowledge", HelpText = "Show configured knowledge sources, installed generations, validation, and update status")]
public sealed class InfoKnowledgeOptions {
	/// <summary>
	/// Gets or sets the optional configured source alias.
	/// </summary>
	[Option("source", Required = false,
		HelpText = "Show only this configured source alias; omit to show every configured source")]
	public string? Source { get; set; }

	/// <summary>
	/// Gets or sets whether the command checks configured transports for updates.
	/// </summary>
	[Option("check-updates", Required = false, Default = false,
		HelpText = "Contact configured transports to check for available updates")]
	public bool CheckUpdates { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for deleting installed knowledge while retaining source configuration.
/// </summary>
[Verb("delete-knowledge", HelpText = "Delete installed knowledge for one source or all enabled sources")]
public sealed class DeleteKnowledgeOptions {
	/// <summary>
	/// Gets or sets the optional configured source alias.
	/// </summary>
	[Option("source", Required = false,
		HelpText = "Delete installed knowledge only for this source alias; omit to delete all enabled sources")]
	public string? Source { get; set; }

	/// <summary>
	/// Gets or sets whether deletion proceeds without an interactive confirmation prompt.
	/// </summary>
	[Option("force", Required = false, Default = false,
		HelpText = "Confirm deletion without an interactive prompt (required for MCP and other non-interactive hosts)")]
	public bool Force { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for adding a trusted knowledge source.
/// </summary>
[Verb("add-knowledge-source", HelpText = "Add and enable a trusted Git or NuGet knowledge source")]
public sealed class AddKnowledgeSourceOptions {
	/// <summary>
	/// Gets or sets the operator-friendly source alias.
	/// </summary>
	[Option("alias", Required = true, HelpText = "Unique operator-friendly source alias")]
	public string Alias { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the stable reverse-DNS library identity.
	/// </summary>
	[Option("library-id", Required = true, HelpText = "Unique stable library identity, such as com.example.partner")]
	public string LibraryId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the transport type.
	/// </summary>
	[Option("type", Required = true, HelpText = "Source transport: git or nuget")]
	public string Type { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the transport location.
	/// </summary>
	[Option("location", Required = true, HelpText = "Git repository URL or NuGet v3 service-index URL without credentials")]
	public string Location { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the signature key identifier authorized for this source.
	/// </summary>
	[Option("trusted-key-id", Required = true, HelpText = "Signature key ID authorized for bundles from this source")]
	public string TrustedKeyId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the existing bounded local regular-file path to the source's P-256 public key.
	/// </summary>
	[Option("trusted-public-key-path", Required = true,
		HelpText = "Existing bounded local P-256 PUBLIC KEY PEM path (no private key or reparse/network path)")]
	public string TrustedPublicKeyPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the NuGet package ID.
	/// </summary>
	[Option("package-id", Required = false, HelpText = "NuGet package ID; required when --type nuget")]
	public string? PackageId { get; set; }

	/// <summary>
	/// Gets or sets the Git branch to follow.
	/// </summary>
	[Option("branch", Required = false, HelpText = "Git branch to follow; the remote default is persisted when no reference is supplied")]
	public string? Branch { get; set; }

	/// <summary>
	/// Gets or sets the Git tag to resolve.
	/// </summary>
	[Option("tag", Required = false, HelpText = "Git tag to resolve to an immutable commit")]
	public string? Tag { get; set; }

	/// <summary>
	/// Gets or sets the immutable Git commit to install.
	/// </summary>
	[Option("commit", Required = false, HelpText = "Immutable Git commit; takes precedence over tag and branch")]
	public string? Commit { get; set; }

	/// <summary>
	/// Gets or sets the repository-relative ready bundle artifact path.
	/// </summary>
	[Option("artifact-path", Required = false,
		HelpText = "Repository-relative ready bundle path for Git; default: knowledge-bundle.zip")]
	public string? ArtifactPath { get; set; }

	/// <summary>
	/// Gets or sets the source priority used for deterministic topic resolution.
	/// </summary>
	[Option("priority", Required = false, Default = 0, HelpText = "Topic resolution priority; higher eligible values win")]
	public int Priority { get; set; }

	/// <summary>
	/// Gets or sets the source participation mode.
	/// </summary>
	[Option("participation", Required = false, Default = "supplement",
		HelpText = "Topic participation: isolated, supplement, or authoritative")]
	public string Participation { get; set; } = "supplement";

	/// <summary>
	/// Gets or sets whether the source is initially disabled.
	/// </summary>
	[Option("disabled", Required = false, Default = false,
		HelpText = "Add the source disabled while retaining its configuration")]
	public bool Disabled { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for removing a configured knowledge source and its managed cache.
/// </summary>
[Verb("remove-knowledge-source", HelpText = "Remove one configured knowledge source and its managed cache")]
public sealed class RemoveKnowledgeSourceOptions {
	/// <summary>
	/// Gets or sets the configured source alias.
	/// </summary>
	[Option("alias", Required = true, HelpText = "Configured source alias to remove")]
	public string Alias { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets whether removal proceeds without an interactive confirmation prompt.
	/// </summary>
	[Option("force", Required = false, Default = false,
		HelpText = "Confirm source and managed-cache removal without an interactive prompt")]
	public bool Force { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for enabling a configured knowledge source.
/// </summary>
[Verb("enable-knowledge-source", HelpText = "Enable one configured knowledge source")]
public sealed class EnableKnowledgeSourceOptions {
	/// <summary>
	/// Gets or sets the configured source alias.
	/// </summary>
	[Option("alias", Required = true, HelpText = "Configured source alias to enable")]
	public string Alias { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for disabling a configured knowledge source while retaining its cache.
/// </summary>
[Verb("disable-knowledge-source", HelpText = "Disable one configured knowledge source without deleting its cache")]
public sealed class DisableKnowledgeSourceOptions {
	/// <summary>
	/// Gets or sets the configured source alias.
	/// </summary>
	[Option("alias", Required = true, HelpText = "Configured source alias to disable")]
	public string Alias { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for listing all configured knowledge sources.
/// </summary>
[Verb("list-knowledge-sources", HelpText = "List all configured knowledge sources, including disabled sources")]
public sealed class ListKnowledgeSourcesOptions {
	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Installs verified knowledge through the configured source-management boundary.
/// </summary>
internal sealed class InstallKnowledgeCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<InstallKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(InstallKnowledgeOptions options) {
		if (!TryNormalizeOptionalSource(options.Source, logger, out string? sourceAlias)) {
			return 1;
		}
		return Report(logger, service.Install(sourceAlias), options.Json);
	}

	internal static int Report(ILogger logger, KnowledgeSourceBatchResult result, bool json) {
		if (json) {
			logger.WriteLine(KnowledgeCommandJson.Serialize(result));
		}
		else {
			PrintOperations(logger, result.Sources);
			WriteStatus(logger, result.Success, result.Message);
		}
		return result.Success ? 0 : 1;
	}

	internal static int Report(ILogger logger, KnowledgeSourceCommandResult result, bool json) {
		if (json) {
			logger.WriteLine(KnowledgeCommandJson.Serialize(result));
		}
		else {
			WriteStatus(logger, result.Success, result.Message);
		}
		return result.Success ? 0 : 1;
	}

	internal static bool TryNormalizeOptionalSource(string? source, ILogger logger, out string? sourceAlias) {
		if (source is not null && string.IsNullOrWhiteSpace(source)) {
			logger.WriteError("Source alias cannot be empty. Omit --source to target all enabled sources.");
			sourceAlias = null;
			return false;
		}
		sourceAlias = source?.Trim();
		return true;
	}

	internal static bool TryNormalizeRequiredAlias(string alias, ILogger logger, out string sourceAlias) {
		if (string.IsNullOrWhiteSpace(alias)) {
			logger.WriteError("Source alias cannot be empty.");
			sourceAlias = string.Empty;
			return false;
		}
		sourceAlias = alias.Trim();
		return true;
	}

	private static void PrintOperations(ILogger logger, IReadOnlyList<KnowledgeSourceOperationResult> operations) {
		if (operations.Count == 0) {
			return;
		}
		ConsoleTable table = new() { Columns = { "Source", "Status", "Message" } };
		foreach (KnowledgeSourceOperationResult operation in operations) {
			table.Rows.Add([operation.SourceAlias, operation.Status, operation.Message]);
		}
		logger.PrintTable(table);
	}

	private static void WriteStatus(ILogger logger, bool success, string message) {
		if (success) {
			logger.WriteInfo(message);
		}
		else {
			logger.WriteError(message);
		}
	}
}

/// <summary>
/// Updates verified knowledge through the configured source-management boundary.
/// </summary>
internal sealed class UpdateKnowledgeCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<UpdateKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(UpdateKnowledgeOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeOptionalSource(options.Source, logger, out string? sourceAlias)) {
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Update(sourceAlias), options.Json);
	}
}

/// <summary>
/// Reports configured knowledge sources and installed generations.
/// </summary>
internal sealed class InfoKnowledgeCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<InfoKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(InfoKnowledgeOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeOptionalSource(options.Source, logger, out string? sourceAlias)) {
			return 1;
		}
		KnowledgeSourceInfoResult result = service.GetInfo(sourceAlias, options.CheckUpdates);
		if (options.Json) {
			logger.WriteLine(KnowledgeCommandJson.Serialize(result));
		}
		else {
			PrintInfo(logger, result);
		}
		return result.Success ? 0 : 1;
	}

	private static void PrintInfo(ILogger logger, KnowledgeSourceInfoResult result) {
		ConsoleTable location = new() { Columns = { "Knowledge property", "Value" } };
		location.Rows.Add(["settings-file", result.SettingsFilePath]);
		location.Rows.Add(["root-path", result.RootPath]);
		location.Rows.Add(["diagnostic", result.Diagnostic ?? string.Empty]);
		logger.PrintTable(location);
		KnowledgeSourceTable.Print(logger, result.Sources, includeInstallation: true);
	}
}

/// <summary>
/// Deletes installed knowledge while retaining source configuration.
/// </summary>
internal sealed class DeleteKnowledgeCommand(
	IKnowledgeSourceManagementService service,
	IInteractiveConsole interactiveConsole,
	ILogger logger) : Command<DeleteKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(DeleteKnowledgeOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeOptionalSource(options.Source, logger, out string? sourceAlias)) {
			return 1;
		}
		string target = sourceAlias is null ? "all enabled knowledge sources" : $"knowledge source '{sourceAlias}'";
		bool confirmed = options.Force || interactiveConsole.Prompt(
			$"Delete Clio-managed installed knowledge for {target} while retaining source configuration?");
		if (!confirmed) {
			logger.WriteError("Knowledge deletion was not confirmed. Use --force in a non-interactive host.");
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Delete(sourceAlias, confirmed: true), options.Json);
	}
}

/// <summary>
/// Adds one trusted knowledge source.
/// </summary>
internal sealed class AddKnowledgeSourceCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<AddKnowledgeSourceOptions> {
	/// <inheritdoc />
	public override int Execute(AddKnowledgeSourceOptions options) {
		if (!TryCreateRequest(options, logger, out KnowledgeSourceAddRequest? request)) {
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Add(request), options.Json);
	}

	private static bool TryCreateRequest(
		AddKnowledgeSourceOptions options,
		ILogger logger,
		out KnowledgeSourceAddRequest? request) {
		request = null;
		if (!InstallKnowledgeCommand.TryNormalizeRequiredAlias(options.Alias, logger, out string alias)) {
			return false;
		}
		if (string.IsNullOrWhiteSpace(options.LibraryId)
				|| string.IsNullOrWhiteSpace(options.Type)
				|| string.IsNullOrWhiteSpace(options.Location)
				|| string.IsNullOrWhiteSpace(options.TrustedKeyId)
				|| string.IsNullOrWhiteSpace(options.TrustedPublicKeyPath)) {
			logger.WriteError("--library-id, --type, --location, --trusted-key-id, and --trusted-public-key-path cannot be empty.");
			return false;
		}
		if (!System.IO.Path.IsPathFullyQualified(options.TrustedPublicKeyPath.Trim())) {
			logger.WriteError("--trusted-public-key-path must be an absolute local file path containing public key material.");
			return false;
		}
		string transportType = options.Type.Trim().ToLowerInvariant();
		if (transportType is not ("git" or "nuget")) {
			logger.WriteError("Knowledge source type must be 'git' or 'nuget'.");
			return false;
		}
		string participation = options.Participation.Trim().ToLowerInvariant();
		if (participation is not ("isolated" or "supplement" or "authoritative")) {
			logger.WriteError("Knowledge source participation must be 'isolated', 'supplement', or 'authoritative'.");
			return false;
		}
		if (transportType == "nuget" && string.IsNullOrWhiteSpace(options.PackageId)) {
			logger.WriteError("--package-id is required when --type is nuget.");
			return false;
		}
		bool hasGitOptions = !string.IsNullOrWhiteSpace(options.Branch)
			|| !string.IsNullOrWhiteSpace(options.Tag)
			|| !string.IsNullOrWhiteSpace(options.Commit)
			|| !string.IsNullOrWhiteSpace(options.ArtifactPath);
		if (transportType == "nuget" && hasGitOptions) {
			logger.WriteError("--branch, --tag, --commit, and --artifact-path are valid only for Git sources.");
			return false;
		}
		if (transportType == "git" && !string.IsNullOrWhiteSpace(options.PackageId)) {
			logger.WriteError("--package-id is valid only for NuGet sources.");
			return false;
		}
		request = new KnowledgeSourceAddRequest(
			alias,
			options.LibraryId.Trim(),
			transportType,
			options.Location.Trim(),
			options.TrustedKeyId.Trim(),
			System.IO.Path.GetFullPath(options.TrustedPublicKeyPath.Trim()),
			TrimToNull(options.PackageId),
			TrimToNull(options.Branch),
			TrimToNull(options.Tag),
			TrimToNull(options.Commit),
			TrimToNull(options.ArtifactPath),
			Enabled: !options.Disabled,
			options.Priority,
			participation);
		return true;
	}

	private static string? TrimToNull(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Removes one configured knowledge source after explicit confirmation.
/// </summary>
internal sealed class RemoveKnowledgeSourceCommand(
	IKnowledgeSourceManagementService service,
	IInteractiveConsole interactiveConsole,
	ILogger logger) : Command<RemoveKnowledgeSourceOptions> {
	/// <inheritdoc />
	public override int Execute(RemoveKnowledgeSourceOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeRequiredAlias(options.Alias, logger, out string alias)) {
			return 1;
		}
		bool confirmed = options.Force || interactiveConsole.Prompt(
			$"Remove knowledge source '{alias}' and delete its Clio-managed installed cache?");
		if (!confirmed) {
			logger.WriteError("Knowledge source removal was not confirmed. Use --force in a non-interactive host.");
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Remove(alias, confirmed: true), options.Json);
	}
}

/// <summary>
/// Enables one configured knowledge source.
/// </summary>
internal sealed class EnableKnowledgeSourceCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<EnableKnowledgeSourceOptions> {
	/// <inheritdoc />
	public override int Execute(EnableKnowledgeSourceOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeRequiredAlias(options.Alias, logger, out string alias)) {
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Enable(alias), options.Json);
	}
}

/// <summary>
/// Disables one configured knowledge source without deleting its installed cache.
/// </summary>
internal sealed class DisableKnowledgeSourceCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<DisableKnowledgeSourceOptions> {
	/// <inheritdoc />
	public override int Execute(DisableKnowledgeSourceOptions options) {
		if (!InstallKnowledgeCommand.TryNormalizeRequiredAlias(options.Alias, logger, out string alias)) {
			return 1;
		}
		return InstallKnowledgeCommand.Report(logger, service.Disable(alias), options.Json);
	}
}

/// <summary>
/// Lists all configured knowledge sources.
/// </summary>
internal sealed class ListKnowledgeSourcesCommand(IKnowledgeSourceManagementService service, ILogger logger)
	: Command<ListKnowledgeSourcesOptions> {
	/// <inheritdoc />
	public override int Execute(ListKnowledgeSourcesOptions options) {
		KnowledgeSourceListResult result = service.List();
		if (options.Json) {
			logger.WriteLine(KnowledgeCommandJson.Serialize(result));
		}
		else {
			KnowledgeSourceTable.Print(logger, result.Sources, includeInstallation: false);
			if (!string.IsNullOrWhiteSpace(result.Diagnostic)) {
				if (result.Success) {
					logger.WriteInfo(result.Diagnostic);
				}
				else {
					logger.WriteError(result.Diagnostic);
				}
			}
		}
		return result.Success ? 0 : 1;
	}
}

internal static class KnowledgeSourceTable {
	internal static void Print(ILogger logger, IReadOnlyList<KnowledgeSourceInfo> sources, bool includeInstallation) {
		ConsoleTable table = includeInstallation
			? new ConsoleTable("Alias", "Library", "Type", "Enabled", "Priority", "Participation", "Installed", "Valid", "Library version", "Sequence", "Bundle digest", "Revision", "Active path", "Update", "Diagnostic")
			: new ConsoleTable("Alias", "Library", "Type", "Enabled", "Priority", "Participation", "Location");
		foreach (KnowledgeSourceInfo source in sources) {
			if (includeInstallation) {
				table.AddRow(
				source.Alias,
				source.LibraryId,
				source.TransportType,
				source.Enabled,
				source.Priority,
				source.Participation,
				source.IsInstalled,
				source.IsValid,
				source.ActiveLibraryVersion ?? string.Empty,
				source.ActiveSequence?.ToString() ?? string.Empty,
				source.BundleDigest ?? string.Empty,
				source.ResolvedRevision ?? string.Empty,
				source.ActiveContentPath ?? string.Empty,
				source.UpdateAvailability ?? string.Empty,
				source.Diagnostic ?? string.Empty);
			}
			else {
				table.AddRow(
					source.Alias,
					source.LibraryId,
					source.TransportType,
					source.Enabled,
					source.Priority,
					source.Participation,
					source.Location);
			}
		}
		logger.PrintTable(table);
	}
}

internal static class KnowledgeCommandJson {
	private static readonly JsonSerializerOptions Options = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
