using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware adapter for sequence discovery.</summary>
[McpServerToolType]
public sealed class SequenceContextTool(SequenceContextCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<SequenceContextOptions>(command, logger, resolver) {
	internal const string ToolName = "get-sequence-context";

	/// <summary>Reads effective fields and bounded choices without changing the environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Discover effective sequence fields, live lookup IDs and ruleset/schedule choices in one read-only call. " +
		"Uses DataService; no OData fallback. Optional sequence-id inspects an existing definition. " +
		"Check each section state: missing, failed or truncated sections are not complete context. " +
		"Schema presence does not prove lifecycle-service availability or write permissions.")]
	public SequenceContextResult GetContext([Required] SequenceContextArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			throw new ArgumentException("environment-name is required.");
		}
		SequenceContextOptions options = new() { Environment = args.EnvironmentName, SequenceId = args.SequenceId };
		return ExecuteUnderTenantLock(options, () => ResolveCommand<SequenceContextCommand>(options).Read(options));
	}
}

/// <summary>Arguments for read-only sequence context.</summary>
public sealed record SequenceContextArgs(
	[property: JsonPropertyName("environment-name"), Required, Description("Registered Creatio environment.")] string EnvironmentName,
	[property: JsonPropertyName("sequence-id"), Description("Optional sequence definition UUID.")] Guid? SequenceId = null);
