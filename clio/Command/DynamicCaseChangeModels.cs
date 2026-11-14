using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>The package floor of <c>describe-dynamic-case</c> and <c>modify-dynamic-case</c>.</summary>
public static class DynamicCaseChangeRequirement {
	/// <summary>The first CrtProcessBuilder archive whose <c>DescribeCase</c> and <c>ModifyCase</c> carry case filters.</summary>
	/// <remarks>
	/// The one place the floor of both tools is written. A version literal rather than presence: the two are
	/// operations a package before 1.6.6.173 does not carry at all, so presence alone would let a call reach a package
	/// that answers its route with a 404. 1.6.6.178 is the first cut that reads and reports a case's filter.
	/// </remarks>
	public const string MinimumProcessBuilderVersion = "1.6.6.178";
}

/// <summary>
/// The outcome vocabulary of <c>modify-dynamic-case</c>: the server's <c>ModifyCase</c> outcomes passed through,
/// plus <see cref="Unknown"/>, which is clio's, because only clio can fail to read a reply.
/// </summary>
public static class DynamicCaseModifyOutcomes {
	/// <summary>A dry run: every check passed and nothing was written; the diff is the plan.</summary>
	public const string Planned = DynamicCaseOutcomes.Planned;

	/// <summary>The case was changed in place.</summary>
	public const string Modified = "modified";

	/// <summary>The case already matches the request; nothing was written.</summary>
	public const string Unchanged = "unchanged";

	/// <summary>A check refused the call before anything was written.</summary>
	public const string Refused = DynamicCaseOutcomes.Refused;

	/// <summary>The save failed its read-back and the server restored the case as it was.</summary>
	public const string FailedRestored = "failed-restored";

	/// <summary>The save failed and the server could not restore all of it.</summary>
	public const string FailedLeftover = DynamicCaseOutcomes.FailedLeftover;

	/// <summary>A write was sent and no readable answer came back, so whether it wrote is not known.</summary>
	public const string Unknown = DynamicCaseOutcomes.Unknown;

	/// <summary>Whether an outcome counts as a success of the call (exit code 0).</summary>
	/// <param name="outcome">One of the outcome constants.</param>
	public static bool IsSuccess(string outcome) => outcome is Planned or Modified or Unchanged;
}

#region Wire shape (ProcessDesignService.DescribeCase and ModifyCase)

/// <summary>The body of a <c>DescribeCase</c> call, sent wrapped under <c>request</c>; one identity is named.</summary>
public sealed record DescribeCaseRequest {
	/// <summary>The case's schema name.</summary>
	[JsonPropertyName("caseName")]
	public string CaseName { get; init; }

	/// <summary>The case's schema UId.</summary>
	[JsonPropertyName("caseUId")]
	public string CaseUId { get; init; }

	/// <summary>The object: every case of it is described, enabled or not.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }
}

/// <summary>One case settings row of the object, as stored; the same shape in the server answer and the result.</summary>
public sealed class DynamicCaseSettingsRow {
	/// <summary>The <c>SysDcmSettings</c> row Id.</summary>
	[JsonPropertyName("sysDcmSettingsId")]
	public string SysDcmSettingsId { get; init; }

	/// <summary>The stage column the row names.</summary>
	[JsonPropertyName("stageColumnName")]
	public string StageColumnName { get; init; }

	/// <summary>True when the row names a filter column: several cases split by it.</summary>
	[JsonPropertyName("hasFilterColumn")]
	public bool HasFilterColumn { get; init; }

	/// <summary>The filter column the row names; absent when it names none.</summary>
	[JsonPropertyName("filterColumnName")]
	public string FilterColumnName { get; init; }
}

/// <summary>The server's <c>DescribeCaseResult</c>.</summary>
public sealed class DescribeCaseResponse {
	/// <summary>True when the case or the object could be read.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>A stable code for a refusal.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case named, or every case of the object; empty when the object has none.</summary>
	[JsonPropertyName("cases")]
	public List<BuildCaseCaseInfo> Cases { get; init; }

