using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool for package storage synchronization operations on a Creatio web
/// application. <c>target='file-system'</c> loads packages from the database into the file system;
/// <c>target='db'</c> loads packages from the file system into the database. Folds the legacy
/// <c>pkg-to-file-system</c> and <c>pkg-to-db</c> tools.
/// </summary>
public class LoadPackagesTool(
	LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<EnvironmentOptions>(loadPackagesToFileSystemCommand, logger, commandResolver) {

	internal const string ToolName = "pkg-mode";
	internal const string TargetFileSystem = "file-system";
	internal const string TargetDb = "db";

	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation.</summary>
	internal const string LegacyPkgToFileSystemToolName = "pkg-to-file-system";
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation.</summary>
	internal const string LegacyPkgToDbToolName = "pkg-to-db";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Synchronizes Creatio package storage between the database and the file system. target='file-system' loads packages from the database into the file system; target='db' loads packages from the file system into the database.")]
	public CommandExecutionResult Apply(
		[Description("pkg-mode parameters")] [Required] PkgModeArgs args) {
		CommandExecutionResult targetError = CommandExecutionResult.ValidateExactlyOneMode(
			"target", args.Target, TargetFileSystem, TargetDb);
		if (targetError != null) {
			return targetError;
		}
		CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
			"environment-name", args.EnvironmentName, args.Target);
		if (missing != null) {
			return missing;
		}
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName
		};
		if (string.Equals(args.Target, TargetFileSystem, StringComparison.OrdinalIgnoreCase)) {
			return InternalExecute<LoadPackagesToFileSystemCommand>(options);
		}
		return InternalExecute<LoadPackagesToDbCommand>(options);
	}
}

/// <summary>
/// Arguments for the consolidated <c>pkg-mode</c> MCP tool.
/// </summary>
public sealed record PkgModeArgs(
	[property: JsonPropertyName("target")]
	[property: Description("Discriminator: 'file-system' or 'db'. Selects the direction of the package storage sync.")]
	[property: Required]
	string Target,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName
);
