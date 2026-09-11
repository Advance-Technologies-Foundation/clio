using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that applies edits to a NEW VERSION of an existing business process instead of to the running one.
/// </summary>
public class ModifyProcessAsNewVersionTool(
	ModifyProcessAsNewVersionCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ModifyProcessAsNewVersionOptions>(command, logger, commandResolver) {

	internal const string ModifyProcessAsNewVersionToolName = "modify-business-process-as-new-version";

	/// <summary>
	/// Applies an inline JSON operations array to a CLONE of an existing process and saves it as a new version.
	/// </summary>
	/// <param name="args">Source identity, optional target package, and the operations array.</param>
	/// <returns>The command execution result with the created version's identity in the log output.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	// Destructive=false is the substantive claim here, not a formality: this operation never opens, saves or
	// activates the source. Everything it writes goes to a clone, and what the environment executes is unchanged
	// when it returns — so a caller gated on destructive consent must not be stopped by it.
	[McpServerTool(Name = ModifyProcessAsNewVersionToolName, ReadOnly = false, Destructive = false,
		 Idempotent = false, OpenWorld = false),
	 Description("Edit an existing Creatio business process and save the result as a NEW VERSION of it, leaving "
		 + "the current one untouched and still running. This is what the product's designer calls "
		 + "\"Save new version\" (Ctrl+Alt+N), as opposed to \"Save current version\" — which is what "
		 + "modify-business-process does. Identify the SOURCE process by name (schema code) or uid; the "
		 + "'operations' array is exactly the one modify-business-process takes, with the same op vocabulary and "
		 + "the same descriptors, so read that tool's description for the operation reference. An EMPTY or "
		 + "omitted operations array is legal and takes a plain snapshot of the source as a new version. "
		 + "Optionally name the target package with 'package-name'; omitted, the version goes to the SOURCE's "
		 + "package, always — there is no fallback to a design package, so if the source's package does not "
		 + "accept edits the call is refused and you must name an editable one. A version does NOT inherit the "
		 + "root's package, and cross-package version families are normal. "
		 + "The new version is created INACTIVE: creating it changes NOTHING about what the environment "
		 + "executes, and the source keeps running until something activates the new one. Activating is a "
		 + "SEPARATE, explicit step — call set-active-business-process-version, and only if the user asked for "
		 + "it; the product itself asks before making a new version actual, so do not activate on your own "
		 + "initiative. The response reports the created version's schema UId, the name the PLATFORM composed "
		 + "(root name + package + number — you cannot predict or choose it), the version NUMBER the platform "
		 + "allocated, isActiveVersion (false), the family ROOT UId (the family is FLAT — a version of a version "
		 + "still points at the root) and the applied-operation count. The number and the flag are reported only "
		 + "when the environment reported them: an omitted number is stated as ABSENT rather than as 0 (0 means "
		 + "family ROOT in this surface's vocabulary, which is the opposite of a new version), and an omitted "
		 + "flag yields 'the environment did not report which version is actual' instead of the reassuring "
		 + "'the source still runs' — do not read that as a claim about what executes. Operations apply in order and any failure "
		 + "aborts the whole call; a failure BEFORE the save leaves nothing behind at all, so there is no "
		 + "half-created version to clean up. Note that a version can never be DELETED — the platform has no such "
		 + "operation — so every version you create is permanent; take that into account before creating one "
		 + "speculatively. Requires the ProcessDesignService (CrtProcessBuilder) package on the target "
		 + "environment at CrtProcessBuilder 1.6.2.1 or newer — an older package is refused up front, naming the "
		 + "version this operation needs; install or update it with install-process-builder. (The operation itself "
		 + "first exists in 1.6.1.0; the floor moved to 1.6.2.1 with the Send email template message mode, because "
		 + "the operations vocabulary now carries email.messageSource/template/templateEntity, which an older server "
		 + "silently discards, and this route runs NO read-back check that could tell you.) After a successful save the version normally stays INTERPRETED and "
		 + "runs as-is once activated, so compile-creatio is not needed — UNLESS the response warns that the "
		 + "version cannot execute until the configuration is compiled, which happens when the source process "
		 + "was itself not interpretable. Heed the warning over this sentence: run compile-creatio in that case. "
		 + "Use describe-business-process to inspect the family.")]
	public CommandExecutionResult ModifyProcessAsNewVersion(
		[Description("modify-business-process-as-new-version parameters")] [Required]
		ModifyProcessAsNewVersionArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		bool hasName = !string.IsNullOrWhiteSpace(args.ProcessName);
		bool hasUid = !string.IsNullOrWhiteSpace(args.ProcessUid);
		if (hasName == hasUid) {
			return CommandExecutionResult.FromError(hasName
				? "Provide only one of process-name or process-uid, not both."
				: "one of process-name or process-uid is required.");
		}

		ModifyProcessAsNewVersionOptions options = new() {
			Environment = args.EnvironmentName,
			ProcessName = args.ProcessName ?? string.Empty,
			ProcessUid = args.ProcessUid ?? string.Empty,
			PackageName = args.PackageName ?? string.Empty,
			OperationsJson = args.Operations ?? string.Empty
		};
		// Same post-op note as the in-place edit: a saved version is interpreted and runs as-is once activated,
		// so "saved" must not be read as "must be compiled" (ENG-95706).
		//
		// CONDITIONAL, unlike the in-place sibling's, and the difference is real rather than defensive. A clone
		// inherits IsInterpretable through the metadata round-trip and the server never recomputes it, so a
		// version taken from a non-interpretable source is saved with IsInterpretable false and the package
		// warns that it cannot execute until the configuration is compiled. Appending the note anyway put that
		// warning and its exact opposite in one response - and an agent that believed the note would activate
		// a version that throws NotImplementedException out of CreateProcess on first run.
		CommandExecutionResult result = InternalExecute<ModifyProcessAsNewVersionCommand>(options);
		if (result.ExitCode != 0) {
			return result;
		}

		return result.WithCompileNotRequiredNote();
	}
}

/// <summary>
/// MCP arguments for the <c>modify-business-process-as-new-version</c> tool (kebab-case wire keys, repo convention).
/// Provide exactly one of <c>process-name</c> / <c>process-uid</c>.
/// </summary>
public sealed record ModifyProcessAsNewVersionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("process-name")]
	[property: Description("Code (schema Name) of the SOURCE process; provide exactly one of process-name or process-uid.")]
	string? ProcessName = null,

	[property: JsonPropertyName("process-uid")]
	[property: Description("Schema UId of the SOURCE process; provide exactly one of process-name or process-uid.")]
	string? ProcessUid = null,

	[property: JsonPropertyName("operations")]
	[property: Description(
		"Inline JSON operations array, identical in shape to modify-business-process. Omit or pass [] to snapshot "
		+ "the source unchanged as a new version.")]
	string? Operations = null,

	[property: JsonPropertyName("package-name")]
	[property: Description(
		"Package the new version is saved into. Omit to let the platform choose; a version does not inherit the "
		+ "root's package.")]
	string? PackageName = null);
