using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>
/// The outcome vocabulary of <c>modify-dynamic-case-as-new-version</c>: the server's <c>ModifyCaseAsNewVersion</c>
/// outcomes passed through. <see cref="Unknown"/> is also clio's, for a write whose reply it could not read.
/// </summary>
public static class DynamicCaseVersionOutcomes {
	/// <summary>A dry run: every check passed and nothing was written; the version block is the plan.</summary>
	public const string Planned = DynamicCaseOutcomes.Planned;

	/// <summary>The version was saved, inactive.</summary>
	public const string VersionCreated = "version-created";

	/// <summary>A check refused the call before anything was written.</summary>
	public const string Refused = DynamicCaseOutcomes.Refused;

	/// <summary>The save failed and nothing of the version stays.</summary>
	public const string FailedRolledBack = DynamicCaseOutcomes.FailedRolledBack;

	/// <summary>The version was saved and stays, but the call failed after the save; see the leftovers.</summary>
	public const string FailedLeftover = DynamicCaseOutcomes.FailedLeftover;

	/// <summary>No readable answer came back after a write, so whether a version was saved is not known.</summary>
	public const string Unknown = DynamicCaseOutcomes.Unknown;

	/// <summary>Whether an outcome counts as a success of the call (exit code 0).</summary>
	/// <param name="outcome">One of the outcome constants.</param>
	public static bool IsSuccess(string outcome) => outcome is Planned or VersionCreated;

	/// <summary>Whether clio knows what the outcome means; anything else on a write is read as <see cref="Unknown"/>.</summary>
	/// <param name="outcome">The outcome the server answered.</param>
	public static bool IsKnown(string outcome) =>
		outcome is Planned or VersionCreated or Refused or FailedRolledBack or FailedLeftover or Unknown;
}

/// <summary>
/// The outcome vocabulary of <c>set-active-dynamic-case-version</c>: the server's <c>SetActiveCaseVersion</c>
/// outcomes passed through. <see cref="Unknown"/> is also clio's, for a write whose reply it could not read.
/// </summary>
public static class DynamicCaseActivationOutcomes {
	/// <summary>A dry run: every check passed and nothing was written.</summary>
	public const string Planned = DynamicCaseOutcomes.Planned;

	/// <summary>The member is now the active one, as read back.</summary>
	public const string Activated = "activated";

	/// <summary>The member was already active and no editable sibling was still flagged; nothing was written.</summary>
	public const string Unchanged = DynamicCaseModifyOutcomes.Unchanged;

	/// <summary>A check refused the call before anything was written.</summary>
	public const string Refused = DynamicCaseOutcomes.Refused;

	/// <summary>The switch was written but did not read back as asked; it is not undone, and the call can be
	/// repeated once the cause is fixed.</summary>
	public const string FailedNotRestored = "failed-not-restored";

	/// <summary>No switch; a link row the call added for the member could not be removed again.</summary>
	public const string FailedLeftover = DynamicCaseOutcomes.FailedLeftover;

	/// <summary>No readable answer came back after a write, so whether the switch took is not known.</summary>
	public const string Unknown = DynamicCaseOutcomes.Unknown;

	/// <summary>Whether an outcome counts as a success of the call (exit code 0).</summary>
	/// <param name="outcome">One of the outcome constants.</param>
	public static bool IsSuccess(string outcome) => outcome is Planned or Activated or Unchanged;

	/// <summary>Whether clio knows what the outcome means; anything else on a write is read as <see cref="Unknown"/>.</summary>
	/// <param name="outcome">The outcome the server answered.</param>
	public static bool IsKnown(string outcome) =>
		outcome is Planned or Activated or Unchanged or Refused or FailedNotRestored or FailedLeftover or Unknown;
}

#region Wire shape (ProcessDesignService.ModifyCaseAsNewVersion and SetActiveCaseVersion)

/// <summary>
/// The body of a <c>ModifyCaseAsNewVersion</c> call, sent wrapped under <c>request</c>. One identity names the
/// family's ACTIVE member; a member left <see langword="null"/> keeps the source's value in the version.
/// </summary>
public sealed record ModifyCaseAsNewVersionRequest {
	/// <summary>The case's schema name: the family's active member.</summary>
	[JsonPropertyName("caseName")]
	public string CaseName { get; init; }

	/// <summary>The case's schema UId: the family's active member.</summary>
	[JsonPropertyName("caseUId")]
	public string CaseUId { get; init; }

	/// <summary>The object's schema name, or a caption the package resolves when it names one object; accepted only when it has exactly one case.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>The package the version is saved in; <see langword="null"/> for the source's package.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The whole desired stage list of the version; <see langword="null"/> keeps the source's stages.</summary>
	[JsonPropertyName("stages")]
	public IReadOnlyList<CaseStageEditRequest> Stages { get; init; }

	/// <summary>The version's caption; <see langword="null"/> keeps the source's.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>The version's filter, or its removal (sent as JSON <c>null</c>); <see langword="null"/> keeps the
	/// source's filter.</summary>
	[JsonPropertyName("filter")]
	public CaseFilterPatch Filter { get; init; }

