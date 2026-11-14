using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware native DataService batch adapter.</summary>
[McpServerToolType]
public sealed class DataServiceBatchTool(DataServiceBatchCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<DataServiceBatchOptions>(command, logger, resolver) {
	internal const string ToolName = "execute-dataservice-batch";
	/// <summary>Submits a bounded explicit-record batch once.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Write 1–100 explicit records through native DataService batching. Continues after item errors; no atomicity guarantee or automatic retry. " +
		"Inspect per-item completed/failed/unknown outcomes and read records back before resubmission. Use native sequence enrollment for participant lifecycle changes; read get-guidance name=sequences for sequence work.")]
	public DataServiceBatchResult Execute([Required] DataServiceBatchArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			throw new ArgumentException("environment-name is required.");
		}
		DataServiceBatchOptions options = new() { Environment = args.EnvironmentName };
		return ExecuteUnderTenantLock(options, () => ResolveCommand<DataServiceBatchCommand>(options).ExecuteBatch(args.Operations));
	}
}

/// <summary>Explicit environment and bounded record writes.</summary>
public sealed record DataServiceBatchArgs(
	[property: JsonPropertyName("environment-name"), Required, Description("Registered Creatio environment.")] string EnvironmentName,
	[property: JsonPropertyName("operations"), Required, MinLength(1), MaxLength(100), Description("Each item: {operation: insert|update|delete, schema-name: object schema name, record-id: nonempty UUID, values: {columnName: {data-value-type: integer, value: scalar or null}}}. Insert/update require 1–50 values; delete omits values. Update/delete target record-id only; values cannot replace Id. Types: 0 UUID, 1 text (including rich text), 4 integer, 5 float, 6 money, 7 datetime, 8 date, 9 time, 10 lookup UUID, 12 boolean. Read back affected records before retrying.")] DataServiceBatchOperation[] Operations);
