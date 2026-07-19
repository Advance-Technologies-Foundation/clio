using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// Options for installing the latest verified external Clio knowledge package.
/// </summary>
[Verb("install-knowledge", HelpText = "Install verified Clio knowledge into the configured local knowledge root")]
public sealed class InstallKnowledgeOptions;

/// <summary>
/// Options for updating an installed Clio knowledge package.
/// </summary>
[Verb("update-knowledge", HelpText = "Update installed Clio knowledge to the latest verified version")]
public sealed class UpdateKnowledgeOptions;

/// <summary>
/// Options for inspecting the local Clio knowledge installation.
/// </summary>
[Verb("info-knowledge", HelpText = "Show installed Clio knowledge version, location, validation, and update status")]
public sealed class InfoKnowledgeOptions {
	/// <summary>
	/// Gets or sets whether the command must avoid contacting the configured NuGet source.
	/// </summary>
	[Option("offline", Required = false, Default = false,
		HelpText = "Report local installation state without checking the NuGet source for updates")]
	public bool Offline { get; set; }

	/// <summary>
	/// Gets or sets whether the result is emitted as JSON.
	/// </summary>
	[Option("json", Required = false, Default = false, HelpText = "Output the result as indented JSON")]
	public bool Json { get; set; }
}

/// <summary>
/// Options for deleting locally installed Clio knowledge.
/// </summary>
[Verb("delete-knowledge", HelpText = "Delete locally installed Clio knowledge while retaining its configured root path")]
public sealed class DeleteKnowledgeOptions {
	/// <summary>
	/// Gets or sets whether deletion proceeds without an interactive confirmation prompt.
	/// </summary>
	[Option("force", Required = false, Default = false,
		HelpText = "Confirm deletion without an interactive prompt (required for MCP and other non-interactive hosts)")]
	public bool Force { get; set; }
}

/// <summary>
/// Installs verified Clio knowledge into the configured local root.
/// </summary>
internal sealed class InstallKnowledgeCommand(IKnowledgeInstallationService service, ILogger logger)
	: Command<InstallKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(InstallKnowledgeOptions options) => Report(logger, service.Install());

	internal static int Report(ILogger logger, KnowledgeInstallationResult result) {
		if (result.IsSuccess) {
			logger.WriteInfo(result.Message);
			return 0;
		}
		logger.WriteError(result.Message);
		return 1;
	}
}

/// <summary>
/// Updates installed Clio knowledge through the verified installation pipeline.
/// </summary>
internal sealed class UpdateKnowledgeCommand(IKnowledgeInstallationService service, ILogger logger)
	: Command<UpdateKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(UpdateKnowledgeOptions options) =>
		InstallKnowledgeCommand.Report(logger, service.Update());
}

/// <summary>
/// Reports local knowledge installation and bounded update information.
/// </summary>
internal sealed class InfoKnowledgeCommand(IKnowledgeInstallationService service, ILogger logger)
	: Command<InfoKnowledgeOptions> {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	/// <inheritdoc />
	public override int Execute(InfoKnowledgeOptions options) {
		KnowledgeInstallationInfo info = service.GetInfo(checkUpdates: !options.Offline);
		if (options.Json) {
			logger.WriteLine(JsonSerializer.Serialize(info, JsonOptions));
		}
		else {
			ConsoleTable table = new() {
				Columns = { "Knowledge property", "Value" }
			};
			table.Rows.Add(["settings-file", info.SettingsFilePath]);
			table.Rows.Add(["root-path", info.RootPath]);
			table.Rows.Add(["installed", info.IsInstalled.ToString()]);
			table.Rows.Add(["valid", info.IsValid.ToString()]);
			table.Rows.Add(["active-version", info.ActiveVersion ?? string.Empty]);
			table.Rows.Add(["previous-version", info.PreviousVersion ?? string.Empty]);
			table.Rows.Add(["active-content-path", info.ActiveContentPath ?? string.Empty]);
			table.Rows.Add(["source", info.Source ?? string.Empty]);
			table.Rows.Add(["package-id", info.PackageId ?? string.Empty]);
			table.Rows.Add(["installed-at-utc", info.InstalledAtUtc?.ToString("O") ?? string.Empty]);
			table.Rows.Add(["update-status", info.UpdateAvailability.ToString()]);
			table.Rows.Add(["latest-version", info.LatestVersion ?? string.Empty]);
			table.Rows.Add(["diagnostic", info.Diagnostic ?? string.Empty]);
			logger.PrintTable(table);
		}
		if (string.IsNullOrWhiteSpace(info.RootPath)) {
			logger.WriteError(info.Diagnostic ?? "Knowledge root could not be resolved.");
			return 1;
		}
		return 0;
	}
}

/// <summary>
/// Deletes locally installed Clio knowledge without removing its visible appsettings pointer.
/// </summary>
internal sealed class DeleteKnowledgeCommand(
	IKnowledgeInstallationService service,
	IInteractiveConsole interactiveConsole,
	ILogger logger) : Command<DeleteKnowledgeOptions> {
	/// <inheritdoc />
	public override int Execute(DeleteKnowledgeOptions options) {
		bool confirmed = options.Force || interactiveConsole.Prompt(
			"Delete all Clio-managed knowledge versions and downloaded examples?");
		if (!confirmed) {
			logger.WriteError("Knowledge deletion was not confirmed. Use --force in a non-interactive host.");
			return 1;
		}
		KnowledgeInstallationResult result = service.Delete(confirmed: true);
		if (result.Status == KnowledgeInstallationStatus.NotInstalled) {
			logger.WriteInfo(result.Message);
			return 0;
		}
		return InstallKnowledgeCommand.Report(logger, result);
	}
}