	/// <summary>The object's case settings rows; more than one is shown, never repaired.</summary>
	[JsonPropertyName("settings")]
	public List<DynamicCaseSettingsRow> Settings { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

/// <summary>
/// One stage of the desired stage list of a change, on the wire. Every member but <c>value</c> left out keeps the
/// stored value, so a member is written only when the call named it; <c>group</c> is written as JSON
/// <c>null</c> when the call named it as null, because that takes the stage out of its group.
/// </summary>
[JsonConverter(typeof(CaseStageEditRequestJsonConverter))]
public sealed record CaseStageEditRequest {
	/// <summary>The lookup value: its Id, or its exact display value.</summary>
	public string Value { get; init; }

	/// <summary>The stage caption in the case; the lookup value itself is never renamed.</summary>
	public string Caption { get; init; }

	/// <summary>The stage colour, <c>#RRGGBB</c>.</summary>
	public string Color { get; init; }

	/// <summary>Whether <see cref="Group"/> is sent at all, <see langword="null"/> included.</summary>
	public bool GroupSent { get; init; }

	/// <summary>The parent stage's lookup value; <see langword="null"/> with <see cref="GroupSent"/> ungroups.</summary>
	public string Group { get; init; }

	/// <summary>False for a final stage that ends the case unsuccessfully.</summary>
	public bool? Successful { get; init; }
}

/// <summary>
/// Writes a <see cref="CaseStageEditRequest"/> with the members the call named: the serializer's ignore conditions
/// cannot tell an explicit null to send from an absent member to leave out.
/// </summary>
public sealed class CaseStageEditRequestJsonConverter : JsonConverter<CaseStageEditRequest> {
	/// <inheritdoc />
	public override CaseStageEditRequest Read(ref Utf8JsonReader reader, Type typeToConvert,
		JsonSerializerOptions options) =>
		throw new NotSupportedException("A stage edit is a request member only; the server never answers with one.");

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, CaseStageEditRequest value, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteString("value", value.Value);
		if (value.Caption is not null) {
			writer.WriteString("caption", value.Caption);
		}
		if (value.Color is not null) {
			writer.WriteString("color", value.Color);
		}
		if (value.GroupSent) {
			if (value.Group is null) {
				writer.WriteNull("group");
			} else {
				writer.WriteString("group", value.Group);
			}
		}
		if (value.Successful.HasValue) {
			writer.WriteBoolean("successful", value.Successful.Value);
		}
		writer.WriteEndObject();
	}
}

/// <summary>
/// The body of a <c>ModifyCase</c> call, sent wrapped under <c>request</c>. Declarative: <see cref="Stages"/> is the
/// whole desired list, and a member left <see langword="null"/> leaves that part of the case as it is.
/// </summary>
public sealed record ModifyCaseRequest {
	/// <summary>The case's schema name.</summary>
	[JsonPropertyName("caseName")]
	public string CaseName { get; init; }

	/// <summary>The case's schema UId.</summary>
	[JsonPropertyName("caseUId")]
	public string CaseUId { get; init; }

	/// <summary>The object; accepted only when it has exactly one case.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>The whole desired stage list in order; <see langword="null"/> leaves the stages as they are.</summary>
	[JsonPropertyName("stages")]
	public IReadOnlyList<CaseStageEditRequest> Stages { get; init; }

	/// <summary>The case caption; <see langword="null"/> leaves it.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>Disable or enable the case; accepted only with no other change.</summary>
	[JsonPropertyName("enabled")]
	public bool? Enabled { get; init; }

	/// <summary>The user's yes to changing a case that has running cases.</summary>
	[JsonPropertyName("confirmRunningInstances")]
	public bool ConfirmRunningInstances { get; init; }

