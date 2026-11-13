using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that reads an existing dynamic case (DCM), or every case of an object, and changes nothing.
/// </summary>
public class DescribeDynamicCaseTool(
	DescribeDynamicCaseCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<DescribeDynamicCaseOptions>(command, logger, commandResolver) {

	internal const string DescribeDynamicCaseToolName = "describe-dynamic-case";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused.</summary>
	internal const string ValidArgsHint = "Valid: environment-name, case-name, case-uid, entity-schema-name.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>Refusal for a call that names neither the case nor its object.</summary>
	internal const string MissingTargetError =
		"One of case-name, case-uid or entity-schema-name is required: name the case, or the object whose cases to "
		+ "read.";

	/// <summary>
	/// Reads the case, or every case of the object, on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="DescribeDynamicCaseArgs"/>.</param>
	/// <returns>The command execution result; the result document is in the log output.</returns>
	[McpServerTool(Name = DescribeDynamicCaseToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		 OpenWorld = false),
	 Description("Read an existing dynamic case (DCM), or every case of an object, and change nothing. Answers the "
		 + "stages in order with their groups, the final stages, which stages hold steps, the running cases per stage, "
		 + "whether the case is enabled, and the object's case settings rows, with the designer link per case. Call "
		 + "it before answering any question about an existing case and before changing one with "
		 + "modify-dynamic-case. Name the case by case-name or case-uid, or the object by entity-schema-name. "
		 + "Requires a CrtProcessBuilder that carries DescribeCase (install-process-builder, after user approval).")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult DescribeDynamicCase(
		[Description("describe-dynamic-case parameters")] [Required] DescribeDynamicCaseArgs args) {
		if (args is null) {
			return CommandExecutionResult.FromValidationError(NullArgsError);
		}

		// The only unknown-key defence a long-tail tool has; McpToolArgumentSupport.BuildUnknownArgumentError says why.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return CommandExecutionResult.FromValidationError(argumentError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.CaseName) && string.IsNullOrWhiteSpace(args.CaseUId)
			&& string.IsNullOrWhiteSpace(args.EntitySchemaName)) {
			return CommandExecutionResult.FromValidationError(MissingTargetError);
		}

		DescribeDynamicCaseOptions options = new() {
			Environment = args.EnvironmentName,
			CaseName = args.CaseName?.Trim(),
			CaseUId = args.CaseUId?.Trim(),
			EntitySchemaName = args.EntitySchemaName?.Trim()
		};
		return InternalExecute<DescribeDynamicCaseCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>describe-dynamic-case</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record DescribeDynamicCaseArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("case-name")]
	[property: Description("The case's schema name.")]
	string CaseName = null,

	[property: JsonPropertyName("case-uid")]
	[property: Description("The case's schema UId.")]
	string CaseUId = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The object: every case of it is read, enabled or not.")]
	string EntitySchemaName = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
