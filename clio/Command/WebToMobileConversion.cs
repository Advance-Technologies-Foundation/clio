namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command.McpServer.Tools;
using Newtonsoft.Json.Linq;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonObject = System.Text.Json.Nodes.JsonObject;

// Freedom UI WEB -> Freedom UI MOBILE conversion ANALYSIS (advisory-only, ENG-89620).
// This service builds NOTHING and performs no Creatio I/O. It inspects the source web page
// (merged component bundle + registries + the version-resolved WebToMobilePageConversionRules)
// and produces a deterministic MobilePageConversionGuide: source structure, the recommended mobile
// template + container correspondence, per-type component suggestions, and inline mobile
// component contracts. An LLM uses the guide to build the mobile page body itself.
// The shared, converter-agnostic category enum/DTOs live in PageConversionModels.cs;
// the guide contract lives in MobilePageConversionGuideModels.cs.

/// <summary>
/// Deterministic Freedom UI WEB -> Freedom UI MOBILE conversion analyzer. Pure (no Creatio I/O,
/// no body generation): the caller supplies the merged page bundle, the resolved component
/// registries, and the version-resolved <see cref="WebToMobilePageConversionRules"/>; the service
/// returns a <see cref="MobilePageConversionGuide"/> the model executes.
/// </summary>
public static class WebToMobileAnalysisService {

	private const string ComponentInfoHint =
		"Use get-component-info with schema-type \"mobile\" to find a supported mobile alternative, or configure this part manually in Freedom UI Mobile Designer.";

	private const string GuidanceArticleName = "freedom-page-web-to-mobile-conversion";

	/// <summary>Source page type this analyzer handles.</summary>
	public const string SourceTypeFreedomWeb = "freedom-web";

	/// <summary>
	/// Inspects the source page bundle and produces the advisory conversion guide.
	/// </summary>
	/// <param name="bundle">Merged (resolved) source page tree, including inherited template components.</param>
	/// <param name="mobileTypes">Component types supported by the mobile registry.</param>
	/// <param name="webTypes">Component types known to the web registry.</param>
	/// <param name="webByType">Web registry entries by component type (container detection).</param>
	/// <param name="mobileByType">Mobile registry entries by component type (inline contracts).</param>
	/// <param name="rules">Version-resolved conversion rules (component equivalence + templates).</param>
	/// <param name="templateRule">The matched web→mobile template rule (may be null).</param>
	/// <param name="sourcePage">Source web page schema name.</param>
	/// <param name="suggestedTarget">Suggested target mobile page schema name.</param>
	/// <param name="containerNameMap">Web→mobile container-name map from the template rule (may be null).</param>
	public static MobilePageConversionGuide Analyze(
		PageBundleInfo bundle,
		IReadOnlySet<string> mobileTypes,
		IReadOnlySet<string> webTypes,
		IReadOnlyDictionary<string, ComponentRegistryEntry> webByType,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		WebToMobilePageConversionRules rules,
		TemplateMappingRule templateRule,
		string sourcePage,
		string sourceTemplate,
		string suggestedTarget,
		IReadOnlyDictionary<string, string> containerNameMap,
		SectionRegistrationInfo sectionRegistration = null) {
		ArgumentNullException.ThrowIfNull(bundle);
		ArgumentNullException.ThrowIfNull(mobileTypes);
		ArgumentNullException.ThrowIfNull(webTypes);
		ArgumentNullException.ThrowIfNull(rules);

		IReadOnlyDictionary<string, string> map =
			containerNameMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		// 1. Walk the merged tree into a flat structure (names, types, parents, container flags) and
		//    record, per web type, the source-component names that carry it.
		JArray tree = bundle.ViewConfig is null ? new JArray() : JArray.Parse(bundle.ViewConfig.ToJsonString());
		var structure = new List<SourceComponentInfo>();
		var namesByType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		WalkStructure(tree, parentName: null, map, webByType, mobileByType, structure, namesByType);

		// 2. Component suggestions: classify each distinct present web type via the rules matrix,
		//    then the registry type sets (direct/unsupported/manual).
		List<ComponentSuggestion> suggestions = BuildComponentSuggestions(namesByType, rules, mobileTypes, webTypes);

		// 3. Inline contracts for every suggested mobile type (+ direct-mapped types).
		List<MobileComponentContract> contracts = BuildMobileContracts(suggestions, mobileByType);

		// 4. Web-only sections and data sources (surfaced, not stripped — the model owns the body).
		List<string> webOnly = CollectWebOnlySections(bundle);
		List<string> dataSources = CollectDataSources(bundle);

		// 5. Instance-level element map (per named element: merge / insert / drop / relocate-children).
		Dictionary<string, string> attrToDs = BuildAttrToDs(bundle);
		Dictionary<string, string> attrToColumn = BuildAttrToColumn(bundle);
		HashSet<string> dataSourceSet = new(dataSources, StringComparer.OrdinalIgnoreCase);
		string primaryDs = ResolvePrimaryDs(attrToDs, dataSources);
		JObject resources = ParseResources(bundle);
		List<ElementMapEntry> elementMap = BuildElementMap(
			tree, map, mobileTypes, mobileByType, rules, attrToDs, attrToColumn, dataSourceSet, primaryDs, resources);

		// 6. Data sections applied to the mobile body verbatim/filtered (identical structural support on
		//    mobile): modelConfig is carried over as-is (preserving attribute types like ForwardReference);
		//    viewModelConfig drops attributes used only by dropped components.
		JsonNode modelConfig = PassthroughModelConfig(bundle);
		JsonNode viewModelConfig = BuildMobileViewModelConfig(bundle, tree, elementMap);
		// Prebuilt, ready-to-paste diffs (single root merge) so the caller never hand-builds the
		// data-source section (the step where attribute `type` was being dropped).
		JsonNode modelConfigDiff = BuildRootMergeDiff(modelConfig);
		JsonNode viewModelConfigDiff = BuildRootMergeDiff(viewModelConfig);

		return new MobilePageConversionGuide {
			SourcePage = sourcePage,
			SourceType = SourceTypeFreedomWeb,
			SourceTemplate = string.IsNullOrWhiteSpace(sourceTemplate) ? null : sourceTemplate,
			SourceStructure = structure,
			WebOnlySections = webOnly.Count > 0 ? webOnly : null,
			DataSources = dataSources.Count > 0 ? dataSources : null,
			ModelConfig = modelConfig,
			ViewModelConfig = viewModelConfig,
			ModelConfigDiff = modelConfigDiff,
			ViewModelConfigDiff = viewModelConfigDiff,
			RecommendedMobileTemplate = templateRule?.Mobile,
			TemplateNote = templateRule?.Note,
			ContainerMap = BuildContainerMap(templateRule),
			ComponentSuggestions = suggestions,
			ElementMap = elementMap,
			MobileContracts = contracts,
			SectionRegistration = sectionRegistration,
			Constraints = BuildConstraints(dataSources.Count > 1, webOnly, modelConfig is not null, viewModelConfig is not null),
			NextSteps = BuildNextSteps(modelConfig is not null || viewModelConfig is not null),
			GuidanceArticle = GuidanceArticleName,
			SuggestedTargetSchemaName = suggestedTarget
		};
	}

