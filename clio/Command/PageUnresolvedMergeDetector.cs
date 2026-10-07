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
	/// Builds the <c>viewModelConfig</c> and <c>modelConfig</c> that the parent schemas produce on top of the
	/// runtime's starting config (an empty <c>attributes</c> object and the <c>PageParameters</c> data source), applies the
	/// body's config diffs on top of them the way the platform does, and returns one warning for every
	/// <c>merge</c> the differ skipped.
	/// </summary>
	/// <param name="body">The full web page body that will be saved.</param>
	/// <param name="readInheritedSchemas">Reads the schemas the saved body layers over, nearest parent first
	/// (designer hierarchy order), without the schema being saved, which the body replaces. Called at most once,
	/// and only when the body has a merge that can fail to apply (any merge except one with <c>path: []</c>, which
	/// targets the always-present root, and object <c>values</c>), so a body without one costs no read. Its
	/// exceptions propagate.</param>
	/// <returns>The warnings; empty when every merge resolves or there is nothing to check.</returns>
	IReadOnlyList<string> Detect(string body, Func<IEnumerable<PageDesignerHierarchySchema>> readInheritedSchemas);

}

/// <inheritdoc cref="IPageUnresolvedMergeDetector"/>
internal sealed class PageUnresolvedMergeDetector : IPageUnresolvedMergeDetector {

	private const string ViewModelConfigDiffSection = "viewModelConfigDiff";
	private const string ModelConfigDiffSection = "modelConfigDiff";
	private const string MergeOperation = "merge";
	private const string PageParametersDataSourceName = "PageParameters";
	private const string PageParametersDataSourceType = "crt.PageParametersDataSource";

	/// <summary>Why the differ does not apply one merge.</summary>
	private enum SkipReason {

		PathNotFound,
		TargetNotObject,
		ValuesNotObject

	}

