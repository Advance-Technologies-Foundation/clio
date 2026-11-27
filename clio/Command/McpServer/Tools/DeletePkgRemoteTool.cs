using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.PackageCommand;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>delete-pkg-remote</c> command: the destructive counterpart of
/// <c>create-package</c>.
/// </summary>
public sealed class DeletePkgRemoteTool(
	DeletePackageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<DeletePkgOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for deleting a package from an environment.
	/// </summary>
	internal const string DeletePkgRemoteToolName = "delete-pkg-remote";

	/// <summary>
	/// Outcome: the package was deleted and the read-back confirmed it is gone from the environment.
	/// </summary>
	internal const string StatusDeleted = "deleted";

	/// <summary>
	/// Outcome: the delete call completed, but the read-back could not run, so the absence is unconfirmed.
	/// </summary>
	internal const string StatusDeleteUnverified = "delete-unverified";

	/// <summary>
	/// Outcome: the environment refused the delete, or the package is still listed after the call.
	/// </summary>
	internal const string StatusDeleteFailed = "delete-failed";

	/// <summary>
	/// Outcome: no package with that name is installed.
	/// </summary>
	internal const string StatusNotFound = "not-found";

	/// <summary>
	/// Outcome: the package is foreign to the environment — its maintainer differs from the environment's,
	/// or it was installed from a repository — and is not one this tool deletes.
	/// </summary>
	internal const string StatusLocked = "locked";

	/// <summary>
	/// Outcome: other installed packages depend on it, so it cannot be deleted.
	/// </summary>
	internal const string StatusBlockedByDependents = "blocked-by-dependents";

	/// <summary>
	/// Outcome: the environment could not be read well enough to decide, so nothing was deleted.
	/// </summary>
	internal const string StatusUnavailable = "unavailable";

	/// <summary>
	/// Deletes a package from a registered Creatio environment and verifies the absence.
	/// </summary>
	/// <remarks>
	/// Three phases, each under the per-tenant lock: a read-only pre-flight that turns Creatio's refusals into
	/// fields of the response (a refusal the environment answers only in prose); the delete, declared the
	/// sole write of the call; and the read-back the response promises — whether the package is still in
	/// <c>SysPackage</c>.
	/// </remarks>
	[McpServerTool(Name = DeletePkgRemoteToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Deletes a package.
				 It is the destructive counterpart of create-package and the same operation as deleting a package in the
				 Configuration section (AppInstallerService.svc/DeletePackage). Use it to remove a scratch
				 package once its schemas are moved or discarded, so the environment does not keep it.

				 It refuses, and deletes nothing, when the package does not exist (status not-found), is foreign to the
				 environment (status locked) — its maintainer differs from the environment's, or it was installed from a
				 repository — or other installed packages
				 depend on it (status blocked-by-dependents, with the dependents named); an environment it
				 cannot read refuses as unavailable rather than deleting blind.

				 On success it first reports what the package holds — schemas, data bindings and install-time
				 SQL scripts, as counts with the first names listed — then deletes it and verifies the absence
				 by reading SysPackage again. success is true only when that read-back confirms the package is
				 gone; with success=false and deleted=null the delete call itself could not be confirmed, so
				 check list-packages before retrying.
				 """)]
	public DeletePkgRemoteResponse DeletePkgRemote(
		[Description("Parameters: environment-name, package-name (both required).")]
		[Required] DeletePkgRemoteArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return Failure(args.PackageName, "environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return Failure(args.EnvironmentName, "package-name is required and cannot be empty.");
		}
		DeletePkgOptions options = new() {
			Environment = args.EnvironmentName,
			Name = args.PackageName
		};

		// Phase 1: the pre-flight. Reads only; its refusals are fields of the response, not the environment's
		// prose, so the caller can branch on them.
		PackageDeletionAssessment assessment;
		try {
			assessment = ExecuteWithCleanLog(options,
				() => ResolveFromCallContainer<IPackageDeletionPreflight>(options).Assess(options.Name));
		} catch (Exception exception) {
			return Failure(options.Name, SensitiveErrorTextRedactor.Redact(exception.Message), StatusUnavailable);
		}

		switch (assessment.Status) {
			case PackageDeletionStatus.NotFound:
				return new DeletePkgRemoteResponse {
					Success = false,
					Status = StatusNotFound,
					PackageName = options.Name,
					Deleted = false,
					Error = $"No package named \"{options.Name}\" is installed in this environment; "
						+ "nothing was deleted."
				};
			case PackageDeletionStatus.Locked:
				return new DeletePkgRemoteResponse {
					Success = false,
					Status = StatusLocked,
					PackageName = options.Name,
					PackageUId = assessment.PackageUId,
					Deleted = false,
					Error = "The package is foreign to the environment — its maintainer differs from the environment's "
						+ "maintainer, or it was installed from a repository — so it is not one this tool deletes; "
						+ "nothing was deleted."
				};
			case PackageDeletionStatus.BlockedByDependents:
				return new DeletePkgRemoteResponse {
					Success = false,
					Status = StatusBlockedByDependents,
					PackageName = options.Name,
					PackageUId = assessment.PackageUId,
					Deleted = false,
					Dependents = assessment.Dependents,
					Error = "Other installed packages depend on it; nothing was deleted. Remove their dependency "
						+ "on it or delete them first."
				};
			case PackageDeletionStatus.Unavailable:
				return new DeletePkgRemoteResponse {
					Success = false,
					Status = StatusUnavailable,
					PackageName = options.Name,
					Error = assessment.Error
				};
		}

		// Phase 2: the delete. One write, declared the sole write of the call, so a site refusal of it can
		// prove the call applied nothing.
		CommandExecutionResult execution =
			InternalExecute<DeletePackageCommand>(options, soleWriteCall: true);

		// Phase 3: the read-back the response promises — is it still in SysPackage? A failed read is
		// "unverified", never "deleted".
		bool? stillExists;
		try {
			stillExists = ExecuteWithCleanLog(options,
				() => ResolveFromCallContainer<IPackageDeletionPreflight>(options).ProbeExists(options.Name));
		} catch (Exception exception) {
			logger.WriteDebug($"Package deletion read-back resolution failed: {exception.Message}");
			stillExists = null;
		}

		bool deleteCallSucceeded = execution.ExitCode == 0;
		string status;
		bool? deleted;
		string error = null;
		if (deleteCallSucceeded && stillExists == false) {
			status = StatusDeleted;
			deleted = true;
		} else if (deleteCallSucceeded && stillExists == null) {
			status = StatusDeleteUnverified;
			deleted = null;
			error = "The delete call completed but the read-back could not confirm the package is gone; check "
				+ "list-packages before retrying.";
		} else if (deleteCallSucceeded) {
			status = StatusDeleteFailed;
			deleted = false;
			error = "The environment still lists the package after the delete call; nothing was confirmed "
				+ "deleted.";
		} else {
			status = StatusDeleteFailed;
			deleted = false;
			error = stillExists == true
				? "The environment refused the delete; the package is still installed."
				: stillExists == false
					? "The delete call failed, but the read-back found the package gone; check list-packages "
					  + "before retrying."
					: "The delete call failed and the read-back could not run; nothing was confirmed deleted.";
		}

		return new DeletePkgRemoteResponse {
			Success = status == StatusDeleted,
			Status = status,
			PackageName = options.Name,
			PackageUId = assessment.PackageUId,
			Deleted = deleted,
			Inventory = assessment.Inventory,
			Warning = assessment.Warning,
			Error = error,
			SiteResponse = execution.SiteResponse,
			Diagnostic = execution.Diagnostic
		};
	}

	private static DeletePkgRemoteResponse Failure(string packageName, string error, string status = null) => new() {
		Success = false,
		Status = status,
		PackageName = string.IsNullOrWhiteSpace(packageName) ? null : packageName,
		Error = error
	};
}

/// <summary>
/// MCP arguments for the <c>delete-pkg-remote</c> tool.
/// </summary>
public sealed record DeletePkgRemoteArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Name of the package to delete, exactly as list-packages reports it")]
	[property: Required]
	string PackageName
);

/// <summary>
/// Structured result of the <c>delete-pkg-remote</c> tool: the refusal, when one happened, otherwise the
/// deleted inventory and the verification of the absence.
/// </summary>
public sealed record DeletePkgRemoteResponse : ISiteResponseCarrier {

	/// <summary>
	/// Gets whether the package was deleted AND the read-back confirmed it is gone.
	/// </summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets the outcome kind: deleted, delete-unverified, delete-failed, not-found, locked,
	/// blocked-by-dependents or unavailable.
	/// </summary>
	[JsonPropertyName("status")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Status { get; init; }

	/// <summary>Gets the package name the caller asked for.</summary>
	[JsonPropertyName("package-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageName { get; init; }

	/// <summary>
	/// Gets the package identifier as read before the delete; omitted when it could not be read.
	/// </summary>
	[JsonPropertyName("package-uid")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageUId { get; init; }

	/// <summary>
	/// Gets whether this call deleted the package: <see langword="true"/> only when the delete completed and the
	/// read-back confirmed the absence; <see langword="false"/> when the call deleted nothing (a refusal, or the
	/// package is still listed after the call); omitted when the outcome could not be established (the environment
	/// could not be read, or the read-back could not run after the call was sent).
	/// </summary>
	[JsonPropertyName("deleted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Deleted { get; init; }

	/// <summary>
	/// Gets the installed packages that depend on it; present when the status is blocked-by-dependents.
	/// </summary>
	[JsonPropertyName("dependents")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Dependents { get; init; }

	/// <summary>
	/// Gets what the package held when the delete was considered; present once the pre-flight passed.
	/// </summary>
	[JsonPropertyName("inventory")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PackageDeletionInventory Inventory { get; init; }

	/// <summary>
	/// Gets which inventory read failed, so a zero count reads as "not read"; present when the inventory is
	/// partial.
	/// </summary>
	[JsonPropertyName("warning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Warning { get; init; }

	/// <summary>Gets the failure detail; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>
	/// What the Creatio site itself answered to the delete call that failed; absent on success, when no HTTP
	/// response exists, and when the failure is clio's own.
	/// </summary>
	[JsonPropertyName("site-response")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SiteResponse SiteResponse { get; set; }

	/// <summary>
	/// Beside <see cref="SiteResponse"/>: whether the site's refusal proves the delete applied nothing
	/// (<c>side-effect</c> <c>none</c>) or not (<c>unknown</c>). Absent whenever <see cref="SiteResponse"/> is.
	/// </summary>
	[JsonPropertyName("diagnostic")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DataWriteDiagnostic Diagnostic { get; set; }

	/// <summary>
	/// Implements <see cref="ISiteResponseCarrier.ReportsFailure"/>: a failed delete reports the failure.
	/// </summary>
	bool ISiteResponseCarrier.ReportsFailure => !Success;
}
