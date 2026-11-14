using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>
/// The outcome vocabulary of <c>create-dynamic-case</c>. The two <c>created-page-*</c> outcomes and
/// <see cref="Unknown"/> are clio's, because only clio writes the page and only clio can fail to read a reply; the
/// rest are the server's <c>BuildCase</c> outcomes passed through.
/// </summary>
public static class DynamicCaseOutcomes {
	/// <summary>A dry run: every check passed and nothing was written.</summary>
	public const string Planned = "planned";

	/// <summary>The case was written, and the progress bar too when it was asked for.</summary>
	public const string Created = "created";

	/// <summary>The case was written and the page was left alone because <c>add-progress-bar</c> was false.</summary>
	public const string CreatedPageSkipped = "created-page-skipped";

	/// <summary>The case was written and kept, but the progress bar could not be added.</summary>
	public const string CreatedPageFailed = "created-page-failed";

	/// <summary>An equivalent enabled case already exists; nothing was written to it.</summary>
	public const string AlreadyPresent = "already-present";

	/// <summary>A check refused the call before anything was written to the case.</summary>
	public const string Refused = "refused";

	/// <summary>The build failed after a write and the server undid it.</summary>
	public const string FailedRolledBack = "failed-rolled-back";

	/// <summary>The build failed after a write and the server could not undo all of it.</summary>
	public const string FailedLeftover = "failed-leftover";

	/// <summary>The build pass was sent and no readable answer came back, so whether it wrote is not known.</summary>
	public const string Unknown = "unknown";

	/// <summary>Whether an outcome counts as a success of the call (exit code 0).</summary>
	/// <param name="outcome">One of the outcome constants.</param>
	public static bool IsSuccess(string outcome) =>
		outcome is Planned or Created or CreatedPageSkipped or AlreadyPresent;
}

/// <summary>
/// The error codes clio adds to the server's <c>BuildCase</c> vocabulary: page refusals and the transport faults only
/// clio can observe. A code is never renamed, only added, because the guidance names each with its remedy.
/// </summary>
public static class DynamicCaseErrorCodes {
	/// <summary>The environment answered the route with a 404: the installed package has no <c>BuildCase</c>.</summary>
	public const string ProcessBuilderWithoutBuildCase = "process-builder-without-build-case";

	/// <summary>
	/// The environment answered <c>DescribeCase</c> or <c>ModifyCase</c> with a 404: the installed package does not
	/// carry the operations that read and change an existing case.
	/// </summary>
	public const string ProcessBuilderWithoutCaseChange = "process-builder-without-case-change";

	/// <summary>
	/// A <c>BuildCase</c> pass could not be sent or its answer could not be read: on the validate pass nothing was
	/// written; on the build pass the outcome is <c>unknown</c>.
	/// </summary>
	public const string RequestFailed = "request-failed";

	/// <summary>Adding a missing package dependency failed with a storage validation error (usually a cycle).</summary>
	public const string DependencyCycle = "dependency-cycle";

	/// <summary>Adding a missing package dependency failed for a reason other than a storage validation error.</summary>
	public const string DependencyAddFailed = "dependency-add-failed";

	/// <summary>No Freedom UI record page could be found for the object.</summary>
	public const string RecordPageNotFound = "record-page-not-found";

	/// <summary>The object has only typed record pages, so the target page has to be named.</summary>
	public const string RecordPageAmbiguous = "record-page-ambiguous";

	/// <summary>The object's record page is a Classic page.</summary>
	public const string RecordPageNotFreedom = "record-page-not-freedom";

	/// <summary>The page's primary data source is not the case object.</summary>
	public const string PageEntityMismatch = "page-entity-mismatch";

	/// <summary>The page has no <c>MainContainer</c> flex container to put the bar in.</summary>
	public const string PlacementUnsupported = "placement-unsupported";

	/// <summary>
	/// The dry run of the exact page write (append, or replace for a full-form page) failed one of the page save's own
	/// gates, or a full-form page could not take the bar.
	/// </summary>
	public const string PageAppendRejected = "page-append-rejected";
}

#region Wire shape (ProcessDesignService.BuildCase)

