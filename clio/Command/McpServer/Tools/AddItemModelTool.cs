using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>add-item model</c> command in all-model generation mode.
/// </summary>
[McpServerToolType]
public sealed class AddItemModelTool(
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem) {
	private static readonly object CommandExecutionLock = new();

	/// <summary>
	/// Stable MCP tool name for all-model generation.
	/// </summary>
	internal const string AddItemModelToolName = "add-item-model";

	/// <summary>
	/// Generates all Creatio entity models into the requested local folder.
	/// </summary>
	[McpServerTool(Name = AddItemModelToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Generates all C# entity models from the specified Creatio environment into the provided local folder.")]
	public CommandExecutionResult AddItemModel(
		[Description("add-item model parameters")]
		[Required]
		AddItemModelArgs args) {
		string? folderValidationError = AddItemModelToolPathValidator.ValidateFolder(fileSystem, args.Folder);
		if (folderValidationError is not null) {
			return new CommandExecutionResult(1, [new ErrorMessage(folderValidationError)]);
		}

		AddItemOptions options = new() {
			ItemType = "model",
			CreateAll = true,
			Namespace = args.Namespace,
			DestinationPath = fileSystem.Path.GetFullPath(args.Folder),
			Environment = args.EnvironmentName
		};
		AddItemCommand command = commandResolver.Resolve<AddItemCommand>(options);
		return Execute(command, options);
	}

	private CommandExecutionResult Execute(AddItemCommand command, AddItemOptions options) {
		int exitCode = -1;
		lock (CommandExecutionLock) {
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				exitCode = command.Execute(options);
				Thread.Sleep(500);
				CommandExecutionResult result = new(exitCode, [.. logger.LogMessages.ToList()]);
				logger.ClearMessages();
				return result;
			}
			catch (Exception exception) {
				List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(exception.Message)];
				CommandExecutionResult result = new(1, logMessages);
				logger.ClearMessages();
				return result;
			}
			finally {
				logger.PreserveMessages = previousPreserveMessages;
			}
		}
	}
}

internal static class AddItemModelToolPathValidator {
	internal static string? ValidateFolder(IFileSystem fileSystem, string? folder) {
		if (string.IsNullOrWhiteSpace(folder)) {
			return "Folder is required.";
		}

		if (IsNetworkPath(folder)) {
			return $"Folder path must be a local absolute path: {folder}";
		}

		if (!IsAbsolutePath(fileSystem, folder)) {
			return $"Folder path must be absolute: {folder}";
		}

		string fullPath = fileSystem.Path.GetFullPath(folder);
		return !fileSystem.Directory.Exists(fullPath)
			? $"Folder path not found: {fullPath}"
			: null;
	}

	private static bool IsAbsolutePath(IFileSystem fileSystem, string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		string root = fileSystem.Path.GetPathRoot(path);
		return fileSystem.Path.IsPathRooted(path) &&
			!string.IsNullOrWhiteSpace(root) &&
			(root.EndsWith(fileSystem.Path.DirectorySeparatorChar) ||
			 root.EndsWith(fileSystem.Path.AltDirectorySeparatorChar));
	}

	private static bool IsNetworkPath(string path) {
		return path.StartsWith(@"\\", StringComparison.Ordinal) ||
			path.StartsWith("//", StringComparison.Ordinal);
	}
}

/// <summary>
/// MCP arguments for the <c>add-item-model</c> tool.
/// </summary>
public sealed record AddItemModelArgs(
	[property: JsonPropertyName("namespace")]
	[property: Description("C# namespace for the generated model classes")]
	[property: Required]
	string Namespace,

	[property: JsonPropertyName("folder")]
	[property: Description("Absolute local folder where model files will be created")]
	[property: Required]
	string Folder,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name used to read Creatio schemas")]
	[property: Required]
	string EnvironmentName
);
