using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that makes one member of a dynamic case's (DCM) version family the active one.
/// </summary>
public class SetActiveDynamicCaseVersionTool(
	SetActiveDynamicCaseVersionCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetActiveDynamicCaseVersionOptions>(command, logger, commandResolver) {

	internal const string SetActiveDynamicCaseVersionToolName = "set-active-dynamic-case-version";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused.</summary>
	internal const string ValidArgsHint = "Valid: environment-name, version-name, version-uid, confirm-package-position, dry-run.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>The pseudo-argument the mini-contract names when the call identifies no member.</summary>
	internal const string VersionIdentityArgument = "version-name | version-uid";

	/// <summary>Minimal valid <c>args</c> object, used by the mini-contract: a dry run that writes nothing.</summary>
	internal const string MinimalExample =
		"{\"environment-name\":\"<env>\",\"version-name\":\"UsrOrderCaseCustom1\",\"dry-run\":true}";

	/// <summary>
	/// Activates the named member, or plans it on a dry run, on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="SetActiveDynamicCaseVersionArgs"/>.</param>
	/// <returns>The command execution result; the result document is in the log output.</returns>
	// Destructive=true: the switch changes which case the bar and the new records its filter fits use, and in an
	// enabled family records with no running case in a stage the new active member lacks show no current stage; it
	// also keeps the call out of the read-response deadline, which must never abandon a family mid-switch.
	// Idempotent=true: the same call again answers unchanged.
	[McpServerTool(Name = SetActiveDynamicCaseVersionToolName, ReadOnly = false, Destructive = true,
		 Idempotent = true, OpenWorld = false),
	 Description("Activates a case version. Read get-guidance name=process-dynamic-case-versions first. Makes one member of a dynamic case (DCM) version family the "
		 + "active one, the family root included (that is how you go back). In an enabled family the version becomes "
		 + "the case of the records its filter fits (every record when it has none): such a NEW record starts it, and "
		 + "one with no running case shows it at once; a running case keeps its version while it runs, and Change "
		 + "case on the record's page cancels it and starts the enabled case the record fits, if any, writing that "
		 + "case's first stage into the record (from this family, the member active at that moment: the previous "
		 + "one before the switch, this version after it). In a disabled family, a record "
		 + "without a running case shows no bar of it, and Change case only cancels its case and starts an enabled "
		 + "case the record fits, if any, never this family's version. A disabled family starts none of its versions "
		 + "until it is enabled; enabling is the user's own decision, never part of the yes to the switch: the note "
		 + "gives the user's steps and says whether enabling also needs confirm-running-instances. Open pages need "
		 + "F5. Run it "
		 + "with dry-run=true first. A user who asked for this switch has given the yes: with notes only (records in "
		 + "stages the version lacks, a disabled family), switch and relay every note in full with the result; with warnings "
		 + "(package position), show them and wait for their yes; the switch itself is then refused until "
		 + "confirm-package-position=true, sent only after that yes. Never call it on your own after "
		 + "modify-dynamic-case-as-new-version: ask the user whether to activate. Name the member by version-name or version-uid (describe-dynamic-case lists versions). The "
		 + "result is READ BACK: failed-not-restored names the member really active and is never undone - the same "
		 + "call can be repeated once the cause is fixed; decidedByPackagePosition names members in packages that do "
		 + "not accept changes, which the switch cannot write. A member without its own link row gets one first, so "
		 + "the case designer can open it. It deletes nothing. Activating the active member again answers unchanged once no other editable member is "
		 + "still flagged active. "
		 + $"No compile, no restart. Requires CrtProcessBuilder {DynamicCaseChangeRequirement.MinimumProcessBuilderVersion} or later (install-process-builder, after "
		 + "user approval).")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult SetActiveDynamicCaseVersion(
		[Description("set-active-dynamic-case-version parameters")] [Required] SetActiveDynamicCaseVersionArgs args) {
		if (args is null) {
			return CommandExecutionResult.FromValidationError(NullArgsError);
		}

		// The only unknown-key defence a long-tail tool has; McpToolArgumentSupport.BuildUnknownArgumentError says why.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return CommandExecutionResult.FromValidationError(argumentError);
		}

		bool hasName = !string.IsNullOrWhiteSpace(args.VersionName);
		bool hasUId = !string.IsNullOrWhiteSpace(args.VersionUId);
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			missing.Add("environment-name");
		}
		if (!hasName && !hasUId) {
			missing.Add(VersionIdentityArgument);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromError("environment-name is required and cannot be empty."),
				missing, ForSetActiveDynamicCaseVersion);
		}
		if (missing.Count > 0) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromValidationError(
					"One of version-name or version-uid is required: name the member to activate."),
				missing, ForSetActiveDynamicCaseVersion);
		}
		if (hasName && hasUId) {
			return DynamicCaseStageArguments.Refuse(
				"Provide only one of version-name or version-uid, not both.",
				DynamicCaseStageArguments.InvalidRequestCode);
		}

		SetActiveDynamicCaseVersionOptions options = new() {
			Environment = args.EnvironmentName,
			VersionName = args.VersionName?.Trim(),
			VersionUId = args.VersionUId?.Trim(),
			DryRun = args.DryRun ?? false,
			ConfirmPackagePosition = args.ConfirmPackagePosition ?? false
		};
		// A case is interpreted, so the deterministic note can never contradict the response; it rides success only,
		// like on every other process-designer write.
		CommandExecutionResult result = InternalExecute<SetActiveDynamicCaseVersionCommand>(options);
		return result.ExitCode != 0 ? result : result.WithCompileNotRequiredNote();
	}

	/// <summary>The mini-contract returned when a required argument is missing.</summary>
	/// <param name="missing">The required arguments the call lacked.</param>
	internal static string ForSetActiveDynamicCaseVersion(IReadOnlyList<string> missing) => ToolMiniContract.Build(
		SetActiveDynamicCaseVersionToolName,
		missing,
		[
			"environment-name (registered environment)",
			"version-name or version-uid (the member of the case's version family to activate, the root included)",
			"optional: confirm-package-position, dry-run (see the tool description)"
		],
		MinimalExample);
}

/// <summary>
/// MCP arguments for the <c>set-active-dynamic-case-version</c> tool (kebab-case wire keys, repo convention). Provide
/// exactly one of <c>version-name</c> / <c>version-uid</c>.
/// </summary>
public sealed record SetActiveDynamicCaseVersionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("version-name")]
	[property: Description("Schema name of the family member to activate, the root included; provide exactly one of "
		+ "version-name or version-uid.")]
	string VersionName = null,

	[property: JsonPropertyName("version-uid")]
	[property: Description("Schema UId of the family member to activate, the root included; provide exactly one of "
		+ "version-name or version-uid.")]
	string VersionUId = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("Run every check and return the planned switch and its notes without writing anything; "
		+ "default false.")]
	bool? DryRun = null,

	[property: JsonPropertyName("confirm-package-position")]
	[property: Description("The user's yes to switch although a member in a package that does not accept changes "
		+ "may outrank the target (the dry run's package-position warning; refused "
		+ "package-position-confirmation-required without it): new records its filter fits may then keep starting "
		+ "that member. "
		+ "Pass true ONLY after the user agreed, never on your own; default false.")]
	bool? ConfirmPackagePosition = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