/// <summary>
/// One stage item as a tool call named it, normalized: the members a call left out are <see langword="null"/>, and
/// <see cref="GroupSent"/> keeps an explicit <c>"group": null</c> apart from an absent key.
/// </summary>
/// <param name="Value">The lookup value: its Id or exact display value.</param>
public sealed record DynamicCaseStageItem(string Value) {
	/// <summary>The stage caption in the case; <see langword="null"/> when not named.</summary>
	public string Caption { get; init; }

	/// <summary>The stage colour, <c>#RRGGBB</c>; <see langword="null"/> when not named.</summary>
	public string Color { get; init; }

	/// <summary>
	/// Whether the call carried a <c>group</c> key at all, <c>null</c> included: on a change an explicit null takes
	/// the stage out of its group, while a left-out key keeps the stored group.
	/// </summary>
	public bool GroupSent { get; init; }

	/// <summary>The parent stage's lookup value (Id or exact display value); <see langword="null"/> for none.</summary>
	public string Group { get; init; }

	/// <summary>False for a final stage that ends the case unsuccessfully; <see langword="null"/> when not named.</summary>
	public bool? Successful { get; init; }
}

/// <summary>
/// A case filter as a tool call named it, normalized: a lookup column of the object and the values whose records start
/// the case, or its empty value.
/// </summary>
/// <param name="Column">The filter column.</param>
/// <param name="Values">The values by Id or exact display value; empty when <paramref name="Empty"/>.</param>
/// <param name="Empty">True for the case of records whose filter column is empty.</param>
public sealed record DynamicCaseFilterArgument(string Column, IReadOnlyList<string> Values, bool Empty);

/// <summary>A case filter on the wire; the server refuses an undeclared key, so an unset member is not written.</summary>
public sealed record BuildCaseFilterRequest {
	/// <summary>The filter column.</summary>
	[JsonPropertyName("column")]
	public string Column { get; init; }

	/// <summary>The values whose records start the case; <see langword="null"/> for an empty-value case.</summary>
	[JsonPropertyName("values")]
	public IReadOnlyList<string> Values { get; init; }

	/// <summary>True for the case of records whose filter column is empty; <see langword="null"/> otherwise.</summary>
	[JsonPropertyName("empty")]
	public bool? Empty { get; init; }

	/// <summary>The wire shape of a normalized filter argument.</summary>
	/// <param name="filter">The argument; <see langword="null"/> for none.</param>
	public static BuildCaseFilterRequest From(DynamicCaseFilterArgument filter) =>
		filter is null
			? null
			: new BuildCaseFilterRequest {
				Column = filter.Column,
				Values = filter.Empty ? null : filter.Values,
				Empty = filter.Empty ? true : null
			};
}

/// <summary>One stage of an explicit order on the wire: the lookup value's Id or exact display value.</summary>
/// <param name="Value">The lookup value.</param>
public sealed record BuildCaseStageRequest([property: JsonPropertyName("value")] string Value) {
	/// <summary>
	/// The parent stage's lookup value, for a stage that joins a stage group; omitted when <see langword="null"/>,
	/// which a build reads as no group.
	/// </summary>
	[JsonPropertyName("group")]
	public string Group { get; init; }

	/// <summary>False for a final stage that ends the case unsuccessfully; omitted when not named.</summary>
	[JsonPropertyName("successful")]
	public bool? Successful { get; init; }
}

/// <summary>
/// The body of a <c>BuildCase</c> pass, sent wrapped under <c>request</c>. The server refuses an undeclared key, so
/// only the members its contract declares are written.
/// </summary>
public sealed record BuildCaseRequest {
	/// <summary>The object the case belongs to.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>The lookup column whose values become the stages.</summary>
	[JsonPropertyName("stageColumnName")]
	public string StageColumnName { get; init; }

	/// <summary>The editable package that receives the case and its settings bindings.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The case's schema name, or <see langword="null"/> for the server's default.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The case's caption, or <see langword="null"/> for the server's default.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>An integer column of the lookup that orders the stages.</summary>
	[JsonPropertyName("orderColumn")]
	public string OrderColumn { get; init; }

	/// <summary>The case's stages in order (any 2 to 20 lookup values), or <see langword="null"/> for every value.</summary>
	[JsonPropertyName("stages")]
	public IReadOnlyList<BuildCaseStageRequest> Stages { get; init; }

