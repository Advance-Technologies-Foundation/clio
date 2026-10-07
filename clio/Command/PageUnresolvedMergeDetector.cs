namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Finds the <c>merge</c> operations in a web page body's <c>viewModelConfigDiff</c> and
/// <c>modelConfigDiff</c> that the Creatio differ skips because their target does not exist, and
/// describes each one as an advisory warning.
/// </summary>
/// <remarks>
/// The differ resolves a path-addressed <c>merge</c> by walking its <c>path</c> through the config the
/// layers below it produced. When a segment is missing it skips the merge and says nothing about it
/// (client <c>json-path-applier.service.ts</c>, cloned by <see cref="JsonPathDiffApplier"/>). The save still
/// succeeds, so without this check <c>update-page</c> reports <c>success:true</c> for an operation that never
/// takes effect (GH-1753).
/// </remarks>
public interface IPageUnresolvedMergeDetector {

	/// <summary>
	/// Builds the <c>viewModelConfig</c> and <c>modelConfig</c> that the parent schemas produce, applies the
	/// body's config diffs on top of them the way the platform does, and returns one warning for every
	/// <c>merge</c> the differ skipped.
	/// </summary>
	/// <param name="body">The full web page body that will be saved.</param>
	/// <param name="readInheritedSchemas">Reads the schemas the saved body layers over, nearest parent first
	/// (designer hierarchy order), without the schema being saved, which the body replaces. Called at most once,
	/// and only when the body has a merge whose target can be missing (any merge except <c>path: []</c>, which
	/// targets the always-present root), so a body without one costs no read. Its exceptions propagate.</param>
	/// <returns>The warnings; empty when every merge resolves or there is nothing to check.</returns>
	IReadOnlyList<string> Detect(string body, Func<IEnumerable<PageDesignerHierarchySchema>> readInheritedSchemas);

}

/// <inheritdoc cref="IPageUnresolvedMergeDetector"/>
internal sealed class PageUnresolvedMergeDetector : IPageUnresolvedMergeDetector {

	private const string ViewModelConfigDiffSection = "viewModelConfigDiff";
	private const string ModelConfigDiffSection = "modelConfigDiff";
	private const string MergeOperation = "merge";

	/// <summary>
	/// Ceiling on reported findings, so a body full of stale operations cannot bury the response. Past it,
	/// one summary line gives the remaining count.
	/// </summary>
	internal const int MaxReportedFindings = 12;

	/// <summary>Longest path or name echoed back in one finding.</summary>
	private const int MaxEchoedChars = 200;

	private readonly IPageSchemaBodyParser _bodyParser;
	private readonly Func<IJsonPathDiffApplier> _pathApplierFactory;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageUnresolvedMergeDetector"/> class.
	/// </summary>
	/// <param name="bodyParser">Parses each schema body into its sections, the same parser get-page uses.</param>
	/// <param name="pathApplierFactory">Creates a fresh path applier per config chain; an applier keeps alias
	/// state for one chain and must not be shared between chains.</param>
	public PageUnresolvedMergeDetector(IPageSchemaBodyParser bodyParser, Func<IJsonPathDiffApplier> pathApplierFactory) {
		_bodyParser = bodyParser;
		_pathApplierFactory = pathApplierFactory;
	}

