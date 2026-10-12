using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

/// <summary>
/// The compact digest a successful page write returns (ENG-103023): which components the write added or changed
/// and whether content validation ran and passed - so the caller does not re-read the page to confirm the write.
/// </summary>
public sealed record PageWriteDigest {

	/// <summary>Most components listed; the rest are counted in <see cref="ComponentsOmitted"/>.</summary>
	internal const int MaxComponents = 25;

	/// <summary>
	/// <c>passed</c> when the content validation chain ran and accepted the body, <c>skipped</c> when the caller
	/// turned it off (<c>validate=false</c>). The structural checks are never skipped, and a write that fails any
	/// check returns an error instead of a digest.
	/// </summary>
	[JsonPropertyName("validation")]
	public string Validation { get; init; }

	/// <summary>
	/// What <see cref="Components"/> lists: <c>fragment</c> (an append write, so these are the incoming fragment's
	/// operations as sent - the response's <c>appendProjection</c> says which of them the merge collapsed into a
	/// later entry of the same fragment) or <c>body</c> (a replace write, so these are the own body's operations,
	/// which now ARE the page's own layer).
	/// </summary>
	[JsonPropertyName("basis")]
	public string Basis { get; init; }

	/// <summary>
	/// The write's <c>viewConfigDiff</c> operations, one line each: <c>operation name (type) in parent</c>.
	/// </summary>
	[JsonPropertyName("components")]
	public IReadOnlyList<string> Components { get; init; } = [];

	/// <summary>
	/// The body's non-component sections, by count: <c>viewModelConfigDiff</c>, <c>modelConfigDiff</c> and
	/// <c>handlers</c> entries. Absent when all are zero, so an empty <see cref="Components"/> beside it means the
	/// write carried no component changes, not that nothing was written.
	/// </summary>
	[JsonPropertyName("otherSections")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, int> OtherSections { get; init; }

	/// <summary>How many further operations were not listed, or absent when none were left out.</summary>
	[JsonPropertyName("componentsOmitted")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? ComponentsOmitted { get; init; }

	/// <summary>
	/// Builds the digest for a body that was saved, or <c>null</c> when the body cannot be parsed (a digest is a
	/// convenience; it never fails the write).
	/// </summary>
	/// <param name="body">The body the write sent.</param>
	/// <param name="appendMode">Whether the write merged the body as a fragment.</param>
	/// <param name="validationRan">Whether the content validation chain ran.</param>
	/// <param name="appendProjection">
	/// What the append merge did, or <c>null</c>. When it reports the merged <c>viewConfigDiff</c> was never
	/// written back (<see cref="PageAppendProjection.ViewConfigDiffApplied"/> false), the fragment's components
	/// did not reach the page, so no digest is returned rather than one that lists them as written.
	/// </param>
	/// <returns>The digest, or <c>null</c>.</returns>
	internal static PageWriteDigest TryBuild(string body, bool appendMode, bool validationRan,
		PageAppendProjection appendProjection = null) {
		if (appendMode && appendProjection is { ViewConfigDiffApplied: false }) {
			return null;
		}
		List<PageOperationInfo> operations;
		Dictionary<string, int> otherSections = new(StringComparer.Ordinal);
		try {
			PageParsedSchemaBody parsed = PageSchemaBodyParser.ParseBody(body);
			operations = PageGetCommand.ExtractViewConfigOps(parsed);
			AddCount(otherSections, "viewModelConfigDiff", (parsed.ViewModelConfigDiff as JArray)?.Count ?? 0);
			AddCount(otherSections, "modelConfigDiff", (parsed.ModelConfigDiff as JArray)?.Count ?? 0);
			AddCount(otherSections, "handlers", PageGetCommand.ExtractHandlerInfo(parsed.Handlers?.Trim()).count);
		} catch (Exception) {
			return null;
		}
		List<string> lines = operations.Select(Describe).ToList();
		return new PageWriteDigest {
			Validation = validationRan ? "passed" : "skipped",
			Basis = appendMode ? "fragment" : "body",
			Components = lines.Take(MaxComponents).ToList(),
			ComponentsOmitted = lines.Count > MaxComponents ? lines.Count - MaxComponents : null,
			OtherSections = otherSections.Count > 0 ? otherSections : null
		};
	}

	private static void AddCount(Dictionary<string, int> counts, string section, int count) {
		if (count > 0) {
			counts[section] = count;
		}
	}

	private static string Describe(PageOperationInfo operation) {
		string line = $"{operation.Operation ?? "?"} {operation.Name ?? "?"}";
		if (!string.IsNullOrWhiteSpace(operation.Type)) {
			line += $" ({operation.Type})";
		}
		if (!string.IsNullOrWhiteSpace(operation.ParentName)) {
			line += $" in {operation.ParentName}";
		}
		return line;
	}
}