	/// <summary>The case filter, or <see langword="null"/> for a case without one.</summary>
	[JsonPropertyName("filter")]
	public BuildCaseFilterRequest Filter { get; init; }

	/// <summary>
	/// True to disable the object's enabled cases without a filter; <see langword="null"/> otherwise, so a call without
	/// it reads the same to every package that carries <c>BuildCase</c>.
	/// </summary>
	[JsonPropertyName("deactivateUnfiltered")]
	public bool? DeactivateUnfiltered { get; init; }

	/// <summary>True to answer the missing package dependencies instead of refusing on them.</summary>
	[JsonPropertyName("allowMissingDependencies")]
	public bool AllowMissingDependencies { get; init; }

	/// <summary>True to run every check and answer the plan without writing anything.</summary>
	[JsonPropertyName("validateOnly")]
	public bool ValidateOnly { get; init; }
}

/// <summary>One stage as the server wrote or planned it; also the stage shape of the tool's result.</summary>
public sealed class DynamicCaseStage {
	/// <summary>Stage name, <c>Stage1..N</c>.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The stage's own UId, never the lookup value's Id.</summary>
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>The lookup value's Id.</summary>
	[JsonPropertyName("value")]
	public string Value { get; init; }

	/// <summary>The lookup value's display value.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>The stage colour.</summary>
	[JsonPropertyName("color")]
	public string Color { get; init; }

	/// <summary>The lookup value Id of the parent of this stage's group; absent for a stage in no group.</summary>
	[JsonPropertyName("group")]
	public string Group { get; init; }

	/// <summary>False only for a final stage that ends the case unsuccessfully; absent from an older package.</summary>
	[JsonPropertyName("successful")]
	public bool? Successful { get; init; }

	/// <summary>A stage of the final group: a record that reaches it completes the case.</summary>
	[JsonPropertyName("isFinal")]
	public bool? IsFinal { get; init; }

	/// <summary>The stage holds steps (process elements), so removing it is refused.</summary>
	[JsonPropertyName("hasSteps")]
	public bool? HasSteps { get; init; }

	/// <summary>A step condition moves the case to this stage, so removing it is refused.</summary>
	[JsonPropertyName("isConditionTarget")]
	public bool? IsConditionTarget { get; init; }

	/// <summary>Running cases that sit in this stage now; absent where it was not read.</summary>
	[JsonPropertyName("runningInstances")]
	public int? RunningInstances { get; init; }
}

/// <summary>One value of a case filter: the lookup value's Id and the display value the filter stores with it.</summary>
public sealed class DynamicCaseFilterValue {
	/// <summary>The lookup value's Id.</summary>
	[JsonPropertyName("value")]
	public string Value { get; init; }

	/// <summary>The display value.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }
}

/// <summary>
/// A case's filter as stored: which new records start the case and which case the record page's bar shows. The same
/// shape in the server's answers and the tools' results.
/// </summary>
public sealed class DynamicCaseFilter {
	/// <summary>The filter column; absent when the stored filter cannot be read.</summary>
	[JsonPropertyName("column")]
	public string Column { get; init; }

	/// <summary>The values whose records start the case.</summary>
	[JsonPropertyName("values")]
	public List<DynamicCaseFilterValue> Values { get; init; }

	/// <summary>True for the case of records whose filter column is empty.</summary>
	[JsonPropertyName("empty")]
	public bool? Empty { get; init; }

	/// <summary>True for a filter in a shape clio's tools cannot change (built in the case designer).</summary>
	[JsonPropertyName("unreadable")]
	public bool? Unreadable { get; init; }
}

/// <summary>
/// The case block of the server's answers: <c>case</c> of <c>BuildCase</c> and <c>ModifyCase</c>, and each item of
/// <c>cases</c> of <c>DescribeCase</c>.
/// </summary>
public sealed class BuildCaseCaseInfo {
	/// <summary>
	/// <c>created</c>, <c>already-present</c> or <c>planned</c> for a build; <c>existing</c> for a describe;
	/// <c>modified</c>, <c>unchanged</c> or <c>planned</c> for a change.
	/// </summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>The case's schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>The case's schema UId.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>The case's caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>The package the case lives in.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The object the case belongs to.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>The stage column.</summary>
	[JsonPropertyName("stageColumnName")]
	public string StageColumnName { get; init; }

