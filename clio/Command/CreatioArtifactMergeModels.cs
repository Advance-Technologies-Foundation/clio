using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>Arguments for <c>merge-creatio-artifact</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatioArtifactMergeArgs(
	[property: JsonPropertyName("artifact-path")]
	[property: Description("Repository-relative path used only to classify the artifact. Rooted paths and '..' segments are rejected.")]
	[property: Required]
	string ArtifactPath,
	[property: JsonPropertyName("base-content")]
	[property: Description("Git stage 1 content.")]
	[property: Required]
	string BaseContent,
	[property: JsonPropertyName("ours-content")]
	[property: Description("Git stage 2 content.")]
	[property: Required]
	string OursContent,
	[property: JsonPropertyName("theirs-content")]
	[property: Description("Git stage 3 content.")]
	[property: Required]
	string TheirsContent,
	[property: JsonPropertyName("descriptor-content")]
	[property: Description("Resolved sibling descriptor content. Required for metadata and data bindings; never read from disk.")]
	string? DescriptorContent = null) {

	/// <summary>Maximum combined UTF-8 size accepted for one merge request or result.</summary>
	public const int MaxCombinedContentBytes = 4 * 1024 * 1024;
}

/// <summary>A preview-only Creatio artifact merge result.</summary>
public sealed record CreatioArtifactMergeResult(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("artifact-kind")] string ArtifactKind,
	[property: JsonPropertyName("resolver-version")] string ResolverVersion,
	[property: JsonPropertyName("content")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Content,
	[property: JsonPropertyName("report")] CreatioArtifactMergeReport Report,
	[property: JsonPropertyName("diagnostics")] IReadOnlyList<string> Diagnostics) {

	/// <summary>Stable result status returned when the semantic merge is complete.</summary>
	public const string ResolvedStatus = "resolved";

	/// <summary>Stable result status returned when a user decision is required.</summary>
	public const string ConflictsRemainStatus = "conflicts-remain";

	/// <summary>Stable result status returned for a recognized but unimplemented artifact.</summary>
	public const string NotImplementedStatus = "not-implemented";

	/// <summary>Stable result status returned for an unsupported artifact shape.</summary>
	public const string UnsupportedStatus = "unsupported";

	/// <summary>Stable result status returned when a request cannot be merged safely.</summary>
	public const string InvalidInputStatus = "invalid-input";

	/// <summary>Stable result status returned when bounded merge capacity is temporarily unavailable.</summary>
	public const string BusyStatus = "busy";
}

/// <summary>A stable projection of the resolver report.</summary>
public sealed record CreatioArtifactMergeReport(
	[property: JsonPropertyName("resolution-type")] string ResolutionType,
	[property: JsonPropertyName("winner-policy")] string WinnerPolicy,
	[property: JsonPropertyName("verification-passed")] bool VerificationPassed,
	[property: JsonPropertyName("local-additions")] IReadOnlyList<string> LocalAdditions,
	[property: JsonPropertyName("remote-additions")] IReadOnlyList<string> RemoteAdditions,
	[property: JsonPropertyName("local-deletions")] IReadOnlyList<string> LocalDeletions,
	[property: JsonPropertyName("remote-deletions")] IReadOnlyList<string> RemoteDeletions,
	[property: JsonPropertyName("true-conflicts")] IReadOnlyList<string> TrueConflicts) {

	/// <summary>An empty, unverified report for outcomes that do not invoke the resolver.</summary>
	public static CreatioArtifactMergeReport Empty { get; } = new(
		string.Empty,
		string.Empty,
		false,
		[],
		[],
		[],
		[],
		[]);
}