	/// <summary>
	/// Recursively flattens the merged viewConfig tree into <see cref="SourceComponentInfo"/> nodes,
	/// recording each node's parent and whether it is a layout container, and indexing component names
	/// by their type so the suggestions can list affected components.
	/// </summary>
	private static void WalkStructure(
		JArray nodes, string parentName,
		IReadOnlyDictionary<string, string> containerNameMap,
		IReadOnlyDictionary<string, ComponentRegistryEntry> webByType,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		List<SourceComponentInfo> structure,
		Dictionary<string, List<string>> namesByType) {
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				continue;
			}
			string name = node["name"]?.ToString();
			string type = node["type"]?.ToString();
			bool isMappedContainer = name is { Length: > 0 } && containerNameMap.ContainsKey(name);
			bool isContainer = isMappedContainer || IsLayoutContainer(type, name, webByType, mobileByType);

			structure.Add(new SourceComponentInfo {
				Name = name,
				Type = string.IsNullOrWhiteSpace(type) ? null : type,
				ParentName = parentName,
				IsContainer = isContainer
			});

			if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrEmpty(name)) {
				if (!namesByType.TryGetValue(type, out List<string> list)) {
					list = [];
					namesByType[type] = list;
				}
				list.Add(name);
			}

			if (node["items"] is JArray items) {
				WalkStructure(items, string.IsNullOrEmpty(name) ? parentName : name,
					containerNameMap, webByType, mobileByType, structure, namesByType);
			}
		}
	}

	/// <summary>
	/// Determines whether a component is a layout container, preferring the registry <c>container</c>
	/// flag (web registry, then mobile). For a type unknown to both registries, falls back to a soft
	/// name-suffix heuristic (<c>...Container</c> / <c>...Panel</c>).
	/// </summary>
	private static bool IsLayoutContainer(
		string type, string name,
		IReadOnlyDictionary<string, ComponentRegistryEntry> webByType,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType) {
		if (!string.IsNullOrWhiteSpace(type)) {
			if (webByType is not null && webByType.TryGetValue(type, out ComponentRegistryEntry webEntry)) {
				return webEntry.Container;
			}
			if (mobileByType is not null && mobileByType.TryGetValue(type, out ComponentRegistryEntry mobileEntry)) {
				return mobileEntry.Container;
			}
		}
		return name is { Length: > 0 }
			&& (name.EndsWith("Container", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("Panel", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Builds one <see cref="ComponentSuggestion"/> per distinct present web type: classified via the
	/// component equivalence matrix first (many→one merges noted), then by registry membership
	/// (direct mapping / unsupported / requires-manual-decision).
	/// </summary>
	private static List<ComponentSuggestion> BuildComponentSuggestions(
		Dictionary<string, List<string>> namesByType,
		WebToMobilePageConversionRules rules,
		IReadOnlySet<string> mobileTypes,
		IReadOnlySet<string> webTypes) {
		var suggestions = new List<ComponentSuggestion>();
		HashSet<string> presentTypes = new(namesByType.Keys, StringComparer.OrdinalIgnoreCase);

		foreach ((string type, List<string> names) in namesByType) {
			ComponentEquivalenceRule rule = FindRule(rules, type);
			ComponentSuggestion suggestion;
			if (rule is not null) {
				ComponentMappingCategory category = ParseCategory(rule.Category);
				string mergeNote = BuildPrimaryWebMergeNote(rule, presentTypes);
				suggestion = new ComponentSuggestion {
					SourceType = type,
					SourceNames = names,
					Category = category.ToString(),
					SuggestedMobileTypes = rule.Mobile ?? [],
					PrimaryWebMerge = mergeNote,
					Note = rule.Note
				};
			} else if (mobileTypes.Contains(type)) {
				suggestion = new ComponentSuggestion {
					SourceType = type,
					SourceNames = names,
					Category = ComponentMappingCategory.DirectMapping.ToString(),
					SuggestedMobileTypes = [type],
					Note = "Same component type exists on mobile — carry it over as-is."
				};
			} else if (webTypes.Contains(type)) {
				suggestion = new ComponentSuggestion {
					SourceType = type,
					SourceNames = names,
					Category = ComponentMappingCategory.Unsupported.ToString(),
					SuggestedMobileTypes = [],
					Note = $"Component \"{type}\" is not supported in Freedom UI Mobile Designer. " + ComponentInfoHint
				};
			} else {
				suggestion = new ComponentSuggestion {
					SourceType = type,
					SourceNames = names,
					Category = ComponentMappingCategory.RequiresManualDecision.ToString(),
					SuggestedMobileTypes = [],
					Note = $"Component \"{type}\" is unknown to both registries (possibly a custom component). " + ComponentInfoHint
				};
			}
			suggestions.Add(suggestion);
		}

		return suggestions
			.OrderBy(s => s.SourceType, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>Finds the first equivalence rule whose web type list contains <paramref name="webType"/>.</summary>
	private static ComponentEquivalenceRule FindRule(WebToMobilePageConversionRules rules, string webType) {
		if (rules.Components is null) {
			return null;
		}
		foreach (ComponentEquivalenceRule rule in rules.Components) {
			if (rule?.Web is not null &&
				rule.Web.Any(w => string.Equals(w, webType, StringComparison.OrdinalIgnoreCase))) {
				return rule;
			}
		}
		return null;
	}

	private static ComponentMappingCategory ParseCategory(string category) =>
		Enum.TryParse(category, ignoreCase: true, out ComponentMappingCategory parsed)
			? parsed
			: ComponentMappingCategory.RequiresManualDecision;

	/// <summary>
	/// When a many→one rule has its primary web type and at least one secondary web type present on the
	/// page, explains that the secondary components are merged into the single mobile component produced
	/// from the primary web component.
	/// </summary>
	private static string BuildPrimaryWebMergeNote(ComponentEquivalenceRule rule, HashSet<string> presentTypes) {
		if (rule?.Web is null || string.IsNullOrWhiteSpace(rule.PrimaryWeb)) {
			return null;
		}
		List<string> present = rule.Web.Where(presentTypes.Contains).ToList();
		if (present.Count <= 1) {
			return null;
		}
		string mobile = rule.Mobile is { Count: > 0 } ? rule.Mobile[0] : "the mobile component";
		IEnumerable<string> secondary = present.Where(t => !string.Equals(t, rule.PrimaryWeb, StringComparison.OrdinalIgnoreCase));
		return $"Many→one: build a single mobile {mobile} from the primary web component {rule.PrimaryWeb}; " +
			$"merge in properties from {string.Join(", ", secondary)} (do not emit them as separate components).";
	}

	/// <summary>
	/// Collects the distinct suggested mobile types (across all suggestions) and emits a compact inline
	/// contract for each known mobile type so the model can build values without extra round-trips.
	/// </summary>
	private static List<MobileComponentContract> BuildMobileContracts(
		IReadOnlyList<ComponentSuggestion> suggestions,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType) {
		var contracts = new List<MobileComponentContract>();
		if (mobileByType is null) {
			return contracts;
		}
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentSuggestion suggestion in suggestions) {
			foreach (string mobileType in suggestion.SuggestedMobileTypes) {
				if (string.IsNullOrWhiteSpace(mobileType) || !seen.Add(mobileType)) {
					continue;
				}
				if (!mobileByType.TryGetValue(mobileType, out ComponentRegistryEntry entry)) {
					continue;
				}
				contracts.Add(new MobileComponentContract {
					ComponentType = mobileType,
					Container = entry.Container,
					Description = entry.Description,
					AllowedProperties = BuildAllowedPropertyNames(entry),
					Example = entry.Example,
					DesignerDefaults = entry.DesignerDefaults
				});
			}
		}
		return contracts;
	}

	private static IReadOnlyList<string> BuildAllowedPropertyNames(ComponentRegistryEntry entry) {
		var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		if (entry is null) {
			return [];
		}
		if (entry.Properties is not null) {
			foreach (string key in entry.Properties.Keys) {
				allowed.Add(key);
			}
		}
		if (entry.Inputs is not null) {
			foreach (string key in entry.Inputs.Keys) {
				allowed.Add(key);
			}
		}
		return allowed.ToList();
	}

	/// <summary>Builds the web→mobile container correspondence from the matched template rule.</summary>
	private static IReadOnlyList<ContainerMapEntry> BuildContainerMap(TemplateMappingRule rule) {
		if (rule?.Containers is null || rule.Containers.Count == 0) {
			return [];
		}
		var entries = new List<ContainerMapEntry>();
		foreach (ContainerMappingRule c in rule.Containers) {
			if (!string.IsNullOrWhiteSpace(c?.Web) && !string.IsNullOrWhiteSpace(c.Mobile)) {
				entries.Add(new ContainerMapEntry { Web = c.Web, Mobile = c.Mobile, Note = c.Note });
			}
		}
		return entries;
	}

	private static List<string> CollectWebOnlySections(PageBundleInfo bundle) {
		var sections = new List<string>();
		if (HasContent(bundle.Handlers, "[]")) {
			sections.Add("handlers");
		}
		if (HasContent(bundle.Validators, "{}")) {
			sections.Add("validators");
		}
		if (HasContent(bundle.Converters, "{}")) {
			sections.Add("converters");
		}
		return sections;
	}

	private static List<string> CollectDataSources(PageBundleInfo bundle) {
		var names = new List<string>();
		if (bundle.ModelConfig is null) {
			return names;
		}
		JObject modelConfig;
		try {
			modelConfig = JObject.Parse(bundle.ModelConfig.ToJsonString());
		} catch {
			return names;
		}
		if (modelConfig["dataSources"] is JObject dataSources) {
			foreach (JProperty prop in dataSources.Properties()) {
				names.Add(prop.Name);
			}
		}
		return names;
	}

	/// <summary>
	/// Returns the source page's merged modelConfig as-is (deep-cloned so it is detached from the bundle).
	/// Mobile has identical structural support, so the model applies it verbatim — preserving each
	/// attribute's <c>type</c> (e.g. ForwardReference) and <c>path</c>. Null when there is no model config.
	/// </summary>
	private static JsonNode PassthroughModelConfig(PageBundleInfo bundle) =>
		bundle.ModelConfig is { Count: > 0 } ? bundle.ModelConfig.DeepClone() : null;

	/// <summary>
	/// Wraps a full config object into a single ready-to-paste diff: one root merge that applies the whole
	/// config (<c>[{ "operation":"merge", "path":[], "values": &lt;config&gt; }]</c>). Carries the config —
	/// including every attribute's <c>type</c> — verbatim, so the caller pastes it instead of rebuilding the
	/// data-source section by hand. The config is deep-cloned (a JsonNode can have only one parent).
	/// </summary>
	private static JsonNode BuildRootMergeDiff(JsonNode config) =>
		config is null
			? null
			: new JsonArray(new JsonObject {
				["operation"] = "merge",
				["path"] = new JsonArray(),
				["values"] = config.DeepClone()
			});

	/// <summary>
	/// Returns the source page's merged viewModelConfig filtered for mobile: an attribute is removed only
	/// when EVERY component that references it (via a <c>$Attr</c> binding) was dropped from the mobile
	/// page (see <paramref name="elementMap"/>). Attributes with no consumer, or with at least one surviving
	/// consumer, are kept. All other viewModelConfig sections are passed through unchanged.
	/// </summary>
	private static JsonNode BuildMobileViewModelConfig(PageBundleInfo bundle, JArray tree, List<ElementMapEntry> elementMap) {
		if (bundle.ViewModelConfig is not { Count: > 0 }) {
			return null;
		}
		JObject vmc;
		try {
			vmc = JObject.Parse(bundle.ViewModelConfig.ToJsonString());
		} catch {
			return null;
		}
		if (vmc["attributes"] is JObject attributes && attributes.Count > 0) {
			HashSet<string> dropped = new(
				elementMap
					.Where(e => string.Equals(e.Operation, "drop", StringComparison.OrdinalIgnoreCase))
					.Select(e => e.WebName)
					.Where(n => !string.IsNullOrEmpty(n)),
				StringComparer.OrdinalIgnoreCase);
			Dictionary<string, HashSet<string>> consumers = BuildAttrConsumers(tree);
			foreach (JProperty attr in attributes.Properties().ToList()) {
				if (consumers.TryGetValue(attr.Name, out HashSet<string> users)
					&& users.Count > 0
					&& users.All(dropped.Contains)) {
					attr.Remove();
				}
			}
		}
		try {
			return JsonNode.Parse(vmc.ToString());
		} catch {
			return null;
		}
	}

	/// <summary>Maps each attribute name to the set of named components that reference it via a <c>$Attr</c> binding.</summary>
	private static Dictionary<string, HashSet<string>> BuildAttrConsumers(JArray tree) {
		var consumers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		WalkConsumers(tree, consumers);
		return consumers;
	}

	private static void WalkConsumers(JArray nodes, Dictionary<string, HashSet<string>> consumers) {
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				continue;
			}
			string name = node["name"]?.ToString();
			if (!string.IsNullOrEmpty(name)) {
				foreach (string attr in ExtractDollarRefs(node)) {
					if (!consumers.TryGetValue(attr, out HashSet<string> set)) {
						set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						consumers[attr] = set;
					}
					set.Add(name);
				}
			}
			if (node["items"] is JArray items) {
				WalkConsumers(items, consumers);
			}
		}
	}

	private static List<string> BuildConstraints(
		bool multipleDataSources, IReadOnlyList<string> webOnlySections,
		bool hasModelConfig, bool hasViewModelConfig) {
		var constraints = new List<string> {
			"Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff — no AMD, no markers, no define() wrapper.",
			"The mobile template provides the Scaffold root — do NOT add a second Scaffold.",
			"Mobile pages support a SINGLE data source per page.",
			"No handlers, no validators, no custom converters in a mobile body. Re-implement conditional visibility / required / read-only / set-value logic as entity-level business rules (create-entity-business-rule). Reference only OOTB converters inline in binding expressions.",
			"Use only mobile-registered component types (get-component-info schema-type \"mobile\")."
		};
		if (hasModelConfig) {
			constraints.Add(
				"Use the provided modelConfigDiff VERBATIM as the page's modelConfigDiff (it is a single root merge of " +
				"the full modelConfig). Do NOT hand-build the data-source section and NEVER source it from a pre-existing " +
				"or reference mobile body — that is how an attribute's \"type\" gets dropped, which makes its binding " +
				"unresolvable in Mobile Designer (\"Item with the path … not found\"). Keep every attribute and all of " +
				"its properties exactly as provided.");
		}
		if (hasViewModelConfig) {
			constraints.Add(
				"viewModelConfig is structurally supported on mobile; the provided block already removed attributes " +
				"used only by unsupported components. Apply it via viewModelConfigDiff and reference only OOTB mobile " +
				"converters — a definitive mobile converter list is forthcoming; flag any custom converter for manual review.");
		}
		if (multipleDataSources) {
			constraints.Add("The source page declares MULTIPLE data sources — keep only the primary one on the mobile page and review the rest.");
		}
		if (webOnlySections is { Count: > 0 }) {
			constraints.Add($"The source page carries web-only section(s): {string.Join(", ", webOnlySections)}. They cannot be transferred to a mobile body — re-implement the supported behavior as entity-level business rules.");
		}
		return constraints;
	}

	private static List<string> BuildNextSteps(bool hasDataSections) {
		var steps = new List<string> {
			"Read get-guidance with name \"freedom-page-web-to-mobile-conversion\".",
			"Create the target mobile page from recommendedMobileTemplate with create-page (it provides the Scaffold root).",
			"Build the mobile body by iterating elementMap (one entry per source element) — do NOT infer merge-vs-insert from containerMap: operation=merge → reuse the template element mobileName (no insert); operation=insert → insert mobileType into parentName/propertyName and, if captionResource is present, register key=sourceValue via update-page resources; operation=relocate-children → do not recreate the container; its children are placed in parentName (each child entry carries that parentName); operation=drop → skip it. Fill each component's values from the matching mobileContracts entry (call get-component-info schema-type \"mobile\" only when more detail is needed).",
			"For every insert, paste elementMap[].mobileValues as the component's values VERBATIM — it already carries the type and EVERY source property the mobile component supports (including the field caption). Never drop a supported property. Then add ONLY the value binding (control, or value for lookups), which is left out on purpose. validate-page is the backstop: it rejects an insert that drops a required property (e.g. a field caption, or a lookup-path attribute's type) and update-page refuses to save."
		};
		if (hasDataSections) {
			steps.Add("Paste the provided modelConfigDiff and viewModelConfigDiff VERBATIM as the page's modelConfigDiff / viewModelConfigDiff (each is a single root merge carrying the full config). Do NOT rebuild them by hand and never copy the data-source section from an existing body — keep every attribute's type and path.");
		}
		steps.Add("Validate the body with validate-page; resolve any findings.");
		steps.Add("Persist with update-page, then open the result in Freedom UI Mobile Designer for final review.");
		return steps;
	}

	private static bool HasContent(string section, string empty) =>
		!string.IsNullOrWhiteSpace(section) &&
		!string.Equals(section.Trim(), empty, StringComparison.Ordinal);

	// ── Instance-level element map ────────────────────────────────────────────────────────────

	/// <summary>Carries the read-only inputs of the element-map pass so the recursion stays terse.</summary>
	private sealed record ElementMapContext(
		IReadOnlyDictionary<string, string> Map,
		IReadOnlySet<string> MobileTypes,
		IReadOnlyDictionary<string, ComponentRegistryEntry> MobileByType,
		WebToMobilePageConversionRules Rules,
		IReadOnlyDictionary<string, string> AttrToDs,
		IReadOnlyDictionary<string, string> AttrToColumn,
		IReadOnlySet<string> DataSources,
		string PrimaryDs,
		JObject Resources,
		string RelocateTarget,
		List<ElementMapEntry> Out);

	/// <summary>
	/// Produces one <see cref="ElementMapEntry"/> per named element of the resolved tree, deciding
	/// merge / insert / drop / relocate-children. Pure: reads only the supplied bundle-derived data.
	/// </summary>
	private static List<ElementMapEntry> BuildElementMap(
		JArray tree,
		IReadOnlyDictionary<string, string> map,
		IReadOnlySet<string> mobileTypes,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		WebToMobilePageConversionRules rules,
		IReadOnlyDictionary<string, string> attrToDs,
		IReadOnlyDictionary<string, string> attrToColumn,
		IReadOnlySet<string> dataSources,
		string primaryDs,
		JObject resources) {
		var ctx = new ElementMapContext(map, mobileTypes, mobileByType ?? new Dictionary<string, ComponentRegistryEntry>(),
			rules, attrToDs, attrToColumn, dataSources, primaryDs, resources, RelocateTargetFor(map), []);
		WalkElements(ctx, tree, mobileParentName: null, parentIsTabs: false);
		return ctx.Out;
	}

	private static void WalkElements(ElementMapContext ctx, JArray nodes, string mobileParentName, bool parentIsTabs) {
		bool firstPageTabConsumed = false;
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				continue;
			}
			string name = node["name"]?.ToString();
			string type = node["type"]?.ToString();
			JArray items = node["items"] as JArray;

			// Anonymous wrapper: no entry, but recurse preserving the parent context.
			if (string.IsNullOrEmpty(name)) {
				if (items is not null) {
					WalkElements(ctx, items, mobileParentName, parentIsTabs);
				}
				continue;
			}

			bool isContainer = (items is { Count: > 0 }) || IsLayoutContainer(type, name, null, ctx.MobileByType);

			// 1. merge — element is a template twin (provided by the mobile template). Recurse so its
			//    children get their own entries (parent = the template element).
			if (ctx.Map.TryGetValue(name, out string twinMobileName)) {
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "merge", MobileName = twinMobileName,
					MobileType = ctx.MobileTypes.Contains(type ?? "") ? type : null,
					Reason = TwinReason(name)
				});
				if (items is not null) {
					WalkElements(ctx, items, twinMobileName, string.Equals(twinMobileName, "Tabs", StringComparison.OrdinalIgnoreCase));
				}
				continue;
			}

			// The first page-specific container directly under the mobile Tabs is the general tab.
			bool isGeneralTab = false;
			if (parentIsTabs && isContainer) {
				isGeneralTab = !firstPageTabConsumed;
				firstPageTabConsumed = true;
			}

			if (isContainer) {
				bool typeSupported = !string.IsNullOrEmpty(type) && ctx.MobileTypes.Contains(type);

				// 3. relocate-children — the general (first) tab (already present on the mobile template)
				//    or a container type with no mobile equivalent: the wrapper is not recreated; its
				//    children are placed directly in the target container (children carry that parentName).
				if (isGeneralTab || !typeSupported) {
					string target = isGeneralTab ? ctx.RelocateTarget : ResolveParent(ctx, mobileParentName);
					ctx.Out.Add(new ElementMapEntry {
						WebName = name, WebType = Nz(type), Operation = "relocate-children", ParentName = target,
						Reason = isGeneralTab
							? $"general (first) tab — its children are placed directly in the mobile {target} (no duplicate tab)"
							: $"container type '{type}' has no mobile equivalent — its children are placed in {target}"
					});
					if (items is not null) {
						WalkElements(ctx, items, target, parentIsTabs: false);
					}
					continue;
				}

				// 2. insert — mobile-supported container; always emitted (even if it ends up empty —
				//    unsupported children simply drop and the user can remove an empty container).
				CaptionResource containerCaption = ResolveCaptionResource(ctx, node, name);
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = type,
					ParentName = ResolveParent(ctx, mobileParentName), PropertyName = "items",
					CaptionResource = containerCaption,
					MobileValues = BuildMobileValues(ctx, node, name, type, containerCaption),
					Reason = "container; mobile-supported"
				});
				if (items is not null) {
					WalkElements(ctx, items, name, parentIsTabs: false);
				}
				continue;
			}

			// leaf — drop only when not transferable (foreign data source or no mobile type).
			if (NodeReferencedForeignDs(ctx, node) is { } foreignDs) {
				ctx.Out.Add(Drop(name, type, $"multi-data-source ({foreignDs})"));
				continue;
			}
			bool leafSupported = !string.IsNullOrEmpty(type) && ctx.MobileTypes.Contains(type);
			string leafMobileType = leafSupported ? type : FindRule(ctx.Rules, type)?.Mobile?.FirstOrDefault();
			if (string.IsNullOrEmpty(leafMobileType)) {
				ctx.Out.Add(Drop(name, type, $"type '{type}' not in mobile registry"));
				continue;
			}
			CaptionResource leafCaption = ResolveCaptionResource(ctx, node, name);
			ctx.Out.Add(new ElementMapEntry {
				WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = leafMobileType,
				ParentName = ResolveParent(ctx, mobileParentName), PropertyName = "items",
				CaptionResource = leafCaption,
				MobileValues = BuildMobileValues(ctx, node, name, leafMobileType, leafCaption),
				Reason = "field/leaf; mobile-supported"
			});
		}
	}

	/// <summary>Returns a referenced data source other than the primary one (multi-data-source), or null.</summary>
	private static string NodeReferencedForeignDs(ElementMapContext ctx, JObject node) {
		if (string.IsNullOrEmpty(ctx.PrimaryDs)) {
			return null;
		}
		var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (node["dataSourceName"]?.ToString() is { Length: > 0 } dsn) {
			referenced.Add(dsn);
		}
		foreach (string token in ExtractDollarRefs(node)) {
			if (ctx.AttrToDs.TryGetValue(token, out string ds)) {
				referenced.Add(ds);
			} else if (ctx.DataSources.Contains(token)) {
				referenced.Add(token);
			}
		}
		return referenced.FirstOrDefault(ds => ctx.DataSources.Contains(ds) && !string.Equals(ds, ctx.PrimaryDs, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Extracts <c>$Token</c> binding references from a node's own properties (excluding child items).</summary>
	private static IEnumerable<string> ExtractDollarRefs(JObject node) {
		var clone = (JObject)node.DeepClone();
		clone.Remove("items");
		string json = clone.ToString(Newtonsoft.Json.Formatting.None);
		foreach (Match match in Regex.Matches(json, @"\$([A-Za-z_][A-Za-z0-9_]*)")) {
			yield return match.Groups[1].Value;
		}
	}

	private static CaptionResource ResolveCaptionResource(ElementMapContext ctx, JObject node, string mobileName) {
		string caption = node["caption"]?.ToString();
		if (string.IsNullOrEmpty(caption)) {
			return null;
		}
		const string prefix = "$Resources.Strings.";
		string sourceValue = caption;
		if (caption.StartsWith(prefix, StringComparison.Ordinal)) {
			string key = caption[prefix.Length..];
			sourceValue = ResolveResourceString(ctx.Resources, key) ?? key;
		}
		return new CaptionResource { Key = mobileName + "_caption", SourceValue = sourceValue };
	}

	/// <summary>
	/// The ready-to-use mobile <c>label</c> binding for an inserted FIELD component (a mobile field renders
	/// its caption only via <c>label</c>). Null for non-field types. Prefers the source field's caption
	/// (<c>$Resources.Strings.&lt;name&gt;_caption</c>, registered via <paramref name="caption"/>); otherwise
	/// falls back to the platform auto-provided column-code resource (<c>$Resources.Strings.&lt;column&gt;</c>),
	/// or the element name when the bound column cannot be resolved.
	/// </summary>
	private static string ResolveFieldLabel(ElementMapContext ctx, JObject node, string mobileName, string mobileType, CaptionResource caption) {
		if (string.IsNullOrEmpty(mobileType) || !SchemaValidationService.StandardFieldComponentTypes.Contains(mobileType)) {
			return null;
		}
		if (caption is not null) {
			return "$Resources.Strings." + caption.Key;
		}
		string column = ResolveBoundColumn(ctx, node);
		return "$Resources.Strings." + (column ?? mobileName);
	}

	/// <summary>
	/// Source-node properties never copied into the prebuilt mobile <c>values</c>: structural keys
	/// (<c>items</c>/<c>name</c>/<c>type</c>), the web-only data-source router (<c>dataSourceName</c>), and the
	/// value binding (<c>control</c>/<c>value</c>) — the binding is a type-specific rename (e.g. a mobile
	/// ComboBox must bind via <c>value</c>; <c>control</c> needs <c>items</c> or it crashes) and is left to the
	/// caller to add. Everything else the mobile component supports is carried verbatim.
	/// </summary>
	private static readonly HashSet<string> ExcludedSourceProps = new(StringComparer.OrdinalIgnoreCase) {
		"items", "name", "type", "dataSourceName", "control", "value"
	};

	/// <summary>
	/// Builds the prebuilt, ready-to-paste mobile <c>values</c> for an inserted component (universal rule:
	/// never drop a property the mobile component supports). Copy-and-prune: start from the source node,
	/// keep every property whose name is a valid mobile property/input (per the mobile registry), drop the
	/// rest; set <c>type</c>; for field components synthesize <c>label</c> from the caption / column. The
	/// value binding is intentionally omitted (see <see cref="ExcludedSourceProps"/>). Returns null for an
	/// unknown mobile type.
	/// </summary>
	private static JsonNode BuildMobileValues(ElementMapContext ctx, JObject node, string mobileName, string mobileType, CaptionResource caption) {
		if (string.IsNullOrEmpty(mobileType)) {
			return null;
		}
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (ctx.MobileByType.TryGetValue(mobileType, out ComponentRegistryEntry entry)) {
			foreach (string prop in BuildAllowedPropertyNames(entry)) {
				allowed.Add(prop);
			}
		}
		var values = new JObject { ["type"] = mobileType };
		foreach (JProperty prop in node.Properties()) {
			if (ExcludedSourceProps.Contains(prop.Name)) {
				continue;
			}
			if (allowed.Contains(prop.Name)) {
				values[prop.Name] = prop.Value.DeepClone();
			}
		}
		string label = ResolveFieldLabel(ctx, node, mobileName, mobileType, caption);
		if (!string.IsNullOrEmpty(label)) {
			values["label"] = label;
		}
		try {
			return JsonNode.Parse(values.ToString(Newtonsoft.Json.Formatting.None));
		} catch {
			return null;
		}
	}

	/// <summary>The bound column code of a field node: the first <c>$ref</c> that maps to a declared attribute's column.</summary>
	private static string ResolveBoundColumn(ElementMapContext ctx, JObject node) {
		foreach (string token in ExtractDollarRefs(node)) {
			if (ctx.AttrToColumn.TryGetValue(token, out string column) && !string.IsNullOrEmpty(column)) {
				return column;
			}
		}
		return null;
	}

	private static string ResolveResourceString(JObject resources, string key) {
		if (resources?[key] is not { } value) {
			return null;
		}
		if (value is JObject cultures) {
			return (cultures["en-US"] ?? cultures.Properties().FirstOrDefault()?.Value)?.ToString();
		}
		return value.ToString();
	}

	private static string ResolveParent(ElementMapContext ctx, string mobileParentName) =>
		!string.IsNullOrEmpty(mobileParentName) ? mobileParentName : ctx.RelocateTarget;

	/// <summary>The mobile container surviving children relocate into; prefers profile/general, else MainContainer.</summary>
	private static string RelocateTargetFor(IReadOnlyDictionary<string, string> map) {
		var values = new HashSet<string>(map.Values, StringComparer.OrdinalIgnoreCase);
		foreach (string preferred in new[] { "AreaProfileContainer", "GeneralTabContainer", "MainContainer" }) {
			if (values.Contains(preferred)) {
				return preferred;
			}
		}
		return "MainContainer";
	}

	private static ElementMapEntry Drop(string name, string type, string reason) =>
		new() { WebName = name, WebType = Nz(type), Operation = "drop", Reason = reason };

	private static string TwinReason(string name) =>
		name.Contains("Attachment", StringComparison.OrdinalIgnoreCase)
			? "provided by the mobile template (merge); review the attachments data source — retarget it to the entity's file object."
			: "provided by the mobile template (merge into the template's element).";

	private static string Nz(string value) => string.IsNullOrEmpty(value) ? null : value;

	private static Dictionary<string, string> BuildAttrToDs(PageBundleInfo bundle) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (bundle.ViewModelConfig is null) {
			return map;
		}
		JObject vmc;
		try {
			vmc = JObject.Parse(bundle.ViewModelConfig.ToJsonString());
		} catch {
			return map;
		}
		if (vmc["attributes"] is JObject attributes) {
			foreach (JProperty attr in attributes.Properties()) {
				string path = (attr.Value as JObject)?["modelConfig"]?["path"]?.ToString();
				if (!string.IsNullOrEmpty(path)) {
					int dot = path.IndexOf('.');
					map[attr.Name] = dot > 0 ? path[..dot] : path;
				}
			}
		}
		return map;
	}

	/// <summary>Maps each attribute name to its column code (the segment after the last dot of <c>modelConfig.path</c>).</summary>
	private static Dictionary<string, string> BuildAttrToColumn(PageBundleInfo bundle) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (bundle.ViewModelConfig is null) {
			return map;
		}
		JObject vmc;
		try {
			vmc = JObject.Parse(bundle.ViewModelConfig.ToJsonString());
		} catch {
			return map;
		}
		if (vmc["attributes"] is JObject attributes) {
			foreach (JProperty attr in attributes.Properties()) {
				string path = (attr.Value as JObject)?["modelConfig"]?["path"]?.ToString();
				if (!string.IsNullOrEmpty(path)) {
					int dot = path.LastIndexOf('.');
					map[attr.Name] = dot >= 0 ? path[(dot + 1)..] : path;
				}
			}
		}
		return map;
	}

	private static string ResolvePrimaryDs(Dictionary<string, string> attrToDs, List<string> dataSources) {
		string pds = dataSources.FirstOrDefault(d => string.Equals(d, "PDS", StringComparison.OrdinalIgnoreCase));
		if (pds is not null) {
			return pds;
		}
		if (attrToDs.Count > 0) {
			return attrToDs.Values
				.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
				.OrderByDescending(g => g.Count())
				.First().Key;
		}
		return dataSources.FirstOrDefault();
	}

	private static JObject ParseResources(PageBundleInfo bundle) {
		try {
			return bundle.Resources?.Strings is { } strings ? JObject.Parse(strings.ToJsonString()) : null;
		} catch {
			return null;
		}
	}
}