	/// <summary>The lookup the stage column references.</summary>
	[JsonPropertyName("stageLookupSchemaName")]
	public string StageLookupSchemaName { get; init; }

	/// <summary>Read back after the save.</summary>
	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; }

	/// <summary>Read back after the save.</summary>
	[JsonPropertyName("activeVersion")]
	public bool ActiveVersion { get; init; }

	/// <summary>What decided the stage order.</summary>
	[JsonPropertyName("orderSource")]
	public string OrderSource { get; init; }

	/// <summary>The stages in order.</summary>
	[JsonPropertyName("stages")]
	public List<DynamicCaseStage> Stages { get; init; }

	/// <summary>Directed stage connections.</summary>
	[JsonPropertyName("connectionCount")]
	public int ConnectionCount { get; init; }

	/// <summary>The captions of the final group, the stages that complete the case.</summary>
	[JsonPropertyName("finalStages")]
	public List<string> FinalStages { get; init; }

	/// <summary>Running cases of this case schema; absent where it was not read.</summary>
	[JsonPropertyName("runningInstances")]
	public int? RunningInstances { get; init; }

	/// <summary>The case's package does not accept changes: it can be described, not modified in place.</summary>
	[JsonPropertyName("packageReadOnly")]
	public bool? PackageReadOnly { get; init; }

	/// <summary>Which new records start the case; absent for a case without a filter, which starts for every record.</summary>
	[JsonPropertyName("filter")]
	public DynamicCaseFilter Filter { get; init; }
}

/// <summary>The settings row and the link row a case needs; the same shape in the server answer and the result.</summary>
public sealed class DynamicCaseSettings {
	/// <summary><c>created</c>, <c>reused</c> or <c>planned</c>; the undo order depends on it.</summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>The <c>SysDcmSettings</c> row Id.</summary>
	[JsonPropertyName("sysDcmSettingsId")]
	public string SysDcmSettingsId { get; init; }

	/// <summary>The <c>SysDcmSchemaInSettings</c> row Id.</summary>
	[JsonPropertyName("sysDcmSchemaInSettingsId")]
	public string SysDcmSchemaInSettingsId { get; init; }

	/// <summary>The package-data bindings the call wrote, by name.</summary>
	[JsonPropertyName("bindings")]
	public List<string> Bindings { get; init; }

	/// <summary>The settings row's filter column after the call; absent when it names none.</summary>
	[JsonPropertyName("filterColumnName")]
	public string FilterColumnName { get; init; }

	/// <summary>True when the call sets the filter column on a row that had none (planned, on a dry run).</summary>
	[JsonPropertyName("filterColumnSet")]
	public bool? FilterColumnSet { get; init; }
}

/// <summary>What one written artifact needs before it takes effect.</summary>
public sealed class DynamicCaseArtifactObligation {
	/// <summary><c>dcm</c>, <c>settings-data</c> or <c>page</c>.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; init; }

	/// <summary>Whether the artifact needs a configuration compile.</summary>
	[JsonPropertyName("compile")]
	public bool Compile { get; init; }

	/// <summary>Whether the artifact needs an application restart.</summary>
	[JsonPropertyName("restart")]
	public bool Restart { get; init; }
}

/// <summary>What the caller owes after the call.</summary>
public sealed class DynamicCaseObligations {
	/// <summary>Whether any artifact needs a configuration compile.</summary>
	[JsonPropertyName("compileRequired")]
	public bool CompileRequired { get; init; }

	/// <summary>Whether any artifact needs an application restart.</summary>
	[JsonPropertyName("restartRequired")]
	public bool RestartRequired { get; init; }

	/// <summary>Whether open pages of the object have to be reloaded to show the bar.</summary>
	[JsonPropertyName("reloadOpenPages")]
	public bool ReloadOpenPages { get; init; }

	/// <summary>One entry per artifact.</summary>
	[JsonPropertyName("artifacts")]
	public List<DynamicCaseArtifactObligation> Artifacts { get; init; }
}

/// <summary>The server's <c>BuildCaseResult</c>.</summary>
public sealed class BuildCaseResponse {
	/// <summary>True for <c>planned</c>, <c>created</c> and <c>already-present</c>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The server outcome.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case written, found or planned.</summary>
	[JsonPropertyName("case")]
	public BuildCaseCaseInfo Case { get; init; }

