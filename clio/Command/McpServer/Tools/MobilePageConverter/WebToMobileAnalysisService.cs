namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonValue = System.Text.Json.Nodes.JsonValue;

// Freedom UI WEB -> Freedom UI MOBILE conversion ANALYSIS (advisory-only, ENG-89620).
// This service builds NOTHING and performs no Creatio I/O. It inspects the source web page
// (merged component bundle + registries + the version-resolved WebToMobilePageConversionRules)
// and produces a deterministic MobilePageConversionGuide: source structure, the recommended mobile
// template + container correspondence, per-type component suggestions, and inline mobile
// component contracts. An LLM uses the guide to build the mobile page body itself.
// The shared, converter-agnostic category enum and DTOs live in PageConversionModels, and
// the guide contract lives in MobilePageConversionGuideModels.

/// <summary>
/// Deterministic Freedom UI WEB -> Freedom UI MOBILE conversion analyzer. Pure (no Creatio I/O,
/// no body generation): the caller supplies the merged page bundle, the resolved component
/// registries, and the version-resolved <see cref="WebToMobilePageConversionRules"/>; the service
/// returns a <see cref="MobilePageConversionGuide"/> the model executes.
/// </summary>
// First-stage deterministic converter. The per-element tree walk and the guide assembly are inherently
// branchy; splitting them to hit the cognitive-complexity threshold risks behavior on a hot path, so that
// (and the wide analyzer signatures / repeated JSON keys) is accepted here rather than refactored (ENG-91228).
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Deterministic conversion walk; refactoring the branchy per-element logic risks behavior. Accepted for the first-stage converter (ENG-91228).")]
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "The analysis entry points thread the resolved registries/rules/maps explicitly; a parameter object would obscure the pure-function contract.")]
[SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null", Justification = "null is a meaningful 'section absent' signal in the guide model (JsonIgnore omits it); an empty collection would change the emitted shape.")]
[SuppressMessage("Minor Code Smell", "S1192:String literals should not be duplicated", Justification = "Freedom UI JSON keys (items/merge/insert/request/…) read more clearly inline than behind constants in this converter.")]
[SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with LINQ", Justification = "Explicit loops with side effects (element-map emission, ref accumulation) are clearer than a LINQ rewrite here.")]
[SuppressMessage("Major Code Smell", "S125:Sections of code should not be commented out", Justification = "The flagged lines are explanatory design notes, not commented-out code.")]
[SuppressMessage("Info Code Smell", "S1135:Track uses of TODO tags", Justification = "The TODO tracks ENG-93027 (dynamic mobile-request set) and is intentionally retained as a pointer.")]
[SuppressMessage("Major Code Smell", "S3358:Ternary operators should not be nested", Justification = "The nested ternaries express a compact fallback chain that reads clearly in context.")]
[SuppressMessage("Major Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "The flagged null checks guard values the analyzer cannot prove non-null across the Newtonsoft/STJ boundary; removing them would risk an NRE on malformed bundles.")]
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
	/// <param name="mobileTemplateViewModelConfig">The mobile template's OWN merged viewModelConfig (the base
	/// the page is created from). The page's viewModelConfig is diffed recursively against it so shared
	/// subtrees emit only the real delta and arrays the base already carries are augmented via insert rather
	/// than replaced. Null when no template rule matched or the template bundle could not be read.</param>
	/// <param name="mobileTemplateModelConfig">The mobile template's OWN merged modelConfig, used the same way
	/// as <paramref name="mobileTemplateViewModelConfig"/> to diff the page's modelConfig. Null when no
	/// template rule matched or the template bundle could not be read.</param>
	/// <param name="mobileTemplateUnavailable">True when a mobile template was known but its bundle could not
	/// be read (no active environment, read failure) - the data-section diffs fall back to a single root merge
	/// and an explicit constraint warns that template-owned arrays may be replaced wholesale.</param>
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
		IReadOnlyDictionary<string, string> mobileContainerParents = null,
		JsonNode mobileTemplateViewModelConfig = null,
		JsonNode mobileTemplateModelConfig = null,
		bool mobileTemplateUnavailable = false,
		IReadOnlyDictionary<string, string> mobileTemplateTypesByName = null,
		IReadOnlyDictionary<string, JObject> webTemplateBaselineNodes = null,
		bool webTemplateUnavailable = false,
		JObject webTemplateResources = null) {
		ArgumentNullException.ThrowIfNull(bundle);
		ArgumentNullException.ThrowIfNull(mobileTypes);
		ArgumentNullException.ThrowIfNull(webTypes);
		ArgumentNullException.ThrowIfNull(rules);

		IReadOnlyDictionary<string, string> map =
			containerNameMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyDictionary<string, ComponentMappingRule> componentMap =
			componentNameMap ?? new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyDictionary<string, string> mobileTypesByName =
			mobileTemplateTypesByName ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyDictionary<string, JObject> webBaselineNodes =
			webTemplateBaselineNodes ?? new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

		// 0. Filter out the web template's own components at read time. The merged tree carries the
		//    chrome the source page inherits from its web template (e.g. PageWithTabsFreedomTemplate:
		//    MainHeader / TitleContainer / BackButton / PageTitle / …) — the mobile template already
		//    provides those (Scaffold + header). Only the page's DELTA over its web template is
		//    converted. Container twins listed in the containerMap are kept (they are merge targets).
		JArray tree = bundle.ViewConfig is null ? new JArray() : JArray.Parse(bundle.ViewConfig.ToJsonString());
		int sourceNamedCount = bundle.ViewConfig is null ? 0 : CollectComponentNames(bundle.ViewConfig).Count;
		bool templatePruned = false;
		if (templateComponentNames is { Count: > 0 }) {
			// A container declared in `nonConvertingScopeContainers` (e.g. MainHeader) must NOT be pruned as chrome:
			// its descendants need it in the tree as an ancestor for `path` matching, and the walk then treats it as
			// a non-converting scope that emits no mobile element of its own. This is decoupled from any rule's
			// `path` on purpose (see CollectScopeContainerNames).
			IReadOnlySet<string> scopeContainerNames = CollectScopeContainerNames(rules);
			tree = PruneTemplateComponents(tree, map, componentMap, templateComponentNames, mobileTypesByName, mobileByType, webBaselineNodes, scopeContainerNames);
			templatePruned = true;
		}

		// 1. Walk the merged tree into a flat structure (names, types, parents, container flags) and
		//    record, per web type, the source-component names that carry it.
		var structure = new List<SourceComponentInfo>();
		var namesByType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		WalkStructure(tree, parentName: null, map, webByType, mobileByType, structure, namesByType);

		// Diagnostic: the source page had components but the converted layout is empty. Do not return this
		// silently — a caller would mistake it for a legitimately layout-less page. The usual cause is an
		// unresolved web-template baseline (a replacing schema layered over a same-named base whose chrome
		// subtraction consumed the whole tree).
		string layoutResolution = structure.Count == 0 && sourceNamedCount > 0
			? $"empty: the source page has {sourceNamedCount} component(s) but the converted layout is empty — "
				+ "the web-template baseline may be unresolved (e.g. a replacing schema over a same-named base, "
				+ "or the parent template could not be read). Verify the source page and its template ancestry."
			: null;

		// 2. Component suggestions: classify each distinct present web type via the rules matrix,
		//    then the registry type sets (direct/unsupported/manual).
		List<ComponentSuggestion> suggestions = BuildComponentSuggestions(namesByType, rules, mobileTypes, webTypes);

		// 3. Inline contracts for every suggested mobile type (+ direct-mapped types).
		List<MobileComponentContract> contracts = BuildMobileContracts(suggestions, mobileByType);

		// 4. Web-only sections and data sources (surfaced, not stripped — the model owns the body).
		List<string> webOnly = CollectWebOnlySections(bundle);
		List<string> dataSources = CollectDataSources(bundle);

		// 5. Instance-level element map (per named element: merge / insert / drop / relocate-children).
		Dictionary<string, string> attrToColumn = BuildAttrToColumn(bundle);
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
			tree, map, componentMap, mobileTypes, mobileByType, webByType, rules, attrToColumn, resources,
			requestMap, convertedRequests, droppedRequests, flaggedRequests, sourceLayouts, gridContainerColumns, positionalParentByAnchor,
			mobileTypesByName, webBaselineNodes, webTemplateResources);

		// Removes a nested component the generic per-element copy already carried verbatim inside one of
		// the host's OWN properties (type-agnostic — which type/host/property is banned comes entirely from
		// the excludedComponents rules). Deliberately BEFORE RemoveEmptyContainers: a host property this
		// pass empties out is then visible to any future cascading rule with already-clean mobileValues, and
		// every removal here is recorded as a synthetic "drop" ElementMapEntry the same way
		// EmptyContainerRemoval records its own.
		ExcludedComponentsPass.RemoveExcludedComponents(elementMap, rules);

		// Deterministic empty-container removal: a converter-created layout container whose items
		// receive NO surviving child is converted to a drop, bottom-up so emptiness cascades. Deliberately
		// BEFORE the adaptive and tab-area passes: adaptive then stacks only surviving children, and a tab this
		// pass removed never gets layers synthesized (nothing resurrects it). ConvertPageBusinessRules and
		// resource-string collection run later and already handle drop entries, so a removed container's rule
		// actions and caption fall out with no extra code. The request-conversion summary is built AFTER the
		// pass for the same reason: a binding recorded for a container this pass removed must be reconciled
		// (reported as discarded, not as converted/flagged for an element the map says not to create). The
		// removed names are threaded to BuildMobileViewModelConfig so the attributes they referenced are KEPT
		// (removal is layout cleanup, not attribute cleanup).
		HashSet<string> emptyRemovedNames = RemoveEmptyContainers(
			elementMap, rules, out HashSet<string> emptyRemovedMobileNames);
		// Positional :top indexes are assigned at walk time to ALL siblings of an anchor, including ones the
		// walk later dropped (unsupported type, foreign data source, unsupported button request) — every drop
		// source leaves the same index hole the empty-container pass does, so compaction runs unconditionally,
		// not only when that pass removed something.
		CompactPositionalIndexes(elementMap);
		// Deterministic tab order: every SURVIVING converted web tab gets an explicit index under the mobile
		// Tabs (starting right after the template's general tab) so the template's Feed/Attachments tabs stay
		// last. The pass order is load-bearing: AFTER RemoveEmptyContainers (a tab removed as empty is a drop
		// by then and never indexed — no holes), and AFTER CompactPositionalIndexes (that compaction rebases
		// each parent's indexed group to 0; run over tab indexes it would rebase the first-tab offset away and
		// put the first web tab BEFORE the general tab).
		AssignConvertedTabIndexes(elementMap);
		RequestConversionInfo requestConversions = BuildRequestConversionInfo(
			convertedRequests, droppedRequests, flaggedRequests, emptyRemovedMobileNames);

		// Adaptive (per-breakpoint) layout for multi-column crt.GridContainer: on the phone (small) collapse
		// to a single column and stack; on tablet/desktop (medium/large) keep the web columns and per-child
		// placement. A 1-column grid gets no adaptive. Both the container columns and each child's
		// layoutConfig.adaptive are baked into mobileValues deterministically.
		List<AdaptiveLayoutGroup> adaptiveLayout = BuildAdaptiveLayout(elementMap, sourceLayouts, gridContainerColumns);

		// Designer's two-layer tab body (tab-body grid + Area card) synthesized into every tab the converter
		// creates. Deliberately AFTER the adaptive pass: that pass indexes children per multi-column web grid
		// container, and running it on the pre-synthesis map keeps the synthesized layers from ever shifting a
		// child's stacking index. The two passes touch disjoint element sets (a tab is not a grid container),
		// so the order is safe either way — it is fixed here so it stays that way.
		List<TabAreaLayerGroup> tabAreaLayers = BuildTabAreaLayers(elementMap, rules, sourcePage);

		// Property normalization: every mobile standard the RULES declare is stamped onto the elements the
		// converter INSERTS, and the web page's own value for those properties is deliberately IGNORED
		// (discarded, never translated). Which component, which properties and which values all come from
		// the rules file — this pass knows none of them. Runs AFTER the tab-area pass so one pass covers
		// converted and synthesized elements alike (the invariant is per-element-map, not per-origin);
		// merge twins the mobile template provides are never touched. Each rule also declares the report
		// group it feeds, so two standards never bleed into each other's summary.
		ComponentPropertyOverrideResult componentPropertyOverrides = ApplyComponentPropertyOverrides(elementMap, rules);
		IReadOnlyList<NormalizationEntry> spacingNormalization =
			componentPropertyOverrides.EntriesOf(SpacingGroup);

		// 6. Data sections applied to the mobile body verbatim/filtered (identical structural support on
		//    mobile): modelConfig is carried over as-is (preserving attribute types like ForwardReference);
		//    viewModelConfig drops attributes used only by dropped components.
		JsonNode modelConfig = PassthroughModelConfig(bundle);
		JsonNode viewModelConfig = BuildMobileViewModelConfig(bundle, tree, elementMap, emptyRemovedNames);
		// Prebuilt, ready-to-paste diffs so the caller never hand-builds the data-source section. Each config
		// is diffed against the mobile template's OWN merged base (the schema the page is created from):
		// a key whose subtree already exists in the base is recursed into, so only the real delta is emitted
		// and every operation targets a path that EXISTS in the base; a NEW page-owned key (attribute, list
		// collection, data source) is carried whole in one merge at its parent path (its columns/arrays travel
		// inline, so nothing is lost and no flat stub is needed); and an ARRAY that already exists in the base
		// is NEVER merged (a merge REPLACES arrays wholesale, dropping one side) -- each of the page's new
		// entries is appended via an insert at the array's own path, preserving the template's natives. When
		// the template base could not be read, this degrades to a single root merge and a constraint warns.
		JsonNode viewModelConfigDiff = BuildTargetedDiff(viewModelConfig, mobileTemplateViewModelConfig, out IReadOnlyList<string> vmcArrayConflicts);
		JsonNode modelConfigDiff = BuildTargetedDiff(modelConfig, mobileTemplateModelConfig, out IReadOnlyList<string> mcArrayConflicts);
		// The root-merge fallback fires per config whenever a page config exists but no usable JsonObject base was
		// supplied for it -- NOT only when the probe reported the template unavailable. A template that carries only
		// the other section (one config null) or a page created with no known template both hit the fallback with
		// mobileTemplateUnavailable == false, so gate the constraint on the fallback actually firing, per config.
		bool viewModelConfigRootMerge = viewModelConfig is JsonObject && mobileTemplateViewModelConfig is not JsonObject;
		bool modelConfigRootMerge = modelConfig is JsonObject && mobileTemplateModelConfig is not JsonObject;
		var dataSectionArrayConflicts = new List<string>();
		dataSectionArrayConflicts.AddRange(vmcArrayConflicts);
		dataSectionArrayConflicts.AddRange(mcArrayConflicts);

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
			LayoutResolution = layoutResolution,
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
			TabAreaLayers = tabAreaLayers.Count > 0 ? tabAreaLayers : null,
			// Back-compat alias: spacingNormalization shipped before normalizations existed, so its shape is
			// preserved verbatim. Every standard — spacing included — is also reported under normalizations.
			SpacingNormalization = spacingNormalization.Count > 0
				? new SpacingNormalizationInfo {
					Note = "Mobile follows the mobile spacing standard: the web page's container spacing was "
						+ "IGNORED (not translated) and every inserted crt.GridContainer / crt.FlexContainer "
						+ "carries gap Medium, already baked into elementMap[].mobileValues — nothing separate "
						+ "to apply. Silent normalization, not a gate decision: report it as ONE aggregated "
						+ "line and never restore the web spacing.",
					Normalized = [.. spacingNormalization.Select(n => new SpacingNormalizationEntry {
						Name = n.Name, Type = n.Type, Properties = n.Properties
					})]
				}
				: null,
			Normalizations = BuildNormalizations(componentPropertyOverrides),
			ResourceStrings = resourceStrings.Count > 0 ? resourceStrings : null,
			// Named arguments deliberately: the tail is a run of defaulted bools, so a positional call silently
			// mis-wires the moment a parameter is inserted rather than appended.
			Constraints = BuildConstraints(webOnly,
				hasModelConfig: modelConfig is not null,
				hasViewModelConfig: viewModelConfig is not null,
				hasAdaptiveLayout: adaptiveLayout.Count > 0,
				templatePruned: templatePruned,
				viewModelConfigRootMerge: viewModelConfigRootMerge,
				modelConfigRootMerge: modelConfigRootMerge,
				mobileTemplateUnavailable: mobileTemplateUnavailable,
				dataSectionArrayConflicts: dataSectionArrayConflicts,
				hasTabAreaLayers: tabAreaLayers.Count > 0,
				hasEmptyContainerRemovals: emptyRemovedNames.Count > 0,
				normalization: componentPropertyOverrides,
				webTemplateUnavailable: webTemplateUnavailable,
				hasComponentTwin: componentMap.Count > 0),
			NextSteps = BuildNextSteps(
				hasDataSections: modelConfig is not null || viewModelConfig is not null,
				hasAdaptiveLayout: adaptiveLayout.Count > 0,
				hasTabAreaLayers: tabAreaLayers.Count > 0,
				normalization: componentPropertyOverrides),
			GuidanceArticle = GuidanceArticleName,
			SuggestedTargetSchemaName = suggestedTarget
		};
	}

	/// <summary>
	/// Converts the source page's PAGE-level business rules for the mobile page (advisory).
	/// Page rules carry only element actions (hide / show / make-editable / read-only / required /
	/// optional). An action converts only for the referenced elements that survive on mobile (elementMap
	/// operation merge/insert), with their names remapped web→mobile and only the survivors kept. A rule
	/// with no surviving action is dropped together with its condition. A rule whose condition mixes AND and OR
	/// across nested groups is also dropped (the flat single-operator condition input cannot represent it without
	/// changing when the rule fires). Otherwise the condition is carried verbatim — EVERY operand type is supported
	/// in a mobile page-rule condition (attribute, const, formula, system-value, system-setting) — with each
	/// operand's attribute path remapped from the source DS column path to the mobile viewModel attribute name, so
	/// the rule is ready for create-page-business-rule. Returns null when no probe ran.
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
			// A condition that cannot be represented by the flat single-operator condition input of
			// create-page-business-rule (mixed AND/OR, or an unrecognized comparison operator) would fire under
			// different conditions if emitted. Drop the whole rule for manual recreation rather than emit wrong
			// semantics.
			if (rule.ConditionIssue != PageRuleConditionIssue.None) {
				dropped.Add(new DroppedPageBusinessRule {
					Caption = rule.Caption,
					Reason = rule.ConditionIssue switch {
						PageRuleConditionIssue.MixedAndOr =>
							"Condition mixes AND and OR across nested groups; a mobile page rule supports only a "
							+ "single flat condition group (one logical operator) and cannot represent this without "
							+ "changing when the rule fires — recreate this rule manually.",
						PageRuleConditionIssue.UnrecognizedComparison =>
							"Condition uses a comparison operator with no supported mobile equivalent; emitting it "
							+ "would silently change the comparison — recreate this rule manually.",
						_ => "Condition cannot be converted for the mobile page — recreate this rule manually."
					}
				});
				continue;
			}

			var actions = new JsonArray();
			bool anyActionConverted = false;
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
					anyActionConverted = true;
				}
				// else: every referenced element drops → this action does not convert.
			}

			if (!anyActionConverted) {
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
			// Descend NON-items child-element slots (a button's menuItems, an ExpansionPanel's tools) so their
			// components appear in sourceStructure / componentSuggestions / mobileContracts — otherwise the guide
			// would insert a converted nested type (e.g. crt.MenuItem) it publishes no contract for.
			WalkStructure(ChildComponentSlots(node), string.IsNullOrEmpty(name) ? parentName : name,
				containerNameMap, webByType, mobileByType, structure, namesByType);
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
			// Also descend NON-items child-element slots (e.g. a header's tools, a Scaffold's floatAction) so a
			// component the template declares outside items still enters the name baseline.
			CollectComponentNames(ChildComponentSlots(obj), names);
		}
	}

	/// <summary>
	/// The NON-<c>items</c> child-element slots of a System.Text.Json node, as one array: every property value that
	/// is a component object (has a <c>crt.*</c> <c>type</c>) or a collection of them (e.g. <c>tools</c>,
	/// <c>menuItems</c>, or the single-object <c>floatAction</c> → <c>crt.FloatingActionButton</c>). A DATA/config
	/// array or object (no <c>crt.*</c> type) is not included, so ordinary values are never mistaken for components.
	/// This lets the template collectors see components the same way the element-map walk does, so a component in a
	/// non-items slot is not treated as page-authored (and the retarget target FAB is discoverable for validation).
	/// </summary>
	private static JsonArray ChildComponentSlots(JsonObject obj) {
		var collected = new JsonArray();
		foreach (KeyValuePair<string, JsonNode> pair in obj) {
			if (string.Equals(pair.Key, "items", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (pair.Value is JsonObject single && IsComponentObject(single)) {
				collected.Add(single.DeepClone());
			} else if (pair.Value is JsonArray array) {
				foreach (JsonNode element in array) {
					if (element is JsonObject component && IsComponentObject(component)) {
						collected.Add(component.DeepClone());
					}
				}
			}
		}
		return collected;
	}

	/// <summary>
	/// The NON-<c>items</c> child-element nodes of a Newtonsoft node, as a fresh JArray of CLONES: every property
	/// value that is a component object (<c>crt.*</c> <c>type</c>) or a member of a collection of them
	/// (<c>tools</c>, <c>menuItems</c>, …). Clones so the result can be walked read-only without reparenting the
	/// source tree. A DATA/config array or object (no <c>crt.*</c> type) is excluded. Used by the read-only
	/// structure/baseline passes so they see components in non-items slots the same way the element-map walk does.
	/// </summary>
	private static JArray ChildComponentSlots(JObject node) {
		var collected = new JArray();
		foreach (JProperty prop in node.Properties()) {
			if (string.Equals(prop.Name, ItemsPropertyName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (prop.Value is JObject single && IsComponentObject(single)) {
				collected.Add(single.DeepClone());
			} else if (prop.Value is JArray array) {
				foreach (JToken element in array) {
					if (element is JObject component && IsComponentObject(component)) {
						collected.Add(component.DeepClone());
					}
				}
			}
		}
		return collected;
	}

	/// <summary>True when a System.Text.Json object is a view component — carries a string <c>type</c> starting
	/// with <c>crt.</c>.</summary>
	private static bool IsComponentObject(JsonObject obj) =>
		StringProp(obj, "type") is { Length: > 0 } type && type.StartsWith("crt.", StringComparison.OrdinalIgnoreCase);

	/// <summary>True when a Newtonsoft object is a view component — a string <c>type</c> starting <c>crt.</c>.</summary>
	private static bool IsComponentObject(JObject obj) =>
		obj["type"]?.Type == JTokenType.String
		&& obj["type"].ToString().StartsWith("crt.", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// A STRUCTURAL child-element array (Newtonsoft): a non-empty array in which every element is a component
	/// object. Unlike <see cref="IsChildElementArray"/> this does NOT require the members to resolve to a mobile
	/// type — it is the "is this a nested component collection to VISIT?" test used by the chrome-prune pass (which
	/// runs before any registry/rules context and must visit every nested component regardless of convertibility).
	/// </summary>
	private static bool IsComponentArray(JArray array) =>
		array.Count > 0 && array.All(element => element is JObject obj && IsComponentObject(obj));

	/// <summary>
	/// Collects a name → component-TYPE map for every named component in a merged viewConfig tree
	/// (System.Text.Json). Built from the MOBILE template so the converter can recognize a template element
	/// the web page also provides under the SAME name and type — an AUTOMATIC same-component twin that needs
	/// no <c>components</c> rule (that rule exists only for a web→mobile NAME change). First occurrence wins.
	/// Case-insensitive.
	/// </summary>
	public static Dictionary<string, string> CollectComponentTypesByName(JsonArray viewConfig) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		CollectComponentTypesByName(viewConfig, map);
		return map;
	}

	private static void CollectComponentTypesByName(JsonArray nodes, Dictionary<string, string> map) {
		if (nodes is null) {
			return;
		}
		foreach (JsonNode node in nodes) {
			if (node is not JsonObject obj) {
				continue;
			}
			// Defensive string reads: an environment-supplied node could carry a non-string `name`/`type`, and
			// GetValue<string>() would THROW — degrading the whole (best-effort, catch-wrapped) mobile probe.
			string name = StringProp(obj, "name");
			if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name)) {
				map[name] = StringProp(obj, "type");
			}
			if (obj.TryGetPropertyValue("items", out JsonNode itemsNode) && itemsNode is JsonArray items) {
				CollectComponentTypesByName(items, map);
			}
			// Non-items child-element slots too, so a name→type outside items (e.g. floatAction →
			// FloatingActionButton) is known — a retarget into it can then be validated against the template.
			CollectComponentTypesByName(ChildComponentSlots(obj), map);
		}
	}

	/// <summary>Reads a JSON STRING property, or null when absent / not a string (no throw on a non-string).</summary>
	private static string StringProp(JsonObject obj, string propertyName) =>
		obj.TryGetPropertyValue(propertyName, out JsonNode node) && node is JsonValue value
			&& value.TryGetValue(out string text) ? text : null;

	/// <summary>
	/// Collects a name → node map (Newtonsoft <see cref="JObject"/>) for every named component in a merged
	/// viewConfig tree (System.Text.Json input). Built from the WEB template as the conversion BASELINE: a
	/// same-component twin carries only the page's DELTA over this baseline, so a property the page left at the
	/// web-template default is omitted and the mobile template's own default stands. First occurrence wins.
	/// Case-insensitive.
	/// </summary>
	public static Dictionary<string, JObject> CollectComponentNodesByName(JsonArray viewConfig) {
		var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
		if (viewConfig is null) {
			return map;
		}
		CollectComponentNodesByName(JArray.Parse(viewConfig.ToJsonString()), map);
		return map;
	}

	private static void CollectComponentNodesByName(JArray nodes, Dictionary<string, JObject> map) {
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				continue;
			}
			string name = node["name"]?.ToString();
			if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name)) {
				map[name] = node;
			}
			if (node["items"] is JArray items) {
				CollectComponentNodesByName(items, map);
			}
			// Non-items child-element slots too, so a component the web template declares outside items enters the
			// delta baseline and is not treated as page-authored.
			CollectComponentNodesByName(ChildComponentSlots(node), map);
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
			// Non-items child-element slots too (tools, floatAction, …), so a component nested outside items still
			// resolves to its parent for positional placement.
			CollectParentByName(ChildComponentSlots(obj), string.IsNullOrWhiteSpace(name) ? parentName : name, map);
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
		IReadOnlySet<string> baseline,
		IReadOnlyDictionary<string, string> mobileTypesByName,
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		IReadOnlyDictionary<string, JObject> webBaselineNodes,
		IReadOnlySet<string> scopeContainerNames) {
		var result = new JArray();
		foreach (JToken token in nodes) {
			if (token is not JObject node) {
				result.Add(token);
				continue;
			}
			string name = node["name"]?.ToString();
			string type = node["type"]?.ToString();
			JArray items = node["items"] as JArray;
			// Kept despite being in the baseline: a container twin (merge target) or a component twin
			// (a content element the template maps web→mobile, e.g. the list grid — its merge carries
			// the page's delta, like grid columns, that the conversion needs).
			bool isMappedTwin = !string.IsNullOrEmpty(name)
				&& (containerNameMap.ContainsKey(name) || (componentMap is not null && componentMap.ContainsKey(name)));
			// Also kept: an AUTOMATIC same-component twin — a BASELINE (inherited-chrome) LEAF content element
			// the mobile template also provides under the SAME name and type. No `components` rule is needed
			// (that is only for a web→mobile name change). WalkElements then carries the page's delta onto it by
			// merge-by-name. Membership reads the SAME map WalkElements' auto-twin gate reads (webBaselineNodes),
			// not the separate `baseline` name set, so prune and walk can never disagree about which elements are
			// twins (a skew would keep here but reject there, re-inserting a duplicate). The container predicate is
			// likewise IDENTICAL to WalkElements' `isContainer` (children, or a registry/name-heuristic container).
			bool isContainerLike = (items is { Count: > 0 }) || IsLayoutContainer(type, name, null, mobileByType);
			bool isAutoTwin = !isMappedTwin
				&& !string.IsNullOrEmpty(name)
				&& webBaselineNodes.ContainsKey(name)
				&& mobileTypesByName is not null
				&& mobileTypesByName.TryGetValue(name, out string mobileTwinType)
				&& !string.IsNullOrEmpty(type)
				&& string.Equals(mobileTwinType, type, StringComparison.OrdinalIgnoreCase)
				&& !isContainerLike;
			// A container declared in `nonConvertingScopeContainers` (e.g. MainHeader) is KEPT even when it is
			// inherited chrome: the conversion needs it in the tree so its descendants retain it as an ancestor for
			// `path` matching. The walk then treats it as a non-converting scope (it emits no mobile element of its
			// own). Membership is the explicit scope list, NOT any rule's `path`.
			bool isScopeContainer = !string.IsNullOrEmpty(name) && scopeContainerNames.Contains(name);
			bool isTemplateOwned = !string.IsNullOrEmpty(name)
				&& baseline.Contains(name)
				&& !isMappedTwin && !isAutoTwin && !isScopeContainer;
			if (isTemplateOwned) {
				// Drop the template node itself; hoist any surviving (application) descendants up — from items AND
				// from non-items child-element slots (tools, menuItems), so a page-authored component nested outside
				// items is not silently discarded with the template container.
				if (items is not null) {
					foreach (JToken survivor in PruneTemplateComponents(items, containerNameMap, componentMap, baseline, mobileTypesByName, mobileByType, webBaselineNodes, scopeContainerNames)) {
						result.Add(survivor);
					}
				}
				foreach (JProperty prop in node.Properties()) {
					if (!string.Equals(prop.Name, ItemsPropertyName, StringComparison.OrdinalIgnoreCase)
						&& prop.Value is JArray childArray && IsComponentArray(childArray)) {
						foreach (JToken survivor in PruneTemplateComponents(childArray, containerNameMap, componentMap, baseline, mobileTypesByName, mobileByType, webBaselineNodes, scopeContainerNames)) {
							result.Add(survivor);
						}
					}
				}
				continue;
			}
			if (items is not null) {
				node["items"] = PruneTemplateComponents(items, containerNameMap, componentMap, baseline, mobileTypesByName, mobileByType, webBaselineNodes, scopeContainerNames);
			}
			// Prune non-items child-element slots in place too, so a component nested in a KEPT container's
			// tools/menuItems is chrome-subtracted (or kept) consistently with items.
			foreach (JProperty prop in node.Properties().ToList()) {
				if (!string.Equals(prop.Name, ItemsPropertyName, StringComparison.OrdinalIgnoreCase)
					&& prop.Value is JArray childArray && IsComponentArray(childArray)) {
					prop.Value = PruneTemplateComponents(childArray, containerNameMap, componentMap, baseline, mobileTypesByName, mobileByType, webBaselineNodes, scopeContainerNames);
				}
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

	/// <summary>
	/// The mobile type a template-group entry declares for a node it matches: the first
	/// <c>viewConfigTemplates[].value.type</c> of the <c>components</c> entry whose <c>filters</c> match. This is
	/// how a grid — whose entry carries no web/mobile pair — resolves to <c>crt.List</c>, the same
	/// <c>value.type</c> that then gates the template in <see cref="ApplyConversionTemplates"/>. Null when no
	/// template-group entry matches.
	/// </summary>
	private static string ResolveTemplateTargetType(WebToMobilePageConversionRules rules, JObject node,
		IReadOnlyList<string> sourceAncestors) {
		if (rules?.Components is null) {
			return null;
		}
		foreach (ComponentEquivalenceRule entry in rules.Components) {
			if (!RuleAppliesTo(entry, node, sourceAncestors)) {
				continue;
			}
			foreach (ViewConfigTemplateRule template in entry.ViewConfigTemplates) {
				if (template.Value is { } value && value.ValueKind == JsonValueKind.Object
					&& value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String) {
					return type.GetString();
				}
			}
		}
		return null;
	}

	/// <summary>
	/// The single admission gate for a template-group entry, so the type, placement and value resolvers can never
	/// drift out of sync about WHICH rules govern an element (each used to copy-paste this triplet). An entry
	/// applies when it carries templates AND its <c>filters</c> match the node's type AND its <c>path</c> scope is
	/// an ordered ancestor-name subsequence. Adding a fourth condition (a new filter dimension) is a one-line change
	/// here that every resolver picks up at once.
	/// </summary>
	private static bool RuleAppliesTo(ComponentEquivalenceRule entry, JObject node, IReadOnlyList<string> sourceAncestors) =>
		entry?.ViewConfigTemplates is { Count: > 0 }
		&& MatchesAnyFilter(entry.Filters, node)
		&& MatchesPath(entry.Path, sourceAncestors);

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
		} catch (Newtonsoft.Json.JsonException) {
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
	/// Builds a minimal, ready-to-paste diff that applies <paramref name="pageConfig"/> (the converted
	/// viewModelConfig or modelConfig) on top of the mobile template's own merged base
	/// (<paramref name="templateBase"/>), recursively. A key whose subtree already exists in the base is
	/// recursed into (only the real delta is emitted, and every operation targets a path that EXISTS in the
	/// base); a NEW key absent from the base -- a page-owned attribute, list collection, or data source -- is
	/// carried whole in a single <c>merge</c> at its parent path (its nested arrays ride inline; there is no
	/// base entry to preserve, and a list collection is thus declared fully in one place so its columns are not
	/// lost and no flat stub is needed); and an ARRAY that already exists in the base is NEVER merged (a merge
	/// REPLACES arrays wholesale, dropping one side) -- each of the page's entries not already present (by
	/// identity) is appended via an <c>insert</c> at the array's own path, preserving the template's natives.
	/// Falls back to a single root merge (<see cref="BuildRootMergeDiff"/>) when the template base is
	/// unavailable (the probe failed); the caller surfaces a constraint. Returns null when
	/// <paramref name="pageConfig"/> is null.
	/// </summary>
	internal static JsonNode BuildTargetedDiff(JsonNode pageConfig, JsonNode templateBase) =>
		BuildTargetedDiff(pageConfig, templateBase, out _);

	/// <summary>
	/// Overload of <see cref="BuildTargetedDiff(JsonNode, JsonNode)"/> that also reports, in
	/// <paramref name="arrayConflicts"/>, every template-owned array element the page changed that no diff
	/// operation can express -- a named entry present in the base but with different content (which would be
	/// silently lost), or a nameless entry the page modified in place (which would silently duplicate). The
	/// caller surfaces these as a guide constraint so a lossy body is never shipped silently.
	/// </summary>
	internal static JsonNode BuildTargetedDiff(JsonNode pageConfig, JsonNode templateBase, out IReadOnlyList<string> arrayConflicts) {
		arrayConflicts = [];
		if (pageConfig is not JsonObject pageObj) {
			return null;
		}
		if (templateBase is not JsonObject baseObj) {
			// No base to diff against -- degrade to a single root merge (arrays may not union; constraint warns).
			return BuildRootMergeDiff(pageConfig);
		}
		var ops = new JsonArray();
		var conflicts = new List<string>();
		DiffObject(pageObj, baseObj, new List<string>(), ops, conflicts, insideCollection: false);
		arrayConflicts = conflicts;
		return ops;
	}

	/// <summary>
	/// Recursive worker for <see cref="BuildTargetedDiff(JsonNode, JsonNode, out IReadOnlyList{string})"/>. At
	/// <paramref name="path"/> it emits one <c>merge</c> carrying every changed scalar and every new object/array
	/// subtree, then recurses into shared object subtrees and appends an <c>insert</c> per new element of a shared
	/// array. Every emitted operation targets a path that exists in the base: the merge's own path is a base
	/// object, inserts target base arrays, and new subtrees ride inside the parent merge.
	/// <para>
	/// When <paramref name="insideCollection"/> is true the current subtree belongs to a template-owned collection
	/// (a base attribute node with <c>isCollection: true</c>). A scalar that is present in the base but differs is
	/// then NOT re-emitted: that value is the mobile template's own collection config (e.g.
	/// <c>modelConfig.path</c>, <c>sortingConfig</c>, <c>pageSize</c>), and the differing page value is web-derived,
	/// so re-emitting it would clobber the mobile-correct value. This deliberately preserves the ENG-89620 safeguard
	/// the old split enforced (a template-owned collection's scalars are dropped rather than re-applied); genuinely
	/// new keys and new array entries still flow through.
	/// </para>
	/// <para>
	/// The flag propagates to the ENTIRE collection subtree at any depth, not just the collection node's immediate
	/// scalars -- and that unbounded scope is deliberate, not an oversight. The differ runs against the mobile
	/// TEMPLATE base, which at those positions carries only the collection's own mobile config (its
	/// <c>modelConfig</c>, <c>pageSize</c>, <c>sortColumns</c>, etc.), never application content: a page's columns,
	/// filter entries and other authored content are ABSENT from the template base, so they surface as NEW keys /
	/// new array entries (carried whole) rather than as "changed scalars". The depth-wide drop therefore only ever
	/// suppresses a web-side override of the template's own config -- it never drops content a user authored. (A
	/// changed scalar on an EXISTING element several levels down is covered by
	/// <c>BuildTargetedDiff_ChangedScalarNestedInExistingCollectionElement_Dropped</c>; a new element at the same
	/// depth still emits, per <c>BuildTargetedDiff_NewElementNestedInCollection_StillEmitted</c>.)
	/// </para>
	/// </summary>
	private static void DiffObject(
		JsonObject page, JsonObject baseObj, List<string> path, JsonArray ops,
		List<string> arrayConflicts, bool insideCollection) {
		var mergeValues = new JsonObject();
		var recurse = new List<(JsonObject Page, JsonObject Base, string Key, bool InCollection)>();
		var arrayInserts = new List<(string Key, List<JsonNode> Elements)>();
		foreach (KeyValuePair<string, JsonNode> kv in page) {
			JsonNode baseVal = baseObj[kv.Key];
			switch (kv.Value) {
				case JsonArray pageArr when baseVal is JsonArray baseArr:
					DiffArray(pageArr, baseArr, path, kv.Key, arrayInserts, arrayConflicts);
					break;
				case JsonObject pageChild when baseVal is JsonObject baseChild:
					// A node is a template-owned collection when EITHER side marks it: the merged template base, or
					// the page's own converted body (isCollection). Consulting only the base would miss a page-marked
					// collection whose base node lacks the flag, re-emitting its scalars and clobbering the
					// mobile-correct template value at runtime — the ENG-89620 dual-signal safeguard.
					recurse.Add((pageChild, baseChild, kv.Key,
						insideCollection || IsCollectionNode(baseChild) || IsCollectionNode(pageChild)));
					break;
				case JsonObject:
				case JsonArray:
					// New object/array subtree (absent from the base) -- carry it whole in this merge.
					mergeValues[kv.Key] = kv.Value.DeepClone();
					break;
				default:
					// Scalar (or JSON null): emit a new key always, but a CHANGED key only outside a
					// template-owned collection (see the method remarks -- a changed collection scalar is
					// template-owned and re-emitting the web value would clobber the mobile-correct one).
					bool changedScalar = baseVal is not null && !JsonNode.DeepEquals(baseVal, kv.Value);
					if (baseVal is null || (!insideCollection && changedScalar)) {
						mergeValues[kv.Key] = kv.Value?.DeepClone();
					} else if (insideCollection && changedScalar) {
						// The change is dropped (template config wins) — but surface it as a conflict rather than
						// silently, so it flows through dataSectionArrayConflicts -> guide.Constraints exactly like
						// DiffArray's named-element conflict. The code cannot tell template plumbing from authored
						// content at this position, so the caller/developer is told the drop happened.
						arrayConflicts.Add(ArrayConflictLabel(path, kv.Key, "changed scalar dropped: template-owned collection config"));
					}
					break;
			}
		}
		if (mergeValues.Count > 0) {
			ops.Add(new JsonObject {
				["operation"] = "merge",
				["path"] = PathArray(path),
				["values"] = mergeValues
			});
		}
		foreach ((JsonObject childPage, JsonObject childBase, string key, bool inCollection) in recurse) {
			path.Add(key);
			DiffObject(childPage, childBase, path, ops, arrayConflicts, inCollection);
			path.RemoveAt(path.Count - 1);
		}
		foreach ((string key, List<JsonNode> elements) in arrayInserts) {
			path.Add(key);
			JsonArray insertPath = PathArray(path);
			path.RemoveAt(path.Count - 1);
			foreach (JsonNode element in elements) {
				ops.Add(new JsonObject {
					["operation"] = "insert",
					["path"] = insertPath.DeepClone(),
					["values"] = element.DeepClone()
				});
			}
		}
	}

	/// <summary>
	/// Diffs a page array against the base array that already exists at the same key. Appends each genuinely NEW
	/// element (absent from the base by identity) to <paramref name="arrayInserts"/> as an <c>insert</c> delta;
	/// an element already present by identity and deep-equal is a no-op. When an element is present in the base by
	/// NAME identity but its content differs, or when the page modified a nameless element in place (leaving a base
	/// nameless element the page no longer reproduces), it records a CONFLICT into
	/// <paramref name="arrayConflicts"/> instead of emitting an operation.
	/// <para>
	/// No diff operation in the mobile vocabulary can edit an existing array element IN PLACE, so a changed element
	/// cannot be expressed as a targeted op: the mobile path applier (<see cref="JsonPathDiffApplier"/>) identifies
	/// and merges elements by <c>_id</c>, while these config elements are keyed by <c>name</c> only -- so a
	/// name-addressed <c>merge</c> has no <c>_id</c> to resolve, and an <c>insert</c> of the changed element would
	/// DUPLICATE the name rather than replace it. The safeguard is therefore the same as the collection-scalar case:
	/// the template's native value wins (the differing web value is a template-owned-config override, not authored
	/// content) and the change is SURFACED as a conflict the caller raises in <c>guide.Constraints</c> -- not
	/// silently dropped. For a nameless in-place edit the page's element is still inserted (nothing dropped) AND the
	/// duplicate-at-runtime risk is flagged. Base identities are hoisted once (O(N+M), no per-candidate re-serialization).
	/// </para>
	/// </summary>
	private static void DiffArray(
		JsonArray pageArr, JsonArray baseArr, IReadOnlyList<string> path, string key,
		List<(string Key, List<JsonNode> Elements)> arrayInserts, List<string> arrayConflicts) {
		var baseByName = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
		var baseJson = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonNode baseElem in baseArr) {
			string named = NamedIdentity(baseElem);
			if (named is not null) {
				baseByName[named] = baseElem;
			} else {
				baseJson.Add(baseElem?.ToJsonString() ?? "null");
			}
		}
		var newElements = new List<JsonNode>();
		bool namelessInserted = false;
		foreach (JsonNode elem in pageArr) {
			string named = NamedIdentity(elem);
			if (named is not null) {
				if (baseByName.TryGetValue(named, out JsonNode baseMatch)) {
					if (!JsonNode.DeepEquals(baseMatch, elem)) {
						// Present in the base by name but changed -- a merge would REPLACE the whole array and an
						// insert would duplicate the name; neither edits it. Flag rather than drop silently.
						arrayConflicts.Add(ArrayConflictLabel(path, key, named));
					}
					// else deep-equal -> already present, no-op.
				} else {
					newElements.Add(elem);
				}
			} else if (!baseJson.Contains(elem?.ToJsonString() ?? "null")) {
				newElements.Add(elem);
				namelessInserted = true;
			}
		}
		// A nameless element the page inserted while the base still holds a nameless element the page no longer
		// reproduces signals an in-place edit that will now DUPLICATE at runtime -- flag it (the insert is still
		// emitted so nothing is dropped, but the caller is told it is not clean).
		if (namelessInserted && HasUnreproducedNameless(baseArr, pageArr)) {
			arrayConflicts.Add(ArrayConflictLabel(path, key, "(nameless element changed in place)"));
		}
		if (newElements.Count > 0) {
			arrayInserts.Add((key, newElements));
		}
	}

	/// <summary>True when every nameless base element is deep-equal to some page element; false when the base has a
	/// nameless element the page does not reproduce (evidence the page changed it in place).</summary>
	private static bool HasUnreproducedNameless(JsonArray baseArr, JsonArray pageArr) {
		foreach (JsonNode baseElem in baseArr) {
			if (NamedIdentity(baseElem) is not null) {
				continue;
			}
			bool reproduced = false;
			foreach (JsonNode pageElem in pageArr) {
				if (JsonNode.DeepEquals(baseElem, pageElem)) {
					reproduced = true;
					break;
				}
			}
			if (!reproduced) {
				return true;
			}
		}
		return false;
	}

	/// <summary>Human-readable label for a conflicting array element: <c>path.key[identity]</c>.</summary>
	private static string ArrayConflictLabel(IReadOnlyList<string> path, string key, string identity) {
		string full = path.Count > 0 ? $"{string.Join(".", path)}.{key}" : key;
		return $"{full}[{identity}]";
	}

	/// <summary>The Freedom UI <c>name</c> identity of an array element (<c>name:&lt;value&gt;</c>), or null when the
	/// element carries no non-empty string <c>name</c>.</summary>
	private static string NamedIdentity(JsonNode node) =>
		(node as JsonObject)?["name"] is JsonValue nameValue
			&& nameValue.TryGetValue(out string nameStr)
			&& !string.IsNullOrWhiteSpace(nameStr)
			? $"name:{nameStr}"
			: null;

	/// <summary>True when a base attribute node declares itself a collection (<c>isCollection: true</c>).</summary>
	private static bool IsCollectionNode(JsonObject node) =>
		node["isCollection"] is JsonValue flag && flag.TryGetValue(out bool value) && value;

	/// <summary>Builds a JsonArray path from string segments.</summary>
	private static JsonArray PathArray(IReadOnlyList<string> path) {
		var array = new JsonArray();
		foreach (string segment in path) {
			array.Add(segment);
		}
		return array;
	}

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
	/// consumer, are kept. A container the EMPTY-container pass removed (<paramref name="emptyRemovedNames"/>)
	/// is deliberately NOT counted as dropped here: that removal is layout cleanup, and the agreed scope
	/// keeps the attributes it referenced (e.g. a bound <c>visible</c>) untouched. All other
	/// viewModelConfig sections are passed through unchanged.
	/// </summary>
	private static JsonNode BuildMobileViewModelConfig(
		PageBundleInfo bundle, JArray tree, List<ElementMapEntry> elementMap,
		IReadOnlySet<string> emptyRemovedNames = null) {
		if (bundle.ViewModelConfig is not { Count: > 0 }) {
			return null;
		}
		JObject vmc;
		try {
			vmc = JObject.Parse(bundle.ViewModelConfig.ToJsonString());
		} catch (Newtonsoft.Json.JsonException) {
			return null;
		}
		if (vmc["attributes"] is JObject attributes && attributes.Count > 0) {
			HashSet<string> dropped = new(
				elementMap
					.Where(e => string.Equals(e.Operation, "drop", StringComparison.OrdinalIgnoreCase))
					.Select(e => e.WebName)
					.Where(n => !string.IsNullOrEmpty(n)),
				StringComparer.OrdinalIgnoreCase);
			if (emptyRemovedNames is { Count: > 0 }) {
				dropped.ExceptWith(emptyRemovedNames);
			}
			Dictionary<string, HashSet<string>> consumers = BuildAttrConsumers(tree);
			// Attributes referenced by any SURVIVING element-map entry's prebuilt MobileValues are ALWAYS kept, even
			// when the source-tree consumer walk (which descends only `items`) attributed the reference to a DROPPED
			// parent. This is the load-bearing case for the header→FAB path: a dropdown button drops while its menu
			// item flattens into FloatingActionButton.menuItems as a surviving insert whose MobileValues still carries
			// e.g. `visible: "$CanPrint"` — without this, $CanPrint would be pruned as "only referenced by a dropped
			// element" and the converted action would lose its access gate. Keying off what actually SHIPS makes the
			// decision independent of how the tree is traversed.
			HashSet<string> referencedBySurvivors = CollectAttributesReferencedBySurvivors(elementMap);
			foreach (JProperty attr in attributes.Properties().ToList()) {
				if (consumers.TryGetValue(attr.Name, out HashSet<string> users)
					&& users.Count > 0
					&& users.All(dropped.Contains)
					&& !referencedBySurvivors.Contains(attr.Name)) {
					attr.Remove();
				}
			}
		}
		try {
			return JsonNode.Parse(vmc.ToString());
		} catch (System.Text.Json.JsonException) {
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

	// Bound every regex execution so a pathological input cannot hang the MCP server (Sonar S6444).
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	private static readonly Regex ResourceStringsRefPattern =
		new(@"\$Resources\.Strings\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled, RegexTimeout);

	/// <summary>The parent slot every converted element is inserted into.</summary>
	private const string ItemsPropertyName = "items";

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
		foreach (Match match in Regex.Matches(json, @"\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.None, RegexTimeout)) {
			yield return match.Groups[1].Value;
		}
	}

	/// <summary>
	/// Every viewModelConfig attribute referenced by a SURVIVING element-map entry's prebuilt <c>MobileValues</c> —
	/// both plain <c>$Attr</c> bindings and <c>$Resources.Strings.&lt;attr&gt;</c> references. A surviving entry is
	/// an <c>insert</c> or <c>merge</c> (a <c>drop</c> ships nothing; <c>relocate-children</c> is a routing hint with
	/// no values). Used to KEEP an attribute a converted element still binds to even when the source-tree consumer
	/// walk credited the reference to a dropped ancestor (a flattened FAB menu item vs its dropped dropdown parent).
	/// </summary>
	private static HashSet<string> CollectAttributesReferencedBySurvivors(List<ElementMapEntry> elementMap) {
		var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in elementMap) {
			if (entry.MobileValues is null
				|| (!string.Equals(entry.Operation, "insert", StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(entry.Operation, "merge", StringComparison.OrdinalIgnoreCase))) {
				continue;
			}
			string json = entry.MobileValues.ToJsonString();
			foreach (Match match in ResourceStringsRefPattern.Matches(json)) {
				referenced.Add(match.Groups[1].Value);
			}
			foreach (Match match in Regex.Matches(json, @"\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.None, RegexTimeout)) {
				referenced.Add(match.Groups[1].Value);
			}
		}
		return referenced;
	}

	private static List<string> BuildConstraints(
		IReadOnlyList<string> webOnlySections,
		bool hasModelConfig, bool hasViewModelConfig, bool hasAdaptiveLayout, bool templatePruned = false,
		bool viewModelConfigRootMerge = false, bool modelConfigRootMerge = false, bool mobileTemplateUnavailable = false,
		IReadOnlyList<string> dataSectionArrayConflicts = null, bool hasTabAreaLayers = false,
		bool hasEmptyContainerRemovals = false, ComponentPropertyOverrideResult normalization = null,
		bool webTemplateUnavailable = false, bool hasComponentTwin = false) {
		var constraints = new List<string> {
			"Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff — no AMD, no markers, no define() wrapper.",
			"The mobile template provides the Scaffold root — do NOT add a second Scaffold.",
			"No handlers, no validators, no custom converters in a mobile body. Re-implement conditional visibility / required / read-only / set-value logic as entity-level business rules (create-entity-business-rule). Reference only OOTB converters inline in binding expressions.",
			"Use only mobile-registered component types (get-component-info schema-type \"mobile\")."
		};
		if (hasModelConfig) {
			// The "targeted, not a root merge" claim only holds when a real base was diffed against; when the
			// modelConfig fell back to a single root merge (no template base), say so instead of the opposite.
			constraints.Add(modelConfigRootMerge
				? "Use the provided modelConfigDiff VERBATIM as the page's modelConfigDiff. NOTE: no mobile template " +
				  "base was available, so it is a SINGLE ROOT MERGE carrying the whole modelConfig, not a set of " +
				  "targeted operations. A root merge REPLACES arrays wholesale, so if any array here (e.g. a data " +
				  "source's own sort/filter array) is also owned by the mobile template, its baseline entries may be " +
				  "dropped -- verify manually, or re-run with environment-name/uri set so clio can diff against the " +
				  "real base and emit inserts. Do NOT hand-build the data-source section, and keep every attribute's " +
				  "type and path exactly as provided."
				: "Use the provided modelConfigDiff VERBATIM as the page's modelConfigDiff (it is a set of targeted " +
				  "merge + insert operations diffed against the mobile template's own base: a merge for changed or " +
				  "new values, and an insert for each new element of an array the template already carries -- so a " +
				  "data source's native sort/filter entries are not replaced; it is NOT a single root merge). Do NOT " +
				  "collapse it into one root merge, do NOT hand-build the data-source section, and NEVER source it " +
				  "from a pre-existing or reference mobile body -- that is how an attribute's \"type\" gets dropped, " +
				  "which makes its binding unresolvable in Mobile Designer (\"Item with the path not found\"). Keep " +
				  "every attribute and all of its properties exactly as provided.");
		}
		if (hasViewModelConfig) {
			constraints.Add(viewModelConfigRootMerge
				? "viewModelConfig is structurally supported on mobile; the provided block already removed attributes " +
				  "used only by unsupported components. Apply it via viewModelConfigDiff and reference only OOTB mobile " +
				  "converters. NOTE: no mobile template base was available, so viewModelConfigDiff is a SINGLE ROOT " +
				  "MERGE carrying the whole viewModelConfig, not targeted operations -- a root merge REPLACES arrays " +
				  "wholesale, so any array the mobile template also owns may lose its baseline entries; verify " +
				  "manually, or re-run with environment-name/uri set so clio can diff against the real base."
				: "viewModelConfig is structurally supported on mobile; the provided block already removed attributes " +
				  "used only by unsupported components. Apply it via viewModelConfigDiff and reference only OOTB mobile " +
				  "converters — a definitive mobile converter list is forthcoming; flag any custom converter for manual review.");
		}
		if (templatePruned) {
			constraints.Add(
				"Components inherited from the source page's web template (and its base templates) are excluded " +
				"from this guide — the mobile template already provides the equivalent header/scaffold chrome. " +
				"Only the page's delta over its web template is converted; do NOT re-add the web header containers.");
		}
		// Only when a NAME-MAPPED twin exists (the rule declares one, e.g. AttachmentList -> AttachmentFileList):
		// an automatic same-name twin cannot fire without a baseline, so an unreadable web template affects only
		// the rule-declared twin, which then degrades to an advisory merge (no prebuilt delta).
		if (webTemplateUnavailable && hasComponentTwin) {
			constraints.Add(
				"Could not read the source page's WEB template bundle (no active environment, or the read failed), " +
				"so its baseline is unknown. A rule-declared same-component twin (e.g. the attachments detail " +
				"AttachmentList -> AttachmentFileList) cannot be diffed against the template, so it degrades to an " +
				"ADVISORY merge with NO prebuilt mobileValues -- configure the mobile element by merge-by-name per " +
				"componentSuggestions, or re-run with environment-name/uri set so clio can diff against the real web " +
				"template and prebuild the delta.");
		}
		if (webOnlySections is { Count: > 0 }) {
			constraints.Add($"The source page carries web-only section(s): {string.Join(", ", webOnlySections)}. They cannot be transferred to a mobile body — re-implement the supported behavior as entity-level business rules.");
		}
		if ((viewModelConfigRootMerge || modelConfigRootMerge) && mobileTemplateUnavailable) {
			constraints.Add(
				"Could not read the mobile template's bundle (no active environment, or the template read failed) -- " +
				"the data-section diffs fell back to a single root merge instead of being diffed against the " +
				"template's own base. If any array in this page's viewModelConfig or modelConfig is also owned by " +
				"the mobile template (e.g. Items.modelConfig.filterAttributes's built-in QuickFilterGroup_Filters " +
				"for BaseMobileListTemplate, or a data source's own sort/filter array in modelConfig), a root merge " +
				"REPLACES it wholesale and the template's baseline entries may be dropped -- verify manually before " +
				"pasting, or re-run this tool with environment-name/uri set so clio can diff against the real base " +
				"and emit inserts.");
		}
		if (dataSectionArrayConflicts is { Count: > 0 }) {
			constraints.Add(
				"The converted page changes an EXISTING element of a template-owned array that no mobile diff " +
				"operation can edit in place: " + string.Join(", ", dataSectionArrayConflicts) + ". A changed " +
				"named entry is NOT re-applied (the mobile template keeps its own value), and a changed nameless " +
				"entry would DUPLICATE at runtime. Review each listed array; if the page's value must win, adjust " +
				"that data-section entry manually before pasting.");
		}
		if (hasAdaptiveLayout) {
			constraints.Add(
				"adaptiveLayout covers every multi-column crt.GridContainer: on the phone (small) it collapses to a " +
				"single column and stacks the children; on tablet/desktop (medium/large) it keeps the web columns and " +
				"per-child placement. A single-column grid gets no adaptive. Both sides are ALREADY baked into " +
				"mobileValues (the container's adaptive columns and each child's layoutConfig.adaptive) — paste " +
				"mobileValues verbatim. Present the layout to the user; they may adjust or decline it.");
		}
		if (hasTabAreaLayers) {
			constraints.Add(
				"tabAreaLayers is MANDATORY, not a proposal: the two-layer tab body is this team's required mobile " +
				"structure, so never ask whether to apply it, never offer to skip it, and never build a converted " +
				"tab any other way. It is ALREADY baked into the element map: every converter-created tab carries " +
				"synthesized containers (the tab body grid, then its Area card) as ordinary inserts placed right " +
				"after the tab's own entry; every one of that tab's top-level children (expansion panels included) " +
				"already points at the Area with a sequential single-column layoutConfig (a child the adaptive pass " +
				"placed per breakpoint keeps that adaptive placement instead). Apply the inserts in element-map order and " +
				"paste mobileValues verbatim — do NOT reparent, reorder or re-place anything yourself, and do NOT " +
				"add an Area of your own. The synthesized containers have no web counterpart, so they carry no " +
				"webName; tabs provided by the mobile template (merge) get no layers and must stay untouched.");
		}
		// One constraint per report group the rules declared, in the wording the RULE carries — so a new
		// standard is a rules-file entry and never another branch here. The legacy spacing group keeps a
		// built-in text for a rules file that predates reportConstraint.
		AppendNormalizationLines(constraints, normalization);
		if (hasEmptyContainerRemovals) {
			constraints.Add(
				"One or more converted containers ended up EMPTY (no child survived conversion) and were already " +
				"REMOVED deterministically — they appear in elementMap as drop entries with reason \"empty " +
				"container\". Do NOT re-create them, do NOT re-parent anything into them, and do NOT ask the user " +
				"whether to remove them (it is done); just include them in the conversion report like any other drop.");
		}
		return constraints;
	}

	private static List<string> BuildNextSteps(bool hasDataSections, bool hasAdaptiveLayout, bool hasTabAreaLayers = false,
		ComponentPropertyOverrideResult normalization = null) {
		var steps = new List<string> {
			"Read get-guidance with name \"freedom-page-web-to-mobile-conversion\".",
			"Create the target mobile page from recommendedMobileTemplate with create-page (it provides the Scaffold root).",
			"Build the mobile body by iterating elementMap (one entry per source element) — do NOT infer merge-vs-insert from containerMap: operation=merge → reuse the template element mobileName (no insert); operation=insert → insert mobileType into parentName/propertyName and, if captionResource is present, register key=sourceValue via update-page resources; operation=relocate-children → do not recreate the container; its children are placed in parentName (each child entry carries that parentName); operation=drop → skip it. Fill each component's values from the matching mobileContracts entry (call get-component-info schema-type \"mobile\" only when more detail is needed).",
			"For every insert, paste elementMap[].mobileValues as the component's values VERBATIM — it already carries the type and EVERY source property the mobile component supports (including the field caption). Never drop a supported property. Then add ONLY the value binding (control, or value for lookups), which is left out on purpose. validate-page is the backstop: it rejects an insert that drops a required property (e.g. a field caption, or a lookup-path attribute's type) and update-page refuses to save."
		};
		if (hasDataSections) {
			steps.Add("Paste the provided modelConfigDiff and viewModelConfigDiff VERBATIM as the page's modelConfigDiff / viewModelConfigDiff (each is diffed against the mobile template's own base: a targeted merge for changed/new values and an insert per new element of an array the template already carries, so the template's native array entries are preserved — unless a constraint reports no template base was available, in which case it degrades to a single root merge). Do NOT rebuild them by hand or collapse targeted operations into one root merge — that lets the mobile diff engine replace arrays and drop the page's own entries; and never copy the data-source section from an existing body — keep every attribute's type and path.");
		}
		if (hasAdaptiveLayout) {
			steps.Add("Adaptive layout for multi-column grid containers is already baked into mobileValues (container adaptive columns + each child's layoutConfig.adaptive: phone collapses to 1 column, tablet/desktop keep the web columns). Present guide.adaptiveLayout to the user for review; they may adjust or decline it.");
		}
		if (hasTabAreaLayers) {
			steps.Add("The mobile designer's two-layer tab body (tab body grid + Area card) is already baked into the element map for every converter-created tab: the tab's top-level content (expansion panels included) is retargeted into the Area and stacked in web order. Apply the element map as it is. This structure is MANDATORY — do NOT ask the user whether to apply it and do NOT offer an alternative; just STATE what it does when you present the plan (guide.tabAreaLayers: tab -> synthesized layer names -> movedChildren in row order).");
		}
		AppendNormalizationLines(steps, normalization);
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
		IReadOnlyDictionary<string, string> AttrToColumn,
		JObject Resources,
		string RelocateTarget,
		List<ElementMapEntry> Out,
		IReadOnlyDictionary<string, RequestMappingRule> RequestMap,
		List<ConvertedRequest> ConvertedRequests,
		List<DroppedRequest> DroppedRequests,
		List<FlaggedRequest> FlaggedRequests,
		Dictionary<string, JObject> SourceLayouts,
		Dictionary<string, int> GridContainerColumns,
		IReadOnlyDictionary<string, string> PositionalParentByAnchor,
		IReadOnlyDictionary<string, string> MobileTypesByName,
		IReadOnlyDictionary<string, JObject> WebBaselineNodes,
		JObject WebBaselineResources,
		IReadOnlySet<string> ScopeContainerNames);

	/// <summary>
	/// The set of NON-CONVERTING scope container names — declared EXPLICITLY by the rules'
	/// <see cref="WebToMobilePageConversionRules.NonConvertingScopeContainers"/>, NOT inferred from any rule's
	/// <c>path</c>. Such a container produces no element of its own, its subtree is walked in scope mode (a matching
	/// action retargets, everything else drops), and it is KEPT through template-chrome pruning so its descendants
	/// retain it as an ancestor for <c>path</c> matching (e.g. <c>MainHeader</c>). Decoupling this from <c>path</c>
	/// is deliberate: a container whose name merely appears in some rule's path must NOT become a drop-everything
	/// scope — otherwise a multi-element path like <c>["Outer","Inner"]</c> would make a standalone <c>Inner</c>
	/// elsewhere silently drop its whole subtree.
	/// </summary>
	private static IReadOnlySet<string> CollectScopeContainerNames(WebToMobilePageConversionRules rules) =>
		new HashSet<string>(
			(rules?.NonConvertingScopeContainers ?? []).Where(name => !string.IsNullOrWhiteSpace(name)),
			StringComparer.OrdinalIgnoreCase);

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
		IReadOnlyDictionary<string, string> attrToColumn,
		JObject resources,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		List<ConvertedRequest> convertedRequests,
		List<DroppedRequest> droppedRequests,
		List<FlaggedRequest> flaggedRequests,
		Dictionary<string, JObject> sourceLayouts,
		Dictionary<string, int> gridContainerColumns,
		IReadOnlyDictionary<string, string> positionalParentByAnchor,
		IReadOnlyDictionary<string, string> mobileTypesByName,
		IReadOnlyDictionary<string, JObject> webBaselineNodes,
		JObject webBaselineResources) {
		var ctx = new ElementMapContext(map,
			componentMap ?? new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase),
			mobileTypes, mobileByType ?? new Dictionary<string, ComponentRegistryEntry>(),
			webByType ?? new Dictionary<string, ComponentRegistryEntry>(),
			rules, attrToColumn, resources, RelocateTargetFor(map), [],
			requestMap, convertedRequests, droppedRequests, flaggedRequests, sourceLayouts, gridContainerColumns,
			positionalParentByAnchor ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			mobileTypesByName ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			webBaselineNodes ?? new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase),
			webBaselineResources,
			CollectScopeContainerNames(rules));
		WalkElements(ctx, tree, mobileParentName: null);
		return ctx.Out;
	}

	private static void WalkElements(ElementMapContext ctx, JArray nodes, string mobileParentName,
		string parentPropertyName = ItemsPropertyName, IReadOnlyList<string> sourceAncestors = null,
		bool inNonConvertingScope = false) {
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

			// Anonymous wrapper: no entry, but recurse preserving the parent context (and the ancestor chain — a
			// nameless wrapper contributes no ancestor name).
			if (string.IsNullOrEmpty(name)) {
				if (items is not null) {
					WalkElements(ctx, items, mobileParentName, sourceAncestors: sourceAncestors,
						inNonConvertingScope: inNonConvertingScope);
				}
				continue;
			}

			// A non-converting scope container (declared in `nonConvertingScopeContainers`, e.g. MainHeader): it
			// produces NO mobile element of its own, but its subtree is walked in "scope" mode — a matching header
			// action retargets (e.g. into FloatingActionButton.menuItems) and everything else is dropped, so the
			// container and its unconverted content are not present on mobile.
			if (!inNonConvertingScope && ctx.ScopeContainerNames.Contains(name)) {
				IReadOnlyList<string> scopeAncestors = Append(sourceAncestors, name);
				if (items is not null) {
					WalkElements(ctx, items, mobileParentName, ItemsPropertyName, scopeAncestors, inNonConvertingScope: true);
				}
				RecurseChildArrays(ctx, node, name, type, scopeAncestors, inNonConvertingScope: true);
				continue;
			}

			// Inside a non-converting scope: only a node a conversion template RETARGETS (e.g. a header crt.Button /
			// crt.MenuItem → FloatingActionButton.menuItems) that also has a CONVERTIBLE `clicked` is converted; it
			// is emitted at the retargeted placement. Any other node (a container-only dropdown with no clicked of
			// its own, an explicitly-unsupported clicked, a component no rule matches, or a retarget whose target the
			// mobile template lacks) is dropped with a reason built FROM DATA — naming the scope container and the
			// specific cause — and its lost action request is recorded so requestConversions reflects it. Either way
			// the subtree is recursed IN SCOPE, so a dropdown's nested menuItems still flatten into the same target.
			if (inNonConvertingScope) {
				string scopeContainer = sourceAncestors?.LastOrDefault(a => ctx.ScopeContainerNames.Contains(a)) ?? name;
				string scopedType = ResolveTemplateTargetType(ctx.Rules, node, sourceAncestors);
				(string Parent, string Property)? scopedTarget = scopedType is null
					? null
					: ResolveTemplatePlacement(ctx, node, scopedType, name, ResolveParent(ctx, mobileParentName),
						ItemsPropertyName, sourceAncestors);
				ClickedConvertibility clicked = ClassifyClicked(ctx, node, out string scopedRequest);
				bool targetMissing = scopedTarget is { } t && RetargetTargetMissing(ctx, t.Parent);
				if (scopedTarget is { } target && clicked == ClickedConvertibility.Convertible && !targetMissing) {
					CaptionResource scopedCaption = ResolveCaptionResource(ctx, node, name);
					// BuildMobileValues → ProcessEventBindings converts (or keeps+flags) the clicked request in place,
					// so the FAB menu item ships the MOBILE request, not the web one, and requestConversions records it.
					JsonNode scopedValues = BuildMobileValues(ctx, node, name, scopedType, scopedCaption,
						target.Parent, target.Property, sourceAncestors);
					ctx.Out.Add(new ElementMapEntry {
						WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = scopedType,
						ParentName = target.Parent, PropertyName = target.Property, Index = null,
						CaptionResource = scopedCaption, MobileValues = scopedValues,
						Reason = $"action under non-converting scope '{scopeContainer}'; converted into {target.Parent}.{target.Property}"
					});
				} else {
					(string dropReason, string requestLossReason) = ScopeDropReason(
						scopeContainer, name, scopedType, scopedTarget, clicked, scopedRequest, targetMissing);
					ctx.Out.Add(Drop(name, type, dropReason));
					// Record the lost action so requestConversions surfaces it (BuildMobileValues did not run, so
					// nothing recorded it yet). None-clicked nodes carry no request and nothing is recorded.
					if (scopedRequest is not null) {
						ctx.DroppedRequests.Add(new DroppedRequest {
							ElementName = name, Binding = "clicked", WebRequest = scopedRequest, Reason = requestLossReason
						});
					}
				}
				IReadOnlyList<string> childScopeAncestors = Append(sourceAncestors, name);
				if (items is not null) {
					WalkElements(ctx, items, mobileParentName, ItemsPropertyName, childScopeAncestors, inNonConvertingScope: true);
				}
				RecurseChildArrays(ctx, node, name, scopedType ?? type, childScopeAncestors, inNonConvertingScope: true);
				continue;
			}

			// A positional sibling of the anchor is rerouted to the mobile anchor's parent (± index).
			bool isPositional = positional.TryGetValue(name, out (string Parent, int? Index) place);

			bool isContainer = (items is { Count: > 0 }) || IsLayoutContainer(type, name, null, ctx.MobileByType);

			// Capture the element's web layoutConfig (grid placement) and, for a grid container, its web
			// column count — the adaptive pass reads both to build the per-breakpoint mobile layout.
			CaptureSource(ctx, name, node);

			// 0. drop — ONLY a crt.Button whose clicked request the Creatio Mobile app does not support: it would
			//    be a dead button, so it is removed. Other component types are NOT dropped for an unsupported
			//    request — some components legitimately use a SYSTEM request that is absent from the supported
			//    list, and dropping the whole component over it loses valid UI. Their bindings are handled when
			//    the component is built (ProcessEventBindings keeps/flags an unknown request rather than dropping).
			if (string.Equals(type, "crt.Button", StringComparison.OrdinalIgnoreCase)
				&& UnsupportedRequestOf(ctx, node) is { } unsupportedRequest) {
				ctx.Out.Add(Drop(name, type, $"button uses request '{unsupportedRequest}' not supported on the Creatio Mobile app"));
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
					WalkElements(ctx, items, twinMobileName, sourceAncestors: Append(sourceAncestors, name));
				}
				continue;
			}

			// 1b. component twin — a content component the template maps web→mobile by NAME (e.g. the list
			//     template's grid "DataTable" → mobile "List"). It is NOT template chrome: it is kept and
			//     configured by merge-by-name. HOW to convert it (e.g. a grid's columns → the list row) is
			//     type-driven — it lives in the general components rule and is surfaced in
			//     componentSuggestions[<type>]; clio hardcodes no component-specific transform here.
			if (ctx.ComponentMap.TryGetValue(name, out ComponentMappingRule compRule)) {
				// The mobile type is normally the web type when it survives on mobile as-is; a rule that maps
				// to a DIFFERENT mobile type (web crt.FolderTree → mobile crt.FolderTreeActions) declares it
				// explicitly so carried values can be shape-coerced against the right registry contract.
				string twinMobileType = !string.IsNullOrWhiteSpace(compRule.MobileType)
					? compRule.MobileType
					: (ctx.MobileTypes.Contains(type ?? "") ? type : null);
				// Deterministic merge payload carried onto the template-provided element:
				//  • an explicit carryProperties whitelist → just those keys (e.g. the folder tree binding);
				//  • otherwise, when the twin is the SAME component on both sides (twinMobileType == web type,
				//    e.g. crt.FileList → crt.FileList) → carry the page's DELTA over the web-template baseline
				//    (only what the page changed; an unchanged property is left to the mobile template's default);
				//  • a structural twin whose web type has no mobile equivalent (crt.DataGrid → crt.List), OR a
				//    same-component twin with no baseline node → no payload; it stays an advisory merge and the
				//    how-to is left to componentSuggestions.
				// The payload never carries the component `type` (a merge targets an element the template already
				// owns) nor placement (layoutConfig — owned by the template). The page's caption IS carried (it
				// overrides the template label; CollectResourceStrings adds its resource to the schema).
				JsonNode twinValues = BuildTwinMergeValues(ctx, node, compRule, twinMobileType, type);
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "merge", MobileName = compRule.Mobile,
					MobileType = twinMobileType,
					MobileValues = twinValues,
					Reason = ComponentTwinReason(name, type, compRule, twinValues is not null)
				});
				if (items is not null) {
					WalkElements(ctx, items, compRule.Mobile, sourceAncestors: Append(sourceAncestors, name));
				}
				continue;
			}

			// 1c. automatic same-component twin — a component INHERITED FROM THE WEB TEMPLATE (in the baseline)
			//     that the mobile template also provides as a LEAF under the SAME name and type (e.g. Feed → Feed).
			//     No `components` rule is needed; that rule exists only for a web→mobile NAME change (AttachmentList
			//     → AttachmentFileList). The web-template membership gate is REQUIRED: without it a page-authored
			//     leaf that merely shares a name+type with a mobile-template element would be reclassified from
			//     insert to merge, losing its ParentName / Index / caption / event bindings. Carry ONLY the page's
			//     DELTA over the web-template baseline, so an inherited component the page did not touch contributes
			//     nothing and the mobile template's own defaults stand — an unchanged twin is an ADVISORY merge
			//     (null values), still emitted so the element is a valid rule target in the survivors map. A layout
			//     container is never an auto twin (handled by the container map / insert paths below).
			if (!isContainer
				&& ctx.WebBaselineNodes.ContainsKey(name)
				&& ctx.MobileTypesByName.TryGetValue(name, out string autoTwinType)
				&& !string.IsNullOrEmpty(type)
				&& string.Equals(autoTwinType, type, StringComparison.OrdinalIgnoreCase)) {
				JsonNode delta = BuildDeltaTwinMergeValues(ctx, node, name, type, name);
				// Always emit (advisory when the delta is null): the element exists on mobile, so it must appear in
				// the survivors map — a page business rule targeting it converts instead of being dropped as
				// "every referenced element is unsupported".
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "merge", MobileName = name, MobileType = type,
					MobileValues = delta,
					Reason = AutoComponentTwinReason(name, type, delta is not null)
				});
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
						WalkElements(ctx, items, target, sourceAncestors: Append(sourceAncestors, name));
					}
					continue;
				}

				// 2. insert — mobile-supported container; emitted unconditionally here. A container whose every
				//    child drops is cleaned up AFTERWARDS by RemoveEmptyContainers (rules-listed types only,
				//    switched by the emptyContainerRemoval rules section) — the walk itself cannot know
				//    emptiness, children are decided after their parent. A web tab
				//    inserts into the mobile Tabs as a new tab; a positional sibling inserts into the mobile
				//    anchor's parent (± index) instead of the walk parent.
				CaptionResource containerCaption = ResolveCaptionResource(ctx, node, name);
				// Resolved BEFORE the values are built: a view-config template may ECHO the placement so the
				// shape it declares can be read in place, and echoing needs the value the entry will carry.
				string containerParent = isPositional ? place.Parent : ResolveParent(ctx, mobileParentName);
				string containerProperty = parentPropertyName;
				int? containerIndex = isPositional ? place.Index : null;
				bool containerRetargeted = false;
				if (ResolveTemplatePlacement(ctx, node, type, name, containerParent, containerProperty, sourceAncestors)
					is { } containerTarget) {
					// A retarget into a parent the mobile template lacks is dropped, not emitted as an unresolvable
					// insert (see the leaf branch). Container children are hoisted to the walk parent so they are not
					// lost with the container that could not be placed.
					if (RetargetTargetMissing(ctx, containerTarget.Parent)) {
						ctx.Out.Add(Drop(name, type,
							$"a conversion template retargets container '{name}' into '{containerTarget.Parent}', which is not "
							+ "present on the mobile template — add it to the target template or adjust the rule"));
						if (items is not null) {
							WalkElements(ctx, items, ResolveParent(ctx, mobileParentName), sourceAncestors: Append(sourceAncestors, name));
						}
						continue;
					}
					containerParent = containerTarget.Parent;
					containerProperty = containerTarget.Property;
					containerIndex = null;
					containerRetargeted = true;
				}
				JsonNode containerValues = BuildMobileValues(ctx, node, name, type, containerCaption,
					containerParent, containerProperty, sourceAncestors);
				ctx.Out.Add(new ElementMapEntry {
					WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = type,
					ParentName = containerParent, PropertyName = containerProperty,
					Index = containerIndex,
					CaptionResource = containerCaption,
					MobileValues = containerValues,
					Reason = containerRetargeted
						? $"container; retargeted by a conversion template into {containerParent}.{containerProperty}"
						: isPositional
							? $"container; placed {(place.Index.HasValue ? "above" : "below")} the mobile Tabs (in {place.Parent})"
							: "container; mobile-supported"
				});
				IReadOnlyList<string> containerChildAncestors = Append(sourceAncestors, name);
				if (items is not null) {
					WalkElements(ctx, items, name, sourceAncestors: containerChildAncestors);
				}
				RecurseChildArrays(ctx, node, name, type, containerChildAncestors);
				continue;
			}

			// leaf — drop only when not transferable, i.e. the type has no mobile equivalent. The data source
			// an element is bound to is NOT a transferability criterion: a mobile page carries the same
			// multi-data-source structure as web (the data-section pass surfaces every data source, not just
			// the primary one), so a detail list bound to a non-primary page data source converts like any
			// other leaf. Dropping it here used to remove whole detail sections — and, because emptiness
			// cascades, their wrapper containers with them.
			// Conversion templates have PRIORITY: a component matched by a components[].filters group is converted
			// via its template (whose value.type is the mobile type) BEFORE the registry-support check — the
			// template path then builds the values inside BuildMobileValues. Only a component with no matching
			// template falls back: kept as-is when the mobile registry supports it, else mapped by a
			// type-equivalence rule (rule.Mobile[0], e.g. crt.Checkbox→crt.Toggle), else dropped.
			string leafMobileType = ResolveConvertedMobileType(ctx, node, sourceAncestors);
			if (string.IsNullOrEmpty(leafMobileType)) {
				ctx.Out.Add(Drop(name, type, $"type '{type}' not in mobile registry"));
				continue;
			}
			CaptionResource leafCaption = ResolveCaptionResource(ctx, node, name);
			string leafParent = isPositional ? place.Parent : ResolveParent(ctx, mobileParentName);
			string leafProperty = parentPropertyName;
			int? leafIndex = isPositional ? place.Index : null;
			// A conversion template may DRIVE placement: retarget the element into a declared container/property
			// (appended, no index) instead of where the walk found it — e.g. a header button → FloatingActionButton.
			bool leafRetargeted = false;
			if (ResolveTemplatePlacement(ctx, node, leafMobileType, name, leafParent, leafProperty, sourceAncestors)
				is { } leafTarget) {
				// Never emit an unresolvable insert: when a template retargets into a parent the mobile template
				// is known to lack, drop the element with a diagnostic instead. Nested actions are still recursed
				// in scope so they too get an explicit outcome rather than vanishing under a missing target.
				if (RetargetTargetMissing(ctx, leafTarget.Parent)) {
					ctx.Out.Add(Drop(name, type,
						$"a conversion template retargets '{name}' into '{leafTarget.Parent}', which is not present on the "
						+ "mobile template — add it to the target template or adjust the rule"));
					ClassifyClicked(ctx, node, out string missingTargetRequest);
					if (missingTargetRequest is not null) {
						ctx.DroppedRequests.Add(new DroppedRequest {
							ElementName = name, Binding = "clicked", WebRequest = missingTargetRequest,
							Reason = $"its element could not be placed (conversion target '{leafTarget.Parent}' is absent on the mobile template)"
						});
					}
					RecurseChildArrays(ctx, node, name, leafMobileType, Append(sourceAncestors, name), inNonConvertingScope: true);
					continue;
				}
				leafParent = leafTarget.Parent;
				leafProperty = leafTarget.Property;
				leafIndex = null;
				leafRetargeted = true;
			}
			JsonNode leafValues = BuildMobileValues(ctx, node, name, leafMobileType, leafCaption,
				leafParent, leafProperty, sourceAncestors);
			string leafReason = leafRetargeted
				? $"field/leaf; retargeted by a conversion template into {leafParent}.{leafProperty}"
				: isPositional
					? $"field/leaf; placed {(place.Index.HasValue ? "above" : "below")} the mobile Tabs (in {place.Parent})"
					: "field/leaf; mobile-supported";
			ctx.Out.Add(new ElementMapEntry {
				WebName = name, WebType = Nz(type), Operation = "insert", MobileName = name, MobileType = leafMobileType,
				ParentName = leafParent, PropertyName = leafProperty,
				Index = leafIndex,
				CaptionResource = leafCaption,
				MobileValues = leafValues,
				Reason = leafReason
			});
			// A leaf can still own nested child-element arrays (e.g. a crt.Button's menuItems) — descend so their
			// components are converted rather than carried verbatim inside the leaf's values. When the leaf itself
			// was RETARGETED by a template, its subtree is descended IN SCOPE MODE so nested actions flatten to the
			// same target (convert-or-drop) — the single placement rule shared with the non-converting-scope path,
			// rather than nesting them under the moved element.
			RecurseChildArrays(ctx, node, name, leafMobileType, Append(sourceAncestors, name),
				inNonConvertingScope: leafRetargeted);
		}
	}

	/// <summary>
	/// Descend the node's child-element arrays OTHER than <c>items</c> (which the branch that owns the node recurses
	/// itself, with its own parent context). A child-element array is recognised by SHAPE — see
	/// <see cref="IsChildElementArray"/> — not by a hardcoded property-name list, so this generically covers
	/// <c>menuItems</c> (crt.Button/crt.MenuItem), <c>tools</c> (crt.ExpansionPanel) and any future nested-component
	/// property, while leaving data arrays (grid columns, a data source's sort/filter) alone. Each such array is
	/// walked with the node's own mobile name as parent and the property name as the slot, so its components become
	/// their own element-map entries under the right <c>propertyName</c>.
	/// </summary>
	private static void RecurseChildArrays(ElementMapContext ctx, JObject node, string mobileParentName,
		string mobileType, IReadOnlyList<string> childAncestors, bool inNonConvertingScope = false) {
		foreach (JProperty prop in node.Properties()) {
			if (string.Equals(prop.Name, ItemsPropertyName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (IsChildElementArray(ctx, mobileType, prop.Name, prop.Value, childAncestors)) {
				WalkElements(ctx, (JArray)prop.Value, mobileParentName, prop.Name, childAncestors, inNonConvertingScope);
			}
		}
	}

	/// <summary>How a node's own <c>clicked</c> binding classifies for scope conversion.</summary>
	private enum ClickedConvertibility {
		/// <summary>No <c>clicked</c> event binding — a container-only node (e.g. a dropdown), not itself an action.</summary>
		None,
		/// <summary>The versioned map explicitly CLEARS this request's mobile target — a dead action on mobile.</summary>
		ExplicitlyUnsupported,
		/// <summary>Supported OR unknown/custom — the action converts (an unknown request is kept and flagged, aligning
		/// with <see cref="ProcessOneEventBinding"/>, rather than silently dropped).</summary>
		Convertible
	}

	/// <summary>
	/// Classifies a node's OWN <c>clicked</c> binding — the gate for converting an action inside a non-converting
	/// scope (e.g. a header button → FAB menu item). Only <c>clicked</c> is considered (a DIFFERENT secondary
	/// binding being unsupported does not disqualify the action). The deciding difference from a hard "supported?"
	/// gate: a request absent from BOTH the versioned map and the bundled set is UNKNOWN/custom, and is treated as
	/// convertible-and-flagged (matching <see cref="ProcessOneEventBinding"/>) instead of dropped — a header button
	/// wired to a <c>usr.*</c> request is exactly the button the feature is meant to convert.
	/// </summary>
	private static ClickedConvertibility ClassifyClicked(ElementMapContext ctx, JObject node, out string request) {
		request = null;
		if (node["clicked"] is not JObject clicked || !IsEventBinding(clicked)) {
			return ClickedConvertibility.None;
		}
		request = clicked["request"].ToString();
		return ctx.RequestMap.TryGetValue(request, out RequestMappingRule rule) && string.IsNullOrWhiteSpace(rule.Mobile)
			? ClickedConvertibility.ExplicitlyUnsupported
			: ClickedConvertibility.Convertible;
	}

	/// <summary>
	/// True when a template RETARGETS an element into a parent the mobile template is known NOT to provide, so
	/// emitting an insert there would be unresolvable on apply. It decides "absent" ONLY when the mobile template's
	/// component names were actually probed (<see cref="ElementMapContext.MobileTypesByName"/> non-empty); with none
	/// probed (template unavailable/unknown) it returns false — absence cannot be proven, so the retarget stands and
	/// the caller emits the insert as before rather than dropping a valid conversion on missing information.
	/// </summary>
	private static bool RetargetTargetMissing(ElementMapContext ctx, string parentName) =>
		ctx.MobileTypesByName is { Count: > 0 } && !string.IsNullOrEmpty(parentName)
		&& !ctx.MobileTypesByName.ContainsKey(parentName);

	/// <summary>
	/// Builds the element drop reason AND the request-loss reason for a node that did NOT convert inside a
	/// non-converting scope, from the specific cause — so the report names the scope container and distinguishes an
	/// absent target, an unsupported action, an unmatched component, and a container-only node instead of collapsing
	/// them into one string. The mechanism is name-agnostic (any <c>nonConvertingScopeContainers</c> entry), so the
	/// wording says "scope", not "header".
	/// </summary>
	private static (string DropReason, string RequestLossReason) ScopeDropReason(
		string scopeContainer, string name, string scopedType, (string Parent, string Property)? scopedTarget,
		ClickedConvertibility clicked, string request, bool targetMissing) {
		if (targetMissing && scopedTarget is { } target) {
			return ($"under non-converting scope '{scopeContainer}'; conversion target '{target.Parent}' is not present on "
					+ $"the mobile template, so '{name}' cannot be placed — add a '{target.Parent}' to the target template or adjust the rule",
				$"its element could not be placed (conversion target '{target.Parent}' is absent on the mobile template)");
		}
		if (clicked == ClickedConvertibility.ExplicitlyUnsupported) {
			return ($"under non-converting scope '{scopeContainer}'; action '{request}' is not supported on the Creatio Mobile app",
				$"'{request}' is not supported on the Creatio Mobile app; the action was dropped");
		}
		if (scopedType is null) {
			return ($"under non-converting scope '{scopeContainer}'; no conversion rule matches this component in scope",
				"no conversion rule matched the component in scope; its action was dropped");
		}
		// A convertible/absent clicked but no template placement, or a container-only node (no clicked): not itself
		// an action to place. Its nested actions, if any, are still flattened by the in-scope recursion below.
		return ($"under non-converting scope '{scopeContainer}'; not an action to place here (no own convertible clicked binding)",
			"the component is not itself a placeable action; its action was dropped");
	}

	/// <summary>
	/// The source-ancestor chain (outer→inner web element names) for the children of <paramref name="name"/>, i.e.
	/// the node's own ancestors plus its name. A nameless node contributes nothing. Used to scope <c>path</c> rules.
	/// </summary>
	private static IReadOnlyList<string> Append(IReadOnlyList<string> ancestors, string name) {
		if (string.IsNullOrEmpty(name)) {
			return ancestors ?? [];
		}
		var result = new List<string>(ancestors ?? []) { name };
		return result;
	}

	/// <summary>
	/// True when a property value is a nested array of child VIEW ELEMENTS the walk can EMIT as its own
	/// element-map entries — a NON-EMPTY <see cref="JArray"/> in which EVERY element is an object whose <c>type</c>
	/// is a component type (a string starting with <c>crt.</c>) AND resolves to a mobile type in this scope (see
	/// <see cref="ResolvesToMobileType"/>). This is how the walk recognises a child-element collection
	/// (<c>items</c>, <c>menuItems</c>, <c>tools</c>, …) generically, without a hardcoded property-name list.
	/// <para>
	/// Two guards keep it conservative so it NEVER destroys a slot it cannot reconstruct:
	/// </para>
	/// <list type="bullet">
	/// <item>Requiring EVERY element to be <c>crt.*</c>-typed (not just one) leaves a DATA/config array (grid column
	/// objects keyed by <c>code</c>, a track-sizing <c>columns</c> array of strings, a data source's sort/filter, or
	/// any array mixing components with non-component objects) carried verbatim as a value.</item>
	/// <item>Requiring EVERY member to RESOLVE to a mobile type leaves a valid nested collection whose member type
	/// has NO mobile counterpart — e.g. a body-level <c>crt.Button</c>'s <c>menuItems</c> of <c>crt.MenuItem</c>
	/// when no conversion template applies here and the registry does not declare <c>crt.MenuItem</c> — carried
	/// verbatim on its owner (a valid mobile input) instead of walked out into <c>Drop</c> entries that would strip
	/// the slot and leave, say, a dropdown button with an empty menu. In a scope where a template DOES convert the
	/// member (a header <c>crt.MenuItem</c> under <c>MainHeader</c>), the member resolves and the slot is walked.</item>
	/// </list>
	/// A string binding (<c>items: "$Attr"</c>) is not an array at all. A property the mobile registry declares as a
	/// single <c>object</c> (e.g. <c>crt.List.itemLayout</c>, whose web array wrapper is coerced to an object) is a
	/// nested CONFIG, not a collection to walk, so it is excluded even when its elements are <c>crt.*</c>-typed —
	/// and, because the registry-shape check comes first, that exclusion holds even in the registry-degraded case
	/// where the resolve check alone would already keep it carried. NOTE: only <c>crt.*</c> is recognised — an array
	/// containing a custom <c>usr.*</c> component is not descended into (it is carried verbatim, as before).
	/// </summary>
	private static bool IsChildElementArray(ElementMapContext ctx, string mobileType, string propName, JToken value,
		IReadOnlyList<string> childAncestors) {
		if (value is not JArray array || array.Count == 0) {
			return false;
		}
		// A registry-declared single-object slot (itemLayout, …) is carried and shape-coerced, never walked.
		if (!string.IsNullOrEmpty(mobileType)
			&& ctx.MobileByType.TryGetValue(mobileType, out ComponentRegistryEntry entry) && entry is not null
			&& ResolveExpectedShape(entry, propName) == JsonValueKind.Object) {
			return false;
		}
		foreach (JToken element in array) {
			if (element is not JObject obj
				|| obj["type"]?.Type != JTokenType.String
				|| !obj["type"].ToString().StartsWith("crt.", StringComparison.OrdinalIgnoreCase)
				|| !ResolvesToMobileType(ctx, obj, childAncestors)) {
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// The mobile type a node converts to — a conversion template that matches it in scope (highest priority), else
	/// the same type when the mobile registry supports it, else the first type-equivalence rule target. Null when
	/// the node has no mobile counterpart. This is the single source of the leaf's resolved type and of the
	/// "can this nested member be re-emitted?" test in <see cref="IsChildElementArray"/>, so the walk and the
	/// child-array detection can never disagree about what converts.
	/// </summary>
	private static string ResolveConvertedMobileType(ElementMapContext ctx, JObject node,
		IReadOnlyList<string> sourceAncestors) {
		string type = node["type"]?.ToString();
		return ResolveTemplateTargetType(ctx.Rules, node, sourceAncestors)
			?? (!string.IsNullOrEmpty(type) && ctx.MobileTypes.Contains(type)
				? type
				: FindRule(ctx.Rules, type)?.Mobile?.FirstOrDefault());
	}

	/// <summary>True when the node resolves to a mobile type (see <see cref="ResolveConvertedMobileType"/>).</summary>
	private static bool ResolvesToMobileType(ElementMapContext ctx, JObject node, IReadOnlyList<string> sourceAncestors) =>
		!string.IsNullOrEmpty(ResolveConvertedMobileType(ctx, node, sourceAncestors));

	/// <summary>
	/// The reason line for a template-mapped component twin: the rule's business <c>note</c> (what the
	/// element is) plus a pointer to the type-driven conversion detail in <c>componentSuggestions</c>. clio
	/// keeps no component-specific transform — the "how" (e.g. a grid's columns → the list row) is defined
	/// by the general components rule and surfaced there for the model to apply.
	/// </summary>
	private static string ComponentTwinReason(string name, string type, ComponentMappingRule rule, bool hasPrebuiltPayload) {
		string basis = !string.IsNullOrWhiteSpace(rule.Note) ? rule.Note : $"web '{name}' maps to mobile '{rule.Mobile}'";
		// Whenever a prebuilt mobileValues payload was produced — a carryProperties whitelist OR a
		// same-component carry-all — tell the caller to paste it (a merge is otherwise advisory). A structural
		// twin with no payload keeps the advisory, type-driven wording (e.g. DataTable → List).
		if (hasPrebuiltPayload) {
			string what = rule.CarryProperties is { Count: > 0 } ? $" ({string.Join(", ", rule.CarryProperties)})" : "";
			return $"{basis} — template-provided element — merge the prebuilt mobileValues{what} onto " +
				$"'{rule.Mobile}' by name (do not insert a duplicate)";
		}
		string detail = string.IsNullOrEmpty(type)
			? $"template-provided element — configure '{rule.Mobile}' by merge-by-name (do not insert a duplicate)"
			: $"template-provided element — configure '{rule.Mobile}' by merge-by-name per componentSuggestions[\"{type}\"] (do not insert a duplicate)";
		return $"{basis} — {detail}";
	}

	/// <summary>
	/// Builds the merge payload for a component twin. An explicit
	/// <see cref="ComponentMappingRule.CarryProperties"/> whitelist carries just those keys
	/// (<see cref="BuildCarriedTwinValues"/>); otherwise, when the twin is the SAME component on both sides
	/// (<paramref name="webType"/> survives on mobile as <paramref name="twinMobileType"/>, e.g.
	/// crt.FileList → crt.FileList), the page's DELTA over the web-template baseline is carried
	/// (<see cref="BuildDeltaTwinMergeValues"/>) — a name twin of one component is just the same element renamed
	/// between the web and mobile templates. A twin whose web type has no mobile equivalent (a structural
	/// conversion, e.g. crt.DataGrid → crt.List) gets no payload and stays advisory.
	/// </summary>
	private static JsonNode BuildTwinMergeValues(ElementMapContext ctx, JObject node, ComponentMappingRule rule, string twinMobileType, string webType) {
		if (rule.CarryProperties is { Count: > 0 }) {
			return BuildCarriedTwinValues(ctx, node, rule, twinMobileType);
		}
		bool sameComponent = !string.IsNullOrEmpty(webType)
			&& string.Equals(twinMobileType, webType, StringComparison.OrdinalIgnoreCase);
		return sameComponent ? BuildDeltaTwinMergeValues(ctx, node, node["name"]?.ToString(), twinMobileType, rule.Mobile) : null;
	}

	/// <summary>
	/// Merge payload for a SAME-component twin (an explicit name-mapped one, e.g. AttachmentList →
	/// AttachmentFileList, or an automatic same-name one, e.g. Feed → Feed): every source property the page
	/// CHANGED from the web-template baseline — added, or set to a value different from the baseline — copied
	/// onto the template-provided element, minus element identity (<c>name</c>/<c>type</c>), the value binding
	/// (<c>control</c>/<c>value</c>) and placement (<c>layoutConfig</c> — the mobile template positions the
	/// element it owns; no merge pass normalizes placement), shape-coerced to the mobile registry contract. A
	/// property still equal to the web baseline is OMITTED so it cannot override the mobile template's own
	/// default (an unchanged attachments <c>recordColumnName</c> leaves the mobile default <c>RecordId</c>). A
	/// page-CHANGED event binding IS carried (converted + recorded via <see cref="ProcessOneEventBinding"/>), so
	/// a rebound handler is not lost; an unchanged binding is the template element's own and is left alone. The
	/// <c>caption</c> is carried ALWAYS (not delta-gated) so the page's label overrides the template element's and
	/// its resource is added to the mobile schema — a Freedom UI rename keeps the same token and only changes the
	/// value, which a delta comparison would miss. Emits NO <c>type</c> (a merge targets an element the template
	/// already owns). Null when the page changed nothing,
	/// AND when no baseline node is known for <paramref name="webName"/> — without the baseline the page's change
	/// cannot be told from the template's own value, so the twin degrades to an advisory merge (no prebuilt
	/// values) rather than pasting the whole web node (including web-only values) onto the mobile element.
	/// </summary>
	private static JsonNode BuildDeltaTwinMergeValues(ElementMapContext ctx, JObject node, string webName, string mobileType, string mobileName) {
		// No baseline node for this element -> we cannot compute a delta. Degrade to an advisory merge (null)
		// instead of carrying the whole web node; the caller reports it as a merge configured per componentSuggestions.
		if (string.IsNullOrEmpty(webName) || !ctx.WebBaselineNodes.TryGetValue(webName, out JObject baseline) || baseline is null) {
			return null;
		}
		var values = new JObject();
		foreach (JProperty prop in node.Properties()) {
			// `items` as an ARRAY is the child view-element collection (structural) — never a value.
			if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value is JArray) {
				continue;
			}
			// A page-CHANGED event binding is part of the page's delta — convert + record it (the only place an
			// interaction enters a twin merge). An unchanged binding is inherited and left to the template element.
			if (IsEventBinding(prop.Value)) {
				if (!JToken.DeepEquals(baseline[prop.Name], prop.Value)) {
					ProcessOneEventBinding(ctx, mobileName, prop.Name, (JObject)prop.Value, values);
				}
				continue;
			}
			// caption: the page's label OVERRIDES the template element's, but ONLY when the page actually RENAMED
			// it and the twin can carry that override. A Freedom UI rename keeps the SAME #ResourceString token and
			// changes only the resolved VALUE, so compare the RESOLVED text (page strings vs the web template's),
			// not the token — a "carry always" would push the inherited template caption onto every untouched twin.
			// And SKIP an automatic same-name twin: same name => same resource key, which the mobile template owns,
			// so update-page would never overwrite it (an inert instruction). A name-mapped twin keeps the page's
			// own key, which the mobile element does not own, so the override lands and CollectResourceStrings
			// registers the page's text into the mobile schema.
			if (string.Equals(prop.Name, "caption", StringComparison.OrdinalIgnoreCase)) {
				bool sameNameTwin = string.Equals(webName, mobileName, StringComparison.OrdinalIgnoreCase);
				if (!sameNameTwin && CaptionValueChanged(ctx, prop.Value, baseline["caption"])) {
					values[prop.Name] = CoerceToDeclaredShape(ctx, mobileType, prop.Name, prop.Value.DeepClone());
				}
				continue;
			}
			// Element identity, the value binding, and placement (layoutConfig — owned by the template) are excluded.
			if (ExcludedSourceProps.Contains(prop.Name) || TwinMergeExcludedProps.Contains(prop.Name)) {
				continue;
			}
			// Carry ONLY what the page changed from the web-template baseline. A value still equal to the baseline
			// is left out so the mobile template's own default stands.
			if (baseline[prop.Name] is { } baseValue && JToken.DeepEquals(baseValue, prop.Value)) {
				continue;
			}
			values[prop.Name] = CoerceToDeclaredShape(ctx, mobileType, prop.Name, prop.Value.DeepClone());
		}
		if (values.Count == 0) {
			return null;
		}
		try {
			return JsonNode.Parse(values.ToString(Newtonsoft.Json.Formatting.None));
		} catch (System.Text.Json.JsonException) {
			return null;
		}
	}

	/// <summary>
	/// Properties EXCLUDED from a same-component twin merge on top of <see cref="ExcludedSourceProps"/>, because
	/// they belong to the template-provided element the merge targets, not to the page's data delta.
	/// <c>layoutConfig</c> is the element's placement, which the MOBILE template owns; carrying the page's web grid
	/// coordinates would override it, and no merge pass normalizes it (adaptive / single-column / property-override
	/// all run on inserts only). NOTE: <c>caption</c> is deliberately NOT here — the page's label overrides the
	/// template's and is carried always (see <see cref="BuildDeltaTwinMergeValues"/>).
	/// </summary>
	private static readonly HashSet<string> TwinMergeExcludedProps = new(StringComparer.OrdinalIgnoreCase) {
		"layoutConfig"
	};

	/// <summary>
	/// Reason line for an AUTOMATIC same-component twin (<see cref="ElementMapContext.MobileTypesByName"/>): the
	/// mobile template provides an element with the same name and type. When the page CHANGED it, the caller
	/// pastes the prebuilt mobileValues onto it by name; when it is UNCHANGED (<paramref name="hasPayload"/> is
	/// false) the entry is advisory — the mobile template already provides the element, nothing to merge, and it
	/// is emitted only so the element is a valid business-rule target. No <c>components</c> rule is involved.
	/// </summary>
	private static string AutoComponentTwinReason(string name, string type, bool hasPayload) =>
		hasPayload
			? $"web '{name}' ({type}) is provided by the mobile template under the same name — merge the prebuilt " +
				$"mobileValues onto '{name}' by name (do not insert a duplicate)"
			: $"web '{name}' ({type}) is provided by the mobile template under the same name and is unchanged from " +
				$"the web template — nothing to merge; the mobile template already provides it (do not insert a duplicate)";

	/// <summary>
	/// Builds the deterministic merge <c>values</c> for a component twin whose rule declares
	/// <see cref="ComponentMappingRule.CarryProperties"/>: each listed property PRESENT on the web node is
	/// copied verbatim (shape-coerced to the mobile registry contract when a mobile type is known), producing
	/// the minimal merge payload the caller pastes onto the mobile element. Returns null when the rule carries
	/// no properties or none are present on the node — the twin then stays an advisory merge (no prebuilt
	/// values). Event bindings are never carried here — a twin's requests are handled by the normal event
	/// pipeline; carryProperties is intended for plain data bindings (e.g. sourceSchemaName/rootSchemaName).
	/// </summary>
	private static JsonNode BuildCarriedTwinValues(ElementMapContext ctx, JObject node, ComponentMappingRule rule, string mobileType) {
		if (rule?.CarryProperties is not { Count: > 0 }) {
			return null;
		}
		var values = new JObject();
		foreach (string propName in rule.CarryProperties) {
			if (string.IsNullOrWhiteSpace(propName) || node[propName] is not { } propValue) {
				continue;
			}
			JToken cloned = propValue.DeepClone();
			values[propName] = string.IsNullOrEmpty(mobileType)
				? cloned
				: CoerceToDeclaredShape(ctx, mobileType, propName, cloned);
		}
		if (values.Count == 0) {
			return null;
		}
		try {
			return JsonNode.Parse(values.ToString(Newtonsoft.Json.Formatting.None));
		} catch (System.Text.Json.JsonException) {
			return null;
		}
	}

	/// <summary>Extracts <c>$Token</c> binding references from a node's own properties (excluding child items).</summary>
	private static IEnumerable<string> ExtractDollarRefs(JObject node) {
		var clone = (JObject)node.DeepClone();
		clone.Remove("items");
		string json = clone.ToString(Newtonsoft.Json.Formatting.None);
		foreach (Match match in Regex.Matches(json, @"\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.None, RegexTimeout)) {
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
	/// True when the page RENAMED a same-component twin's caption — the page's resolved label text differs from
	/// the web template's for the SAME element. A Freedom UI rename keeps the same <c>#ResourceString</c> token and
	/// changes only the resolved VALUE, so a token comparison misses it and a "carry always" would push the
	/// inherited template caption onto every untouched twin. A caption the template did NOT have, or a changed
	/// TOKEN, is a change; for an identical token the resolved text is compared (page strings vs the web template's
	/// via <see cref="ElementMapContext.WebBaselineResources"/>) — treated as UNCHANGED when either side cannot be
	/// resolved, so the inherited caption is never over-carried.
	/// </summary>
	private static bool CaptionValueChanged(ElementMapContext ctx, JToken pageCaption, JToken baselineCaption) {
		if (pageCaption is null) {
			return false;
		}
		if (baselineCaption is null || !JToken.DeepEquals(baselineCaption, pageCaption)) {
			return true; // the page added a caption the template lacked, or bound it to a different token
		}
		// Same token — a value-only rename. Compare the resolved label on both sides; a template value we cannot
		// resolve counts as "not a rename" (never over-carry the inherited caption).
		string key = ResourceStringHelper.ExtractKeys(pageCaption.ToString()).FirstOrDefault();
		if (string.IsNullOrEmpty(key)) {
			return false; // identical literal caption — unchanged
		}
		string pageText = ResolveResourceString(ctx.Resources, key);
		string baseText = ResolveResourceString(ctx.WebBaselineResources, key);
		return pageText is not null && baseText is not null && !string.Equals(pageText, baseText, StringComparison.Ordinal);
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
	/// (<c>name</c>/<c>type</c>) and the value binding (<c>control</c>/<c>value</c>) — the binding is a
	/// type-specific rename (e.g. a mobile ComboBox must bind via <c>value</c>; <c>control</c> needs
	/// <c>items</c> or it crashes) and is left to the caller to add. <c>dataSourceName</c> is NOT excluded:
	/// a surviving element only ever references the primary data source (foreign-DS elements are dropped
	/// wholesale), so its <c>dataSourceName</c> is the valid primary DS and some components require it (e.g.
	/// <c>crt.Feed</c> needs <c>dataSourceName</c> + <c>entitySchemaName</c>). NOTE: <c>items</c> is NOT here
	/// — it is excluded only when it is an ARRAY of child view elements (structural, handled by the tree
	/// walk); as a STRING it is a real collection binding (e.g. <c>crt.CommunicationOptions</c>/<c>crt.List</c>
	/// <c>items: "$Attr"</c>) and is carried like any other property. Everything else is carried verbatim.
	/// </summary>
	private static readonly HashSet<string> ExcludedSourceProps = new(StringComparer.OrdinalIgnoreCase) {
		"name", "type", "control", "value"
	};

	/// <summary>
	/// The keys held back when a <c>preserveSourceProperties</c> template copies the whole source node: only the
	/// element's identity (<c>name</c>) and its resolved <c>type</c> (set from the template's <c>value.type</c>).
	/// Unlike <see cref="ExcludedSourceProps"/> this KEEPS the value binding (<c>control</c>/<c>value</c>), so a
	/// like-for-like field conversion (crt.Checkbox → crt.Toggle) carries its binding across instead of leaving it
	/// to the caller — which is the whole point of opting a template into the full copy.
	/// </summary>
	private static readonly HashSet<string> PreserveExcludedProps = new(StringComparer.OrdinalIgnoreCase) {
		"name", "type"
	};

	/// <summary>
	/// Builds the prebuilt, ready-to-paste mobile <c>values</c> for an inserted component. Copy rule: carry
	/// EVERY source property verbatim, dropping only the element identity/type and the value binding (see
	/// <see cref="ExcludedSourceProps"/>) and event bindings (converted separately). A property is NOT dropped
	/// because the mobile registry fails to declare it: the generated mobile registry is currently incomplete
	/// (missing <c>inputs</c> for several components, e.g. <c>crt.Feed</c>, <c>crt.EntityStageProgressBar</c> —
	/// ENG-91859), so pruning against it would discard required, genuinely-supported properties (e.g. Feed's
	/// <c>dataSourceName</c>/<c>entitySchemaName</c>). The registry is still consulted for SHAPE, not
	/// membership: <see cref="CoerceToDeclaredShape"/> reshapes a property the registry does describe
	/// (e.g. crt.List <c>itemLayout</c> array→object). <c>type</c> is set and, for field components,
	/// <c>label</c> is synthesized. Returns null for an unknown mobile type.
	/// </summary>
	private static JsonNode BuildMobileValues(ElementMapContext ctx, JObject node, string mobileName,
		string mobileType, CaptionResource caption, string parentName, string propertyName,
		IReadOnlyList<string> sourceAncestors) {
		if (string.IsNullOrEmpty(mobileType)) {
			return null;
		}
		var values = new JObject { ["type"] = mobileType };
		var roots = new TemplateRoots(
			new JObject { ["name"] = mobileName, ["parentName"] = parentName, ["propertyName"] = propertyName },
			node);
		// The conversion templates that govern this element (matched by filters + path scope + declared target type +
		// placement echo). Resolved up front because whether — and how much of — the source is carried depends on them.
		IReadOnlyList<ViewConfigTemplateRule> templates =
			MatchingConversionTemplates(ctx, node, mobileType, sourceAncestors);
		bool hasTemplate = templates.Count > 0;
		bool preserve = templates.Any(t => t.PreserveSourceProperties);
		// Carry the source properties when there is NO template (a registry-supported leaf or a type-equivalence
		// rule — the mobile shape is the source shape retyped, and the caller adds the value binding), OR when a
		// template opts into the full copy with preserveSourceProperties (keeping every source property except the
		// ones the template names — the value binding included). An AUTHORITATIVE template (present, no flag)
		// carries nothing here: its values are formed exclusively from what it declares. Either way layoutConfig is
		// copied just below, since it is layout placement rather than a component property.
		// The ancestor chain the node's OWN child arrays are walked under — the node's ancestors plus its name. This
		// is EXACTLY the chain RecurseChildArrays passes, so the carry-skip below and the walk agree byte-for-byte
		// on which slots are structural (walked out) versus carried.
		IReadOnlyList<string> childAncestors = Append(sourceAncestors, mobileName);
		if (!hasTemplate || preserve) {
			HashSet<string> excluded = preserve ? PreserveExcludedProps : ExcludedSourceProps;
			foreach (JProperty prop in node.Properties()) {
				// `items` as an ARRAY is ALWAYS the structural child-element slot (emitted by the tree walk), empty
				// or not; as a STRING it is a real collection binding (items: "$Attr") and is carried below.
				if (string.Equals(prop.Name, ItemsPropertyName, StringComparison.OrdinalIgnoreCase)
					&& prop.Value is JArray) {
					continue;
				}
				// Any OTHER slot the walk descended as a child-element collection (menuItems, tools, … — recognised
				// by shape AND resolving to mobile types, see IsChildElementArray) is structural too: skip exactly
				// those so the walk's entries are never duplicated as a value. A genuinely-empty data/config array
				// (options: [], columns: []) is NOT a child-element array — IsChildElementArray requires a non-empty
				// array of crt.*-typed, mobile-resolvable members — so it is carried verbatim, preserving both a
				// legitimate empty collection and its ability to overwrite a non-empty template default via the diff.
				if (IsChildElementArray(ctx, mobileType, prop.Name, prop.Value, childAncestors)) {
					continue;
				}
				if (excluded.Contains(prop.Name)) {
					continue;
				}
				// Event bindings (clicked / valueChange / updated …) carry a request — they are converted
				// deliberately by ProcessEventBindings below, so skip them here.
				if (IsEventBinding(prop.Value)) {
					continue;
				}
				// Carry the property verbatim. Do NOT prune against the mobile registry — while it is incomplete
				// (ENG-91859) a registry-absent property is treated as supported, not web-only. CoerceToDeclaredShape
				// only reshapes (object vs array) a property the registry DOES describe; otherwise it is a no-op.
				values[prop.Name] = CoerceToDeclaredShape(ctx, mobileType, prop.Name, prop.Value.DeepClone());
			}
		}
		// layoutConfig is layout PLACEMENT, not a component property — always copy it (even an authoritative
		// template governs the component's values, not where it sits). The adaptive pass may later fold it into a
		// per-breakpoint form; a template that explicitly names layoutConfig still wins via the overlay below.
		if (values["layoutConfig"] is null && node["layoutConfig"] is JObject sourceLayout) {
			values["layoutConfig"] = sourceLayout.DeepClone();
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
		// Apply the conversion templates that govern this element: authoritatively (over just its type +
		// layoutConfig) by default, or laid over the copied source when preserveSourceProperties. A template
		// declares the mobile structure the web node has no counterpart for — e.g. a grid → list row
		// (ENG-95046) — or simply the target type for a like-for-like field conversion.
		foreach (ViewConfigTemplateRule template in templates) {
			RenderOne(ctx, template, values, roots);
		}
		// A converted element still CARRIES its source properties, including ones the mobile type does not
		// declare. Removing them per-rule would be a second pruning mechanism beside the registry one, and the
		// registry is the right owner once ENG-91859 makes it complete.
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
		} catch (System.Text.Json.JsonException) {
			return null;
		}
	}


	/// <summary>
	/// A <c>{{ … }}</c> reference. The captured path is whatever sits between the braces, because it is JSON
	/// PATH — indexes and slices (<c>columns[0].code</c>, <c>columns[1:]</c>) are part of it. An earlier version
	/// spelled out an identifier grammar, which silently failed to MATCH a bracketed path at all and shipped the
	/// literal <c>{{ … }}</c> into the page as a value. Validating the path here would duplicate the JSON
	/// library's own parser; an unresolvable one already drops its key.
	/// </summary>
	private static readonly Regex TemplateTokenPattern =
		new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled, RegexTimeout);

	/// <summary>
	/// The conversion templates that govern this element: from every <c>components</c> entry whose <c>filters</c>
	/// (and <c>path</c> scope) match the node, the <see cref="ViewConfigTemplateRule"/>s whose declared
	/// <c>value.type</c> equals the resolved mobile type.
	/// </summary>
	/// <remarks>
	/// Because templates have priority in leaf resolution (<see cref="ResolveTemplateTargetType"/> runs before
	/// the registry check), the resolved type for a matched component IS the template's <c>value.type</c>, so the
	/// type gate passes; the filters narrow which source elements an entry applies to, they do not authorize. A
	/// template declares the mobile structure the web node has no counterpart for — e.g. a grid → list
	/// <c>crt.ListItem</c> under <c>itemLayout</c> — and, for a field conversion, the full target shape. Placement
	/// no longer gates admission: a template may DRIVE placement by declaring a <c>parentName</c>/<c>propertyName</c>
	/// that names a different target (<see cref="ResolveTemplatePlacement"/>), which retargets the element rather
	/// than being refused; the value is applied regardless.
	/// </remarks>
	private static IReadOnlyList<ViewConfigTemplateRule> MatchingConversionTemplates(
		ElementMapContext ctx, JObject node, string mobileType, IReadOnlyList<string> sourceAncestors) {
		if (ctx.Rules?.Components is not { Count: > 0 } components) {
			return [];
		}
		var matches = new List<ViewConfigTemplateRule>();
		foreach (ComponentEquivalenceRule entry in components) {
			if (!RuleAppliesTo(entry, node, sourceAncestors)) {
				continue;
			}
			foreach (ViewConfigTemplateRule template in entry.ViewConfigTemplates) {
				// Placement (parentName/propertyName) no longer gates admission: a template may DRIVE placement
				// (retarget the element into a declared container/property — see ResolveTemplatePlacement), so its
				// value is applied whether it echoes the walked position or names a different target. Only the
				// declared target TYPE gates, so a template for another mobile type never applies here.
				if (DeclaresTargetType(template.Value, mobileType)) {
					matches.Add(template);
				}
			}
		}
		return matches;
	}

	private static void RenderOne(ElementMapContext ctx, ViewConfigTemplateRule template,
		JObject values, TemplateRoots roots) {
		// The template resolves source.* straight off the web node (roots.Source): its positional paths —
		// source.columns[0], source.columns[1:] — address the node's own arrays directly. There is no
		// projection step and nothing to validate: a column code IS a real attribute name in the source
		// config, so an entry never needs dropping, reordering, or repairing before the template sees it.
		if (RenderTemplateToken(JToken.Parse(template.Value.Value.GetRawText()), roots) is not JObject rendered) {
			return;
		}
		// Real authored content wins over anything synthesized. Only the STRUCTURE a template introduces counts —
		// a rendered value that is an object declaring its own type, i.e. the thing the web node had no
		// counterpart for. Comparing every key instead would be wrong: the generic copy already carried `items`
		// and the rest, so any template naming them would look "authored" and never apply at all.
		if (rendered.Properties().Any(p => p.Value is JObject nested
			&& nested["type"] is not null && values[p.Name] is not null)) {
			return;
		}
		// The element's own resolved type, which the values already carry as their first key — the shape guard
		// reshapes against the component the value is landing on, not against the one it came from.
		OverlayRenderedValues(ctx, values, values["type"]?.ToString(), rendered);
		ValidateIntroducedStructure(ctx, values, rendered);
	}

	/// <summary>
	/// Validates the structure a template introduced against the mobile registry. Located by the type the
	/// template DECLARES for it, so nothing has to say where the structure lands.
	/// </summary>
	/// <remarks>
	/// It catches the one failure nothing else sees — a scalar the registry declares emitted in the wrong
	/// shape, e.g. a row title written as the <c>{ "value": … }</c> BODY form, which RENDERS and leaves only
	/// the Title column empty (ENG-95046).
	/// </remarks>
	private static void ValidateIntroducedStructure(ElementMapContext ctx, JObject values, JObject rendered) {
		foreach (JProperty prop in rendered.Properties()) {
			if (prop.Value is not JObject introduced || introduced["type"]?.ToString() is not { Length: > 0 } type) {
				continue;
			}
			if (values[prop.Name] is JObject shipped) {
				DropValuesContradictingDeclaredScalars(ctx, type, shipped);
			}
			return;
		}
	}

	/// <summary>
	/// True when the template's own <c>value.type</c> is the mobile type the element resolved to. This is what
	/// gates a template: the mapping decides WHICH mobile type an element becomes, the template decides what
	/// that type's values look like, and neither needs a second declaration tying them together.
	/// </summary>
	private static bool DeclaresTargetType(JsonElement? declared, string mobileType) =>
		declared is { } value && !string.IsNullOrWhiteSpace(mobileType)
		&& value.ValueKind == JsonValueKind.Object
		&& value.TryGetProperty("type", out JsonElement type)
		&& type.ValueKind == JsonValueKind.String
		&& string.Equals(type.GetString(), mobileType, StringComparison.OrdinalIgnoreCase);

	/// <summary>The roots a view-config template resolves its paths against, for ONE converted element.</summary>
	/// <param name="Diff">
	/// The operation being produced — <c>name</c>, <c>parentName</c>, <c>propertyName</c>. <c>name</c> is read-only
	/// (a template may echo it, never rename the element). <c>parentName</c>/<c>propertyName</c> may be ECHOED to
	/// keep the walked placement or rendered to a DIFFERENT value to RETARGET the element (see
	/// <see cref="ResolveTemplatePlacement"/>).
	/// </param>
	/// <param name="Source">The WEB node being converted; <c>source.*</c> paths read off it directly.</param>
	private sealed record TemplateRoots(JObject Diff, JObject Source);

	private const string DiffRoot = "diff.";
	private const string SourceRoot = "source.";

	/// <summary>
	/// Resolves one template path. A path prefixed <c>diff.</c> or <c>source.</c> reads the matching root;
	/// anything else is read against <paramref name="item"/>, the member a <c>$each</c> is currently on. Both
	/// roots go through the JSON library's own path syntax, so indexes and slices work without a template
	/// engine. An unresolvable path yields nothing rather than its own text, so a typo drops a key instead of
	/// shipping <c>{{ … }}</c> into the page as a value.
	/// </summary>
	private static JToken ResolveTemplatePath(string path, TemplateRoots roots, JToken item) {
		try {
			if (path.StartsWith(DiffRoot, StringComparison.Ordinal)) {
				return roots.Diff.SelectToken(path[DiffRoot.Length..]);
			}
			if (path.StartsWith(SourceRoot, StringComparison.Ordinal)) {
				return roots.Source.SelectToken(path[SourceRoot.Length..]);
			}
			return item?.SelectToken(path);
		}
		catch (JsonException) {
			// A malformed path is a defect in the rules DATA, which is resolved at runtime and may come from
			// outside this binary. Dropping the key matches every other unresolvable path rather than failing a
			// whole page's conversion over one property.
			return null;
		}
	}

	/// <summary>
	/// Resolves the COLLECTION a <c>$each</c> repeats over. Separate from
	/// <see cref="ResolveTemplatePath"/> because a slice yields many tokens where an index yields one, and the
	/// library exposes those as different calls — asking for a single token would silently return the first
	/// entry of a slice and repeat once instead of once per member.
	/// </summary>
	private static IReadOnlyList<JToken> ResolveTemplateCollection(string path, TemplateRoots roots, JToken item) {
		try {
			if (path.StartsWith(DiffRoot, StringComparison.Ordinal)) {
				return roots.Diff.SelectTokens(path[DiffRoot.Length..]).ToList();
			}
			if (path.StartsWith(SourceRoot, StringComparison.Ordinal)) {
				return roots.Source.SelectTokens(path[SourceRoot.Length..]).ToList();
			}
			return item?.SelectTokens(path).ToList() ?? [];
		}
		catch (JsonException) {
			return [];
		}
	}

	/// <summary>
	/// The placement a matching conversion template DRIVES for a node, or null to keep the walked position. A
	/// template may declare a <c>parentName</c>/<c>propertyName</c>: when it renders to the value the converter
	/// already computed (an ECHO, e.g. <c>"{{ diff.parentName }}"</c>) it changes nothing; when it renders to a
	/// DIFFERENT value it RETARGETS the element — the converted element is emitted as an insert into that declared
	/// container/property (appended, no index) instead of where the walk found it. This is how a source element is
	/// regrouped elsewhere on mobile (e.g. a header button → <c>FloatingActionButton.menuItems</c>). A template that
	/// declares neither field, or only echoes, returns null. When several matching templates disagree, the first
	/// declaring a retarget wins.
	/// </summary>
	private static (string Parent, string Property)? ResolveTemplatePlacement(
		ElementMapContext ctx, JObject node, string mobileType, string mobileName,
		string computedParent, string computedProperty, IReadOnlyList<string> sourceAncestors) {
		var roots = new TemplateRoots(
			new JObject { ["name"] = mobileName, ["parentName"] = computedParent, ["propertyName"] = computedProperty },
			node);
		foreach (ViewConfigTemplateRule template in MatchingConversionTemplates(ctx, node, mobileType, sourceAncestors)) {
			string parent = RenderPlacementField(template.ParentName, roots) ?? computedParent;
			string property = RenderPlacementField(template.PropertyName, roots) ?? computedProperty;
			if (!string.Equals(parent, computedParent, StringComparison.Ordinal)
				|| !string.Equals(property, computedProperty, StringComparison.Ordinal)) {
				return (parent, property);
			}
		}
		return null;
	}

	private static string RenderPlacementField(string declared, TemplateRoots roots) =>
		string.IsNullOrWhiteSpace(declared) ? null : RenderTemplateString(declared, roots, item: null)?.ToString();

	/// <summary>
	/// Lays the rendered structure over the values: a key the template names WINS, a key it does not name
	/// survives. The element's identity and its value binding are the exception — the copy rule refuses to carry
	/// them on purpose, so filling that gap from a template would let the rules file rename an element or prebuild
	/// the type-specific binding (which a like-for-like conversion carries via preserveSourceProperties instead).
	/// </summary>
	private static void OverlayRenderedValues(ElementMapContext ctx, JObject target, string mobileType,
		JObject rendered) {
		foreach (JProperty prop in rendered.Properties()) {
			if (ExcludedSourceProps.Contains(prop.Name)) {
				continue;
			}
			// The same two guards the copy rule applies, for the same reasons. `items` as an ARRAY is the child
			// view-element collection — structural, emitted by the tree walk — so writing it here would nest a
			// whole child tree inside its parent's values; as a STRING it is a real collection binding and is
			// written like anything else. And the value is reshaped to what the registry declares, so a template
			// that writes an object where the mobile component wants an array does not ship the wrong container.
			if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value is JArray) {
				continue;
			}
			target[prop.Name] = CoerceToDeclaredShape(ctx, mobileType, prop.Name, prop.Value.DeepClone());
		}
	}

	/// <summary>True when any filter matches the node, or the mapping declares none (match everything).</summary>
	private static bool MatchesAnyFilter(IReadOnlyList<ElementFilterRule> filters, JObject node) {
		if (filters is not { Count: > 0 }) {
			return true;
		}
		string type = node["type"]?.ToString();
		return filters.Any(f => !string.IsNullOrWhiteSpace(f?.Type)
			&& string.Equals(f.Type, type, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// True when a rule's <c>path</c> scope is satisfied for a node. Empty (or null) path = no scoping (matches
	/// anywhere), mirroring <see cref="MatchesAnyFilter"/>. Otherwise the path names must appear, by name and IN
	/// ORDER, as a SUBSEQUENCE of the node's source ancestors (outer→inner) — at any depth, so intermediate
	/// containers or child-arrays between two path elements are allowed. Single element <c>["MainHeader"]</c> means
	/// "the node has an ancestor named MainHeader anywhere above it".
	/// </summary>
	private static bool MatchesPath(IReadOnlyList<string> rulePath, IReadOnlyList<string> ancestors) {
		if (rulePath is not { Count: > 0 }) {
			return true;
		}
		int matched = 0;
		foreach (string ancestor in ancestors ?? []) {
			if (matched < rulePath.Count
				&& string.Equals(ancestor, rulePath[matched], StringComparison.OrdinalIgnoreCase)) {
				matched++;
			}
		}
		return matched == rulePath.Count;
	}

	/// <summary>
	/// How deep a template may nest before rendering gives up on the branch. Well past anything a real skeleton
	/// needs — the shipped one nests three.
	/// </summary>
	/// <remarks>
	/// The rules file is resolved at RUNTIME (<see cref="WebToMobilePageConversionRulesCatalog"/> fetches it and
	/// falls back to the bundled copy), so a template is input from outside this binary. The JSON reader already
	/// refuses to parse past its own depth limit and the catalog treats that as an unusable payload, so this is
	/// not what stands between the process and a stack overflow — it is defence in depth for the recursion, and
	/// it bounds DEPTH only. It does NOT bound the cost of nested <c>$each</c> repeats, which multiply: three
	/// repeats over fifty entries is depth four and 125 000 nodes, well inside this budget. A node budget would
	/// answer that, and there is none today.
	/// </remarks>
	private const int MaxTemplateDepth = 32;

	/// <summary>
	/// Renders one template node. A string interpolates its <c>{{ path }}</c>s — a string that is EXACTLY one
	/// path yields that path's own value, so a slot can carry a non-string; an object carrying <c>$each</c>
	/// repeats its <c>as</c> body once per member of the resolved collection; any other object and array
	/// recurse. A path resolving to nothing drops its key.
	/// </summary>
	/// <param name="item">
	/// The current <c>$each</c> member, or null outside one. ONE method handles both cases on purpose: while
	/// there were two, only the outer one knew about <c>$each</c>, so a nested repeat fell through to the plain
	/// object branch and wrote its own <c>$each</c>/<c>as</c> keys into the page as data.
	/// </param>
	private static JToken RenderTemplateToken(JToken template, TemplateRoots roots, JToken item = null,
		int depth = 0) {
		// Degrades the same way an unresolvable path does — the branch yields nothing and its key is dropped —
		// rather than throwing, because the surrounding contract is that bad rules data costs a property, never
		// the conversion and never the process.
		if (depth > MaxTemplateDepth) {
			return null;
		}
		switch (template) {
			case JValue { Type: JTokenType.String } value:
				return RenderTemplateString(value.ToString(), roots, item);
			case JObject obj when obj["$each"] is { } collectionPath:
				return RenderEach(collectionPath.ToString(), obj["as"], roots, item, depth);
			case JObject obj: {
				var rendered = new JObject();
				foreach (JProperty prop in obj.Properties()) {
					if (RenderTemplateToken(prop.Value, roots, item, depth + 1) is { Type: not JTokenType.Null } value) {
						rendered[prop.Name] = value;
					}
				}
				return rendered;
			}
			case JArray arr:
				return new JArray(arr
					.Select(element => RenderTemplateToken(element, roots, item, depth + 1))
					.Where(t => t is not null));
			default:
				return template;
		}
	}

	/// <summary>
	/// Repeats <paramref name="body"/> once per member of the resolved collection, rendering the body against
	/// the member. An empty or unresolvable collection yields an empty array rather than nothing, so a grid with
	/// a single column still ships the collection its row declares instead of omitting the key.
	/// </summary>
	private static JToken RenderEach(string collectionPath, JToken body, TemplateRoots roots, JToken outerItem,
		int depth) {
		if (body is null) {
			return new JArray();
		}
		return new JArray(ResolveTemplateCollection(collectionPath, roots, outerItem)
			.Select(member => RenderTemplateToken(body, roots, member, depth + 1))
			.Where(t => t is not null));
	}

	/// <summary>
	/// Interpolates a template string. A string that is EXACTLY one path returns that path's raw value (so a
	/// reference may be an object, an array or a number); otherwise every path is substituted textually and the
	/// result is a string — which is what makes <c>"${{ code }}"</c> work as a literal <c>$</c> followed by the
	/// binding, and <c>"{{ diff.name }}_ListItem"</c> work as a name with a suffix. A single path resolving to
	/// nothing returns null so its key is dropped.
	/// </summary>
	private static JToken RenderTemplateString(string template, TemplateRoots roots, JToken item) {
		Match single = TemplateTokenPattern.Match(template);
		if (single.Success && single.Length == template.Trim().Length) {
			return ResolveTemplatePath(single.Groups[1].Value, roots, item);
		}
		// A string that references something absent yields NOTHING, so its key is dropped — the same rule as a
		// lone path, extended to interpolation. Substituting the empty string instead would keep the key and
		// ship the literal part alone: the mandated skeleton writes the binding prefix outside the braces
		// ("${{ … }}"), so a row with no acceptable lead value would carry title "$" — a present property of the
		// right type and no meaning, which is worse than an absent one and invisible to a shape check.
		bool resolvedEverything = true;
		string rendered = TemplateTokenPattern.Replace(template, m => {
			JToken value = ResolveTemplatePath(m.Groups[1].Value, roots, item);
			if (value is null or { Type: JTokenType.Null }) {
				resolvedEverything = false;
				return string.Empty;
			}
			return value.ToString();
		});
		// The cast is load-bearing: without it the conditional types as string, and the implicit string->JToken
		// conversion turns a null string into a JSON NULL — a PRESENT key of the wrong shape rather than an
		// absent one. The two JSON stacks then disagree about whether the property exists.
		return resolvedEverything ? (JToken)rendered : null;
	}

	/// <summary>
	/// Removes any property of a SYNTHESIZED row whose value contradicts a scalar the mobile registry declares
	/// for <paramref name="mobileType"/> — e.g. a <c>crt.ListItem.title</c> emitted as
	/// <c>{ "value": … }</c> (the BODY entry shape) where the registry declares a plain string.
	/// </summary>
	/// <remarks>
	/// This is the one failure mode nothing else catches. An object-wrapped title RENDERS: the list shows its
	/// body rows and only the Title column comes up empty, so it reads as a data problem rather than a shape
	/// one, and <c>validate-page</c>'s client-engine simulation — which does catch the neighbouring mistake of
	/// addressing <c>itemLayout</c> as a child slot, because that breaks the build — passes it (ENG-95046).
	/// Dropping rather than throwing is deliberate: the guide is a report, not a build, and killing a whole
	/// page's conversion over one slot would cost the caller far more than an absent title, which
	/// <c>validate-page</c> then flags for the caller to set in the designer.
	/// Verifies against the registry rather than a hardcoded name, so it keeps holding if the producer
	/// changes the declared shape.
	/// </remarks>
	private static void DropValuesContradictingDeclaredScalars(ElementMapContext ctx, string mobileType, JObject row) {
		if (string.IsNullOrWhiteSpace(mobileType) || ctx?.MobileByType is null
			|| !ctx.MobileByType.TryGetValue(mobileType, out ComponentRegistryEntry entry) || entry is null) {
			return;
		}
		foreach (JProperty prop in row.Properties().ToList()) {
			if (DeclaresScalarString(entry, prop.Name) && prop.Value is not JValue { Type: JTokenType.String }) {
				row.Remove(prop.Name);
			}
		}
	}

	/// <summary>
	/// True when the registry entry declares <paramref name="propName"/> as a plain <c>string</c>. Deliberately
	/// separate from <see cref="ResolveExpectedShape"/>, which answers a different question — which CONTAINER
	/// (object vs array) a value belongs in — and must keep returning null for scalars so
	/// <see cref="CoerceToDeclaredShape"/> leaves them alone.
	/// </summary>
	private static bool DeclaresScalarString(ComponentRegistryEntry entry, string propName) {
		if (entry.Inputs is not null) {
			foreach (KeyValuePair<string, JsonElement> input in entry.Inputs) {
				if (string.Equals(input.Key, propName, StringComparison.OrdinalIgnoreCase)) {
					return input.Value.ValueKind == JsonValueKind.Object
						&& input.Value.TryGetProperty("type", out JsonElement t)
						&& t.ValueKind == JsonValueKind.String
						&& string.Equals(t.GetString(), "string", StringComparison.OrdinalIgnoreCase);
				}
			}
		}
		if (entry.Properties is not null) {
			foreach (KeyValuePair<string, ComponentPropertyDefinition> prop in entry.Properties) {
				if (string.Equals(prop.Key, propName, StringComparison.OrdinalIgnoreCase)) {
					return string.Equals(prop.Value.Type, "string", StringComparison.OrdinalIgnoreCase);
				}
			}
		}
		return false;
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
	/// OFFLINE-FALLBACK set of requests the Creatio Mobile app supports (from the monorepo
	/// <c>@CrtInterfaceDesignerMobileRequest</c> decorators). The AUTHORITATIVE source is the versioned rules
	/// file's <c>requests</c> section (<see cref="ElementMapContext.RequestMap"/>): an entry with a mobile
	/// target is supported, an entry that clears the target is unsupported. This constant is consulted only for
	/// a request the versioned file does not cover, so a CDN rules update can enable/disable a request without a
	/// clio release. TODO(ENG-93027): fold this constant into the versioned file —
	/// https://creatio.atlassian.net/browse/ENG-93027.
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
	/// The first event-binding request on the node the Creatio Mobile app does not support (or null when every
	/// binding is supported). Support is decided by the versioned rules file first
	/// (<see cref="ElementMapContext.RequestMap"/>): an entry with a mobile target is supported (direct/rename),
	/// an entry that clears the target is explicitly unsupported. A request the versioned file does not cover
	/// falls back to the bundled <see cref="MobileSupportedRequests"/> constant. Keeping one authoritative
	/// source (the versioned file) prevents dropping a common request the file already maps.
	/// </summary>
	private static string UnsupportedRequestOf(ElementMapContext ctx, JObject node) {
		foreach (JProperty prop in node.Properties()) {
			if (!IsEventBinding(prop.Value)) {
				continue;
			}
			string webRequest = ((JObject)prop.Value)["request"].ToString();
			if (ctx.RequestMap.TryGetValue(webRequest, out RequestMappingRule rule)) {
				// Authoritative: a non-empty mobile target is supported (effective = rule.Mobile); an entry that
				// explicitly clears the mobile target marks the request unsupported.
				if (string.IsNullOrWhiteSpace(rule.Mobile)) {
					return webRequest;
				}
				continue;
			}
			// Not covered by the versioned file — fall back to the bundled offline set.
			if (!MobileSupportedRequests.Contains(webRequest)) {
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
			if (IsEventBinding(prop.Value)) {
				ProcessOneEventBinding(ctx, elementName, prop.Name, (JObject)prop.Value, values);
			}
		}
	}

	/// <summary>
	/// Converts ONE event-binding request and writes the outcome into <paramref name="values"/> + the
	/// requestConversions collectors — the per-binding core shared by the insert builder (which processes every
	/// binding on the node) and the same-component-twin delta (which processes only a binding the page CHANGED).
	/// </summary>
	private static void ProcessOneEventBinding(ElementMapContext ctx, string elementName, string binding, JObject source, JObject values) {
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
			return;
		}

		// Not in the map: unknown OOTB request or a custom usr.* — keep it but flag for review.
		values[binding] = (JObject)source.DeepClone();
		ctx.FlaggedRequests.Add(new FlaggedRequest {
			ElementName = elementName, Binding = binding, Request = webRequest,
			Reason = "Request is not in the conversion map (custom or unknown) — verify it exists on mobile before relying on it."
		});
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

	/// <summary>
	/// Assembles the advisory request-conversion summary; null when the page references no requests.
	/// Reconciles with the empty-container removal pass first: a binding is recorded while the element map
	/// is built, so a container removed as empty AFTERWARDS would still be reported as converted/flagged —
	/// contradicting its own drop entry (the binding's payload was discarded with the entry's mobileValues).
	/// Such records are reclassified into <c>droppedRequests</c> with the removal named as the reason, so
	/// the report stays consistent and the discarded binding stays visible.
	/// </summary>
	private static RequestConversionInfo BuildRequestConversionInfo(
		List<ConvertedRequest> converted, List<DroppedRequest> dropped, List<FlaggedRequest> flagged,
		HashSet<string> emptyRemovedMobileNames) {
		const string emptyRemovedReason =
			"its container was removed as an empty container — the binding was discarded with it";
		for (int i = converted.Count - 1; i >= 0; i--) {
			if (emptyRemovedMobileNames.Contains(converted[i].ElementName)) {
				dropped.Add(new DroppedRequest {
					ElementName = converted[i].ElementName, Binding = converted[i].Binding,
					WebRequest = converted[i].WebRequest, Reason = emptyRemovedReason
				});
				converted.RemoveAt(i);
			}
		}
		for (int i = flagged.Count - 1; i >= 0; i--) {
			if (emptyRemovedMobileNames.Contains(flagged[i].ElementName)) {
				dropped.Add(new DroppedRequest {
					ElementName = flagged[i].ElementName, Binding = flagged[i].Binding,
					WebRequest = flagged[i].Request, Reason = emptyRemovedReason
				});
				flagged.RemoveAt(i);
			}
		}
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
	/// keyed by element name) and, for a grid container (a node carrying <c>columns</c>), its web column count
	/// (keyed by the WEB container name; <see cref="BuildAdaptiveLayout"/> translates it to the mobile parent
	/// name via the element map, since a merge twin / relocated wrapper may rename the container).
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
		// Grid-container column counts are captured under the WEB container name, but children carry the MOBILE
		// parent name in their element-map entries — a merge twin or relocated wrapper renames the container
		// (e.g. GeneralInfoTabContainer -> GeneralTabContainer, SideAreaProfileContainer -> AreaProfileContainer).
		// Translate each count to the container's mobile name via its element-map entry so the lookup below
		// matches renamed pairs; keep the web name as a fallback for containers that are not renamed.
		var colsByMobileParent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry e in elementMap) {
			if (e.WebName is { Length: > 0 } && gridContainerColumns.TryGetValue(e.WebName, out int cols)) {
				colsByMobileParent[string.IsNullOrEmpty(e.MobileName) ? e.WebName : e.MobileName] = cols;
			}
		}
		foreach (KeyValuePair<string, int> kv in gridContainerColumns) {
			colsByMobileParent.TryAdd(kv.Key, kv.Value);
		}

		// Children (any type) of a captured grid container, grouped by mobile parent in tree (= elementMap) order.
		var byContainer = new Dictionary<string, List<ElementMapEntry>>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		foreach (ElementMapEntry e in elementMap) {
			if (!string.Equals(e.Operation, "insert", StringComparison.Ordinal) ||
				string.IsNullOrEmpty(e.ParentName) || e.MobileValues is not JsonObject ||
				!colsByMobileParent.ContainsKey(e.ParentName)) {
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
			int webCols = colsByMobileParent[container];
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

	/// <summary>
	/// Deterministic empty-container removal: converts to a <c>drop</c> every converter-created
	/// container of a rules-listed type whose items receive NO surviving child, so an empty layout shell
	/// never reaches the mobile page. Runs to a fixed point, which IS the bottom-up cascade: a FlexContainer
	/// holding only an empty GridContainer follows it out on the next round, and a TabPanel whose every tab
	/// emptied drops too.
	/// <para>
	/// Emptiness is judged on the element map itself — a container is occupied when ANY surviving
	/// <c>insert</c> entry names it as <c>parentName</c>. That definition bakes in the agreed semantics with
	/// no special cases: a <c>visible: false</c> child is an insert and so COUNTS as content (hidden at
	/// runtime only — it must keep its designer home); a dropped or relocated-away child does not; a
	/// <c>relocate-children</c> routing hint is not an element and never occupies its target (only the
	/// children it re-homed do, via their own entries). A candidate whose <c>items</c> survived into
	/// mobileValues as a NON-array (a <c>"$Attr"</c> collection binding — see BuildMobileValues) is a
	/// repeater with data, not empty scaffolding, and is kept.
	/// </para>
	/// <para>
	/// Template protection is structural, not name-based: a template merge twin is <c>merge</c> (never
	/// <c>insert</c>) and carries no parentName, and the tab-area layers are synthesized AFTER this pass
	/// (only for tabs that survived it), so neither is ever a candidate. A container is judged on ALL its
	/// surviving children, in any slot: an ExpansionPanel whose <c>tools</c> buttons converted (structural
	/// child-array traversal) is occupied by them and kept, so it is removed only when nothing — items OR
	/// tools — survived. (This supersedes the earlier items-only decision of 2026-08-03, made when tools were
	/// discarded rather than converted.)
	/// </para>
	/// <para>
	/// Removal is IN PLACE (each removed entry is replaced by a drop at the same position, so the report
	/// keeps tree order). The removed elements' web names are returned so
	/// <see cref="BuildMobileViewModelConfig"/> can KEEP the attributes they referenced (that matching is
	/// web-name-keyed), and their MOBILE names go to <paramref name="removedMobileNames"/> so
	/// <see cref="BuildRequestConversionInfo"/> can reconcile the request summary (bindings are recorded
	/// under the element's mobile name). Positional-index compaction is the caller's job — it runs
	/// unconditionally after this pass because every drop source leaves the same index holes. With no
	/// <c>emptyContainerRemoval</c> rules section the pass is a no-op (switched by data, not code).
	/// </para>
	/// </summary>
	private static HashSet<string> RemoveEmptyContainers(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules,
		out HashSet<string> removedMobileNames) {
		var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		removedMobileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var removable = new HashSet<string>(
			(rules?.EmptyContainerRemoval?.RemovableTypes ?? []).Where(t => !string.IsNullOrWhiteSpace(t)),
			StringComparer.OrdinalIgnoreCase);
		if (removable.Count == 0) {
			return removed;
		}
		bool anyRemovedThisRound = true;
		while (anyRemovedThisRound) {
			anyRemovedThisRound = false;
			// Parents with at least one surviving insert child, recomputed per round. Within a round the set
			// over-approximates (a child removed mid-round still counts as an occupier), which only DEFERS its
			// parent to the next round — a container is removed strictly on round-start evidence, never early.
			var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ElementMapEntry entry in elementMap) {
				if (string.Equals(entry.Operation, "insert", StringComparison.Ordinal)
					&& entry.ParentName is { Length: > 0 }) {
					occupied.Add(entry.ParentName);
				}
			}
			for (int i = 0; i < elementMap.Count; i++) {
				ElementMapEntry entry = elementMap[i];
				if (!IsEmptyRemovalCandidate(entry, removable) || occupied.Contains(entry.MobileName)) {
					continue;
				}
				elementMap[i] = Drop(entry.WebName, entry.WebType, EmptyContainerDropReason);
				removed.Add(entry.WebName);
				removedMobileNames.Add(entry.MobileName);
				anyRemovedThisRound = true;
			}
		}
		return removed;
	}

	/// <summary>
	/// A removal candidate is a WEB-SOURCED insert (webName present — a synthesized layer has none and is
	/// out of scope by construction) of a rules-listed container type whose mobileValues carry no <c>items</c>
	/// collection binding (items-as-string marks a repeater with data; items-as-array is never carried).
	/// </summary>
	private static bool IsEmptyRemovalCandidate(ElementMapEntry entry, HashSet<string> removableTypes) =>
		string.Equals(entry.Operation, "insert", StringComparison.Ordinal)
		&& entry.WebName is { Length: > 0 }
		&& entry.MobileName is { Length: > 0 }
		&& entry.MobileType is { Length: > 0 }
		&& removableTypes.Contains(entry.MobileType)
		&& (entry.MobileValues is not JsonObject values || values["items"] is null);

	/// <summary>
	/// The drop reason for a removed empty container. A container's <c>tools</c>/<c>menuItems</c> buttons are now
	/// converted as their OWN child entries (structural child-array traversal), so a panel is empty here only when
	/// none of its children — items OR tools — survived; each discarded child already carries its own drop entry, so
	/// the loss is visible without naming it again on the parent.
	/// </summary>
	private const string EmptyContainerDropReason = "empty container — no mobile content survived conversion";

	/// <summary>
	/// Re-compacts positional insert indexes after the drop passes: <c>:top</c> siblings of an anchor are
	/// numbered 0..N-1 at walk time (see ResolvePositionalSiblings) BEFORE any drop decision, so a sibling
	/// dropped for ANY reason (unsupported type, foreign data source, unsupported button request, empty
	/// container) leaves a hole that would misplace the survivors. Surviving indexed inserts are renumbered
	/// per parent in their original relative order; appended (<c>index</c>-less) entries are untouched.
	/// Idempotent and a no-op when no indexed inserts exist, so the caller runs it unconditionally.
	/// </summary>
	private static void CompactPositionalIndexes(List<ElementMapEntry> elementMap) {
		IEnumerable<IGrouping<string, ElementMapEntry>> indexedByParent = elementMap
			.Where(e => string.Equals(e.Operation, "insert", StringComparison.Ordinal)
				&& e.Index is not null && e.ParentName is { Length: > 0 })
			.GroupBy(e => e.ParentName, StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, ElementMapEntry> group in indexedByParent) {
			int next = 0;
			foreach (ElementMapEntry entry in group.OrderBy(e => e.Index.Value)) {
				entry.Index = next++;
			}
		}
	}

	/// <summary>Mobile Tabs element name that converted web tabs are inserted under.</summary>
	private const string MobileTabsElementName = "Tabs";

	/// <summary>Mobile component type of a single tab.</summary>
	private const string MobileTabComponentType = "crt.TabContainer";

	/// <summary>
	/// 0-based index of the FIRST converted tab within the mobile Tabs items: 1 places it right after the
	/// template's general tab (position 0) and before the template's Feed/Attachments tabs, which shift
	/// right and stay last.
	/// </summary>
	private const int FirstConvertedTabIndex = 1;

	/// <summary>
	/// Assigns an explicit ordering index to every SURVIVING converted web tab inserted under the mobile
	/// Tabs element, so the template's Feed/Attachments tabs stay LAST. The mobile tabbed template ships
	/// its tabs as [general(0), Feed, Attachments]; an index-less insert appends AFTER them, which is how
	/// converted tabs used to land past Feed/Attachments (the "keep them last" requirement lived only as
	/// guidance prose, and the mechanical "no index — append" rule always won). Indexing survivors from
	/// <see cref="FirstConvertedTabIndex"/> up in element-map order (= the web tree order) inserts each tab
	/// right after the general tab and preserves the web page's own tab order; the template twins are
	/// merges, never move, and get pushed last by construction.
	/// <para>
	/// Pass order is load-bearing (enforced at the call site): AFTER <see cref="RemoveEmptyContainers"/>
	/// so a tab removed as empty is a drop by then and is never indexed (survivors stay contiguous), and
	/// AFTER <see cref="CompactPositionalIndexes"/> — that compaction rebases each parent's indexed group
	/// to 0, which over tab indexes would erase the first-tab offset and put the first converted tab BEFORE
	/// the general tab. The two never meet in one group anyway (positional inserts target the Tabs
	/// anchor's PARENT, e.g. MainContainer, never Tabs itself), but the order makes that a non-issue by
	/// construction. Synthesized tab-area layers are created later, INSIDE tabs, and are never matched.
	/// The pass is UNCONDITIONAL: correct tab order is a correctness invariant, not an opt-in, and the
	/// values it needs are constants of the mobile tabbed template (the Tabs element name, the tab
	/// component type, the general tab owning position 0) rather than variable data — a rules file could
	/// only ever restate them, and its absence would silently reorder tabs behind the guidance contract,
	/// which promises the caller the indexes are already there. On a non-tabbed page nothing inserts a tab
	/// under Tabs, so the loop matches nothing and the pass costs one map walk.
	/// </para>
	/// </summary>
	private static void AssignConvertedTabIndexes(List<ElementMapEntry> elementMap) {
		int next = FirstConvertedTabIndex;
		foreach (ElementMapEntry entry in elementMap) {
			if (string.Equals(entry.Operation, "insert", StringComparison.Ordinal)
				&& string.Equals(entry.ParentName, MobileTabsElementName, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.MobileType, MobileTabComponentType, StringComparison.OrdinalIgnoreCase)) {
				entry.Index = next++;
				entry.Reason = entry.Reason
					+ "; explicit index keeps it before the template's Feed/Attachments tabs (they stay last)";
			}
		}
	}

	private static string TwinReason(string name) =>
		name.Contains("Attachment", StringComparison.OrdinalIgnoreCase)
			? "provided by the mobile template (merge); review the attachments data source — retarget it to the entity's file object."
			: "provided by the mobile template (merge into the template's element).";

	private static string Nz(string value) => string.IsNullOrEmpty(value) ? null : value;

	/// <summary>
	/// Synthesizes the mobile designer's two-layer tab body inside every tab the CONVERTER creates:
	/// a grid "tab body" (<c>MainTabContainer_&lt;suffix&gt;</c>) holding the tab's Area
	/// card(s) (<c>GridContainer_&lt;suffix&gt;</c>). Mobile design puts a tab's content inside a colored,
	/// rounded Area rather than in the tab body itself, and a tab carried over from web brings neither
	/// layer. Everything is baked straight into the element map as ordinary inserts placed RIGHT AFTER the
	/// tab's own entry, so applying entries in element-map order always creates a parent before its
	/// children — the caller adds nothing of its own.
	/// <para>
	/// Two cases are excluded BY CONSTRUCTION rather than by a special case: a tab the mobile TEMPLATE
	/// provides is a <c>merge</c> twin and this pass only looks at inserts; and a tab with no top-level
	/// content gets no layers at all, so an empty Area is never created in the first place (AC#5 — there is
	/// nothing to delete afterwards).
	/// </para>
	/// <para>
	/// The tab's top-level content (expansion panels included — a panel is an ordinary component here) is
	/// then RETARGETED into the Area, and every retargeted component gets a sequential single-column
	/// <c>layoutConfig</c> so the mobile order matches the web order. The element map is walked in tree
	/// order, so the row numbers follow the source page's own ordering.
	/// </para>
	/// <para>
	/// The whole pass is switched by DATA: with no usable <c>tabAreaLayers</c> section in the rules file it
	/// is a no-op.
	/// </para>
	/// </summary>
	private static List<TabAreaLayerGroup> BuildTabAreaLayers(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules, string sourcePage) {
		TabAreaLayersRule rule = rules?.TabAreaLayers;
		// The Area card rule is nested inside the tab-body rule — the rules JSON mirrors the DOM it produces.
		SynthesizedContainerRule mainRule = rule?.MainTabContainer;
		SynthesizedContainerRule areaRule = mainRule?.AreaContainer;
		if (string.IsNullOrWhiteSpace(rule?.TabComponentType)
			|| !IsUsableLayer(mainRule) || !IsUsableLayer(areaRule)) {
			return [];
		}
		// Every name already spoken for: the source element names and the mobile names the template owns
		// (merge targets), plus the layers synthesized for tabs handled earlier in this same pass. A suffix is
		// free only when BOTH names built from it are free — the two layers share one suffix, so checking just
		// one of them could hand out a suffix whose other name already exists.
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in elementMap) {
			if (entry.WebName is { Length: > 0 } webName) {
				taken.Add(webName);
			}
			if (entry.MobileName is { Length: > 0 } mobileName) {
				taken.Add(mobileName);
			}
		}

		var groups = new List<TabAreaLayerGroup>();
		List<ElementMapEntry> convertedTabs = elementMap
			.Where(e => string.Equals(e.Operation, "insert", StringComparison.Ordinal)
				&& string.Equals(e.MobileType, rule.TabComponentType, StringComparison.OrdinalIgnoreCase)
				&& e.MobileName is { Length: > 0 })
			.ToList();
		foreach (ElementMapEntry tab in convertedTabs) {
			// Top-level content of the tab, in element-map order (= the source tree order): its own inserted
			// children, plus a wrapper dissolved INTO the tab (a relocate-children entry names the tab as the
			// container its children are placed in, and those children carry the tab as their parent too).
			// Anything nested deeper carries its own container as parentName and is none of this pass's
			// business; a merge twin carries no parentName at all and stays wherever the template put it.
			List<ElementMapEntry> content = elementMap
				.Where(e => string.Equals(e.ParentName, tab.MobileName, StringComparison.OrdinalIgnoreCase)
					&& (string.Equals(e.Operation, "insert", StringComparison.Ordinal)
						|| string.Equals(e.Operation, "relocate-children", StringComparison.Ordinal)))
				.ToList();
			if (content.Count == 0) {
				continue;
			}
			string suffix = StableSuffix(sourcePage, tab.MobileName,
				candidate => taken.Contains(mainRule.NamePrefix + candidate)
					|| taken.Contains(areaRule.NamePrefix + candidate));
			string mainName = mainRule.NamePrefix + suffix;
			taken.Add(mainName);

			// Freshly resolved index: earlier tabs have already shifted this one by their own inserts.
			// insertAt walks forward so every synthesized layer lands right after the tab's entry, parent
			// always before child (layer 2 → Area; the tab's children sit later in the map anyway).
			int insertAt = elementMap.IndexOf(tab);
			elementMap.Insert(++insertAt, SynthesizedLayerEntry(mainRule, mainName, tab.MobileName,
				$"synthesized by the converter (no web counterpart) — the tab body of the converted tab "
				+ $"'{tab.MobileName}'; it holds the Area card that follows"));

			// The Area exists only when real content remains: a relocate-children routing hint never
			// occupies a row, so a tab whose content is hints alone gets no Area (an Area that would hold
			// nothing must not be created — the same AC#5 construction, one level down).
			string areaName = null;
			if (content.Any(c => string.Equals(c.Operation, "insert", StringComparison.Ordinal))) {
				areaName = areaRule.NamePrefix + suffix;
				taken.Add(areaName);
				elementMap.Insert(insertAt + 1, SynthesizedLayerEntry(areaRule, areaName, mainName,
					$"synthesized by the converter (no web counterpart) — the Area card of the converted tab "
					+ $"'{tab.MobileName}'; on mobile a tab's content lives in an Area, not in the tab body itself"));
			}

			// Move the tab's top-level content into the Area and stack it in source order. The Area is a
			// single-column grid, so each component gets row N of column 1 — element-map order IS the web
			// order.
			var moved = new List<string>();
			int row = 1;
			foreach (ElementMapEntry child in content) {
				// Without an Area only routing hints can remain here; they point at the tab body.
				child.ParentName = areaName ?? mainName;
				if (!string.Equals(child.Operation, "insert", StringComparison.Ordinal)) {
					continue; // a relocate-children entry is a routing hint, not an element — nothing to place
				}
				moved.Add(child.MobileName);
				PlaceInSingleColumn(child, row);
				row++;
			}

			groups.Add(new TabAreaLayerGroup {
				TabName = tab.MobileName, MainTabContainerName = mainName, AreaName = areaName,
				MovedChildren = moved
			});
		}
		return groups;
	}

	/// <summary>A single-column grid cell: column 1 of the given row, spanning nothing.</summary>
	private static JsonObject SingleColumnPlacement(int row) => new() {
		["column"] = 1, ["colSpan"] = 1, ["row"] = row, ["rowSpan"] = 1
	};

	/// <summary>
	/// Stacks one retargeted element into its single-column parent at the given row. An element the
	/// adaptive pass already placed per breakpoint keeps that placement: mobile resolves layoutConfig from
	/// <c>adaptive</c> when it is present, so replacing it with a flat base placement would drop the
	/// responsive columns and gain nothing. A layoutConfig the web page carried as anything but an object
	/// (scalar, array) cannot hold <c>adaptive</c> and is replaceable — string-indexing it directly would
	/// throw InvalidOperationException.
	/// </summary>
	private static void PlaceInSingleColumn(ElementMapEntry child, int row) {
		if (child.MobileValues is JsonObject childValues
			&& (childValues["layoutConfig"] is not JsonObject layoutConfig
				|| layoutConfig["adaptive"] is null)) {
			childValues["layoutConfig"] = SingleColumnPlacement(row);
		}
	}

	/// <summary>
	/// True when a synthesized-container rule can actually produce an element: it needs a name prefix (two
	/// layers sharing one suffix would otherwise collapse to the same name) and a component <c>type</c>.
	/// A rules file that declares neither switches the pass off rather than emitting a broken element.
	/// </summary>
	private static bool IsUsableLayer(SynthesizedContainerRule container) =>
		!string.IsNullOrWhiteSpace(container?.NamePrefix)
		&& container.Values is not null
		&& container.Values.TryGetValue("type", out JsonElement type)
		&& type.ValueKind == JsonValueKind.String
		&& !string.IsNullOrWhiteSpace(type.GetString());

	/// <summary>
	/// One synthesized container: an ordinary <c>insert</c> with NO <c>webName</c> (there is no source
	/// element behind it), carrying the rule's <c>values</c> verbatim plus an initialized <c>items</c> slot —
	/// <see cref="BuildMobileValues"/> deliberately drops <c>items</c> arrays, so nothing else would give a
	/// synthesized container somewhere for its children to land.
	/// </summary>
	private static ElementMapEntry SynthesizedLayerEntry(
		SynthesizedContainerRule container, string name, string parentName, string reason) {
		var values = new JsonObject();
		foreach (KeyValuePair<string, JsonElement> pair in container.Values) {
			values[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
		}
		values["items"] ??= new JsonArray();
		return new ElementMapEntry {
			Operation = "insert",
			MobileName = name,
			// Guaranteed a non-empty string by IsUsableLayer, which gates every call to this method.
			MobileType = values["type"].GetValue<string>(),
			ParentName = parentName,
			PropertyName = "items",
			MobileValues = values,
			Reason = reason
		};
	}

	/// <summary>
	/// Applies the rules' <c>componentPropertyOverrides</c> to every element-map INSERT, stamping each mobile
	/// standard the rules file declares (container spacing, metric style). For each entry whose
	/// <c>mobileType</c> matches an override rule, the listed properties are SET on the prebuilt
	/// <c>mobileValues</c> — by default REPLACING whatever the web page carried (any shape: token, px
	/// number, CSS string, per-axis object; the web value is discarded, never translated) and ADDED when
	/// the web page carried none, so the converted body is self-describing instead of leaning on the
	/// mobile client's defaults. A rule that sets <c>mergeNestedObjects</c> instead merges its object
	/// value into the element's own, which is what a rule targeting a nested leaf needs — see
	/// <see cref="StampOverrideValue"/> for both semantics, for why a PRESENT non-object is never
	/// overwritten, and for why an ABSENT branch is created. Covers converted and synthesized inserts alike (run it after the tab-area pass);
	/// merge twins, drops and relocate hints are never touched, and the element identity keys
	/// (<c>name</c>/<c>type</c>) can never be overridden. Switched by DATA: an absent/empty group is a
	/// no-op. Returns one advisory entry per normalized element, bucketed into the report section its rule
	/// declared via <c>reportGroup</c>.
	/// </summary>
	private static ComponentPropertyOverrideResult ApplyComponentPropertyOverrides(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules) {
		var result = new ComponentPropertyOverrideResult();
		IReadOnlyList<ComponentPropertyOverrideRule> overrides = rules?.ComponentPropertyOverrides;
		if (overrides is not { Count: > 0 }) {
			return result;
		}
		// One rule per mobile type: a duplicate `type` in the rules file LAST-WINS, silently. That also means
		// a type cannot carry two rules (e.g. replace one key, merge another) — a limit to lift here, in the
		// pass, if a standard ever needs it, rather than by loosening the per-rule merge flag.
		var byType = new Dictionary<string, ComponentPropertyOverrideRule>(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentPropertyOverrideRule rule in overrides) {
			if (!string.IsNullOrWhiteSpace(rule?.Type) && rule.Values is { Count: > 0 }) {
				byType[rule.Type] = rule;
			}
		}
		if (byType.Count == 0) {
			return result;
		}
		foreach (ElementMapEntry entry in elementMap) {
			if (!string.Equals(entry.Operation, "insert", StringComparison.Ordinal)
				|| entry.MobileType is not { Length: > 0 }
				|| entry.MobileValues is not JsonObject values
				|| !byType.TryGetValue(entry.MobileType, out ComponentPropertyOverrideRule rule)) {
				continue;
			}
			var properties = new List<string>();
			var skippedPaths = new List<string>();
			foreach (KeyValuePair<string, JsonElement> pair in rule.Values) {
				if (string.Equals(pair.Key, "name", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(pair.Key, "type", StringComparison.OrdinalIgnoreCase)) {
					continue; // element identity is never overridable, whatever the rules file says
				}
				StampOverrideValue(values, pair.Key, pair.Value, rule.MergeNestedObjects, properties, skippedPaths);
			}
			// An element that was only partly normalized (or not at all) is still reported — as a skip entry —
			// so a caller can tell "nothing to normalize" from "could not normalize".
			if (properties.Count > 0 || skippedPaths.Count > 0) {
				result.Add(ResolveReportGroup(entry.MobileType), entry.MobileName, entry.MobileType,
					properties, skippedPaths);
			}
		}
		return result;
	}

	/// <summary>
	/// Stamps one rule value onto <paramref name="values"/> under <paramref name="key"/> and records what
	/// was stamped in <paramref name="stamped"/>.
	/// <para>
	/// Default (<paramref name="mergeNestedObjects"/> false) REPLACES the value outright and reports the
	/// top-level key — the long-standing behavior, kept byte-for-byte so a spacing rule still discards the
	/// web value instead of translating it.
	/// </para>
	/// <para>
	/// When the rule opts into merging, an object rule value is merged into the element's own object and
	/// the concrete LEAF PATHS actually written are reported (e.g. <c>config.text.fontSizeMode</c>) rather
	/// than the merged root, which alone would under-report what a rules file touched. Merging NEVER
	/// overwrites a value that is PRESENT but is not an object — at ANY depth, not just at the top-level
	/// key — because that value is typically a whole-value binding: replacing it with an object assembled
	/// from the rule alone destroys the binding and leaves the component missing registry-required fields
	/// (an indicator widget without <c>config.data</c> renders nothing). Such a branch is recorded in
	/// <paramref name="skipped"/>, so the refusal is visible in the report instead of silent.
	/// </para>
	/// <para>
	/// An ABSENT branch is the opposite case and IS created — that is the normalization itself, and the
	/// long-standing contract of this pass ("added when the web page carried none, so the converted body is
	/// self-describing"). A real converted metric carries <c>layout</c> with a colour and icon but no
	/// <c>border</c>, so refusing to create would make the standard unreachable on every real page. The
	/// trade-off is knowing: a created branch holds ONLY what the rule declares, so it may be partial by the
	/// component's own schema. Accepted deliberately — the source element had no value there to preserve,
	/// and validate-page is the backstop.
	/// Leaves are written, creating or overwriting, but only when the value actually DIFFERS — an element
	/// already authored at the standard is left alone and is not reported. A non-object rule value still
	/// replaces, so a merging rule can carry flat entries too.
	/// </para>
	/// </summary>
	private static void StampOverrideValue(JsonObject values, string key, JsonElement ruleValue,
		bool mergeNestedObjects, List<string> stamped, List<string> skipped) {
		JsonNode incoming = JsonNode.Parse(ruleValue.GetRawText());
		if (mergeNestedObjects && incoming is JsonObject incomingObject) {
			if (!HasLeaf(incomingObject)) {
				// Nothing is writable anywhere below, so creating the branch would change the body and report
				// nothing. Counting keys is not enough: { "layout": {} } has a key and no leaf.
				return;
			}
			if (!values.ContainsKey(key)) {
				values[key] = new JsonObject(); // absent: same rule as any deeper branch
			}
			if (values[key] is not JsonObject existing) {
				skipped.Add(key);
				return;
			}
			MergeJsonObject(existing, incomingObject, key, stamped, skipped);
			return;
		}
		values[key] = incoming;
		stamped.Add(key);
	}

	/// <summary>
	/// True when <paramref name="source"/> carries at least one non-object value somewhere below it — the
	/// only thing a merge can actually write. A tree of empty objects writes nothing, so it must not cause a
	/// branch to be created either, at any depth.
	/// </summary>
	private static bool HasLeaf(JsonObject source) {
		foreach (KeyValuePair<string, JsonNode> pair in source) {
			if (pair.Value is not JsonObject child || HasLeaf(child)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Merges <paramref name="source"/> into <paramref name="target"/>, recording the dotted path of every
	/// leaf written into <paramref name="stamped"/> and of every object branch refused into
	/// <paramref name="skipped"/>. A branch is refused only when the target carries a NON-OBJECT at that
	/// key (typically a whole-value binding); an ABSENT key is created and descended into. This is the same
	/// guard <see cref="StampOverrideValue"/> applies to the top-level key, enforced at every depth so a
	/// nested binding cannot be clobbered. Leaves are written with a detached clone (a
	/// <see cref="JsonNode"/> already owned by another parent cannot be re-attached), and ONLY when the
	/// value actually differs — an element already at the standard is left alone and is not reported as
	/// normalized, since the section's wording ("the web value was ignored") would then be untrue of it.
	/// The replace path deliberately keeps reporting unconditionally: narrowing it would change what the
	/// long-standing spacing section lists.
	/// </summary>
	private static void MergeJsonObject(
		JsonObject target, JsonObject source, string prefix, List<string> stamped, List<string> skipped) {
		foreach (KeyValuePair<string, JsonNode> pair in source) {
			string path = $"{prefix}.{pair.Key}";
			if (pair.Value is JsonObject sourceChild) {
				if (sourceChild.Count == 0) {
					continue; // nothing to write below: neither create the branch nor claim it was refused
				}
				if (!target.ContainsKey(pair.Key)) {
					target[pair.Key] = new JsonObject(); // absent: creating is the normalization itself
				}
				if (target[pair.Key] is JsonObject existingChild) {
					MergeJsonObject(existingChild, sourceChild, path, stamped, skipped);
				} else {
					skipped.Add(path); // present but NOT an object: never clobber it
				}
				continue;
			}
			JsonNode incomingLeaf = pair.Value?.DeepClone();
			if (JsonNode.DeepEquals(target[pair.Key], incomingLeaf)) {
				continue; // already at the standard — writing it would be a no-op, reporting it a false claim
			}
			target[pair.Key] = incomingLeaf;
			stamped.Add(path);
		}
	}

	/// <summary>The report group each normalizable component type belongs to.</summary>
	private static readonly IReadOnlyDictionary<string, string> ReportGroupsByType =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["crt.GridContainer"] = SpacingGroup,
			["crt.FlexContainer"] = SpacingGroup,
			["crt.IndicatorWidget"] = "metricStyle"
		};

	/// <summary>The group the <c>spacingNormalization</c> back-compat alias mirrors.</summary>
	private const string SpacingGroup = "spacing";

	/// <summary>
	/// The guide section a standard reports into, derived from the component TYPE it targets rather than
	/// declared by the rules file. The binary owns this deliberately: the section is a presentation detail,
	/// a free-form key in a runtime-resolved file lets an authoring typo ("metricstyle") silently open a new
	/// section instead of failing, and renaming the spacing rules' group would silently delete the
	/// documented <c>spacingNormalization</c> alias from the response. An unmapped type falls back to its
	/// own name, so a new standard still reports somewhere sensible; adding it here is what gives it a
	/// curated section name.
	/// </summary>
	private static string ResolveReportGroup(string mobileType) =>
		mobileType is { Length: > 0 } && ReportGroupsByType.TryGetValue(mobileType, out string group)
			? group
			: mobileType;

	/// <summary>
	/// Appends ONE line per report group that recorded something, composed from the actual counts.
	/// Deliberately built here rather than taken from the rules file: that file is resolved at runtime
	/// (env var → local cache → CDN), and <c>constraints</c>/<c>nextSteps</c> are the arrays the calling
	/// agent treats as clio's own hard rules — nothing outside this binary may write into them. It is also
	/// deterministic, and one line instead of the several hundred tokens per page that per-rule prose cost,
	/// while still saying the one thing the caller cannot derive from the data: do not undo it.
	/// </summary>
	private static void AppendNormalizationLines(
		List<string> lines, ComponentPropertyOverrideResult normalization) {
		if (normalization is null) {
			return;
		}
		foreach ((string group, ComponentPropertyOverrideResult.GroupAccumulator accumulator) in normalization.Groups) {
			lines.Add(SummaryFor(group, accumulator));
		}
	}

	/// <summary>The single caller-facing sentence describing one group's outcome.</summary>
	private static string SummaryFor(string group, ComponentPropertyOverrideResult.GroupAccumulator accumulator) {
		string skipped = accumulator.Skipped.Count > 0
			? $", {accumulator.Skipped.Count} skipped (kept their web values — worth calling out)"
			: string.Empty;
		return $"{group}: {accumulator.Normalized.Count} element(s) normalized{skipped} — see "
			+ $"guide.normalizations.{group}. The values are already in elementMap[].mobileValues; the web "
			+ "page's own values for those properties were IGNORED by design. Do NOT restore them, do NOT "
			+ "treat the difference as a defect, and never raise it as a gate question.";
	}

	/// <summary>
	/// Projects the pass output into the guide's <c>normalizations</c> map — one section per group that
	/// recorded something. Null when nothing was normalized, so the section is omitted rather than empty.
	/// </summary>
	private static IReadOnlyDictionary<string, NormalizationInfo> BuildNormalizations(
		ComponentPropertyOverrideResult result) {
		if (result.IsEmpty) {
			return null;
		}
		var sections = new Dictionary<string, NormalizationInfo>(StringComparer.OrdinalIgnoreCase);
		foreach ((string group, ComponentPropertyOverrideResult.GroupAccumulator accumulator) in result.Groups) {
			sections[group] = new NormalizationInfo {
				Note = SummaryFor(group, accumulator),
				Normalized = accumulator.Normalized,
				Skipped = accumulator.Skipped.Count > 0 ? accumulator.Skipped : null
			};
		}
		return sections;
	}

	/// <summary>
	/// Output of the shared component-property override pass, bucketed by the report group each element's
	/// type maps to. One pass stamps every standard; the reporting stays separate because each standard is
	/// a distinct thing the caller has to be told about.
	/// </summary>
	private sealed class ComponentPropertyOverrideResult {
		private readonly Dictionary<string, GroupAccumulator> _groups = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> _order = [];

		/// <summary>Report groups that recorded something, in first-seen (element-map) order.</summary>
		public IEnumerable<KeyValuePair<string, GroupAccumulator>> Groups =>
			_order.Select(group => new KeyValuePair<string, GroupAccumulator>(group, _groups[group]));

		/// <summary>True when no group recorded anything — the guide then omits the section entirely.</summary>
		public bool IsEmpty => _order.Count == 0;

		/// <summary>The entries of one group, or an empty list when that group recorded nothing.</summary>
		public IReadOnlyList<NormalizationEntry> EntriesOf(string group) =>
			_groups.TryGetValue(group, out GroupAccumulator accumulator) ? accumulator.Normalized : [];

		/// <summary>
		/// Records one element under its group. Only a merging rule can skip — a replacing rule always
		/// writes its key.
		/// </summary>
		public void Add(string group, string name, string type,
			IReadOnlyList<string> properties, IReadOnlyList<string> skipped) {
			if (!_groups.TryGetValue(group, out GroupAccumulator accumulator)) {
				accumulator = new GroupAccumulator();
				_groups[group] = accumulator;
				_order.Add(group);
			}
			if (properties.Count > 0) {
				accumulator.Normalized.Add(new NormalizationEntry {
					Name = name, Type = type, Properties = properties
				});
			}
			if (skipped.Count > 0) {
				accumulator.Skipped.Add(new NormalizationSkip {
					Name = name, Type = type, Properties = skipped,
					Reason = "the element already carries a non-object value at this path — typically a "
						+ "whole-value binding — and a merging rule never overwrites one: replacing it with an "
						+ "object built from the rule alone would destroy the binding and leave the component "
						+ "missing fields it needs, while still appearing normalized. This element keeps its "
						+ "WEB value here"
				});
			}
		}

		/// <summary>What one report group accumulated.</summary>
		internal sealed class GroupAccumulator {
			public List<NormalizationEntry> Normalized { get; } = [];
			public List<NormalizationSkip> Skipped { get; } = [];
		}
	}

	/// <summary>Base36 digit alphabet for <see cref="StableSuffix"/> (lowercase, designer-style).</summary>
	private const string Base36Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

	/// <summary>Preferred suffix length — visually matches designer-generated names (e.g. "g2cfpql").</summary>
	private const int StableSuffixLength = 7;

	/// <summary>Attempts allowed in the pathological counter fallback of <see cref="StableSuffix"/>.</summary>
	private const int StableSuffixFallbackLimit = 1000;

	/// <summary>
	/// Deterministic name suffix for the containers synthesized inside a converter-created tab.
	/// A random suffix (Guid.NewGuid) would break reproducibility — baseline diffs and
	/// repeated guide runs must produce identical names — so the suffix is a content hash: the first
	/// <see cref="StableSuffixLength"/> lowercase base36 characters of SHA-256 over
	/// <c>$"{sourcePage}:{tabName}"</c>. Stable across runs, unique across tabs, visually identical to
	/// designer-generated names (<c>MainTabContainer_g2cfpql</c>). When <paramref name="isSuffixTaken"/>
	/// reports a collision (a synthesized name already exists in the element map or the mobile
	/// template), the suffix is deterministically EXTENDED with further hash characters until free.
	/// </summary>
	internal static string StableSuffix(string sourcePage, string tabName, Func<string, bool> isSuffixTaken = null) {
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourcePage}:{tabName}"));
		string base36 = ToBase36(hash).PadLeft(StableSuffixLength, '0');
		for (int length = StableSuffixLength; length <= base36.Length; length++) {
			string candidate = base36[..length];
			if (isSuffixTaken is null || !isSuffixTaken(candidate)) {
				return candidate;
			}
		}
		// Pathological fallback (every hash prefix taken): extend with a deterministic counter. Bounded so a
		// predicate that always reports "taken" fails loudly instead of spinning; the null check keeps this
		// reachable branch safe on its own, independently of the PadLeft above.
		for (int i = 0; i < StableSuffixFallbackLimit; i++) {
			string candidate = base36 + i.ToString(CultureInfo.InvariantCulture);
			if (isSuffixTaken is null || !isSuffixTaken(candidate)) {
				return candidate;
			}
		}
		throw new InvalidOperationException(
			$"Cannot derive a free name suffix for tab '{tabName}' on page '{sourcePage}': every candidate is reported as taken.");
	}

	/// <summary>Encodes hash bytes as lowercase base36 (most-significant digit first).</summary>
	private static string ToBase36(byte[] bytes) {
		var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
		if (value.IsZero) {
			return "0";
		}
		var digits = new StringBuilder();
		while (!value.IsZero) {
			value = BigInteger.DivRem(value, 36, out BigInteger rem);
			digits.Insert(0, Base36Digits[(int)rem]);
		}
		return digits.ToString();
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
		} catch (Newtonsoft.Json.JsonException) {
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

	private static JObject ParseResources(PageBundleInfo bundle) {
		try {
			return bundle.Resources?.Strings is { } strings ? JObject.Parse(strings.ToJsonString()) : null;
		} catch (Newtonsoft.Json.JsonException) {
			return null;
		}
	}
}