	/// <summary>True to run every check and answer the planned change without writing anything.</summary>
	[JsonPropertyName("validateOnly")]
	public bool ValidateOnly { get; init; }

	/// <summary>
	/// The case's new filter, or its removal; <see langword="null"/> leaves the filter as it is. A removal is sent as
	/// JSON <c>null</c>, which the serializer's ignore condition would otherwise drop with an absent member.
	/// </summary>
	[JsonPropertyName("filter")]
	public CaseFilterPatch Filter { get; init; }
}

/// <summary>A change of a case's filter: a new filter, or its removal.</summary>
/// <param name="NewFilter">The new filter; <see langword="null"/> removes the filter.</param>
[JsonConverter(typeof(CaseFilterPatchJsonConverter))]
public sealed record CaseFilterPatch(BuildCaseFilterRequest NewFilter);

/// <summary>Writes a <see cref="CaseFilterPatch"/> as the filter object, or as JSON <c>null</c> for a removal.</summary>
public sealed class CaseFilterPatchJsonConverter : JsonConverter<CaseFilterPatch> {
	/// <inheritdoc />
	public override CaseFilterPatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		throw new NotSupportedException("A filter change is a request member only; the server never answers with one.");

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, CaseFilterPatch value, JsonSerializerOptions options) {
		if (value.NewFilter is null) {
			writer.WriteNullValue();
			return;
		}
		JsonSerializer.Serialize(writer, value.NewFilter, options);
	}
}

/// <summary>One changed stage, or the case caption or enabled flag: what it was and what it becomes.</summary>
public sealed class DynamicCaseStageChange {
	/// <summary>The stage's lookup value Id.</summary>
	[JsonPropertyName("value")]
	public string Value { get; init; }

	/// <summary>The stage caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>The stored value before the change.</summary>
	[JsonPropertyName("from")]
	public string From { get; init; }

	/// <summary>The value after the change.</summary>
	[JsonPropertyName("to")]
	public string To { get; init; }
}

/// <summary>What a change does to the stored case; the same block for a dry run and for a write.</summary>
public sealed class DynamicCaseDiff {
	/// <summary>Stages the change adds.</summary>
	[JsonPropertyName("added")]
	public List<DynamicCaseStageChange> Added { get; init; }

	/// <summary>Stages the change removes.</summary>
	[JsonPropertyName("removed")]
	public List<DynamicCaseStageChange> Removed { get; init; }

	/// <summary>Stages whose place among the stages that stay changed; positions are 1-based.</summary>
	[JsonPropertyName("moved")]
	public List<DynamicCaseStageChange> Moved { get; init; }

	/// <summary>Stages whose caption in the case changed.</summary>
	[JsonPropertyName("recaptioned")]
	public List<DynamicCaseStageChange> Recaptioned { get; init; }

	/// <summary>Stages whose colour changed.</summary>
	[JsonPropertyName("recolored")]
	public List<DynamicCaseStageChange> Recolored { get; init; }

	/// <summary>Stages that joined, left or changed a group; from / to are the parent captions.</summary>
	[JsonPropertyName("regrouped")]
	public List<DynamicCaseStageChange> Regrouped { get; init; }

	/// <summary>Final stages whose successful flag changed.</summary>
	[JsonPropertyName("successChanged")]
	public List<DynamicCaseStageChange> SuccessChanged { get; init; }

	/// <summary>The case caption change, when there is one.</summary>
	[JsonPropertyName("caption")]
	public DynamicCaseStageChange Caption { get; init; }

	/// <summary>The enabled change, when there is one.</summary>
	[JsonPropertyName("enabled")]
	public DynamicCaseStageChange Enabled { get; init; }

	/// <summary>The case filter change in words, when there is one; <c>none</c> for no filter.</summary>
	[JsonPropertyName("filter")]
	public DynamicCaseStageChange Filter { get; init; }
}