	/// <summary>The settings rows.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>Every package the case's package must reach.</summary>
	[JsonPropertyName("requiredPackages")]
	public List<string> RequiredPackages { get; init; }

	/// <summary>The required packages the case's package does not reach yet.</summary>
	[JsonPropertyName("missingDependencies")]
	public List<string> MissingDependencies { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>The cases without a filter that <c>deactivateUnfiltered</c> disabled (or would, on a dry run).</summary>
	[JsonPropertyName("deactivatedCases")]
	public List<string> DeactivatedCases { get; init; }

	/// <summary>What a failure could not undo, in the order to remove it.</summary>
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

#region Result shape (create-dynamic-case)

/// <summary>The stage connections of a case.</summary>
public sealed class DynamicCaseConnections {
	/// <summary>Always <c>all-to-all</c>: every ordered pair of stages, so every stage is clickable on the bar.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; init; }

	/// <summary>How many directed connections the case has.</summary>
	[JsonPropertyName("count")]
	public int Count { get; init; }
}

/// <summary>The case block of a dynamic-case tool's result: the server's block plus the designer link.</summary>
public sealed class DynamicCaseInfo {
	/// <summary>
	/// <c>created</c>, <c>already-present</c> or <c>planned</c> for a build; <c>existing</c> for a describe;
	/// <c>modified</c>, <c>unchanged</c> or <c>planned</c> for a change.
	/// </summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>The case's schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>The case's schema UId.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>The case's caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>The package the case lives in.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The object the case belongs to.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>The stage column.</summary>
	[JsonPropertyName("stageColumnName")]
	public string StageColumnName { get; init; }

	/// <summary>The lookup the stage column references.</summary>
	[JsonPropertyName("stageLookupSchemaName")]
	public string StageLookupSchemaName { get; init; }

	/// <summary>Read back after the save.</summary>
	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; }

	/// <summary>Read back after the save.</summary>
	[JsonPropertyName("activeVersion")]
	public bool ActiveVersion { get; init; }

	/// <summary>What decided the stage order.</summary>
	[JsonPropertyName("orderSource")]
	public string OrderSource { get; init; }

	/// <summary>The stages in order.</summary>
	[JsonPropertyName("stages")]
	public List<DynamicCaseStage> Stages { get; init; }

	/// <summary>The stage connections.</summary>
	[JsonPropertyName("connections")]
	public DynamicCaseConnections Connections { get; init; }

	/// <summary>The captions of the final group, the stages that complete the case; absent from an older package.</summary>
	[JsonPropertyName("finalStages")]
	public List<string> FinalStages { get; init; }

	/// <summary>Running cases of this case schema; absent where it was not read.</summary>
	[JsonPropertyName("runningInstances")]
	public int? RunningInstances { get; init; }

	/// <summary>The case's package does not accept changes: it can be described, not modified in place.</summary>
	[JsonPropertyName("packageReadOnly")]
	public bool? PackageReadOnly { get; init; }

	/// <summary>Which new records start the case; absent for a case without a filter.</summary>
	[JsonPropertyName("filter")]
	public DynamicCaseFilter Filter { get; init; }

	/// <summary>The case designer link; only for a case that exists.</summary>
	[JsonPropertyName("designerUrl")]
	public string DesignerUrl { get; init; }
}

/// <summary>The package dependencies the case needs and what this call did about them.</summary>
public sealed class DynamicCaseDependencies {
	/// <summary>
	/// <c>none-missing</c>, <c>added</c>, <c>planned</c>, <c>removed-after-failure</c>, <c>left-after-failure</c>,
	/// <c>kept-for-leftovers</c> (the build failed with leftovers that still need them) or <c>missing</c> (not
	/// declared: <c>add-package-dependencies=false</c>, or the add was refused or undone).
	/// </summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>Every package the case's package must reach.</summary>
	[JsonPropertyName("required")]
	public List<string> Required { get; init; }

	/// <summary>The required packages the case's package did not reach when the call started.</summary>
	[JsonPropertyName("missing")]
	public List<string> Missing { get; init; }