	/// <summary>The user's yes to a version without a stage that records hold; null when not given.</summary>
	[JsonPropertyName("confirmStageRecords")]
	public bool? ConfirmStageRecords { get; init; }

	/// <summary>The user's yes to renaming a stage's lookup value, which acts at once; null when not given.</summary>
	[JsonPropertyName("confirmValueRename")]
	public bool? ConfirmValueRename { get; init; }

	/// <summary>True to run every check and answer the planned version without writing anything.</summary>
	[JsonPropertyName("validateOnly")]
	public bool ValidateOnly { get; init; }
}

/// <summary>A member of a case's version family, as the version operations answer it.</summary>
public sealed class CaseVersionInfo {
	/// <summary>The member's schema name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The member's schema UId.</summary>
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>The stored version number; unique per package within the family, not across packages.</summary>
	[JsonPropertyName("version")]
	public int Version { get; init; }

	/// <summary>The member's package.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The family root's schema name.</summary>
	[JsonPropertyName("rootName")]
	public string RootName { get; init; }

	/// <summary>Whether the bar and new records use this member.</summary>
	[JsonPropertyName("active")]
	public bool Active { get; init; }
}

/// <summary>A member of a case's version family, as <c>DescribeCase</c> lists it under <c>versions</c>.</summary>
public sealed class CaseVersionMemberInfo {
	/// <summary>The member's schema name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The member's schema UId.</summary>
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>The stored version number; unique per package within the family, not across packages.</summary>
	[JsonPropertyName("version")]
	public int Version { get; init; }

	/// <summary>The member's package.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The member's package does not accept changes.</summary>
	[JsonPropertyName("packageReadOnly")]
	public bool PackageReadOnly { get; init; }

	/// <summary>Whether the platform's pick names this member: the one the bar and new records use.</summary>
	[JsonPropertyName("active")]
	public bool Active { get; init; }

	/// <summary>Case instances that follow this member.</summary>
	[JsonPropertyName("runningInstances")]
	public int RunningInstances { get; init; }

	/// <summary>Whether the member has its own link row; the case designer opens only a member that has one.</summary>
	[JsonPropertyName("hasLinkRow")]
	public bool HasLinkRow { get; init; }

	/// <summary>The stored <c>IsDelivered</c>.</summary>
	[JsonPropertyName("isDelivered")]
	public bool IsDelivered { get; init; }
}

/// <summary>The server's <c>ModifyCaseAsNewVersionResult</c>.</summary>
public sealed class ModifyCaseAsNewVersionResponse {
	/// <summary>True for <c>planned</c> and <c>version-created</c>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The server outcome; see <see cref="DynamicCaseVersionOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The new version, or the planned one on a dry run; always inactive.</summary>
	[JsonPropertyName("version")]
	public CaseVersionInfo Version { get; init; }

	/// <summary>The version as a case: its stages, caption and filter.</summary>
	[JsonPropertyName("case")]
	public BuildCaseCaseInfo Case { get; init; }

	/// <summary>The member the version was copied from: the family's active one.</summary>
	[JsonPropertyName("sourceName")]
	public string SourceName { get; init; }

	/// <summary>The change against the source.</summary>
	[JsonPropertyName("diff")]
	public DynamicCaseDiff Diff { get; init; }

