using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>add-package</c> command.
/// </summary>
public class AddPackageTool(
	AddPackageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IProcessExecutor processExecutor)
	: BaseTool<AddPackageOptions>(command, logger, commandResolver) {
	private readonly IProcessExecutor _processExecutor = processExecutor;

	/// <summary>
	/// Adds a package to the current workspace or to a local folder.
	/// </summary>
	/// <param name="args">Arguments that map to <see cref="AddPackageOptions"/>.</param>
	/// <returns>Execution result with command log output.</returns>
	[McpServerTool(Name = "add-package", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Adds a package to a workspace or a local folder.
				 
				 When `path` is provided, clio executes the command as if it were started from that directory.
				 Use a workspace root path to add the package into `packages/<package-name>` inside that workspace.
				 """)]
	public async Task<CommandExecutionResult> AddPackage(
		[Description("add-package parameters")] [Required] AddPackageArgs args
	) {
		AddPackageOptions options = new() {
			Name = args.PackageName,
			AsApp = args.AsApp,
			BuildZipPath = args.BuildZipPath,
			Environment = args.EnvironmentName
		};

		if (!string.IsNullOrWhiteSpace(args.WorkspacePath)) {
			return await ExecuteInWorkspaceAsync(BuildCommandArguments(args), args.WorkspacePath);
		}

		return string.IsNullOrWhiteSpace(args.EnvironmentName)
			? InternalExecute(options)
			: InternalExecute<AddPackageCommand>(options);
	}

	private async Task<CommandExecutionResult> ExecuteInWorkspaceAsync(string commandArguments, string workspacePath) {
		string validationError = ValidateWorkspacePath(workspacePath);
		if (validationError is not null) {
			return new CommandExecutionResult(1, [new ErrorMessage(validationError)]);
		}

		string entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
		if (string.IsNullOrWhiteSpace(entryAssemblyPath)) {
			return new CommandExecutionResult(1, [new ErrorMessage("Unable to resolve the current clio entry assembly path.")]);
		}

		ProcessExecutionResult result = await _processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(
			"dotnet",
			$"{Quote(entryAssemblyPath)} {commandArguments}") {
			WorkingDirectory = workspacePath
		});

		return CreateResult(result);
	}

	private static string BuildCommandArguments(AddPackageArgs args) {
		List<string> arguments = ["add-package", Quote(args.PackageName)];
		if (args.AsApp) {
			arguments.Add("--asApp");
		}
		if (!string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			arguments.Add("--Environment");
			arguments.Add(Quote(args.EnvironmentName));
		}
		if (!string.IsNullOrWhiteSpace(args.BuildZipPath)) {
			arguments.Add("--build");
			arguments.Add(Quote(args.BuildZipPath));
		}
		return string.Join(" ", arguments);
	}

	private static string ValidateWorkspacePath(string workspacePath) {
		if (!Directory.Exists(workspacePath)) {
			return $"Workspace path '{workspacePath}' does not exist.";
		}

		string workspaceSettingsPath = Path.Combine(workspacePath, ".clio", "workspaceSettings.json");
		return File.Exists(workspaceSettingsPath)
			? null
			: $"Workspace path '{workspacePath}' does not contain '.clio\\workspaceSettings.json'.";
	}

	private static CommandExecutionResult CreateResult(ProcessExecutionResult result) {
		List<LogMessage> output = [];
		output.AddRange(SplitLines(result.StandardOutput).Select(line => new InfoMessage(line)));
		output.AddRange(SplitLines(result.StandardError).Select(line => new ErrorMessage(line)));

		if (!result.Started && output.Count == 0) {
			output.Add(new ErrorMessage("Failed to start child clio process."));
		}

		return new CommandExecutionResult(result.ExitCode ?? 1, output);
	}

	private static IEnumerable<string> SplitLines(string value) {
		return string.IsNullOrWhiteSpace(value)
			? []
			: value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}

/// <summary>
/// Arguments for the <c>add-package</c> MCP tool.
/// </summary>
public record AddPackageArgs(
	[property: JsonPropertyName("package-name")]
	[Description("Package name to create")]
	[Required]
	string PackageName,

	[property: JsonPropertyName("as-app")]
	[Description("When true, create or update app-descriptor.json for the package")]
	bool AsApp = false,

	[property: JsonPropertyName("workspace-path")]
	[Description("Optional workspace root path. The path must contain .clio/workspaceSettings.json.")]
	string WorkspacePath = null,

	[property: JsonPropertyName("environment-name")]
	[Description("Optional environment name used by follow-up steps such as configuration download.")]
	string EnvironmentName = null,

	[property: JsonPropertyName("build-zip-path")]
	[Description("Optional path to a Creatio zip file or extracted build directory used by follow-up steps.")]
	string BuildZipPath = null
);
