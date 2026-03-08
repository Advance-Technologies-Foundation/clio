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
/// MCP tool surface for the <c>new-test-project</c> command.
/// </summary>
public class CreateTestProjectTool(
	CreateTestProjectCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IProcessExecutor processExecutor)
	: BaseTool<CreateTestProjectOptions>(command, logger, commandResolver) {
	private readonly IProcessExecutor _processExecutor = processExecutor;

	/// <summary>
	/// Creates backend unit test project scaffolding for a package.
	/// </summary>
	/// <param name="args">Arguments that map to <see cref="CreateTestProjectOptions"/>.</param>
	/// <returns>Execution result with command log output.</returns>
	[McpServerTool(Name = "new-test-project", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Creates backend unit test project scaffolding for a package.
				 
				 When `path` is provided, clio executes the command as if it were started from that directory.
				 Use a workspace root path so the generated test project is attached to that workspace.
				 """)]
	public async Task<CommandExecutionResult> CreateTestProject(
		[Description("new-test-project parameters")] [Required] CreateTestProjectArgs args
	) {
		CreateTestProjectOptions options = new() {
			PackageName = args.PackageName,
			Environment = args.EnvironmentName
		};

		if (!string.IsNullOrWhiteSpace(args.WorkspacePath)) {
			return await ExecuteInWorkspaceAsync(BuildCommandArguments(args), args.WorkspacePath);
		}

		return string.IsNullOrWhiteSpace(args.EnvironmentName)
			? InternalExecute(options)
			: InternalExecute<CreateTestProjectCommand>(options);
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

	private static string BuildCommandArguments(CreateTestProjectArgs args) {
		List<string> arguments = ["new-test-project", "--package", Quote(args.PackageName)];
		if (!string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			arguments.Add("--Environment");
			arguments.Add(Quote(args.EnvironmentName));
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
/// Arguments for the <c>new-test-project</c> MCP tool.
/// </summary>
public record CreateTestProjectArgs(
	[property: JsonPropertyName("package-name")]
	[Description("Package name. The command also accepts comma-separated package names.")]
	[Required]
	string PackageName,

	[property: JsonPropertyName("workspace-path")]
	[Description("Optional workspace root path. The path must contain .clio/workspaceSettings.json.")]
	string WorkspacePath = null,

	[property: JsonPropertyName("environment-name")]
	[Description("Optional environment name. Included only for parity with the CLI option surface.")]
	string EnvironmentName = null
);