	/// <summary>The dependencies this call added; on <c>left-after-failure</c> they are still there.</summary>
	[JsonPropertyName("added")]
	public List<string> Added { get; init; }

	/// <summary>The server asked for a configuration compilation after this call added the dependencies.</summary>
	[JsonPropertyName("compilationRequired")]
	public bool CompilationRequired { get; init; }
}

/// <summary>The exact call that adds the progress bar when this call could not.</summary>
public sealed class DynamicCasePageRetry {
	/// <summary>Always <c>update-page</c>.</summary>
	[JsonPropertyName("tool")]
	public string Tool { get; init; }

	/// <summary>
	/// Its arguments: the fragment with <c>mode: append</c>, or for a page stored in the full form the whole body with
	/// <c>mode: replace</c> and the <c>checksum</c> it was built against.
	/// </summary>
	[JsonPropertyName("args")]
	public Dictionary<string, string> Args { get; init; }
}

/// <summary>A typed record page offered when the target page has to be named.</summary>
public sealed class DynamicCasePageCandidate {
	/// <summary>The page schema name.</summary>
	[JsonPropertyName("pageSchemaName")]
	public string PageSchemaName { get; init; }

	/// <summary>The record type the page opens for.</summary>
	[JsonPropertyName("typeColumnValue")]
	public string TypeColumnValue { get; init; }
}

/// <summary>The record page step of the call.</summary>
public sealed class DynamicCasePageInfo {
	/// <summary><c>planned</c>, <c>inserted</c>, <c>already-present</c>, <c>skipped</c>, <c>failed</c> or <c>refused</c>.</summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>The record page.</summary>
	[JsonPropertyName("pageSchemaName")]
	public string PageSchemaName { get; init; }

	/// <summary><c>explicit</c>, <c>related-page-addon</c> or <c>sys-module-edit</c>.</summary>
	[JsonPropertyName("resolvedBy")]
	public string ResolvedBy { get; init; }

	/// <summary>The design package the bar is written into.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>The nearest template in the page's schema chain.</summary>
	[JsonPropertyName("template")]
	public string Template { get; init; }

	/// <summary>
	/// The marker-wrapped append body with the bar's two items, by the names the write gives them. A page stored in the
	/// full form is written by a replace of its whole body instead; <c>page.retry</c> then carries that body.
	/// </summary>
	[JsonPropertyName("fragment")]
	public string Fragment { get; init; }

	/// <summary>
	/// The bar's mode in <see cref="Fragment"/>: true when a stage click saves the record at once, false when it only
	/// sets the field and the user saves (the bar's default). Absent when this call did not plan the bar.
	/// </summary>
	[JsonPropertyName("saveOnChange")]
	public bool? SaveOnChange { get; init; }

	/// <summary>Why the page step failed or was refused.</summary>
	[JsonPropertyName("error")]
	public string Error { get; init; }

	/// <summary>The typed pages to choose from, on <c>record-page-ambiguous</c>.</summary>
	[JsonPropertyName("candidates")]
	public List<DynamicCasePageCandidate> Candidates { get; init; }

	/// <summary>The exact <c>update-page</c> call; only when the write failed.</summary>
	[JsonPropertyName("retry")]
	public DynamicCasePageRetry Retry { get; init; }
}

/// <summary>The result document of <c>create-dynamic-case</c>.</summary>
public sealed class DynamicCaseResult {
	/// <summary>Whether the call succeeded; see <see cref="DynamicCaseOutcomes.IsSuccess"/>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>One of <see cref="DynamicCaseOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case written, found or planned.</summary>
	[JsonPropertyName("case")]
	public DynamicCaseInfo Case { get; init; }

	/// <summary>The settings rows.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>The package dependencies.</summary>
	[JsonPropertyName("dependencies")]
	public DynamicCaseDependencies Dependencies { get; init; }

	/// <summary>The record page step.</summary>
	[JsonPropertyName("page")]
	public DynamicCasePageInfo Page { get; init; }

	/// <summary>What the caller owes after the call, per artifact.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>The cases without a filter that <c>deactivate-unfiltered</c> disabled (or would, on a dry run).</summary>
	[JsonPropertyName("deactivatedCases")]
	public List<string> DeactivatedCases { get; init; }

	/// <summary>What a failure could not undo.</summary>
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