	/// <summary>The version's link row and its binding into the version's package.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>What stays after a failure, to remove or review by hand.</summary>
	[JsonPropertyName("leftovers")]
	public List<string> Leftovers { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

/// <summary>The body of a <c>SetActiveCaseVersion</c> call, sent wrapped under <c>request</c>; one identity is named.</summary>
public sealed record SetActiveCaseVersionRequest {
	/// <summary>The member to activate, by schema name.</summary>
	[JsonPropertyName("versionName")]
	public string VersionName { get; init; }

	/// <summary>The member to activate, by schema UId.</summary>
	[JsonPropertyName("versionUId")]
	public string VersionUId { get; init; }

	/// <summary>True to run every check and answer the planned switch without writing anything.</summary>
	[JsonPropertyName("validateOnly")]
	public bool ValidateOnly { get; init; }
}

/// <summary>The server's <c>SetActiveCaseVersionResult</c>.</summary>
public sealed class SetActiveCaseVersionResponse {
	/// <summary>True for <c>planned</c>, <c>activated</c> and <c>unchanged</c>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The server outcome; see <see cref="DynamicCaseActivationOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The member the bar and new records use after the call, as read back.</summary>
	[JsonPropertyName("activeVersion")]
	public CaseVersionInfo ActiveVersion { get; init; }

	/// <summary>The member that was active before the call.</summary>
	[JsonPropertyName("previousActiveVersion")]
	public CaseVersionInfo PreviousActiveVersion { get; init; }

	/// <summary>Members in editable packages, other than the target, whose stored flag still reads active.</summary>
	[JsonPropertyName("siblingsStillFlagged")]
	public List<string> SiblingsStillFlagged { get; init; }

	/// <summary>Members in packages that do not accept changes, still flagged active: package position decides.</summary>
	[JsonPropertyName("decidedByPackagePosition")]
	public List<string> DecidedByPackagePosition { get; init; }

	/// <summary>The link row added for a member that had none; absent when it had one.</summary>
	[JsonPropertyName("linkRow")]
	public DynamicCaseSettings LinkRow { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>A link row this call added and could not remove again.</summary>
	[JsonPropertyName("leftovers")]
	public List<string> Leftovers { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

#endregion

#region Result shape (modify-dynamic-case-as-new-version, set-active-dynamic-case-version, describe versions)

/// <summary>A member of a case's version family in a tool's result: the server's block plus the designer link.</summary>
public sealed class DynamicCaseVersion {
	/// <summary>The member's schema name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The member's schema UId.</summary>
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>The stored version number; unique per package within the family, not across packages.</summary>
	[JsonPropertyName("version")]
	public int Version { get; init; }

	/// <summary>The member's package.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The family root's schema name.</summary>
	[JsonPropertyName("rootName")]
	public string RootName { get; init; }

	/// <summary>Whether the bar and new records use this member.</summary>
	[JsonPropertyName("active")]
	public bool Active { get; init; }

	/// <summary>The case designer link; only for a member that exists.</summary>
	[JsonPropertyName("designerUrl")]
	public string DesignerUrl { get; init; }
}

/// <summary>A member of a case's version family in <c>describe-dynamic-case</c>'s <c>versions</c>.</summary>
public sealed class DynamicCaseVersionMember {
	/// <summary>The member's schema name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The member's schema UId.</summary>
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>The stored version number; unique per package within the family, not across packages.</summary>
	[JsonPropertyName("version")]
	public int Version { get; init; }

	/// <summary>The member's package.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The member's package does not accept changes.</summary>
	[JsonPropertyName("packageReadOnly")]
	public bool PackageReadOnly { get; init; }

	/// <summary>Whether the platform's pick names this member: the one the bar and new records use.</summary>
	[JsonPropertyName("active")]
	public bool Active { get; init; }

	/// <summary>Case instances that follow this member.</summary>
	[JsonPropertyName("runningInstances")]
	public int RunningInstances { get; init; }

	/// <summary>Whether the member has its own link row; the case designer opens only a member that has one.</summary>
	[JsonPropertyName("hasLinkRow")]
	public bool HasLinkRow { get; init; }

	/// <summary>The stored <c>IsDelivered</c>.</summary>
	[JsonPropertyName("isDelivered")]
	public bool IsDelivered { get; init; }

	/// <summary>The case designer link of this member; absent when it has no link row, which the designer needs.</summary>
	[JsonPropertyName("designerUrl")]
	public string DesignerUrl { get; init; }
}

/// <summary>The result document of <c>modify-dynamic-case-as-new-version</c>.</summary>
public sealed class ModifyDynamicCaseAsNewVersionResult {
	/// <summary>Whether the call succeeded; see <see cref="DynamicCaseVersionOutcomes.IsSuccess"/>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>One of <see cref="DynamicCaseVersionOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The new version, or the planned one on a dry run, with its designer link once it exists.</summary>
	[JsonPropertyName("version")]
	public DynamicCaseVersion Version { get; init; }

	/// <summary>The version as a case: its stages, caption and filter.</summary>
	[JsonPropertyName("case")]
	public DynamicCaseInfo Case { get; init; }

	/// <summary>The member the version was copied from: the family's active one.</summary>
	[JsonPropertyName("sourceName")]
	public string SourceName { get; init; }

	/// <summary>The change against the source.</summary>
	[JsonPropertyName("diff")]
	public DynamicCaseDiff Diff { get; init; }

	/// <summary>The version's link row and its binding into the version's package.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>What stays after a failure.</summary>
	[JsonPropertyName("leftovers")]
	public List<string> Leftovers { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

/// <summary>The result document of <c>set-active-dynamic-case-version</c>.</summary>
public sealed class SetActiveDynamicCaseVersionResult {
	/// <summary>Whether the call succeeded; see <see cref="DynamicCaseActivationOutcomes.IsSuccess"/>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>One of <see cref="DynamicCaseActivationOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The member the bar and new records use after the call, as read back, with its designer link.</summary>
	[JsonPropertyName("activeVersion")]
	public DynamicCaseVersion ActiveVersion { get; init; }

	/// <summary>The member that was active before the call, with its designer link.</summary>
	[JsonPropertyName("previousActiveVersion")]
	public DynamicCaseVersion PreviousActiveVersion { get; init; }

	/// <summary>Members in editable packages, other than the target, whose stored flag still reads active.</summary>
	[JsonPropertyName("siblingsStillFlagged")]
	public List<string> SiblingsStillFlagged { get; init; }

	/// <summary>Members in packages that do not accept changes, still flagged active: package position decides.</summary>
	[JsonPropertyName("decidedByPackagePosition")]
	public List<string> DecidedByPackagePosition { get; init; }

	/// <summary>The link row added for a member that had none; absent when it had one.</summary>
	[JsonPropertyName("linkRow")]
	public DynamicCaseSettings LinkRow { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>A link row this call added and could not remove again.</summary>
	[JsonPropertyName("leftovers")]
	public List<string> Leftovers { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

#endregion
