namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Finds the <c>merge</c> operations in a web page body's <c>viewModelConfigDiff</c> and
/// <c>modelConfigDiff</c> that the Creatio differ does not apply. A merge the differ skips or applies with no
/// effect is described as an advisory warning; a merge that makes the differ throw, so the page fails to build,
/// is described as an error.
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
	/// body's config diffs on top of them the way the platform does, and returns one finding for every
	/// <c>merge</c> the differ skips or throws on.
	/// </summary>
	/// <param name="body">The full web page body that will be saved.</param>
	/// <param name="readInheritedSchemas">Reads the schemas the saved body layers over, nearest parent first
	/// (designer hierarchy order), without the schema being saved, which the body replaces. Called at most once,
	/// and only when the body has a merge that can fail to apply (any merge except one with <c>path: []</c>, which
	/// targets the always-present root, and object <c>values</c>), so a body without one costs no read. Its
	/// exceptions propagate.</param>
	/// <returns>The errors and the warnings; both empty when every merge resolves or there is nothing to check.</returns>
	/// <exception cref="JsonDiffApplierException">An operation that is not a merge makes the differ throw, so the
	/// merges cannot be checked.</exception>
	PageConfigMergeReport Detect(string body, Func<IEnumerable<PageDesignerHierarchySchema>> readInheritedSchemas);

}

/// <summary>The outcome of <see cref="IPageUnresolvedMergeDetector.Detect"/>.</summary>
/// <param name="Errors">Merges that make the runtime differ throw, so the page fails to build.</param>
/// <param name="Warnings">Merges the runtime differ skips, or applies with no effect.</param>
public sealed record PageConfigMergeReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {

	/// <summary>No findings.</summary>
	public static PageConfigMergeReport Empty { get; } = new([], []);

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
		TargetIsArray,
		ValuesNotObject,
		TargetNotObject,
		ValuesMissing

	}

	private readonly record struct SkippedMerge(string Section, JObject Operation, SkipReason Reason) {

		/// <summary>The runtime throws on this merge, so the page fails to build.</summary>
		public bool IsError => Reason is SkipReason.TargetNotObject or SkipReason.ValuesMissing;

	}

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
	public PageConfigMergeReport Detect(string body, Func<IEnumerable<PageDesignerHierarchySchema>> readInheritedSchemas) {
		if (string.IsNullOrWhiteSpace(body)) {
			return PageConfigMergeReport.Empty;
		}
		PageParsedSchemaBody candidate = _bodyParser.Parse(body);
		if (!ContainsCheckableMerge(candidate.ViewModelConfigDiff) && !ContainsCheckableMerge(candidate.ModelConfigDiff)) {
			return PageConfigMergeReport.Empty;
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
		return new PageConfigMergeReport(
			Describe(skipped.Where(merge => merge.IsError)),
			Describe(skipped.Where(merge => !merge.IsError)));
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
		var skipped = new List<SkippedMerge>();
		var unresolved = new List<JObject>();
		var arrayTargets = new List<JObject>();
		try {
			// The whole diff in one call, exactly as the runtime applies it.
			applier.Apply(current, operations,
				new JsonApplierOperationsOptions { UnresolvedMerges = unresolved, ArrayTargetMerges = arrayTargets });
			skipped.AddRange(unresolved.Select(operation => new SkippedMerge(section, operation, SkipReason.PathNotFound)));
			skipped.AddRange(arrayTargets.Select(operation => new SkippedMerge(section, operation, SkipReason.TargetIsArray)));
		} catch (JsonDiffApplierException) {
			// The runtime throws on this diff too. Replay the merges one by one to name the merge that throws and
			// still check the others; merges run first and in array order, so the other operations in the diff
			// cannot change the outcome. When no merge throws, an insert, move, remove or set is the cause, which
			// this check does not cover: rethrow, so the caller says the check did not run.
			List<SkippedMerge> oneByOne = FindSkippedMergesOneByOne(applier, current, operations, section);
			if (!oneByOne.Exists(merge => merge.IsError)) {
				throw;
			}
			skipped.AddRange(oneByOne);
		}
		// A merge whose "values" is an array, a string, a number or a boolean applies its indexes as keys, or
		// nothing at all, so it does not do what it says. Reported unless another finding already names it.
		skipped.AddRange(operations.OfType<JObject>()
			.Where(operation => IsMerge(operation) && HasNonObjectValues(operation)
				&& !skipped.Exists(merge => JToken.DeepEquals(merge.Operation, operation)))
			.Select(operation => new SkippedMerge(section, operation, SkipReason.ValuesNotObject))
			.ToList());
		return skipped;
	}

	private static bool HasNonObjectValues(JObject operation) =>
		operation["values"] is { Type: not (JTokenType.Object or JTokenType.Null or JTokenType.Undefined) };

	private static bool IsMerge(JObject operation) =>
		string.Equals(operation.Value<string>("operation"), MergeOperation, StringComparison.Ordinal);

	private static List<SkippedMerge> FindSkippedMergesOneByOne(
		IJsonPathDiffApplier applier, JObject current, JArray operations, string section) {
		var skipped = new List<SkippedMerge>();
		JObject config = current;
		foreach (JObject operation in operations.OfType<JObject>().Where(IsMerge)) {
			var unresolved = new List<JObject>();
			var arrayTargets = new List<JObject>();
			try {
				config = applier.Apply(config, new JArray(operation),
					new JsonApplierOperationsOptions { UnresolvedMerges = unresolved, ArrayTargetMerges = arrayTargets })
					as JObject ?? config;
			} catch (JsonDiffApplierException) {
				SkipReason reason = operation["values"] is null or { Type: JTokenType.Null or JTokenType.Undefined }
					? SkipReason.ValuesMissing
					: SkipReason.TargetNotObject;
				skipped.Add(new SkippedMerge(section, operation, reason));
				continue;
			}
			skipped.AddRange(unresolved.Select(merge => new SkippedMerge(section, merge, SkipReason.PathNotFound)));
			skipped.AddRange(arrayTargets.Select(merge => new SkippedMerge(section, merge, SkipReason.TargetIsArray)));
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
			findings.Add($"{unlisted} more viewModelConfigDiff/modelConfigDiff merge(s) have the same kind of problem; "
				+ "they are not listed here.");
		}
		return findings;
	}

	private static string Describe(SkippedMerge merge, JArray path, string name) {
		if (path is null && merge.Reason == SkipReason.PathNotFound) {
			return DescribeMergeWithoutPath(merge.Section, name);
		}
		string target = path is null
			? $"{merge.Section} merge named '{Shorten(name ?? string.Empty)}'"
			: $"{merge.Section} merge at path {Shorten(path.ToString(Formatting.None))}";
		return merge.Reason switch {
			SkipReason.TargetNotObject => $"{target} breaks the page: the value at that path is a single value, not an "
				+ "object, and the Creatio runtime throws when it sets the merged keys on it, so the page fails to "
				+ "build. Merge into the object that holds it instead, with the new value inside \"values\".",
			SkipReason.ValuesMissing => $"{target} breaks the page: it has no \"values\" object, and the Creatio "
				+ "runtime throws when it reads the keys of a missing or null \"values\", so the page fails to build. "
				+ "Give the merge a \"values\" object, or remove the operation.",
			SkipReason.TargetIsArray => $"{target} is not applied: the value at that path is an array, and the Creatio "
				+ "differ sets the merged keys on the array, where they are lost. Merge into the object that holds the "
				+ "array instead, with the new array inside \"values\".",
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
