namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

	/// <summary>Mobile container a positional insert falls back to when the mobile anchor's parent is unknown.</summary>
	private const string PositionalFallbackParent = "MainContainer";

	/// <summary>
	/// A positional container rule parsed from a <c>&lt;webAnchor&gt;:top</c> / <c>:bottom</c> template entry.
	/// Content that is a sibling of the web <paramref name="WebAnchor"/> container — appearing above it — is
	/// placed above the mobile <paramref name="MobileAnchor"/> (in that anchor's parent container); content
	/// below it is placed below. Both the <c>:top</c> and <c>:bottom</c> entries of an anchor resolve to the
	/// same mobile parent (the anchor's parent); the side is inferred from the sibling's position.
	/// </summary>
	public sealed record PositionalPlacement(string WebAnchor, string MobileAnchor);

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
		SectionRegistrationInfo sectionRegistration = null,
		PageBusinessRuleProbeResult pageBusinessRulesProbe = null,
		IReadOnlySet<string> templateComponentNames = null,
		IReadOnlyDictionary<string, ComponentMappingRule> componentNameMap = null,
		IReadOnlyList<PositionalPlacement> positionalPlacements = null,
		IReadOnlyDictionary<string, string> mobileContainerParents = null) {
		ArgumentNullException.ThrowIfNull(bundle);
		ArgumentNullException.ThrowIfNull(mobileTypes);
		ArgumentNullException.ThrowIfNull(webTypes);
		ArgumentNullException.ThrowIfNull(rules);

		IReadOnlyDictionary<string, string> map =
			containerNameMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyDictionary<string, ComponentMappingRule> componentMap =
			componentNameMap ?? new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase);

		// 0. Filter out the web template's own components at read time. The merged tree carries the
		//    chrome the source page inherits from its web template (e.g. PageWithTabsFreedomTemplate:
		//    MainHeader / TitleContainer / BackButton / PageTitle / …) — the mobile template already
		//    provides those (Scaffold + header). Only the page's DELTA over its web template is
		//    converted. Container twins listed in the containerMap are kept (they are merge targets).
		JArray tree = bundle.ViewConfig is null ? new JArray() : JArray.Parse(bundle.ViewConfig.ToJsonString());
		bool templatePruned = false;
		if (templateComponentNames is { Count: > 0 }) {
			tree = PruneTemplateComponents(tree, map, componentMap, templateComponentNames);
			templatePruned = true;
		}

		// 1. Walk the merged tree into a flat structure (names, types, parents, container flags) and
		//    record, per web type, the source-component names that carry it.
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
		// Request (action) conversion: as the element map prebuilds each insert's mobileValues, the
		// event-binding requests (a button's clicked, etc.) are remapped/stripped/flagged in-place and
		// recorded into these collectors for the advisory requestConversions summary.
		IReadOnlyDictionary<string, RequestMappingRule> requestMap = BuildRequestMap(rules);
		var convertedRequests = new List<ConvertedRequest>();
		var droppedRequests = new List<DroppedRequest>();
		var flaggedRequests = new List<FlaggedRequest>();
		var sourceLayouts = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
		var gridContainerColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		// Positional placement: a web anchor container (e.g. CardContentWrapper) whose siblings above it go
		// above the mobile anchor (Tabs) and below it go below — realized by inserting those siblings into the
		// mobile anchor's PARENT container with an index. Resolve each web anchor to that mobile parent.
		IReadOnlyDictionary<string, string> positionalParentByAnchor =
			ResolvePositionalParents(positionalPlacements, mobileContainerParents);
		List<ElementMapEntry> elementMap = BuildElementMap(
			tree, map, componentMap, mobileTypes, mobileByType, webByType, rules, attrToDs, attrToColumn, dataSourceSet, primaryDs, resources,
			requestMap, convertedRequests, droppedRequests, flaggedRequests, sourceLayouts, gridContainerColumns, positionalParentByAnchor);
		RequestConversionInfo requestConversions = BuildRequestConversionInfo(convertedRequests, droppedRequests, flaggedRequests);

		// Adaptive (per-breakpoint) layout for multi-column crt.GridContainer: on the phone (small) collapse
		// to a single column and stack; on tablet/desktop (medium/large) keep the web columns and per-child
		// placement. A 1-column grid gets no adaptive. Both the container columns and each child's
		// layoutConfig.adaptive are baked into mobileValues deterministically.
		List<AdaptiveLayoutGroup> adaptiveLayout = BuildAdaptiveLayout(elementMap, sourceLayouts, gridContainerColumns);

		// 6. Data sections applied to the mobile body verbatim/filtered (identical structural support on
		//    mobile): modelConfig is carried over as-is (preserving attribute types like ForwardReference);
		//    viewModelConfig drops attributes used only by dropped components.
		JsonNode modelConfig = PassthroughModelConfig(bundle);
		JsonNode viewModelConfig = BuildMobileViewModelConfig(bundle, tree, elementMap);
		// Prebuilt, ready-to-paste diffs (single root merge) so the caller never hand-builds the
		// data-source section (the step where attribute `type` was being dropped).
		JsonNode modelConfigDiff = BuildRootMergeDiff(modelConfig);
		JsonNode viewModelConfigDiff = BuildRootMergeDiff(viewModelConfig);

		// 7. Page-level business rules: carry each rule's condition (operand paths remapped from the source
		//    DS column path to the mobile viewModel attribute name) and only the actions that survive on
		//    mobile; drop a rule whose every action drops (object-level rules are untouched).
		PageBusinessRuleConversionInfo pageBusinessRules = ConvertPageBusinessRules(pageBusinessRulesProbe, elementMap, bundle?.ViewModelConfig);

		// 8. Every localized string the converted body references (top-level captions AND nested tokens such
		//    as config.title / text.template), resolved to its text — so the caller registers them all.
		IReadOnlyDictionary<string, string> resourceStrings = CollectResourceStrings(elementMap, modelConfig, viewModelConfig, resources);

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
			PageBusinessRules = pageBusinessRules,
			RequestConversions = requestConversions,
			AdaptiveLayout = adaptiveLayout.Count > 0 ? adaptiveLayout : null,
			ResourceStrings = resourceStrings.Count > 0 ? resourceStrings : null,
			Constraints = BuildConstraints(webOnly, modelConfig is not null, viewModelConfig is not null, adaptiveLayout.Count > 0, templatePruned),
			NextSteps = BuildNextSteps(modelConfig is not null || viewModelConfig is not null, adaptiveLayout.Count > 0),
			GuidanceArticle = GuidanceArticleName,
			SuggestedTargetSchemaName = suggestedTarget
		};
	}

	/// <summary>
	/// Converts the source page's PAGE-level business rules for the mobile page (advisory).
	/// Page rules carry only element actions (hide / show / make-editable / read-only / required /
	/// optional). An action converts only for the referenced elements that survive on mobile (elementMap
	/// operation merge/insert), with their names remapped web→mobile and only the survivors kept. A rule
	/// with no surviving action is dropped together with its condition; otherwise the condition is carried
	/// verbatim — EVERY operand type is supported in a mobile page-rule condition (attribute, const, formula,
	/// system-value, system-setting) — with each operand's attribute path remapped from the source DS column
	/// path to the mobile viewModel attribute name, so the rule is ready for create-page-business-rule. Returns
	/// null when no probe ran.
	/// </summary>
	internal static PageBusinessRuleConversionInfo ConvertPageBusinessRules(
		PageBusinessRuleProbeResult probe,
		IReadOnlyList<ElementMapEntry> elementMap,
		JsonNode viewModelConfig = null) {
		if (probe is null) {
			return null;
		}
		if (!probe.ProbeOk) {
			return new PageBusinessRuleConversionInfo { ProbeOk = false, Note = probe.Note };
		}

		// Elements that survive on mobile: merge (template twin) or insert. Map web name -> mobile name.
		var survivors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in elementMap ?? []) {
			if (string.IsNullOrWhiteSpace(entry?.WebName)) {
				continue;
			}
			if (string.Equals(entry.Operation, "merge", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(entry.Operation, "insert", StringComparison.OrdinalIgnoreCase)) {
				survivors[entry.WebName] = string.IsNullOrWhiteSpace(entry.MobileName) ? entry.WebName : entry.MobileName;
			}
		}

		// A condition operand references an attribute by its DATA path (e.g. the source stores "PDS.QualifiedAccount"
		// or the column "QualifiedAccount"), but create-page-business-rule expects the page's viewModel ATTRIBUTE
		// NAME (e.g. "Parameter_3pxm4wn", whose modelConfig.path is "PDS.QualifiedAccount"). Build the reverse map.
		AttributePathResolver pathResolver = BuildAttributePathResolver(viewModelConfig);

		var converted = new List<ConvertedPageBusinessRule>();
		var dropped = new List<DroppedPageBusinessRule>();

		foreach (SourcePageBusinessRule rule in probe.Rules ?? []) {
			var actions = new JsonArray();
			foreach (SourcePageRuleAction action in rule.Actions ?? []) {
				List<string> mobileItems = (action.ElementItems ?? [])
					.Where(survivors.ContainsKey)
					.Select(name => survivors[name])
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
				if (mobileItems.Count > 0) {
					actions.Add(new JsonObject {
						["type"] = action.ActionType,
						["items"] = new JsonArray(mobileItems.Select(i => (JsonNode)i).ToArray())
					});
				}
				// else: every referenced element drops → this action does not convert.
			}

			if (actions.Count == 0) {
				dropped.Add(new DroppedPageBusinessRule {
					Caption = rule.Caption,
					Reason = "No action converts to mobile: every referenced element is dropped or unsupported on mobile."
				});
				continue;
			}

			// The condition ALWAYS converts, verbatim — every operand type (attribute, const, formula,
			// system-value, system-setting …) is supported in a mobile page-rule condition. Only actions are
			// filtered (by surviving components, above). Operand attribute paths are remapped from the source
			// DS column path to the mobile viewModel attribute name.
			JsonNode conditionClone = rule.Condition?.DeepClone();
			RemapConditionAttributePaths(conditionClone, pathResolver);
			converted.Add(new ConvertedPageBusinessRule {
				Caption = rule.Caption,
				Rule = new JsonObject {
					["caption"] = rule.Caption,
					["condition"] = conditionClone,
					["actions"] = actions
				}
			});
		}

		return new PageBusinessRuleConversionInfo {
			ProbeOk = true,
			Note = probe.Note,
			ConvertedRules = converted,
			DroppedRules = dropped
		};
	}

	/// <summary>
	/// Reverse lookup for condition operand remapping: given a source data path it returns the mobile viewModel
	/// attribute NAME. Matches (in order): an exact attribute name (already correct), the full
	/// <c>"&lt;DS&gt;.&lt;Column&gt;"</c> modelConfig path, or the bare column name when it is unambiguous.
	/// </summary>
	private sealed record AttributePathResolver(
		IReadOnlyDictionary<string, string> ByPath,
		IReadOnlyDictionary<string, string> ByColumn,
		IReadOnlySet<string> AttributeNames) {

		public string Resolve(string sourcePath) {
			if (string.IsNullOrWhiteSpace(sourcePath) || AttributeNames.Contains(sourcePath)) {
				return sourcePath;
			}
			if (ByPath.TryGetValue(sourcePath, out string byFullPath)) {
				return byFullPath;
			}
			string column = sourcePath.Contains('.') ? sourcePath[(sourcePath.LastIndexOf('.') + 1)..] : sourcePath;
			return ByColumn.TryGetValue(column, out string byColumn) ? byColumn : sourcePath;
		}
	}

	/// <summary>
	/// Builds the source-path → viewModel-attribute-name resolver from the (mobile) viewModelConfig. Each
	/// top-level attribute's <c>modelConfig.path</c> (e.g. <c>"PDS.QualifiedAccount"</c>) is indexed both by the
	/// full path and by the bare column; a column shared by more than one attribute is dropped from the column
	/// index (ambiguous) so it never remaps to the wrong attribute.
	/// </summary>
	private static AttributePathResolver BuildAttributePathResolver(JsonNode viewModelConfig) {
		var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var byColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var ambiguousColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (viewModelConfig is JsonObject root && root["attributes"] is JsonObject attributes) {
			foreach (KeyValuePair<string, JsonNode> attr in attributes) {
				names.Add(attr.Key);
				string path = (attr.Value as JsonObject)?["modelConfig"]?["path"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(path)) {
					continue;
				}
				byPath[path] = attr.Key;
				string column = path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..] : path;
				if (ambiguousColumns.Contains(column)) {
					continue;
				}
				if (byColumn.ContainsKey(column)) {
					byColumn.Remove(column);
					ambiguousColumns.Add(column);
				} else {
					byColumn[column] = attr.Key;
				}
			}
		}
		return new AttributePathResolver(byPath, byColumn, names);
	}

	/// <summary>
	/// Rewrites every AttributeValue operand path inside a (possibly nested) condition group from the source data
	/// path to the mobile viewModel attribute name, in place. Leaves non-attribute operands (Const/SysValue/Formula)
	/// and unresolvable paths untouched — the condition always converts.
	/// </summary>
	private static void RemapConditionAttributePaths(JsonNode conditionNode, AttributePathResolver resolver) {
		if (conditionNode is not JsonObject node) {
			return;
		}
		if (node["conditions"] is JsonArray inner) {
			foreach (JsonNode child in inner) {
				RemapConditionAttributePaths(child, resolver);
			}
		}
		RemapOperandPath(node["leftExpression"], resolver);
		RemapOperandPath(node["rightExpression"], resolver);
	}

	private static void RemapOperandPath(JsonNode expression, AttributePathResolver resolver) {
		if (expression is not JsonObject operand
			|| operand["path"]?.GetValue<string>() is not { } path
			|| string.IsNullOrWhiteSpace(path)) {
			return;
		}
		string type = operand["type"]?.GetValue<string>();
		if (type is not null && !string.Equals(type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			return;
		}
		operand["path"] = resolver.Resolve(path);
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
	/// Collects the names of every named component in a merged viewConfig tree (System.Text.Json).
	/// Used to build the web template's component-name baseline so the source page's inherited chrome
	/// can be filtered out at read time. Case-insensitive.
	/// </summary>
	public static HashSet<string> CollectComponentNames(JsonArray viewConfig) {
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectComponentNames(viewConfig, names);
		return names;
	}

	private static void CollectComponentNames(JsonArray nodes, HashSet<string> names) {
		if (nodes is null) {
			return;
		}
		foreach (JsonNode node in nodes) {
			if (node is not JsonObject obj) {
				continue;
			}
			if (obj.TryGetPropertyValue("name", out JsonNode nameNode) && nameNode is not null) {
				string name = nameNode.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
			if (obj.TryGetPropertyValue("items", out JsonNode itemsNode) && itemsNode is JsonArray items) {
				CollectComponentNames(items, names);
			}
		}
	}

	/// <summary>
	/// Builds a child-name → parent-name map for every named component of a merged viewConfig tree
	/// (System.Text.Json). Used to resolve the mobile parent a positional (<c>:top</c> / <c>:bottom</c>)
	/// insert attaches to — e.g. the mobile <c>Tabs</c> anchor lives in <c>MainContainer</c>, so content
	/// mapped above/below the Tabs is inserted into <c>MainContainer</c>. Case-insensitive.
	/// </summary>
	public static Dictionary<string, string> CollectParentByName(JsonArray viewConfig) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		CollectParentByName(viewConfig, parentName: null, map);
		return map;
	}

	private static void CollectParentByName(JsonArray nodes, string parentName, Dictionary<string, string> map) {
		if (nodes is null) {
			return;
		}
		foreach (JsonNode node in nodes) {
			if (node is not JsonObject obj) {
				continue;
			}
			string name = obj.TryGetPropertyValue("name", out JsonNode nameNode) && nameNode is not null
				? nameNode.GetValue<string>()
				: null;
			if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parentName) && !map.ContainsKey(name)) {
				map[name] = parentName;
			}
			if (obj.TryGetPropertyValue("items", out JsonNode itemsNode) && itemsNode is JsonArray items) {
				CollectParentByName(items, string.IsNullOrWhiteSpace(name) ? parentName : name, map);
			}
		}
	}

	/// <summary>
	/// Removes from the merged page tree every component the source page inherits from its web template
	/// (and the template's base schemas): a node whose name is in <paramref name="baseline"/> is dropped
	/// unless it is a container twin listed in <paramref name="containerNameMap"/> (kept as a merge target).
	/// Surviving (non-baseline) descendants of a dropped node are hoisted up to its parent so no
	/// application-added element is lost. Anonymous wrappers and kept nodes are recursed in place.
	/// </summary>
	private static JArray PruneTemplateComponents(
		JArray nodes,
		IReadOnlyDictionary<string, string> containerNameMap,
		IReadOnlyDictionary<string, ComponentMappingRule> componentMap,
		IReadOnlySet<string> baseline) {
		var result = new JArray();
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				result.Add(token);
				continue;
			}
			string name = node["name"]?.ToString();
			JArray items = node["items"] as JArray;
			// Kept despite being in the baseline: a container twin (merge target) or a component twin
			// (a content element the template maps web→mobile, e.g. the list grid — its merge carries
			// the page's delta, like grid columns, that the conversion needs).
			bool isMappedTwin = !string.IsNullOrEmpty(name)
				&& (containerNameMap.ContainsKey(name) || (componentMap is not null && componentMap.ContainsKey(name)));
			bool isTemplateOwned = !string.IsNullOrEmpty(name)
				&& baseline.Contains(name)
				&& !isMappedTwin;
			if (isTemplateOwned) {
				// Drop the template node itself; hoist any surviving (application) descendants up.
				if (items is not null) {
					foreach (JToken survivor in PruneTemplateComponents(items, containerNameMap, componentMap, baseline)) {
						result.Add(survivor);
					}
				}
				continue;
			}
			if (items is not null) {
				node["items"] = PruneTemplateComponents(items, containerNameMap, componentMap, baseline);
			}
			result.Add(node);
		}
		return result;
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

	/// <summary>
	/// Resolves each positional web anchor to the mobile container its top/bottom siblings insert into:
	/// the mobile anchor's parent (looked up in <paramref name="mobileContainerParents"/>), falling back to
	/// <see cref="PositionalFallbackParent"/> when the parent is unknown. Returns an empty map when there
	/// are no positional placements.
	/// </summary>
	private static IReadOnlyDictionary<string, string> ResolvePositionalParents(
		IReadOnlyList<PositionalPlacement> placements,
		IReadOnlyDictionary<string, string> mobileContainerParents) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (placements is null) {
			return map;
		}
		foreach (PositionalPlacement p in placements) {
			if (p is null || string.IsNullOrWhiteSpace(p.WebAnchor) || string.IsNullOrWhiteSpace(p.MobileAnchor)) {
				continue;
			}
			string parent = mobileContainerParents is not null
				&& mobileContainerParents.TryGetValue(p.MobileAnchor, out string resolved)
				&& !string.IsNullOrWhiteSpace(resolved)
					? resolved
					: PositionalFallbackParent;
			map[p.WebAnchor] = parent;
		}
		return map;
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
				foreach (string attr in ExtractConsumedAttributes(node)) {
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

	private static readonly Regex ResourceStringsRefPattern =
		new(@"\$Resources\.Strings\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

	/// <summary>
	/// Every viewModelConfig attribute a node references — both plain <c>$Attr</c> bindings AND
	/// <c>$Resources.Strings.&lt;attr&gt;</c> label/caption references (the platform auto-provides that
	/// resource from the attribute's bound column, so referencing it USES the attribute). Used to decide
	/// which attributes survive: an attribute is dropped only when EVERY node that references it (in either
	/// form) is itself dropped — so an attribute a surviving element captions off is always kept.
	/// </summary>
	private static IEnumerable<string> ExtractConsumedAttributes(JObject node) {
		var clone = (JObject)node.DeepClone();
		clone.Remove("items");
		string json = clone.ToString(Newtonsoft.Json.Formatting.None);
		foreach (Match match in ResourceStringsRefPattern.Matches(json)) {
			yield return match.Groups[1].Value;
		}
		foreach (Match match in Regex.Matches(json, @"\$([A-Za-z_][A-Za-z0-9_]*)")) {
			yield return match.Groups[1].Value;
		}
	}

	private static List<string> BuildConstraints(
		IReadOnlyList<string> webOnlySections,
		bool hasModelConfig, bool hasViewModelConfig, bool hasAdaptiveLayout, bool templatePruned = false) {
		var constraints = new List<string> {
			"Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff — no AMD, no markers, no define() wrapper.",
			"The mobile template provides the Scaffold root — do NOT add a second Scaffold.",
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
		if (templatePruned) {
			constraints.Add(
				"Components inherited from the source page's web template (and its base templates) are excluded " +
				"from this guide — the mobile template already provides the equivalent header/scaffold chrome. " +
				"Only the page's delta over its web template is converted; do NOT re-add the web header containers.");
		}
		if (webOnlySections is { Count: > 0 }) {
			constraints.Add($"The source page carries web-only section(s): {string.Join(", ", webOnlySections)}. They cannot be transferred to a mobile body — re-implement the supported behavior as entity-level business rules.");
		}
		if (hasAdaptiveLayout) {
			constraints.Add(
				"adaptiveLayout covers every multi-column crt.GridContainer: on the phone (small) it collapses to a " +
				"single column and stacks the children; on tablet/desktop (medium/large) it keeps the web columns and " +
				"per-child placement. A single-column grid gets no adaptive. Both sides are ALREADY baked into " +
				"mobileValues (the container's adaptive columns and each child's layoutConfig.adaptive) — paste " +
				"mobileValues verbatim. Present the layout to the user; they may adjust or decline it.");
		}
		return constraints;
	}

	private static List<string> BuildNextSteps(bool hasDataSections, bool hasAdaptiveLayout) {
		var steps = new List<string> {
			"Read get-guidance with name \"freedom-page-web-to-mobile-conversion\".",
			"Create the target mobile page from recommendedMobileTemplate with create-page (it provides the Scaffold root).",
			"Build the mobile body by iterating elementMap (one entry per source element) — do NOT infer merge-vs-insert from containerMap: operation=merge → reuse the template element mobileName (no insert); operation=insert → insert mobileType into parentName/propertyName and, if captionResource is present, register key=sourceValue via update-page resources; operation=relocate-children → do not recreate the container; its children are placed in parentName (each child entry carries that parentName); operation=drop → skip it. Fill each component's values from the matching mobileContracts entry (call get-component-info schema-type \"mobile\" only when more detail is needed).",
			"For every insert, paste elementMap[].mobileValues as the component's values VERBATIM — it already carries the type and EVERY source property the mobile component supports (including the field caption). Never drop a supported property. Then add ONLY the value binding (control, or value for lookups), which is left out on purpose. validate-page is the backstop: it rejects an insert that drops a required property (e.g. a field caption, or a lookup-path attribute's type) and update-page refuses to save."
		};
		if (hasDataSections) {
			steps.Add("Paste the provided modelConfigDiff and viewModelConfigDiff VERBATIM as the page's modelConfigDiff / viewModelConfigDiff (each is a single root merge carrying the full config). Do NOT rebuild them by hand and never copy the data-source section from an existing body — keep every attribute's type and path.");
		}
		if (hasAdaptiveLayout) {
			steps.Add("Adaptive layout for multi-column grid containers is already baked into mobileValues (container adaptive columns + each child's layoutConfig.adaptive: phone collapses to 1 column, tablet/desktop keep the web columns). Present guide.adaptiveLayout to the user for review; they may adjust or decline it.");
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
		IReadOnlyDictionary<string, ComponentMappingRule> ComponentMap,
		IReadOnlySet<string> MobileTypes,
		IReadOnlyDictionary<string, ComponentRegistryEntry> MobileByType,
		IReadOnlyDictionary<string, ComponentRegistryEntry> WebByType,
		WebToMobilePageConversionRules Rules,
		IReadOnlyDictionary<string, string> AttrToDs,
		IReadOnlyDictionary<string, string> AttrToColumn,
		IReadOnlySet<string> DataSources,
		string PrimaryDs,
		JObject Resources,
		string RelocateTarget,
		List<ElementMapEntry> Out,
		IReadOnlyDictionary<string, RequestMappingRule> RequestMap,
		List<ConvertedRequest> ConvertedRequests,
		List<DroppedRequest> DroppedRequests,
		List<FlaggedRequest> FlaggedRequests,
		Dictionary<string, JObject> SourceLayouts,
		Dictionary<string, int> GridContainerColumns,
		IReadOnlyDictionary<string, string> PositionalParentByAnchor);

	/// <summary>
	/// Produces one <see cref="ElementMapEntry"/> per named element of the resolved tree, deciding
	/// merge / insert / drop / relocate-children. Pure: reads only the supplied bundle-derived data.
	/// </summary>
	private static List<ElementMapEntry> BuildElementMap(
		JArray tree,
		IReadOnlyDictionary<string, string> map,
		IReadOnlyDictionary<string, ComponentMappingRule> componentMap,
		IReadOnlySet<string> mobileTypes,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		IReadOnlyDictionary<string, ComponentRegistryEntry> webByType,
		WebToMobilePageConversionRules rules,
		IReadOnlyDictionary<string, string> attrToDs,
		IReadOnlyDictionary<string, string> attrToColumn,
		IReadOnlySet<string> dataSources,
		string primaryDs,
		JObject resources,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		List<ConvertedRequest> convertedRequests,
		List<DroppedRequest> droppedRequests,
		List<FlaggedRequest> flaggedRequests,
		Dictionary<string, JObject> sourceLayouts,
		Dictionary<string, int> gridContainerColumns,
		IReadOnlyDictionary<string, string> positionalParentByAnchor) {
		var ctx = new ElementMapContext(map,
			componentMap ?? new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase),
			mobileTypes, mobileByType ?? new Dictionary<string, ComponentRegistryEntry>(),
			webByType ?? new Dictionary<string, ComponentRegistryEntry>(),
			rules, attrToDs, attrToColumn, dataSources, primaryDs, resources, RelocateTargetFor(map), [],
			requestMap, convertedRequests, droppedRequests, flaggedRequests, sourceLayouts, gridContainerColumns,
			positionalParentByAnchor ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		WalkElements(ctx, tree, mobileParentName: null);
		return ctx.Out;
	}

	private static void WalkElements(ElementMapContext ctx, JArray nodes, string mobileParentName) {
		// Positional siblings: when this array holds a positional anchor container (e.g. CardContentWrapper),
		// each sibling ABOVE it is placed above the mobile anchor (Tabs) — inserted into the anchor's parent
		// (MainContainer) with an ascending index from 0 — and each sibling BELOW it is appended after.
		IReadOnlyDictionary<string, (string Parent, int? Index)> positional = ResolvePositionalSiblings(ctx, nodes);
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
					WalkElements(ctx, items, mobileParentName);
				}
				continue;
			}

			// A positional sibling of the anchor is rerouted to the mobile anchor's parent (± index).
			bool isPositional = positional.TryGetValue(name, out (string Parent, int? Index) place);

			bool isContainer = (items is { Count: > 0 }) || IsLayoutContainer(type, name, null, ctx.MobileByType);

			// Capture the element's web layoutConfig (grid placement) and, for a grid container, its web
			// column count — the adaptive pass reads both to build the per-breakpoint mobile layout.
			CaptureSource(ctx, name, node);

			// 0. drop — the element triggers a request the Creatio Mobile app does not support (e.g. a button
			//    whose clicked request is web-only). Such a component would be non-functional on mobile, so it
			//    is removed rather than shipped with a dead action.
			if (UnsupportedRequestOf(ctx, node) is { } unsupportedRequest) {
				ctx.Out.Add(Drop(name, type, $"uses request '{unsupportedRequest}' not supported on the Creatio Mobile app"));
				continue;
			}

			// 1. merge — element is a template twin (provided by the mobile template). Recurse so its
			//    children get their own entries (parent = the template element).
			if (ctx.Map.TryGetValue(name, out string twinMobileName)) {
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "merge", MobileName = twinMobileName,
					MobileType = ctx.MobileTypes.Contains(type ?? "") ? type : null,
					Reason = TwinReason(name)
				});
				if (items is not null) {
					WalkElements(ctx, items, twinMobileName);
				}
				continue;
			}

			// 1b. component twin — a content component the template maps web→mobile by NAME (e.g. the list
			//     template's grid "DataTable" → mobile "List"). It is NOT template chrome: it is kept and
			//     configured by merge-by-name. HOW to convert it (e.g. a grid's columns → the list row) is
			//     type-driven — it lives in the general components rule and is surfaced in
			//     componentSuggestions[<type>]; clio hardcodes no component-specific transform here.
			if (ctx.ComponentMap.TryGetValue(name, out ComponentMappingRule compRule)) {
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "merge", MobileName = compRule.Mobile,
					MobileType = ctx.MobileTypes.Contains(type ?? "") ? type : null,
					Reason = ComponentTwinReason(name, type, compRule)
				});
				if (items is not null) {
					WalkElements(ctx, items, compRule.Mobile);
				}
				continue;
			}

			if (isContainer) {
				bool typeSupported = !string.IsNullOrEmpty(type) && ctx.MobileTypes.Contains(type);

				// 3. relocate-children — a container type with no mobile equivalent: the wrapper is not
				//    recreated; its children are placed directly in the target container (children carry
				//    that parentName). A web tab (crt.TabContainer) IS mobile-supported, so it falls through
				//    to the insert below and becomes its OWN new mobile tab (no more general-tab collapsing).
				if (!typeSupported) {
					string target = isPositional ? place.Parent : ResolveParent(ctx, mobileParentName);
					ctx.Out.Add(new ElementMapEntry {
						WebName = name, WebType = Nz(type), Operation = "relocate-children", ParentName = target,
						Reason = $"container type '{type}' has no mobile equivalent — its children are placed in {target}"
					});
					if (items is not null) {
						WalkElements(ctx, items, target);
					}
					continue;
				}

				// 2. insert — mobile-supported container; always emitted (even if it ends up empty —
				//    unsupported children simply drop and the user can remove an empty container). A web tab
				//    inserts into the mobile Tabs as a new tab; a positional sibling inserts into the mobile
				//    anchor's parent (± index) instead of the walk parent.
				CaptionResource containerCaption = ResolveCaptionResource(ctx, node, name);
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = type,
					ParentName = isPositional ? place.Parent : ResolveParent(ctx, mobileParentName), PropertyName = "items",
					Index = isPositional ? place.Index : null,
					CaptionResource = containerCaption,
					MobileValues = BuildMobileValues(ctx, node, name, type, containerCaption),
					Reason = isPositional
						? $"container; placed {(place.Index.HasValue ? "above" : "below")} the mobile Tabs (in {place.Parent})"
						: "container; mobile-supported"
				});
				if (items is not null) {
					WalkElements(ctx, items, name);
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
				ParentName = isPositional ? place.Parent : ResolveParent(ctx, mobileParentName), PropertyName = "items",
				Index = isPositional ? place.Index : null,
				CaptionResource = leafCaption,
				MobileValues = BuildMobileValues(ctx, node, name, leafMobileType, leafCaption),
				Reason = isPositional
					? $"field/leaf; placed {(place.Index.HasValue ? "above" : "below")} the mobile Tabs (in {place.Parent})"
					: "field/leaf; mobile-supported"
			});
		}
	}

	/// <summary>
	/// The reason line for a template-mapped component twin: the rule's business <c>note</c> (what the
	/// element is) plus a pointer to the type-driven conversion detail in <c>componentSuggestions</c>. clio
	/// keeps no component-specific transform — the "how" (e.g. a grid's columns → the list row) is defined
	/// by the general components rule and surfaced there for the model to apply.
	/// </summary>
	private static string ComponentTwinReason(string name, string type, ComponentMappingRule rule) {
		string basis = !string.IsNullOrWhiteSpace(rule.Note) ? rule.Note : $"web '{name}' maps to mobile '{rule.Mobile}'";
		string detail = string.IsNullOrEmpty(type)
			? $"template-provided element — configure '{rule.Mobile}' by merge-by-name (do not insert a duplicate)"
			: $"template-provided element — configure '{rule.Mobile}' by merge-by-name per componentSuggestions[\"{type}\"] (do not insert a duplicate)";
		return $"{basis} — {detail}";
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

	/// <summary>
	/// Resolves the resource a source element's caption references, so the caller can register it on the
	/// mobile page (its raw caption token is carried into mobileValues by the generic copy rule). The web
	/// caption may be a resource token in any form — <c>$Resources.Strings.KEY</c>, <c>#ResourceString(KEY)#</c>,
	/// or <c>#MacrosTemplateString(#ResourceString(KEY)#)#</c>; its KEY is extracted (reusing
	/// <see cref="ResourceStringHelper.ExtractKeys"/>) and looked up in the page's localized strings for its
	/// en-US text. <see cref="CaptionResource.Key"/> is that referenced KEY (matching the carried token), so
	/// registering it makes the token resolve. Returns null when the caption references no resource (a plain
	/// literal — carried as-is — or a data binding such as <c>$HeaderCaption</c>).
	/// </summary>
	private static CaptionResource ResolveCaptionResource(ElementMapContext ctx, JObject node, string mobileName) {
		string caption = node["caption"]?.ToString();
		if (string.IsNullOrEmpty(caption)) {
			return null;
		}
		string sourceKey = ResourceStringHelper.ExtractKeys(caption).FirstOrDefault();
		if (string.IsNullOrEmpty(sourceKey)) {
			return null; // literal (carried verbatim) or data binding — no resource to register
		}
		// Re-key the caption to a key UNIQUE to this new mobile element (<mobileName>_caption). A web element
		// can carry an INHERITED caption key whose name does not match the element (e.g. web OverviewTab is
		// bound to the base-template key GeneralInfoTab_caption). If carried verbatim, that key collides with
		// one the mobile template already owns with a different value (GeneralInfoTab_caption = "Details"), and
		// update-page — which never overwrites an existing page/template key — silently drops our override, so
		// the template value wins at render. A per-element key avoids the collision. SourceValue keeps the
		// web caption's own text (resolved from the source key). When the source key already equals the
		// element key, nothing changes and the caller keeps the source token verbatim.
		string key = mobileName + "_caption";
		return new CaptionResource { Key = key, SourceValue = ResolveResourceString(ctx.Resources, sourceKey) ?? sourceKey };
	}

	/// <summary>Resolves a page resource key into its en-US text (else the first culture) from the bundle's strings.</summary>
	private static string ResolveResourceString(JObject resources, string key) {
		if (resources?[key] is not { } value) {
			return null;
		}
		if (value is JObject cultures) {
			return (cultures["en-US"] ?? cultures.Properties().FirstOrDefault()?.Value)?.ToString();
		}
		return value.ToString();
	}

	/// <summary>
	/// Collects every localized-string resource the converted body references — the <c>#ResourceString(key)#</c>
	/// / <c>$Resources.Strings.key</c> tokens carried verbatim in the element mobileValues (top-level AND nested,
	/// e.g. <c>config.title</c>) and in the data-section configs — and resolves each to its en-US text from the
	/// source page's strings. Keys that do not resolve are skipped (the platform auto-provides some). The caller
	/// registers this map on the mobile page so every carried token renders.
	/// </summary>
	private static IReadOnlyDictionary<string, string> CollectResourceStrings(
		List<ElementMapEntry> elementMap, JsonNode modelConfig, JsonNode viewModelConfig, JObject resources) {
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		void Scan(string json) {
			if (string.IsNullOrEmpty(json)) {
				return;
			}
			foreach (string key in ResourceStringHelper.ExtractKeys(json)) {
				if (result.ContainsKey(key)) {
					continue;
				}
				string text = ResolveResourceString(resources, key);
				if (!string.IsNullOrEmpty(text)) {
					result[key] = text;
				}
			}
		}
		foreach (ElementMapEntry entry in elementMap) {
			// Register the element's caption key with its source text FIRST. A re-keyed caption
			// (<mobileName>_caption, used to dodge a template key collision) does not exist under that name in
			// the source strings, so a token scan alone would not resolve it — take the value from the
			// CaptionResource, which carries the web caption's own text.
			if (entry.CaptionResource is { } cap
				&& !string.IsNullOrEmpty(cap.Key) && !string.IsNullOrEmpty(cap.SourceValue)
				&& !result.ContainsKey(cap.Key)) {
				result[cap.Key] = cap.SourceValue;
			}
			if (entry.MobileValues is not null) {
				Scan(entry.MobileValues.ToJsonString());
			}
		}
		Scan(modelConfig?.ToJsonString());
		Scan(viewModelConfig?.ToJsonString());
		return result;
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
	/// Source-node properties never copied into the prebuilt mobile <c>values</c>: the element identity/type
	/// (<c>name</c>/<c>type</c>), the web-only data-source router (<c>dataSourceName</c>), and the value
	/// binding (<c>control</c>/<c>value</c>) — the binding is a type-specific rename (e.g. a mobile ComboBox
	/// must bind via <c>value</c>; <c>control</c> needs <c>items</c> or it crashes) and is left to the caller
	/// to add. NOTE: <c>items</c> is NOT here — it is excluded only when it is an ARRAY of child view elements
	/// (structural, handled by the tree walk); as a STRING it is a real collection binding (e.g.
	/// <c>crt.CommunicationOptions</c>/<c>crt.List</c> <c>items: "$Attr"</c>) and is carried like any other
	/// property. Everything else the mobile component supports is carried verbatim.
	/// </summary>
	private static readonly HashSet<string> ExcludedSourceProps = new(StringComparer.OrdinalIgnoreCase) {
		"name", "type", "dataSourceName", "control", "value"
	};

	/// <summary>
	/// Builds the prebuilt, ready-to-paste mobile <c>values</c> for an inserted component. Copy rule (no
	/// hardcoded property list): a source property is carried when the MOBILE registry declares it OR when
	/// NEITHER the web nor the mobile registry declares it — the latter are framework/system properties
	/// (e.g. <c>caption</c>, <c>layoutConfig</c>) that no component contract lists but the client resolves
	/// via a preprocessor, so they must survive. A property is DROPPED only when it is web-registry-specific
	/// and absent from the mobile registry (a genuine web-only component property). Structural keys and the
	/// value binding are always excluded (see <see cref="ExcludedSourceProps"/>); <c>type</c> is set and, for
	/// field components, <c>label</c> is synthesized. Returns null for an unknown mobile type.
	/// </summary>
	private static JsonNode BuildMobileValues(ElementMapContext ctx, JObject node, string mobileName, string mobileType, CaptionResource caption) {
		if (string.IsNullOrEmpty(mobileType)) {
			return null;
		}
		HashSet<string> allowed = AllowedProps(ctx.MobileByType, mobileType);
		HashSet<string> webAllowed = AllowedProps(ctx.WebByType, node["type"]?.ToString());
		var values = new JObject { ["type"] = mobileType };
		foreach (JProperty prop in node.Properties()) {
			// `items` as an ARRAY is the child view-element collection — structural, emitted by the tree
			// walk, not a value. `items` as a STRING is a real collection binding (e.g. "$Attr") and is
			// carried like any other property below.
			if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value is JArray) {
				continue;
			}
			if (ExcludedSourceProps.Contains(prop.Name)) {
				continue;
			}
			// Event bindings (clicked / valueChange / updated …) carry a request — they are NOT plain
			// registry props (they are request-binding outputs, absent from allowed) and are converted
			// deliberately by ProcessEventBindings below, so skip them here.
			if (IsEventBinding(prop.Value)) {
				continue;
			}
			// Carry when the mobile component supports the property, OR when NEITHER registry declares it
			// (a system/framework property — e.g. caption, layoutConfig — the client resolves itself).
			// Drop only a web-registry-specific property the mobile component does not support.
			if (allowed.Contains(prop.Name) || !webAllowed.Contains(prop.Name)) {
				// Coerce the carried value to the shape the MOBILE registry declares for this input
				// (e.g. crt.List `itemLayout` is a single object on mobile but the web node carries a
				// one-element array). Registry-driven, so no property names are hardcoded.
				values[prop.Name] = CoerceToDeclaredShape(ctx, mobileType, prop.Name, prop.Value.DeepClone());
			}
		}
		// Re-key the carried caption token ONLY when the source references a key different from this element's
		// unique key (the collision case, e.g. OverviewTab carrying GeneralInfoTab_caption): emit a plain
		// #ResourceString(<mobileName>_caption)# so it cannot clash with a template-owned key. When the keys
		// already match, keep the source token verbatim (preserving wrappers like #MacrosTemplateString(...)#).
		if (caption is not null && values["caption"] is { } carriedCaption) {
			string carriedKey = ResourceStringHelper.ExtractKeys(carriedCaption.ToString()).FirstOrDefault();
			if (!string.Equals(carriedKey, caption.Key, StringComparison.Ordinal)) {
				values["caption"] = "#ResourceString(" + caption.Key + ")#";
			}
		}
		ProcessEventBindings(ctx, node, values, mobileName);
		// Synthesize a field label ONLY as a fallback — when the source did not carry one. Most fields carry
		// their own web `label` verbatim above (e.g. "$Resources.Strings.<attribute>", which auto-resolves to
		// the bound column's caption); overwriting it with a guessed column-code key breaks that resolution.
		bool hasCarriedLabel = values["label"] is { } lbl && !string.IsNullOrWhiteSpace(lbl.ToString());
		if (!hasCarriedLabel) {
			string label = ResolveFieldLabel(ctx, node, mobileName, mobileType, caption);
			if (!string.IsNullOrEmpty(label)) {
				values["label"] = label;
			}
		}
		try {
			return JsonNode.Parse(values.ToString(Newtonsoft.Json.Formatting.None));
		} catch {
			return null;
		}
	}

	/// <summary>The set of property/input names a registry declares for a component type (empty when unknown).</summary>
	private static HashSet<string> AllowedProps(IReadOnlyDictionary<string, ComponentRegistryEntry> registry, string type) {
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(type) && registry.TryGetValue(type, out ComponentRegistryEntry entry)) {
			foreach (string prop in BuildAllowedPropertyNames(entry)) {
				allowed.Add(prop);
			}
		}
		return allowed;
	}

	/// <summary>
	/// Coerces a carried value to the shape (object vs array) the MOBILE registry declares for
	/// <paramref name="propName"/> on <paramref name="mobileType"/>. Some web nodes carry a property in a
	/// different container shape than mobile expects — e.g. crt.List <c>itemLayout</c> is a single object
	/// on mobile, but the web node carries a one-element array. The expected shape comes from the input
	/// descriptor's <c>type</c> (<c>"array"</c>/<c>"object"</c>); when the type is <c>"unknown"</c> (or
	/// absent) it is inferred from the descriptor's <c>default</c> value kind. No property names are
	/// hardcoded — the rule is registry-driven. Returns the value unchanged when there is no descriptor,
	/// the expected shape is indeterminate, or it already matches.
	/// </summary>
	private static JToken CoerceToDeclaredShape(ElementMapContext ctx, string mobileType, string propName, JToken value) {
		if (value is null || string.IsNullOrEmpty(mobileType)
			|| !ctx.MobileByType.TryGetValue(mobileType, out ComponentRegistryEntry entry) || entry is null) {
			return value;
		}
		JsonValueKind? expected = ResolveExpectedShape(entry, propName);
		if (expected is null) {
			return value;
		}
		if (expected == JsonValueKind.Object && value is JArray arr) {
			// The mobile slot is a single map: unwrap the first object element (drop array wrapper).
			JToken first = arr.FirstOrDefault(t => t is JObject);
			return first ?? value;
		}
		if (expected == JsonValueKind.Array && value is JObject) {
			// The mobile slot is a collection: wrap the single object.
			return new JArray(value);
		}
		return value;
	}

	/// <summary>
	/// Resolves the container shape (Object/Array) a mobile registry entry declares for an input — from the
	/// input descriptor's <c>type</c>, falling back to the kind of its <c>default</c> when the type is
	/// <c>"unknown"</c>. Checks both the wrapped <c>inputs</c> shape and the legacy <c>properties</c> shape.
	/// Returns null when the property is absent or its shape cannot be determined.
	/// </summary>
	private static JsonValueKind? ResolveExpectedShape(ComponentRegistryEntry entry, string propName) {
		if (entry.Inputs is not null) {
			foreach (KeyValuePair<string, JsonElement> input in entry.Inputs) {
				if (string.Equals(input.Key, propName, StringComparison.OrdinalIgnoreCase)) {
					return ShapeFromDescriptor(input.Value);
				}
			}
		}
		if (entry.Properties is not null) {
			foreach (KeyValuePair<string, ComponentPropertyDefinition> prop in entry.Properties) {
				if (string.Equals(prop.Key, propName, StringComparison.OrdinalIgnoreCase)) {
					return ShapeFromTypeAndDefault(prop.Value.Type, prop.Value.Default);
				}
			}
		}
		return null;
	}

	/// <summary>Reads <c>type</c>/<c>default</c> from a wrapped-registry input descriptor JSON element.</summary>
	private static JsonValueKind? ShapeFromDescriptor(JsonElement descriptor) {
		if (descriptor.ValueKind != JsonValueKind.Object) {
			return null;
		}
		string type = descriptor.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String
			? t.GetString()
			: null;
		JsonElement? def = descriptor.TryGetProperty("default", out JsonElement d) ? d : (JsonElement?)null;
		return ShapeFromTypeAndDefault(type, def);
	}

	/// <summary>
	/// Maps a declared <c>type</c> string (and a fallback <c>default</c> value) to an expected container kind.
	/// A concrete <c>"array"</c>/<c>"object"</c>/<c>"map"</c> type wins; an <c>"unknown"</c>/absent type is
	/// resolved from the <c>default</c> value kind (object/array). Returns null when indeterminate.
	/// </summary>
	private static JsonValueKind? ShapeFromTypeAndDefault(string type, JsonElement? def) {
		if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)) {
			return JsonValueKind.Array;
		}
		if (string.Equals(type, "object", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(type, "map", StringComparison.OrdinalIgnoreCase)) {
			return JsonValueKind.Object;
		}
		if (def is { } d) {
			if (d.ValueKind == JsonValueKind.Object) {
				return JsonValueKind.Object;
			}
			if (d.ValueKind == JsonValueKind.Array) {
				return JsonValueKind.Array;
			}
		}
		return null;
	}

	/// <summary>
	/// Requests the Creatio Mobile app supports (from the monorepo <c>@CrtInterfaceDesignerMobileRequest</c>
	/// decorators). A converted component whose event-binding request does not resolve to one of these is
	/// dropped. TODO(ENG-93027): source this list dynamically (like the versioned component registries)
	/// instead of hardcoding it — https://creatio.atlassian.net/browse/ENG-93027.
	/// </summary>
	private static readonly HashSet<string> MobileSupportedRequests = new(StringComparer.OrdinalIgnoreCase) {
		"crt.AddCommunicationOptionsRequest",
		"crt.CancelRecordChangesRequest",
		"crt.ClosePageRequest",
		"crt.CreateRecordRequest",
		"crt.DeleteRecordRequest",
		"crt.LoadDataRequest",
		"crt.OpenPageRequest",
		"crt.RunBusinessProcessRequest",
		"crt.SaveRecordRequest",
		"crt.SetAttributeFromBarcodeRequest",
		"crt.SetAttributeFromNfcRequest",
		"crt.UpdateQuickFilterGroupRequest",
		"crt.UpdateRecordRequest",
		"crt.UploadFileRequest"
	};

	/// <summary>
	/// The first event-binding request on the node whose EFFECTIVE mobile request the Creatio Mobile app does
	/// not support (or null when every binding is supported). "Effective" honours the rules remap: a request
	/// the rules map renames to a supported one (e.g. → crt.OpenPageRequest) is fine; an unknown request, or
	/// one the rules explicitly mark unsupported, resolves to itself and is checked against
	/// <see cref="MobileSupportedRequests"/>.
	/// </summary>
	private static string UnsupportedRequestOf(ElementMapContext ctx, JObject node) {
		foreach (JProperty prop in node.Properties()) {
			if (!IsEventBinding(prop.Value)) {
				continue;
			}
			string webRequest = ((JObject)prop.Value)["request"].ToString();
			string effective = ctx.RequestMap.TryGetValue(webRequest, out RequestMappingRule rule)
				&& !string.IsNullOrWhiteSpace(rule.Mobile)
					? rule.Mobile
					: webRequest;
			if (!MobileSupportedRequests.Contains(effective)) {
				return webRequest;
			}
		}
		return null;
	}

	/// <summary>
	/// A component event binding is a property whose value is an object carrying a string <c>request</c>
	/// (the Freedom UI <c>{ request, params }</c> shape used by <c>clicked</c> / <c>valueChange</c> /
	/// <c>updated</c>). This structural test recognizes every such binding without a registry of outputs.
	/// </summary>
	private static bool IsEventBinding(JToken value) =>
		value is JObject obj && obj["request"] is JValue { Type: JTokenType.String } req
		&& !string.IsNullOrWhiteSpace(req.ToString());

	/// <summary>
	/// Converts the source node's event-binding requests (actions) for mobile and writes the surviving
	/// ones into the prebuilt <paramref name="values"/>: a SUPPORTED request is kept (and its name remapped
	/// when the mobile type differs, params renamed per the rule's paramMap); an UNSUPPORTED request has its
	/// whole binding omitted (the component stays, the dead action is dropped); an UNKNOWN/custom request is
	/// kept verbatim and flagged. Each outcome is recorded for the advisory requestConversions summary.
	/// </summary>
	private static void ProcessEventBindings(ElementMapContext ctx, JObject node, JObject values, string elementName) {
		foreach (JProperty prop in node.Properties()) {
			if (!IsEventBinding(prop.Value)) {
				continue;
			}
			string binding = prop.Name;
			var source = (JObject)prop.Value;
			string webRequest = source["request"].ToString();
			values.Remove(binding); // own this property regardless of the prune loop

			if (ctx.RequestMap.TryGetValue(webRequest, out RequestMappingRule rule)) {
				if (!string.IsNullOrWhiteSpace(rule.Mobile)) {
					var clone = (JObject)source.DeepClone();
					clone["request"] = rule.Mobile;
					ApplyParamMap(clone, rule.ParamMap);
					values[binding] = clone;
					ctx.ConvertedRequests.Add(new ConvertedRequest {
						ElementName = elementName, Binding = binding, WebRequest = webRequest, MobileRequest = rule.Mobile
					});
				} else {
					ctx.DroppedRequests.Add(new DroppedRequest {
						ElementName = elementName, Binding = binding, WebRequest = webRequest,
						Reason = string.IsNullOrWhiteSpace(rule.Note)
							? "Request is not supported on mobile; the binding was removed (the component still renders)."
							: rule.Note
					});
				}
				continue;
			}

			// Not in the map: unknown OOTB request or a custom usr.* — keep it but flag for review.
			values[binding] = (JObject)source.DeepClone();
			ctx.FlaggedRequests.Add(new FlaggedRequest {
				ElementName = elementName, Binding = binding, Request = webRequest,
				Reason = "Request is not in the conversion map (custom or unknown) — verify it exists on mobile before relying on it."
			});
		}
	}

	/// <summary>Renames keys in the binding's <c>params</c> object per the rule's web→mobile param map (no-op when empty).</summary>
	private static void ApplyParamMap(JObject binding, IReadOnlyDictionary<string, string> paramMap) {
		if (paramMap is null || paramMap.Count == 0 || binding["params"] is not JObject prms) {
			return;
		}
		foreach (KeyValuePair<string, string> pair in paramMap) {
			if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) {
				continue;
			}
			if (prms[pair.Key] is { } moved) {
				prms.Remove(pair.Key);
				prms[pair.Value] = moved;
			}
		}
	}

	/// <summary>Builds the web-request → mapping-rule lookup (case-insensitive) from the resolved rules.</summary>
	private static IReadOnlyDictionary<string, RequestMappingRule> BuildRequestMap(WebToMobilePageConversionRules rules) {
		var map = new Dictionary<string, RequestMappingRule>(StringComparer.OrdinalIgnoreCase);
		foreach (RequestMappingRule rule in rules?.Requests ?? []) {
			if (!string.IsNullOrWhiteSpace(rule?.Web)) {
				map[rule.Web] = rule;
			}
		}
		return map;
	}

	/// <summary>Assembles the advisory request-conversion summary; null when the page references no requests.</summary>
	private static RequestConversionInfo BuildRequestConversionInfo(
		List<ConvertedRequest> converted, List<DroppedRequest> dropped, List<FlaggedRequest> flagged) {
		if (converted.Count == 0 && dropped.Count == 0 && flagged.Count == 0) {
			return null;
		}
		return new RequestConversionInfo {
			ConvertedRequests = converted,
			DroppedRequests = dropped,
			FlaggedRequests = flagged
		};
	}

	// ── Adaptive (per-breakpoint) layout proposal ──────────────────────────────────────────────

	/// <summary>Reads an integer property from a Newtonsoft node, or null when absent / non-integer.</summary>
	private static int? ReadInt(JObject obj, string prop) =>
		obj[prop] is { Type: JTokenType.Integer } token ? token.Value<int>() : null;

	/// <summary>
	/// Captures per element the data the adaptive pass needs: its web <c>layoutConfig</c> (grid placement,
	/// keyed by element name) and, for a grid container (a node carrying <c>columns</c>), its web column
	/// count (keyed by the container name — the mobile parent its children are placed under).
	/// </summary>
	private static void CaptureSource(ElementMapContext ctx, string name, JObject node) {
		if (node["layoutConfig"] is JObject layout) {
			ctx.SourceLayouts[name] = (JObject)layout.DeepClone();
		}
		if (node["columns"] is JArray columns && columns.Count > 0) {
			ctx.GridContainerColumns[name] = columns.Count;
		}
	}

	/// <summary>
	/// Builds the per-breakpoint layout for every MULTI-column <c>crt.GridContainer</c>: on the phone
	/// (<c>small</c>) it collapses to ONE column and stacks the children in tree order; on tablet/desktop
	/// (<c>medium</c> / <c>large</c>) it keeps the web column count and each child's web placement. A grid
	/// with a single column gets NO adaptive (the mobile client renders the plain layout). Both sides are
	/// baked deterministically: the container columns into the container's own mobileValues, and each child's
	/// <c>layoutConfig.adaptive</c> (replacing the base placement, which is folded into medium/large) into
	/// the child's mobileValues. Also returns an advisory group per converted container.
	/// </summary>
	private static List<AdaptiveLayoutGroup> BuildAdaptiveLayout(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, JObject> sourceLayouts,
		IReadOnlyDictionary<string, int> gridContainerColumns) {
		// Children (any type) of a captured grid container, grouped by mobile parent in tree (= elementMap) order.
		var byContainer = new Dictionary<string, List<ElementMapEntry>>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		foreach (ElementMapEntry e in elementMap) {
			if (!string.Equals(e.Operation, "insert", StringComparison.Ordinal) ||
				string.IsNullOrEmpty(e.ParentName) || e.MobileValues is not JsonObject ||
				!gridContainerColumns.ContainsKey(e.ParentName)) {
				continue;
			}
			if (!byContainer.TryGetValue(e.ParentName, out List<ElementMapEntry> list)) {
				list = [];
				byContainer[e.ParentName] = list;
				order.Add(e.ParentName);
			}
			list.Add(e);
		}

		var groups = new List<AdaptiveLayoutGroup>();
		foreach (string container in order) {
			int webCols = gridContainerColumns[container];
			if (webCols <= 1) {
				continue; // single-column grid — the mobile client works with the non-adaptive config
			}
			List<ElementMapEntry> children = byContainer[container];

			var items = new List<AdaptiveLayoutItem>();
			for (int i = 0; i < children.Count; i++) {
				ElementMapEntry child = children[i];
				(int col, int row, int colSpan, int rowSpan) = WebPlacement(sourceLayouts, child.WebName, i, webCols);
				var adaptive = new JsonObject {
					["small"] = Cell(1, i + 1, 1, 1),               // phone: single-column stack
					["medium"] = Cell(col, row, colSpan, rowSpan),  // tablet/desktop: keep the web placement
					["large"] = Cell(col, row, colSpan, rowSpan)
				};
				// Replace layoutConfig with the adaptive form (the web placement is folded into medium/large).
				((JsonObject)child.MobileValues)["layoutConfig"] = new JsonObject { ["adaptive"] = adaptive.DeepClone() };
				items.Add(new AdaptiveLayoutItem { Name = child.MobileName, LayoutConfigAdaptive = adaptive });
			}

			// Container columns: small = 1, medium/large = the web column count. Fold INTO the container's own
			// element-map entry (insert or merge twin) so the result is a SINGLE operation on that element — no
			// separate merge diff for the model to apply on top (which would duplicate the operation).
			ElementMapEntry containerEntry = elementMap.FirstOrDefault(e =>
				(string.Equals(e.Operation, "insert", StringComparison.Ordinal) ||
				 string.Equals(e.Operation, "merge", StringComparison.Ordinal)) &&
				string.Equals(e.MobileName, container, StringComparison.OrdinalIgnoreCase));
			if (containerEntry is not null) {
				if (containerEntry.MobileValues is not JsonObject containerValues) {
					containerValues = new JsonObject();
					containerEntry.MobileValues = containerValues;
				}
				containerValues["adaptive"] = new JsonObject {
					["small"] = new JsonObject { ["columns"] = ColumnsNode(1) },
					["medium"] = new JsonObject { ["columns"] = ColumnsNode(webCols) },
					["large"] = new JsonObject { ["columns"] = ColumnsNode(webCols) }
				};
			}

			groups.Add(new AdaptiveLayoutGroup {
				ContainerName = container,
				ColumnsByBreakpoint = new Dictionary<string, IReadOnlyList<string>> {
					["small"] = Cols(1), ["medium"] = Cols(webCols), ["large"] = Cols(webCols)
				},
				Items = items
			});
		}
		return groups;

		static JsonObject Cell(int column, int row, int colSpan, int rowSpan) =>
			new() { ["row"] = row, ["column"] = column, ["colSpan"] = colSpan, ["rowSpan"] = rowSpan };
		static IReadOnlyList<string> Cols(int n) => Enumerable.Repeat("1fr", n).ToList();
	}

	/// <summary>
	/// The web grid placement of a child (<c>column</c>/<c>row</c>/<c>colSpan</c>/<c>rowSpan</c> from its web
	/// <c>layoutConfig</c>). Falls back to a left-to-right flow (<paramref name="cols"/> per row, spans of 1)
	/// using the child's <paramref name="index"/> when the source declared no placement.
	/// </summary>
	private static (int Col, int Row, int ColSpan, int RowSpan) WebPlacement(
		IReadOnlyDictionary<string, JObject> sourceLayouts, string name, int index, int cols) {
		if (name is not null && sourceLayouts.TryGetValue(name, out JObject lc)) {
			return (
				ReadInt(lc, "column") ?? (index % cols) + 1,
				ReadInt(lc, "row") ?? (index / cols) + 1,
				ReadInt(lc, "colSpan") ?? 1,
				ReadInt(lc, "rowSpan") ?? 1);
		}
		return ((index % cols) + 1, (index / cols) + 1, 1, 1);
	}

	/// <summary>A JSON array of <paramref name="n"/> "1fr" column sizes.</summary>
	private static JsonArray ColumnsNode(int n) {
		var arr = new JsonArray();
		for (int i = 0; i < n; i++) {
			arr.Add("1fr");
		}
		return arr;
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

	private static string ResolveParent(ElementMapContext ctx, string mobileParentName) =>
		!string.IsNullOrEmpty(mobileParentName) ? mobileParentName : ctx.RelocateTarget;

	/// <summary>
	/// If <paramref name="nodes"/> contains a positional anchor container (a name in
	/// <see cref="ElementMapContext.PositionalParentByAnchor"/>), classifies its other named siblings:
	/// those declared ABOVE the anchor get an ascending index from 0 (so they land before the mobile anchor,
	/// e.g. above the Tabs); those BELOW get a null index (appended after). Both resolve to the anchor's
	/// mobile parent. Returns an empty map when this array has no positional anchor.
	/// </summary>
	private static IReadOnlyDictionary<string, (string Parent, int? Index)> ResolvePositionalSiblings(
		ElementMapContext ctx, JArray nodes) {
		var result = new Dictionary<string, (string Parent, int? Index)>(StringComparer.Ordinal);
		if (ctx.PositionalParentByAnchor.Count == 0) {
			return result;
		}
		int anchorIdx = -1;
		string parent = null;
		var named = new List<(int Pos, string Name)>();
		for (int i = 0; i < nodes.Count; i++) {
			if (nodes[i] is not JObject o) {
				continue;
			}
			string nm = o["name"]?.ToString();
			if (string.IsNullOrEmpty(nm)) {
				continue;
			}
			named.Add((i, nm));
			if (anchorIdx < 0 && ctx.PositionalParentByAnchor.TryGetValue(nm, out string p)) {
				anchorIdx = i;
				parent = p;
			}
		}
		if (anchorIdx < 0 || string.IsNullOrEmpty(parent)) {
			return result;
		}
		int topIndex = 0;
		foreach ((int pos, string nm) in named) {
			if (pos == anchorIdx) {
				continue;
			}
			result[nm] = pos < anchorIdx ? (parent, topIndex++) : (parent, (int?)null);
		}
		return result;
	}

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
