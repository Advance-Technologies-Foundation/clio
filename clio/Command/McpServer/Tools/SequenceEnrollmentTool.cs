using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware native sequence enrollment adapter.</summary>
[McpServerToolType]
public sealed class SequenceEnrollmentTool(SequenceEnrollmentCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<SequenceEnrollmentOptions>(command, logger, resolver) {
	internal const string ToolName = "enroll-sequence-participants";
	/// <summary>Enrolls an explicit bounded audience using the native engine.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Enroll 1–100 explicit contacts through Creatio's native sequence service. Native eligibility, duplicates, capacity and activity creation apply. " +
		"Does not activate a sequence. May start activities for an already active sequence. No automatic retry. Inspect completion, platform counts and readback; " +
		"uncertain completion requires verification before resubmission. Added does not mean Active.")]
	public SequenceEnrollmentResult Enroll([Required] SequenceEnrollmentArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			throw new ArgumentException("environment-name is required.");
		}
		SequenceEnrollmentOptions options = new() {
			Environment = args.EnvironmentName, SequenceId = args.SequenceId, ContactIds = args.ContactIds
		};
		return ExecuteUnderTenantLock(options, () => ResolveCommand<SequenceEnrollmentCommand>(options).Enroll(options));
	}
}

/// <summary>Explicit sequence and contact identifiers for native enrollment.</summary>
public sealed record SequenceEnrollmentArgs(
	[property: JsonPropertyName("environment-name"), Required, Description("Registered Creatio environment.")] string EnvironmentName,
	[property: JsonPropertyName("sequence-id"), Required, Description("Sequence UUID; no implicit activation.")] Guid SequenceId,
	[property: JsonPropertyName("contact-ids"), Required, MinLength(1), MaxLength(100), Description("1–100 unique contact UUIDs.")] Guid[] ContactIds);
