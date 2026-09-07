using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that makes one member of a process version family the actual one.
/// </summary>
public class SetActiveProcessVersionTool(
	SetActiveProcessVersionCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetActiveProcessVersionOptions>(command, logger, commandResolver) {

	internal const string SetActiveProcessVersionToolName = "set-active-business-process-version";

	/// <summary>
	/// Activates the named version and reports the version the environment reports as actual afterwards.
	/// </summary>
	/// <param name="args">Identity of the version to activate.</param>
	/// <returns>The command execution result with the read-back active version in the log output.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	// Destructive=true excludes this tool from the 120 s read-response deadline by construction:
	// McpReadDeadlineGate.IsRetrySafe is `!destructive && …`, and that gate deliberately admits no server
	// write, idempotent or not — a deadline that abandoned this call would leave the family mid-switch with
	// nobody reading it back. Idempotent=true is about REPEATING it, which is safe and lands in the same
	// state; it is not a claim that an abandoned call is safe to retry blindly.
	[McpServerTool(Name = SetActiveProcessVersionToolName, ReadOnly = false, Destructive = true,
		 Idempotent = true, OpenWorld = false),
	 Description("Make one version of a Creatio business process the ACTUAL one — the product's UI word for "
		 + "what the platform's data calls the active version. Identify the version by name (schema code) or "
		 + "uid; it must be the version itself, not the family root. This is the rollback gesture: point the "
		 + "environment at whichever member of the family should run. "
		 + "WHAT IT CHANGES: only which version NEW process instances start on. Instances already running stay "
		 + "on the version they started with and finish on it — activation never migrates them, so a long-lived "
		 + "process keeps executing the old graph after this call and that is correct, not a failure. "
		 + "WHAT IT DOES NOT DO: it does not delete anything. There is NO operation anywhere that deletes a "
		 + "version — not this tool, not any other — so 'roll back' means activating an earlier version, never "
		 + "removing the newer one, and the newer one stays visible in the family forever. "
		 + "The result is READ BACK from the environment after the write rather than echoed from the request, "
		 + "because the platform logs and SWALLOWS a failure to deactivate a sibling: a call that reported plain "
		 + "success could leave two members flagged active with package order deciding which one actually runs. "
		 + "So a mismatch FAILS and names the version the environment really reports as actual. If instead the "
		 + "environment answers SUCCESS while still reporting siblings flagged active, or while naming a "
		 + "different member as actual, the call succeeds and says so as a WARNING rather than failing — the "
		 + "write did take effect, so do not retry it; read the family back with describe-business-process "
		 + "before reporting a rollback as done. Activation also "
		 + "re-saves every member of the family in one transaction — a write you did not ask for by name, "
		 + "reported as a warning. "
		 + "ASK FIRST: creating a version and making it actual are separate questions, and the product's own "
		 + "designer asks before making a new version actual. Do not call this on your own initiative after "
		 + "modify-business-process-as-new-version — call it because the user asked for it. "
		 + "Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment at "
		 + "CrtProcessBuilder 1.4.15.1 or newer, which is where this operation first exists — an older package is "
		 + "refused up front, naming both versions; install or update it with install-process-builder. Use "
		 + "describe-business-process to see the family and which member is active.")]
	public CommandExecutionResult SetActiveProcessVersion(
		[Description("set-active-business-process-version parameters")] [Required]
		SetActiveProcessVersionArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		bool hasName = !string.IsNullOrWhiteSpace(args.VersionName);
		bool hasUid = !string.IsNullOrWhiteSpace(args.VersionUid);
		if (hasName == hasUid) {
			return CommandExecutionResult.FromError(hasName
				? "Provide only one of version-name or version-uid, not both."
				: "one of version-name or version-uid is required.");
		}

		SetActiveProcessVersionOptions options = new() {
			Environment = args.EnvironmentName,
			VersionName = args.VersionName ?? string.Empty,
			VersionUid = args.VersionUid ?? string.Empty
		};
		// Same post-op note as both edit paths: activating a version does not put the environment into a state
		// that needs compiling, and an agent that assumes otherwise runs compile-creatio for nothing (ENG-95706).
		CommandExecutionResult result = InternalExecute<SetActiveProcessVersionCommand>(options);
		if (result.ExitCode != 0) {
			return result;
		}

		return result with {
			Note = string.IsNullOrWhiteSpace(result.Note)
				? CommandExecutionResult.CompileNotRequiredNote
				: result.Note + " " + CommandExecutionResult.CompileNotRequiredNote
		};
	}
}

/// <summary>
/// MCP arguments for the <c>set-active-business-process-version</c> tool (kebab-case wire keys, repo convention).
/// Provide exactly one of <c>version-name</c> / <c>version-uid</c>.
/// </summary>
public sealed record SetActiveProcessVersionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("version-name")]
	[property: Description(
		"Code (schema Name) of the VERSION to make actual; provide exactly one of version-name or version-uid.")]
	string? VersionName = null,

	[property: JsonPropertyName("version-uid")]
	[property: Description(
		"Schema UId of the VERSION to make actual; provide exactly one of version-name or version-uid.")]
	string? VersionUid = null);
