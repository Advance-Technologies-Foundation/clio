namespace Clio.Command;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// Advisory contract for the `get-mobile-page-conversion-guide` MCP tool (ENG-89620).
// The tool is REPORT-ONLY: it never builds a mobile page body and never writes to Creatio.
// It detects the source page type and returns a deterministic "conversion guide" that an LLM uses
// to build the mobile page body itself (via create-page / update-page / validate-page +
// get-component-info). The guide is intentionally extensible — new advisory sections (and new
// source page types) can be added over time.

/// <summary>
/// One node of the source page's resolved (merged) component tree, surfaced so the model can
/// see the full structure including components inherited from the base template.
/// </summary>
public sealed class SourceComponentInfo {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("type")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Type { get; init; }

	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	[JsonPropertyName("isContainer")]
	public bool IsContainer { get; init; }
}

/// <summary>
/// A web→mobile container-name correspondence from the matched template pair. The model uses it
/// to set each component's <c>parentName</c> to the correct mobile container.
/// </summary>
public sealed class ContainerMapEntry {
	[JsonPropertyName("web")]
	public string Web { get; init; }

	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }
}

/// <summary>
/// A deterministic suggestion for one source component type: how it classifies and which mobile
/// type(s) it maps to (from the WebToMobilePageConversionRules matrix + registry type comparison).
/// </summary>
public sealed class ComponentSuggestion {
	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	/// <summary>Names of the source-page components that have this type.</summary>
	[JsonPropertyName("sourceNames")]
	public IReadOnlyList<string> SourceNames { get; init; } = [];

	/// <summary>One of the five ComponentMappingCategory values, as a string.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>Suggested mobile component type(s). Empty for unsupported / manual-decision.</summary>
	[JsonPropertyName("suggestedMobileTypes")]
	public IReadOnlyList<string> SuggestedMobileTypes { get; init; } = [];

	/// <summary>When several web types collapse to one mobile component, explains the merge (many→one).</summary>
	[JsonPropertyName("primaryWebMerge")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PrimaryWebMerge { get; init; }

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }
}

/// <summary>
/// Caption/resource convention for a newly inserted named element. <see cref="Key"/> is UNIQUE to the
/// element (<c>&lt;mobileName&gt;_caption</c>) — never the web element's inherited key — so it cannot collide
/// with a caption key the mobile template already owns (a collision would be silently dropped by update-page,
/// which does not overwrite an existing key). The caller registers <see cref="Key"/> = <see cref="SourceValue"/>
/// (the web caption's resolved en-US text) via <c>update-page resources</c>; the inserted element's caption
/// token references the same <see cref="Key"/>.
/// </summary>
public sealed class CaptionResource {
	[JsonPropertyName("key")]
	public string Key { get; init; }

	[JsonPropertyName("sourceValue")]
	public string SourceValue { get; init; }
}

/// <summary>
/// Instance-level conversion decision for ONE named element of the source page (ENG-89620). One
/// entry per named element of <c>sourceStructure</c>. The <see cref="Operation"/> tells the caller
/// exactly what to do with this element on the mobile page; it never has to infer merge-vs-insert
/// from <c>containerMap</c> + <c>componentSuggestions</c>.
/// </summary>
public sealed class ElementMapEntry {
	[JsonPropertyName("webName")]
	public string WebName { get; init; }

