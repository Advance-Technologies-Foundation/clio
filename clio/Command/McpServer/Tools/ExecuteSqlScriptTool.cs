using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Exposes SQL execution and existing result-file formats through MCP.</summary>
[McpServerToolType]
public sealed class ExecuteSqlScriptTool(
	SqlScriptCommand.SqlScriptCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ExecuteSqlScriptOptions>(command, logger, commandResolver) {

	internal const string ToolName = "execute-sql-script";

	/// <summary>Executes explicit SQL in the requested environment and optionally exports its result.</summary>
	/// <param name="args">SQL input, environment, and output options.</param>
	/// <returns>The command exit code and execution log.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Executes SQL through ClioGate in the specified Creatio environment. SQL can modify or delete data. Table output limits each displayed cell line to 40 characters and wraps longer values; do not use it to extract full cell content. For full content, set view=json (recommended), csv, or xlsx and destination-path to an output file on the MCP server host. Set silent=true to avoid returning the result body in the MCP response. Saving view=table retains the display formatting. Requires ClioGate 2.0.0.41 or later.")]
	public CommandExecutionResult Execute([Required] ExecuteSqlScriptArgs args) {
		if (args is null || string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required.");
		}
		if (string.IsNullOrWhiteSpace(args.Script) == string.IsNullOrWhiteSpace(args.File)) {
			return CommandExecutionResult.FromValidationError("Provide exactly one of script or file.");
		}
		string view = (args.View ?? "table").ToLowerInvariant();
		if (view is not ("table" or "json" or "csv" or "xlsx")) {
			return CommandExecutionResult.FromValidationError("view must be table, json, csv, or xlsx.");
		}
		if ((view is "csv" or "xlsx") && string.IsNullOrWhiteSpace(args.DestinationPath)) {
			return CommandExecutionResult.FromValidationError("destination-path is required for csv and xlsx.");
		}
		return InternalExecute<SqlScriptCommand.SqlScriptCommand>(new ExecuteSqlScriptOptions {
			Environment = args.EnvironmentName,
			Script = string.IsNullOrWhiteSpace(args.Script) ? null : args.Script,
			File = string.IsNullOrWhiteSpace(args.File) ? null : args.File,
			ViewType = view,
			DestPath = args.DestinationPath,
			IsSilent = args.Silent
		});
	}
}

/// <summary>Arguments for SQL execution and result export on the MCP server host.</summary>
public sealed record ExecuteSqlScriptArgs(
	[property: JsonPropertyName("environment-name"), Required]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string EnvironmentName,
	[property: JsonPropertyName("script")]
	[property: Description("SQL text. Supply either script or file, never both. SQL can modify or delete data.")]
	string? Script = null,
	[property: JsonPropertyName("file")]
	[property: Description("Path to a SQL input file on the MCP server host; alternative to script.")]
	string? File = null,
	[property: JsonPropertyName("view")]
	[property: Description("Output format: table (default; 40 characters per displayed cell line), json (recommended for full content), csv, or xlsx. CSV uses semicolons without escaping; prefer JSON for arbitrary text.")]
	string? View = "table",
	[property: JsonPropertyName("destination-path")]
	[property: Description("Output file on the MCP server host, required for csv/xlsx. Use an absolute path for later inspection. Existing files are overwritten. Choose json/csv/xlsx for full cell values; table saves the formatted display.")]
	string? DestinationPath = null,
	[property: JsonPropertyName("silent")]
	[property: Description("Suppress the result body in the MCP response (default false); use true with destination-path for large exports. Completion messages remain.")]
	bool Silent = false);