	/// <inheritdoc />
	public IReadOnlyList<string> Detect(string body, Func<IEnumerable<PageDesignerHierarchySchema>> readInheritedSchemas) {
		if (string.IsNullOrWhiteSpace(body)) {
			return [];
		}
		PageParsedSchemaBody candidate = _bodyParser.Parse(body);
		if (!ContainsCheckableMerge(candidate.ViewModelConfigDiff) && !ContainsCheckableMerge(candidate.ModelConfigDiff)) {
			return [];
		}
		IEnumerable<PageDesignerHierarchySchema> inheritedSchemas =
			readInheritedSchemas?.Invoke() ?? Enumerable.Empty<PageDesignerHierarchySchema>();
		// One applier per config chain, kept for the candidate too: the applier carries aliases declared by an
		// ancestor, so a fresh instance for the candidate would resolve differently from the runtime.
		IJsonPathDiffApplier viewModelApplier = _pathApplierFactory();
		IJsonPathDiffApplier modelApplier = _pathApplierFactory();
		JObject viewModelConfig = new();
		JObject modelConfig = new();
		foreach (PageDesignerHierarchySchema schema in inheritedSchemas.Reverse()) {
			if (string.IsNullOrWhiteSpace(schema?.Body)) {
				continue;
			}
			PageParsedSchemaBody layer = _bodyParser.Parse(schema.Body);
			viewModelConfig = ApplyLayer(viewModelApplier, viewModelConfig, layer.ViewModelConfigDiff, layer.ViewModelConfig);
			modelConfig = ApplyLayer(modelApplier, modelConfig, layer.ModelConfigDiff, layer.ModelConfig);
		}
		var skipped = new List<(string Section, JObject Operation)>();
		skipped.AddRange(FindUnresolvedMerges(viewModelApplier, viewModelConfig, candidate.ViewModelConfigDiff)
			.Select(operation => (ViewModelConfigDiffSection, operation)));
		skipped.AddRange(FindUnresolvedMerges(modelApplier, modelConfig, candidate.ModelConfigDiff)
			.Select(operation => (ModelConfigDiffSection, operation)));
		return Describe(skipped);
	}

	private static bool ContainsCheckableMerge(JToken diff) =>
		diff is JArray operations && operations.OfType<JObject>().Any(IsCheckableMerge);

	private static bool IsCheckableMerge(JObject operation) =>
		string.Equals(operation.Value<string>("operation"), MergeOperation, StringComparison.Ordinal)
		&& operation["path"] is not JArray { Count: 0 };

	private static JObject ApplyLayer(IJsonPathDiffApplier applier, JObject current, JToken diff, JToken config) =>
		PageBundleMergeHelpers.ApplyConfigLayer(applier, current, diff, config);

	private static List<JObject> FindUnresolvedMerges(IJsonPathDiffApplier applier, JObject current, JToken diff) {
		var unresolved = new List<JObject>();
		if (diff is JArray { Count: > 0 } operations) {
			applier.Apply(current, operations, new JsonApplierOperationsOptions { UnresolvedMerges = unresolved });
		}
		return unresolved;
	}

	private static List<string> Describe(IEnumerable<(string Section, JObject Operation)> skipped) {
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var findings = new List<string>();
		int unlisted = 0;
		foreach ((string section, JObject operation) in skipped) {
			JArray path = operation["path"] as JArray;
			string name = operation.Value<string>("name");
			// The same operation authored twice is skipped twice; naming it twice adds nothing.
			if (!seen.Add(section + "|" + (path?.ToString(Formatting.None) ?? "name:" + name))) {
				continue;
			}
			if (findings.Count < MaxReportedFindings) {
				findings.Add(path is null ? DescribeMergeWithoutPath(section, name) : DescribeMergeWithPath(section, path));
			} else {
				unlisted++;
			}
		}
		if (unlisted > 0) {
			findings.Add($"{unlisted} more viewModelConfigDiff/modelConfigDiff merge(s) are not applied "
				+ "because their path does not resolve; they are not listed here.");
		}
		return findings;
	}

	private static string DescribeMergeWithPath(string section, JArray path) =>
		$"{section} merge at path {Shorten(path.ToString(Formatting.None))} is not applied: that path does not exist "
		+ "in the config built from the parent schemas and this body's earlier merges, and the Creatio differ skips a "
		+ "merge whose path does not resolve, so the operation stays in the body but changes nothing. To add a new key, "
		+ "merge into its existing parent and put the key inside \"values\" (for example path [\"dataSources\"] with "
		+ "values {\"NewDS\": {...}}, or path [] with the whole branch); to change a nested value, check every path "
		+ "segment against get-page's bundle.";

	private static string DescribeMergeWithoutPath(string section, string name) {
		string target = string.IsNullOrEmpty(name) ? "with no name" : $"named '{Shorten(name)}'";
		return $"{section} merge {target} is not applied: it has no \"path\" and its name matches no element, so the "
			+ "Creatio differ has nothing to merge into. Use path [] to merge at the config root, or the path of an "
			+ "existing parent.";
	}

	// Echoes caller-authored text, so bound it: one finding must not grow with a pathological path.
	private static string Shorten(string text) =>
		text.Length <= MaxEchoedChars ? text : text[..MaxEchoedChars] + "...";

}