	[JsonPropertyName("webType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebType { get; init; }

	/// <summary>One of: <c>merge</c> | <c>insert</c> | <c>drop</c> | <c>relocate-children</c>.</summary>
	[JsonPropertyName("operation")]
	public string Operation { get; init; }

	/// <summary>Target element name on mobile (merge / insert).</summary>
	[JsonPropertyName("mobileName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileName { get; init; }

	/// <summary>Target mobile type (insert / merge), when known to the mobile registry.</summary>
	[JsonPropertyName("mobileType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileType { get; init; }

	/// <summary>
	/// Mobile parent element to attach to. For <c>insert</c> it is the element's parent; for
	/// <c>relocate-children</c> it is the container the element's children are placed into instead.
	/// </summary>
	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	/// <summary>Parent property to insert into (insert); defaults to <c>items</c>.</summary>
	[JsonPropertyName("propertyName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PropertyName { get; init; }

	/// <summary>
	/// Optional 0-based insert position within the parent's <c>items</c>. Set ONLY for a positional insert
	/// — a web element mapped above/below an anchor container via a <c>&lt;container&gt;:top</c> /
	/// <c>:bottom</c> template rule. <c>:top</c> elements get an ascending index from 0 so they land before
	/// the anchor (e.g. above the mobile <c>Tabs</c>); <c>:bottom</c> elements are appended (no index). Add it
	/// to the insert operation verbatim when present. Omitted for every other element — the mobile designer
	/// owns ordering.
	/// </summary>
	[JsonPropertyName("index")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Index { get; init; }

	/// <summary>For an <c>insert</c> of a named element with a localizable caption.</summary>
	[JsonPropertyName("captionResource")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CaptionResource CaptionResource { get; init; }

	/// <summary>
	/// For an <c>insert</c>: the prebuilt, ready-to-paste mobile component <c>values</c>. It carries the
	/// component <c>type</c> and EVERY source property the mobile component supports (per the mobile
	/// registry) — copied verbatim, with only mobile-unsupported properties pruned. Paste it as the inserted
	/// component's <c>values</c> WITHOUT dropping anything; then add ONLY the value binding (e.g.
	/// <c>control</c>, or <c>value</c> for lookups), which is type-specific and intentionally left out. Null
	/// for non-insert operations.
	/// </summary>
	[JsonPropertyName("mobileValues")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode MobileValues { get; set; }

	[JsonPropertyName("reason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Reason { get; init; }
}

/// <summary>
/// Compact, inline contract for a suggested mobile component type, drawn from the mobile registry,
/// so the model can build the component's <c>values</c> without extra get-component-info round-trips.
/// </summary>
public sealed class MobileComponentContract {
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; }

	[JsonPropertyName("container")]
	public bool Container { get; init; }

	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Description { get; init; }

	/// <summary>Property/input names this mobile component accepts (Properties ∪ Inputs).</summary>
	[JsonPropertyName("allowedProperties")]
	public IReadOnlyList<string> AllowedProperties { get; init; } = [];

	[JsonPropertyName("example")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Example { get; init; }

	[JsonPropertyName("designerDefaults")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? DesignerDefaults { get; init; }
}

/// <summary>
/// A workplace (SysWorkplace) the converted section could be registered in. <see cref="IsMobile"/>
/// marks workplaces of the Mobile client type; <see cref="ContainsSection"/> is true when the source
/// section is already a member of this workplace.
/// </summary>
public sealed class WorkplaceInfo {
	[JsonPropertyName("id")]
	public string Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("isMobile")]
	public bool IsMobile { get; init; }

	[JsonPropertyName("containsSection")]
	public bool ContainsSection { get; init; }
}

/// <summary>
/// Read-only facts about whether the source page is registered as a section (SysModule) and what it
/// takes to make that section available in the Creatio Mobile app. The tool only DETECTS and reports
/// this; the model performs the writes (odata-update / odata-create) after the user approves (Gate S).
/// </summary>
public sealed class SectionRegistrationInfo {
	/// <summary>True when a SysModule row references the source page as its section / list page.</summary>
	[JsonPropertyName("sourcePageIsSection")]
	public bool SourcePageIsSection { get; init; }

	/// <summary>Id of the matched SysModule row — the odata-update target for MobileSectionSchemaUId.</summary>
	[JsonPropertyName("sysModuleId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SysModuleId { get; init; }

	[JsonPropertyName("sectionCode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SectionCode { get; init; }

	[JsonPropertyName("sectionCaption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SectionCaption { get; init; }

	[JsonPropertyName("entitySchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EntitySchemaName { get; init; }

	/// <summary>Current MobileSectionSchemaUId on the SysModule row (null/empty when not yet registered).</summary>
	[JsonPropertyName("mobileSectionSchemaUId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileSectionSchemaUId { get; init; }

	/// <summary>True when MobileSectionSchemaUId is already set to a non-empty schema UId.</summary>
	[JsonPropertyName("mobileSectionRegistered")]
	public bool MobileSectionRegistered { get; init; }

	/// <summary>True when the source page is an edit/form page (vs a list/section page).</summary>
	[JsonPropertyName("isFormPage")]
	public bool IsFormPage { get; init; }

	/// <summary>Best-effort: the source page is the entity's default edit page (RelatedPage addon).</summary>
	[JsonPropertyName("sourcePageIsDefaultEditPage")]
	public bool SourcePageIsDefaultEditPage { get; init; }

	/// <summary>Best-effort: a mobile default edit page (MobileRelatedPage addon) already exists.</summary>
	[JsonPropertyName("mobileDefaultEditPageExists")]
	public bool MobileDefaultEditPageExists { get; init; }

	/// <summary>Workplaces the source section is currently a member of.</summary>
	[JsonPropertyName("currentWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> CurrentWorkplaces { get; init; } = [];

	/// <summary>Workplaces of the Mobile client type the section could be added to.</summary>
	[JsonPropertyName("availableMobileWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> AvailableMobileWorkplaces { get; init; } = [];

	/// <summary>Human-readable registration steps the model should propose to the user (Gate S).</summary>
	[JsonPropertyName("registrationActions")]
	public IReadOnlyList<string> RegistrationActions { get; init; } = [];

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>False when the environment could not be queried (registration facts are best-effort/unknown).</summary>
	[JsonPropertyName("probeOk")]
	public bool ProbeOk { get; init; }
}

/// <summary>
/// Deterministic advisory "conversion guide" for turning a source page into a Freedom UI mobile
/// page. The model executes the conversion using this guide; the tool builds nothing. The
/// <see cref="SourceType"/> records which source page type was detected (today: <c>freedom-web</c>).
/// </summary>
public sealed class MobilePageConversionGuide {
	// ── Source analysis ───────────────────────────────────────────────
	[JsonPropertyName("sourcePage")]
	public string SourcePage { get; init; }

	/// <summary>Detected source page type, e.g. <c>freedom-web</c> (future: other source types).</summary>
	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	/// <summary>The source page's parent (base) template schema name.</summary>
	[JsonPropertyName("sourceTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceTemplate { get; init; }

	/// <summary>Full resolved component tree (incl. inherited template components).</summary>
	[JsonPropertyName("sourceStructure")]
	public IReadOnlyList<SourceComponentInfo> SourceStructure { get; init; } = [];

	/// <summary>Web-only body sections present on the source (handlers / validators / converters).</summary>
	[JsonPropertyName("webOnlySections")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> WebOnlySections { get; init; }

	/// <summary>Data source names declared on the source page (mobile supports one).</summary>
	[JsonPropertyName("dataSources")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> DataSources { get; init; }

	// ── Data sections (apply to the mobile body via *Diff) ────────────
	/// <summary>
	/// The source page's full merged <c>modelConfig</c> (data sources + attributes). Mobile has identical
	/// structural support, so APPLY IT VERBATIM via <c>modelConfigDiff</c> — keep every attribute and ALL of
	/// its properties exactly as provided (do not omit, rename, or reconstruct any fields). Dropping or
	/// altering an attribute's declared metadata can make its binding unresolvable in Mobile Designer
	/// (<c>Item with the path … not found</c>). Null when the source page declares no model config.
	/// </summary>
	[JsonPropertyName("modelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfig { get; init; }

	/// <summary>
	/// The source page's merged <c>viewModelConfig</c>, already FILTERED for mobile: attributes referenced
	/// only by dropped/unsupported components are removed (see <see cref="ElementMap"/>). Apply it via
	/// <c>viewModelConfigDiff</c>. Reference only OOTB mobile converters — a definitive mobile converter
	/// list is forthcoming; flag any custom converter for manual review. Null when none is declared.
	/// </summary>
	[JsonPropertyName("viewModelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfig { get; init; }

	/// <summary>
	/// Ready-to-paste <c>modelConfigDiff</c> built from <see cref="ModelConfig"/> — a single root merge
	/// (<c>[{ "operation":"merge", "path":[], "values": &lt;modelConfig&gt; }]</c>). Paste it VERBATIM as the
	/// mobile page's <c>modelConfigDiff</c>; do NOT hand-build it and NEVER source it from a pre-existing
	/// body (that is how attribute <c>type</c> metadata gets dropped). Null when there is no model config.
	/// </summary>
	[JsonPropertyName("modelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfigDiff { get; init; }

	/// <summary>
	/// Ready-to-paste <c>viewModelConfigDiff</c> built from the filtered <see cref="ViewModelConfig"/> — a
	/// single root merge. Paste it VERBATIM as the mobile page's <c>viewModelConfigDiff</c>. Null when none.
	/// </summary>
	[JsonPropertyName("viewModelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfigDiff { get; init; }

	// ── Template recommendation ───────────────────────────────────────
	[JsonPropertyName("recommendedMobileTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string RecommendedMobileTemplate { get; init; }

	[JsonPropertyName("templateNote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TemplateNote { get; init; }

	[JsonPropertyName("containerMap")]
	public IReadOnlyList<ContainerMapEntry> ContainerMap { get; init; } = [];

	// ── Component mapping suggestions ─────────────────────────────────
	[JsonPropertyName("componentSuggestions")]
	public IReadOnlyList<ComponentSuggestion> ComponentSuggestions { get; init; } = [];

	/// <summary>
	/// Instance-level decision (merge / insert / drop / relocate-children) for every named element of
	/// the source page. Iterate this to build the body — do not infer merge-vs-insert from containerMap.
	/// </summary>
	[JsonPropertyName("elementMap")]
	public IReadOnlyList<ElementMapEntry> ElementMap { get; init; } = [];

	/// <summary>Inline contracts for every suggested / direct-mapped mobile component type.</summary>
	[JsonPropertyName("mobileContracts")]
	public IReadOnlyList<MobileComponentContract> MobileContracts { get; init; } = [];

	// ── Section / workplace registration (read-only facts) ────────────
	/// <summary>
	/// Whether the source page is a registered section and what it takes to make it available in the
	/// Mobile app (set MobileSectionSchemaUId, add to a workplace). Read-only — the model performs the
	/// writes after the user approves (Gate S). Null when the source page is not list/section-like.
	/// </summary>
	[JsonPropertyName("sectionRegistration")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SectionRegistrationInfo SectionRegistration { get; init; }

	// ── Page-level business rules (advisory conversion) ───────────────
	/// <summary>
	/// Page-level business rules of the source page, deterministically converted for the mobile page.
	/// Object-/entity-level rules are shared across web and mobile and are intentionally NOT touched.
	/// Each converted rule keeps its condition verbatim and only the actions that survive on mobile
	/// (a hide/show/make-* action survives only for the referenced elements that convert); a rule whose
	/// every action drops is reported under <c>droppedRules</c> instead. Null when no environment probe ran.
	/// </summary>
	[JsonPropertyName("pageBusinessRules")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageBusinessRuleConversionInfo PageBusinessRules { get; init; }

	/// <summary>
	/// Requests (actions) referenced by the source page's component event bindings (a button's
	/// <c>clicked</c>, a field's <c>valueChange</c>/<c>updated</c>), deterministically converted for
	/// mobile. Supported requests are remapped in-place inside the affected element's
	/// <c>elementMap[].mobileValues</c>; unsupported requests have their binding stripped (the component
	/// stays); unknown/custom requests are kept and flagged for manual review. This section is an
	/// advisory SUMMARY — the actionable result is already baked into <c>mobileValues</c>. Null when the
	/// source page references no requests. (Page <c>handlers</c> are web-only and never transferred.)
	/// </summary>
	[JsonPropertyName("requestConversions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RequestConversionInfo RequestConversions { get; init; }

	// ── Adaptive (per-breakpoint) layout proposal ─────────────────────
	/// <summary>
	/// The responsive layout applied to each MULTI-column mobile grid container: how many grid columns per
	/// breakpoint (<c>small</c> phone = 1, <c>medium</c>/<c>large</c> tablet = the web columns) and which
	/// cell each child occupies. Both sides are ALREADY baked into mobileValues — the container's
	/// <c>adaptive</c> columns into its own values and each child's placement into
	/// <c>elementMap[].mobileValues.layoutConfig.adaptive</c> — so there is nothing separate to apply. This
	/// is an advisory summary / PROPOSAL — present it at the conversion gate so the user can adjust or
	/// decline it. Null when no multi-column grid container is present (a single-column grid gets no adaptive).
	/// </summary>
	[JsonPropertyName("adaptiveLayout")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<AdaptiveLayoutGroup> AdaptiveLayout { get; init; }

	/// <summary>
	/// Every localized string the converted body references, keyed by resource name and resolved to its
	/// en-US text (e.g. <c>{ "EmailsSentNewMetric_title": "Emails sent" }</c>). The converted mobileValues
	/// carry the <c>#ResourceString(key)#</c> tokens verbatim (top-level captions AND nested ones like
	/// <c>config.title</c>); register this whole map on the mobile page via <c>update-page resources</c> so
	/// every token resolves. Null when the page references no resolvable localized strings.
	/// </summary>
	[JsonPropertyName("resourceStrings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, string> ResourceStrings { get; init; }

	// ── Guidance ──────────────────────────────────────────────────────
	[JsonPropertyName("constraints")]
	public IReadOnlyList<string> Constraints { get; init; } = [];

	[JsonPropertyName("nextSteps")]
	public IReadOnlyList<string> NextSteps { get; init; } = [];

	[JsonPropertyName("guidanceArticle")]
	public string GuidanceArticle { get; init; }

	[JsonPropertyName("suggestedTargetSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SuggestedTargetSchemaName { get; init; }
}

/// <summary>
/// Response envelope for the <c>get-mobile-page-conversion-guide</c> MCP tool.
/// </summary>
public sealed class MobilePageConversionGuideResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("sourceSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceSchemaName { get; init; }

	/// <summary>Detected source page type even on failure (e.g. an unsupported type).</summary>
	[JsonPropertyName("sourceType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceType { get; init; }

	[JsonPropertyName("guide")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public MobilePageConversionGuide Guide { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}

/// <summary>
/// Result of converting the source page's PAGE-level business rules for the mobile page.
/// Advisory only: the model recreates the supported rules on the mobile page schema with
/// <c>create-page-business-rule</c> after approval; the tool writes nothing.
/// </summary>
public sealed class PageBusinessRuleConversionInfo {
	/// <summary>Whether the source page's business-rule add-on metadata could be read from the environment.</summary>
	[JsonPropertyName("probeOk")]
	public bool ProbeOk { get; init; }

	/// <summary>Human-readable status (e.g. "no page-level business rules found", or why the probe failed).</summary>
	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>Rules where at least one action converts. Recreate on the mobile page (see each entry).</summary>
	[JsonPropertyName("convertedRules")]
	public IReadOnlyList<ConvertedPageBusinessRule> ConvertedRules { get; init; } = [];

	/// <summary>Rules dropped because no action converts (every referenced element drops, no data action).</summary>
	[JsonPropertyName("droppedRules")]
	public IReadOnlyList<DroppedPageBusinessRule> DroppedRules { get; init; } = [];
}

/// <summary>
/// A source page-level rule whose condition and surviving actions were carried to the mobile page.
/// </summary>
public sealed class ConvertedPageBusinessRule {
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>
	/// Ready-to-paste <c>rule</c> argument for <c>create-page-business-rule</c> on the mobile page —
	/// the condition verbatim plus the actions that survive (element names remapped web→mobile). Pass
	/// it to <c>create-page-business-rule</c> verbatim.
	/// </summary>
	[JsonPropertyName("rule")]
	public JsonNode Rule { get; init; }
}

/// <summary>A source page-level rule that does not convert (no surviving action).</summary>
public sealed class DroppedPageBusinessRule {
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	[JsonPropertyName("reason")]
	public string Reason { get; init; }
}

/// <summary>
/// Advisory summary of how the source page's component event-binding requests (actions) were converted
/// for mobile. The actionable result is already applied to each affected element's
/// <c>elementMap[].mobileValues</c>; this section explains what happened so the user can review.
/// </summary>
public sealed class RequestConversionInfo {
	/// <summary>Requests carried to mobile (kept in the binding; remapped when the mobile name differs).</summary>
	[JsonPropertyName("convertedRequests")]
	public IReadOnlyList<ConvertedRequest> ConvertedRequests { get; init; } = [];

	/// <summary>Requests with no mobile equivalent: the binding was stripped (the component still renders).</summary>
	[JsonPropertyName("droppedRequests")]
	public IReadOnlyList<DroppedRequest> DroppedRequests { get; init; } = [];

	/// <summary>Unknown/custom requests kept verbatim but flagged: verify they exist on mobile.</summary>
	[JsonPropertyName("flaggedRequests")]
	public IReadOnlyList<FlaggedRequest> FlaggedRequests { get; init; } = [];
}

/// <summary>A request carried to mobile from a component's event binding.</summary>
public sealed class ConvertedRequest {
	/// <summary>Name of the component that carries the binding (e.g. "SaveButton").</summary>
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	/// <summary>Event binding the request is wired to (e.g. "clicked", "valueChange").</summary>
	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("webRequest")]
	public string WebRequest { get; init; }

	[JsonPropertyName("mobileRequest")]
	public string MobileRequest { get; init; }
}

/// <summary>A request stripped from a component's event binding (no mobile equivalent).</summary>
public sealed class DroppedRequest {
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("webRequest")]
	public string WebRequest { get; init; }

	[JsonPropertyName("reason")]
	public string Reason { get; init; }
}

/// <summary>An unknown/custom request kept in the binding but flagged for manual verification.</summary>
public sealed class FlaggedRequest {
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("request")]
	public string Request { get; init; }

	[JsonPropertyName("reason")]
	public string Reason { get; init; }
}

/// <summary>
/// The adaptive (per-breakpoint) layout applied to one multi-column mobile grid container. Both sides are
/// ALREADY baked into mobileValues by the tool — the container's <c>adaptive</c> columns into the
/// container's own values, and each child's placement into its <c>mobileValues.layoutConfig.adaptive</c> —
/// so there is nothing separate to apply (no duplicate merge). This is an advisory summary; present it at
/// the conversion gate so the user can adjust or decline.
/// </summary>
public sealed class AdaptiveLayoutGroup {
	/// <summary>The mobile container these fields are grouped into (e.g. "AreaProfileContainer").</summary>
	[JsonPropertyName("containerName")]
	public string ContainerName { get; init; }

	/// <summary>
	/// Advisory overview of the grid columns per breakpoint (already baked into the container's mobileValues):
	/// keys <c>small</c> / <c>medium</c> / <c>large</c>, each a list of CSS column sizes (e.g. ["1fr","1fr"]).
	/// </summary>
	[JsonPropertyName("columnsByBreakpoint")]
	public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnsByBreakpoint { get; init; }
		= new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>The fields placed in this container and the per-breakpoint cell each occupies.</summary>
	[JsonPropertyName("items")]
	public IReadOnlyList<AdaptiveLayoutItem> Items { get; init; } = [];
}

/// <summary>One field's proposed per-breakpoint cell placement (mirrors its baked-in mobileValues).</summary>
public sealed class AdaptiveLayoutItem {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// The <c>layoutConfig.adaptive</c> object: keys <c>small</c> / <c>medium</c> / <c>large</c>, each
	/// <c>{ row, column, colSpan, rowSpan }</c> (1-based). Identical to what is already written into the
	/// field's <c>elementMap[].mobileValues.layoutConfig.adaptive</c>.
	/// </summary>
	[JsonPropertyName("layoutConfigAdaptive")]
	public JsonNode LayoutConfigAdaptive { get; init; }
}

// ── Intermediate read-model (not serialized) ──────────────────────────
// Produced by PageBusinessRuleProbe from persisted add-on metadata, consumed by
// WebToMobileAnalysisService.ConvertPageBusinessRules. Conditions/expressions are already
// reverse-mapped into the create-page-business-rule INPUT contract shape so conversion stays pure.

/// <summary>One source page-level business rule (single case) parsed from add-on metadata.</summary>
internal sealed class SourcePageBusinessRule {
	public string Caption { get; init; }

	/// <summary>Condition group in create-page-business-rule input shape ({logicalOperation, conditions}); may be null.</summary>
	public JsonNode Condition { get; init; }

	/// <summary>
	/// Why the source condition cannot be faithfully represented in the create-page-business-rule input (or
	/// <see cref="PageRuleConditionIssue.None"/> when it can). Such a rule is dropped for manual recreation
	/// rather than emitted with fabricated semantics. <see cref="PageRuleConditionIssue"/> for the cases.
	/// </summary>
	public PageRuleConditionIssue ConditionIssue { get; init; }

	public List<SourcePageRuleAction> Actions { get; init; } = [];
}

/// <summary>
/// Why a source page-rule condition cannot be converted losslessly into the flat, single-operator
/// create-page-business-rule condition input. A non-<see cref="None"/> value drops the rule for manual recreation.
/// </summary>
internal enum PageRuleConditionIssue {
	/// <summary>The condition converts faithfully (or there is no condition).</summary>
	None = 0,

	/// <summary>
	/// The condition mixes AND and OR across nested groups (e.g. <c>A AND (B OR C)</c>); the flat single-operator
	/// input cannot represent it without changing when the rule fires.
	/// </summary>
	MixedAndOr,

	/// <summary>
	/// A condition uses a present comparison operator that maps to no supported comparison (e.g. "begins with").
	/// Emitting it would silently change the comparison, so the rule is dropped instead.
	/// </summary>
	UnrecognizedComparison
}

/// <summary>One action of a source page-level business rule. Page rules support only element actions.</summary>
internal sealed class SourcePageRuleAction {
	/// <summary>Short action type: hide-element / show-element / make-editable / make-read-only / make-required / make-optional.</summary>
	public string ActionType { get; init; }

	/// <summary>Referenced page element names.</summary>
	public List<string> ElementItems { get; init; } = [];
}

/// <summary>Outcome of reading a source page's page-level business rules.</summary>
public sealed class PageBusinessRuleProbeResult {
	public bool ProbeOk { get; init; }
	public string Note { get; init; }
	internal IReadOnlyList<SourcePageBusinessRule> Rules { get; init; } = [];
}