	private readonly record struct SkippedMerge(string Section, JObject Operation, SkipReason Reason);

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
		JObject viewModelConfig = RuntimeViewModelConfigSeed();
		JObject modelConfig = RuntimeModelConfigSeed();
		foreach (PageDesignerHierarchySchema schema in inheritedSchemas.Reverse()) {
			if (string.IsNullOrWhiteSpace(schema?.Body)) {
				continue;
			}
			PageParsedSchemaBody layer = _bodyParser.Parse(schema.Body);
			viewModelConfig = ApplyLayer(viewModelApplier, viewModelConfig, layer.ViewModelConfigDiff, layer.ViewModelConfig);
			modelConfig = ApplyLayer(modelApplier, modelConfig, layer.ModelConfigDiff, layer.ModelConfig);
		}
		var skipped = new List<SkippedMerge>();
		skipped.AddRange(FindSkippedMerges(viewModelApplier, viewModelConfig, candidate.ViewModelConfigDiff, ViewModelConfigDiffSection));
		skipped.AddRange(FindSkippedMerges(modelApplier, modelConfig, candidate.ModelConfigDiff, ModelConfigDiffSection));
		return Describe(skipped);
	}

	// The runtime starts every config chain from these two objects before the first schema is applied (client
	// BaseSchemaBuilderService.getEmptySchemaPart), so a merge into "attributes" or "dataSources" resolves even
	// when no schema in the chain declares them - on a BlankPageTemplate page, for instance.
	private static JObject RuntimeViewModelConfigSeed() => new() { ["attributes"] = new JObject() };

	private static JObject RuntimeModelConfigSeed() => new() {
		["dataSources"] = new JObject {
			[PageParametersDataSourceName] = new JObject {
				["type"] = PageParametersDataSourceType,
				["config"] = new JObject()
			}
		}
	};

	private static bool ContainsCheckableMerge(JToken diff) =>
		diff is JArray operations && operations.OfType<JObject>().Any(IsCheckableMerge);

	private static bool IsCheckableMerge(JObject operation) =>
		IsMerge(operation) && (operation["path"] is not JArray { Count: 0 } || operation["values"] is not JObject);

	private static JObject ApplyLayer(IJsonPathDiffApplier applier, JObject current, JToken diff, JToken config) =>
		PageBundleMergeHelpers.ApplyConfigLayer(applier, current, diff, config);

	private static List<SkippedMerge> FindSkippedMerges(
		IJsonPathDiffApplier applier, JObject current, JToken diff, string section) {
		if (diff is not JArray { Count: > 0 } operations) {
			return [];
		}
		// A merge whose "values" is not an object never applies as written (the runtime reads its keys), and the
		// clone cannot replay it at all, so it is reported here and left out of the replay.
		List<JObject> malformed = operations.OfType<JObject>()
			.Where(operation => IsMerge(operation) && operation["values"] is not JObject)
			.ToList();
		var skipped = malformed.Select(operation => new SkippedMerge(section, operation, SkipReason.ValuesNotObject)).ToList();
		JArray replay = malformed.Count == 0
			? operations
			: new JArray(operations.Where(operation => !malformed.Any(m => ReferenceEquals(m, operation))));
		var unresolved = new List<JObject>();
		try {
			// The whole diff in one call, exactly as the runtime applies it.
			applier.Apply(current, replay, new JsonApplierOperationsOptions { UnresolvedMerges = unresolved });
			skipped.AddRange(unresolved.Select(operation => new SkippedMerge(section, operation, SkipReason.PathNotFound)));
		} catch (InvalidCastException) {
			// The clone casts the merge target to an object. A merge whose path ends on an array or a single value
			// throws here, where the runtime merges nothing into it. Replay the merges one by one to name that merge
			// and still check the others; merges run first and in array order, so the other operations in the diff
			// cannot change the outcome.
			skipped.AddRange(FindSkippedMergesOneByOne(applier, current, replay, section));
		}
		return skipped;
	}

	private static bool IsMerge(JObject operation) =>
		string.Equals(operation.Value<string>("operation"), MergeOperation, StringComparison.Ordinal);

	private static List<SkippedMerge> FindSkippedMergesOneByOne(
		IJsonPathDiffApplier applier, JObject current, JArray operations, string section) {
		var skipped = new List<SkippedMerge>();
		JObject config = current;
		foreach (JObject operation in operations.OfType<JObject>().Where(IsMerge)) {
			var unresolved = new List<JObject>();
			try {
				config = applier.Apply(config, new JArray(operation),
					new JsonApplierOperationsOptions { UnresolvedMerges = unresolved }) as JObject ?? config;
			} catch (InvalidCastException) {
				skipped.Add(new SkippedMerge(section, operation, SkipReason.TargetNotObject));
				continue;
			}
			skipped.AddRange(unresolved.Select(merge => new SkippedMerge(section, merge, SkipReason.PathNotFound)));
		}
		return skipped;
	}

	private static List<string> Describe(IEnumerable<SkippedMerge> skipped) {
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var findings = new List<string>();
		int unlisted = 0;
		foreach (SkippedMerge merge in skipped) {
			JArray path = merge.Operation["path"] as JArray;
			string name = merge.Operation.Value<string>("name");
			// The same operation authored twice is skipped twice; naming it twice adds nothing.
			if (!seen.Add($"{merge.Section}|{merge.Reason}|{path?.ToString(Formatting.None) ?? "name:" + name}")) {
				continue;
			}
			if (findings.Count < MaxReportedFindings) {
				findings.Add(Describe(merge, path, name));
			} else {
				unlisted++;
			}
		}
		if (unlisted > 0) {
			findings.Add($"{unlisted} more viewModelConfigDiff/modelConfigDiff merge(s) are not applied; "
				+ "they are not listed here.");
		}
		return findings;
	}

	private static string Describe(SkippedMerge merge, JArray path, string name) {
		if (path is null) {
			return DescribeMergeWithoutPath(merge.Section, name);
		}
		string target = $"{merge.Section} merge at path {Shorten(path.ToString(Formatting.None))}";
		return merge.Reason switch {
			SkipReason.TargetNotObject => $"{target} is not applied: the value at that path is not an object (it is an "
				+ "array or a single value), so there is nothing to merge \"values\" into. Merge into the object that "
				+ "holds it instead, with the new value inside \"values\".",
			SkipReason.ValuesNotObject => $"{target} is not applied as written: its \"values\" is not an object. A "
				+ "merge's \"values\" must be an object whose keys are merged into the target.",
			_ => DescribeMergeWithPath(target)
		};
	}

	private static string DescribeMergeWithPath(string target) =>
		$"{target} is not applied: that path does not exist in the config built from the parent schemas and this "
		+ "body's earlier merges, and the Creatio differ skips a merge whose path does not resolve, so the operation "
		+ "stays in the body but changes nothing. To add a new key, merge into its existing parent and put the key "
		+ "inside \"values\" (for example path [\"dataSources\"] with values {\"NewDS\": {...}}, or path [] with the "
		+ "whole branch); to change a nested value, make sure every segment of the path already exists.";

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