/// <summary>Running cases that sit in one stage.</summary>
public sealed class DynamicCaseStageCount {
	/// <summary>The stage's lookup value Id.</summary>
	[JsonPropertyName("value")]
	public string Value { get; init; }

	/// <summary>The stage caption.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; }

	/// <summary>How many running cases sit in the stage.</summary>
	[JsonPropertyName("count")]
	public int Count { get; init; }
}

/// <summary>The running cases of the case schema, in total and per stage.</summary>
public sealed class DynamicCaseRunningInstances {
	/// <summary>Every running case of the case schema.</summary>
	[JsonPropertyName("total")]
	public int Total { get; init; }

	/// <summary>The running cases per stage.</summary>
	[JsonPropertyName("perStage")]
	public List<DynamicCaseStageCount> PerStage { get; init; }
}

/// <summary>The server's <c>ModifyCaseResult</c>.</summary>
public sealed class ModifyCaseResponse {
	/// <summary>True for <c>planned</c>, <c>modified</c> and <c>unchanged</c>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The server outcome; see <see cref="DynamicCaseModifyOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case after the change; for <c>planned</c>, the case as it would be.</summary>
	[JsonPropertyName("case")]
	public BuildCaseCaseInfo Case { get; init; }

	/// <summary>What the change does to the stored case.</summary>
	[JsonPropertyName("diff")]
	public DynamicCaseDiff Diff { get; init; }

	/// <summary>The running cases of the case schema.</summary>
	[JsonPropertyName("runningInstances")]
	public DynamicCaseRunningInstances RunningInstances { get; init; }

	/// <summary>The object's settings row when a first filter sets its filter column; absent otherwise.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>What the caller owes after the call.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>What a failed restore left behind.</summary>
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

#region Result shape (describe-dynamic-case and modify-dynamic-case)

/// <summary>The result document of <c>describe-dynamic-case</c>.</summary>
public sealed class DescribeDynamicCaseResult {
	/// <summary>Whether the case or the object could be read.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case named, or every case of the object, each with its designer link.</summary>
	[JsonPropertyName("cases")]
	public List<DynamicCaseInfo> Cases { get; init; }

	/// <summary>The object's case settings rows.</summary>
	[JsonPropertyName("settings")]
	public List<DynamicCaseSettingsRow> Settings { get; init; }

	/// <summary>What the caller should relay to the user.</summary>
	[JsonPropertyName("notes")]
	public List<string> Notes { get; init; }

	/// <summary>Caveats of the call.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; init; }
}

/// <summary>The result document of <c>modify-dynamic-case</c>.</summary>
public sealed class ModifyDynamicCaseResult {
	/// <summary>Whether the call succeeded; see <see cref="DynamicCaseModifyOutcomes.IsSuccess"/>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>One of <see cref="DynamicCaseModifyOutcomes"/>.</summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; init; }

	/// <summary>A stable code for a refusal or a failure.</summary>
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; init; }

	/// <summary>The refusal or failure text.</summary>
	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; init; }

	/// <summary>The case after the change, or as it would be on a dry run, with its designer link.</summary>
	[JsonPropertyName("case")]
	public DynamicCaseInfo Case { get; init; }

	/// <summary>What the change does to the stored case.</summary>
	[JsonPropertyName("diff")]
	public DynamicCaseDiff Diff { get; init; }

	/// <summary>The running cases of the case schema.</summary>
	[JsonPropertyName("runningInstances")]
	public DynamicCaseRunningInstances RunningInstances { get; init; }

	/// <summary>The object's settings row when a first filter sets its filter column; absent otherwise.</summary>
	[JsonPropertyName("settings")]
	public DynamicCaseSettings Settings { get; init; }

	/// <summary>What the caller owes after the call, per artifact.</summary>
	[JsonPropertyName("obligations")]
	public DynamicCaseObligations Obligations { get; init; }

	/// <summary>What a failed restore left behind.</summary>
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
