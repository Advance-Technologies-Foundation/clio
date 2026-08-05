namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// Conversion RULES contract (ENG-89620). Version-dependent, loaded as JSON today and
// CDN-endpoint-ready (mirrors the academy component registries).
// This is the GENERAL group structure only — detailed matrix content is filled in a later step.

/// <summary>
/// Versioned set of page-conversion rule groups. New groups are added as new properties;
/// unmapped producer fields are preserved via <see cref="Extensions"/> (forward-compat guard).
/// </summary>
public sealed class WebToMobilePageConversionRules {
	/// <summary>Creatio/registry version these rules apply to (e.g. "latest" or "8.3.3").</summary>
	[JsonPropertyName("version")]
	public string Version { get; init; } = "latest";

	/// <summary>Group: base page template (schema) equivalence between web and mobile.</summary>
	[JsonPropertyName("templates")]
	public IReadOnlyList<TemplateMappingRule> Templates { get; init; } = [];

	/// <summary>Group: equivalent components — web↔mobile mappings that are not a same-type match.</summary>
	[JsonPropertyName("components")]
	public IReadOnlyList<ComponentEquivalenceRule> Components { get; init; } = [];

	/// <summary>
	/// Group: web↔mobile request (action) equivalence. Requests are wired declaratively to a
	/// component's event binding (e.g. a button's <c>clicked: { request, params }</c>); the mobile app
	/// supports only a subset of web requests. Used to remap a supported request, strip an unsupported
	/// one, or flag an unknown/custom one during conversion.
	/// </summary>
	[JsonPropertyName("requests")]
	public IReadOnlyList<RequestMappingRule> Requests { get; init; } = [];

	/// <summary>
	/// Group: the designer's 2-layer tab body synthesized into every converter-created tab:
	/// a grid "tab body" (MainTabContainer_&lt;suffix&gt;) holding one Area card
	/// (GridContainer_&lt;suffix&gt;) that receives the tab's content. Null when the section is
	/// absent from the rules file — the tab-area pass is then a no-op (the feature is switched
	/// by data, not code).
	/// </summary>
	[JsonPropertyName("tabAreaLayers")]
	public TabAreaLayersRule TabAreaLayers { get; init; }

	/// <summary>
	/// Group: per-mobile-type property overrides stamped onto EVERY element the converter INSERTS
	/// (spacing normalization). Mobile pages follow the mobile spacing standard, so a listed
	/// property is SET to the rule's value — replacing whatever the web page carried (any shape: token,
	/// px number, CSS string, per-axis object) and added even when the web page carried none, so the
	/// converted body is self-describing instead of leaning on client defaults. Applies to converted AND
	/// synthesized inserts alike; merge twins the mobile template provides are never touched. Empty or
	/// absent switches the pass off (the feature is data-driven, like <see cref="TabAreaLayers"/>).
	/// </summary>
	[JsonPropertyName("insertValueOverrides")]
	public IReadOnlyList<InsertValueOverrideRule> InsertValueOverrides { get; init; } = [];

	/// <summary>
	/// Group: deterministic removal of converter-created layout containers that end up EMPTY after all
	/// element-map decisions — a closed allowlist of removable types, evaluated bottom-up so
	/// emptiness cascades. Null when the section is absent from the rules file — the removal pass is then
	/// a no-op (the feature is switched by data, not code).
	/// </summary>
	[JsonPropertyName("emptyContainerRemoval")]
	public EmptyContainerRemovalRule EmptyContainerRemoval { get; init; }

	/// <summary>
	/// Group: deterministic placement of converted web tabs under the mobile Tabs element — every
	/// surviving converted tab gets an explicit ordering index right after the template's general tab,
	/// so the template's Feed/Attachments tabs stay LAST. Null when the section is absent from the
	/// rules file — the placement pass is then a no-op (the feature is switched by data, not code).
	/// </summary>
	[JsonPropertyName("convertedTabPlacement")]
	public ConvertedTabPlacementRule ConvertedTabPlacement { get; init; }

	/// <summary>Any future producer field not yet mapped to a typed group.</summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement> Extensions { get; init; }
}

/// <summary>
/// Maps a web base page template (schema) to its mobile counterpart. Used to recommend the
/// mobile template to create for a converted page. Details (per-template specifics) come later.
/// </summary>
public sealed class TemplateMappingRule {
	/// <summary>Web base page template schema name (e.g. "PageWithTabsFreedomTemplate").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile base page template schema name (e.g. "MobilePageWithTabsFreedomTemplate").</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>
	/// Container-name correspondence between the web template and the mobile template. Used to
	/// remap each converted element's <c>parentName</c> from its web container to the mobile one.
	/// </summary>
	[JsonPropertyName("containers")]
	public IReadOnlyList<ContainerMappingRule> Containers { get; init; } = [];

	/// <summary>
	/// Named CONTENT-component correspondence between the web template and the mobile template
	/// (analogous to <see cref="Containers"/>, but for components rather than layout containers).
	/// A web element listed here is NOT removed as inherited template chrome; instead it is kept and
	/// recorded as a merge-by-name twin onto the mapped mobile element. HOW to convert / reconcile its
	/// values is type-driven and comes from the general components rule (surfaced in
	/// <c>componentSuggestions</c>) — the model transfers them; clio prebuilds no values here.
	/// </summary>
	[JsonPropertyName("components")]
	public IReadOnlyList<ComponentMappingRule> Components { get; init; } = [];

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps a web container name to its mobile counterpart within a template pair.
/// </summary>
public sealed class ContainerMappingRule {
	/// <summary>Web container name (e.g. "SideAreaProfileContainer").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile container name (e.g. "AreaProfileContainer").</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps a web component to its mobile counterpart by element NAME within a template pair (any content
/// component, not just a list). Unlike <see cref="ComponentEquivalenceRule"/> (which maps component TYPES
/// globally and carries the conversion detail), this is per-template and keyed by the element name the
/// template provides (e.g. the list template's grid named "DataTable"). A mapped element is kept through
/// inherited-template-chrome subtraction and configured by merge-by-name; HOW to convert it is type-driven
/// and comes from the general components rule (surfaced in <c>componentSuggestions</c>) — not from here.
/// </summary>
public sealed class ComponentMappingRule {
	/// <summary>Web element name (e.g. "DataTable").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>
	/// Mobile element name it corresponds to (e.g. "List"). The mobile template provides this element;
	/// it is configured by merge-by-name (not inserted as a duplicate).
	/// </summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>
	/// Optional mobile component TYPE of the mapped element (e.g. "crt.FolderTreeActions"). Set it only
	/// together with <see cref="CarryProperties"/> and only when the web type has no same-name twin in the
	/// mobile registry (so the type cannot be inferred from the web node) — it is used to shape-coerce the
	/// carried values against the mobile registry contract. Null for a plain advisory twin (e.g. DataTable).
	/// </summary>
	[JsonPropertyName("mobileType")]
	public string MobileType { get; init; }

	/// <summary>
	/// Optional whitelist of web-node property names carried VERBATIM onto the mapped mobile element as a
	/// deterministic merge — e.g. <c>["sourceSchemaName", "rootSchemaName"]</c> for the folder tree, whose
	/// app-authored folder-schema binding the mobile template does not itself supply. Without this the web
	/// node (inherited template chrome) is pruned and the value is lost. Empty (the default) keeps the
	/// advisory-merge behavior — e.g. <c>DataTable → List</c>, whose grid→row transform is structural, not a
	/// property copy, and is left to the caller per <c>componentSuggestions</c>.
	/// </summary>
	[JsonPropertyName("carryProperties")]
	public IReadOnlyList<string> CarryProperties { get; init; } = [];

	/// <summary>Business meaning of the element (e.g. "Primary list component"), not conversion mechanics.</summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Rule for the two containers synthesized inside every converter-created tab: the
/// tab-body grid (layer 2) and the Area card inside it. Mirrors the mobile designer's own
/// <c>TabItemFactory.getMobileTabContainerConfig()</c> output, kept as DATA so the props follow
/// the platform without a code change. These values apply only to the synthesized nodes, never to
/// elements converted from the web page.
/// </summary>
public sealed class TabAreaLayersRule {
	[JsonPropertyName("note")]
	public string Note { get; init; }

	/// <summary>
	/// Mobile component type of a tab body — the element that gets the two synthesized layers. Only a tab the
	/// converter INSERTS is matched; a tab the mobile template provides arrives as a merge twin and is out of
	/// scope regardless of type. Absent from the rules file means the platform's own tab type; an explicit
	/// null/empty switches the whole pass off (there is nothing to match against).
	/// </summary>
	[JsonPropertyName("tabComponentType")]
	public string TabComponentType { get; init; } = "crt.TabContainer";

	/// <summary>The synthesized tab-body grid (layer 2, the tab's direct child).</summary>
	[JsonPropertyName("mainTabContainer")]
	public SynthesizedContainerRule MainTabContainer { get; init; }

	/// <summary>The synthesized Area card (child of the tab-body grid; receives the tab's content).</summary>
	[JsonPropertyName("areaContainer")]
	public SynthesizedContainerRule AreaContainer { get; init; }
}

/// <summary>
/// One container the converter synthesizes (no web counterpart): the element-name prefix and the
/// full mobile <c>values</c> the synthesized node carries verbatim (including its <c>type</c>).
/// </summary>
public sealed class SynthesizedContainerRule {
	/// <summary>Element-name prefix (e.g. "MainTabContainer_"); a deterministic per-tab suffix completes the name.</summary>
	[JsonPropertyName("namePrefix")]
	public string NamePrefix { get; init; }

	/// <summary>Property name → value the synthesized element carries as its mobile values.</summary>
	[JsonPropertyName("values")]
	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();
}

/// <summary>
/// One per-mobile-type value override applied to every INSERTED element of that type.
/// The element identity keys (<c>name</c>/<c>type</c>) can never be overridden — a rules file listing
/// them is ignored for those keys.
/// </summary>
public sealed class InsertValueOverrideRule {
	/// <summary>Mobile component type the override applies to (e.g. "crt.GridContainer").</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// Property name → value stamped onto the inserted element's mobile values. A value that is a JSON
	/// object is MERGED into the element's existing object key-by-key (recursively), so stamping a
	/// nested property such as <c>config.text.fontSizeMode</c> keeps the converter's sibling subtrees
	/// (e.g. <c>config.data.providing</c>) intact. Every other shape — scalar, array, or a type
	/// mismatch against what the element carries — replaces the existing value outright.
	/// </summary>
	[JsonPropertyName("values")]
	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();

	/// <summary>
	/// Which guide normalization section this rule reports into, so one pass can serve several
	/// standards without their report sections bleeding into each other (container spacing vs metric
	/// style). Absent or unrecognized falls back to <see cref="InsertValueOverrideReportGroup.Spacing"/>,
	/// which keeps a rules file written before this field behaving exactly as before.
	/// </summary>
	[JsonPropertyName("reportGroup")]
	public string ReportGroup { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Guide report sections an <see cref="InsertValueOverrideRule"/> can feed. The pass is shared; the
/// reporting is not, because each standard carries its own caller-facing summary and constraint.
/// </summary>
public enum InsertValueOverrideReportGroup {
	/// <summary>Container spacing normalization (gap Medium on inserted Grid/Flex containers).</summary>
	Spacing,

	/// <summary>Metric (indicator widget) style normalization — extra-small text, hidden border.</summary>
	MetricStyle
}

/// <summary>
/// Rule for the deterministic empty-container removal pass. <see cref="RemovableTypes"/> is
/// a CLOSED allowlist of mobile container types the converter may drop when they end up empty — layout
/// scaffolding whose disappearance loses nothing. It is deliberately NOT derived from the component
/// registry's <c>container</c> flag: "can hold children" (crt.List, crt.Tabs) is not "safe to delete
/// when empty", the registry is environment-fetched and incomplete, and the failure mode of a too-wide
/// set is silent content loss. Widening the set is an explicit rules-file decision, never inference.
/// </summary>
public sealed class EmptyContainerRemovalRule {
	[JsonPropertyName("note")]
	public string Note { get; init; }

	/// <summary>
	/// Mobile container types removable when empty (e.g. crt.FlexContainer, crt.GridContainer,
	/// crt.TabPanel, crt.TabContainer, crt.ExpansionPanel). Empty or absent switches the pass off.
	/// </summary>
	[JsonPropertyName("removableTypes")]
	public IReadOnlyList<string> RemovableTypes { get; init; } = [];
}

/// <summary>
/// Rule for the deterministic converted-tab placement pass. The mobile tabbed template provides its own
/// tabs (general/Details first, Feed and Attachments LAST); a converted web tab must land BETWEEN them.
/// Until this rule the ordering lived only as guidance prose while the element map said "no index —
/// append" (which lands a converted tab AFTER Feed/Attachments); with the section present every
/// surviving converted tab gets an explicit index starting at <see cref="FirstIndex"/>, so applying the
/// element map verbatim yields: general tab, converted web tabs, Feed, Attachments.
/// </summary>
public sealed class ConvertedTabPlacementRule {
	[JsonPropertyName("note")]
	public string Note { get; init; }

	/// <summary>Mobile Tabs element name the converted tabs are inserted under (e.g. "Tabs").</summary>
	[JsonPropertyName("tabsElementName")]
	public string TabsElementName { get; init; }

	/// <summary>Mobile component type of a single tab (e.g. "crt.TabContainer").</summary>
	[JsonPropertyName("tabComponentType")]
	public string TabComponentType { get; init; }

	/// <summary>
	/// 0-based index of the FIRST converted tab within the mobile Tabs items — 1 places it right after
	/// the template's general tab (position 0) and before the template's Feed/Attachments tabs, which
	/// shift right and stay last.
	/// </summary>
	[JsonPropertyName("firstIndex")]
	public int FirstIndex { get; init; } = 1;
}

/// <summary>
/// Maps a web request (action) to its mobile counterpart. A request is dispatched declaratively from a
/// component's event binding (<c>clicked</c> / <c>valueChange</c> / <c>updated</c>) as
/// <c>{ "request": "crt.X", "params": { ... } }</c>. An empty/null <see cref="Mobile"/> means the
/// request is NOT supported on mobile (the binding is stripped during conversion). A request absent from
/// this map entirely is treated as unknown/custom and flagged for manual review (kept as-is).
/// </summary>
public sealed class RequestMappingRule {
	/// <summary>Web request type, e.g. "crt.SaveRecordRequest".</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile request type (often the same name). Empty/null when unsupported on mobile.</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>One of: DirectMapping, WithAdaptation, Unsupported, RequiresManualDecision.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>
	/// Optional web→mobile rename of <c>params</c> keys (for requests whose parameter names differ).
	/// Empty for direct mappings — params are carried verbatim.
	/// </summary>
	[JsonPropertyName("paramMap")]
	public IReadOnlyDictionary<string, string> ParamMap { get; init; } = new Dictionary<string, string>();

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps equivalent components between web and mobile. Both sides are lists so the rule can express
/// one→one, one→many and many→one cardinality (and same-type-but-structurally-different cases).
/// <see cref="Category"/> is one of the five <see cref="ComponentMappingCategory"/> values
/// (parsed case-insensitively). Structural / per-property details are filled in a later step.
/// </summary>
public sealed class ComponentEquivalenceRule {
	/// <summary>Web component type(s), e.g. ["crt.Checkbox"].</summary>
	[JsonPropertyName("web")]
	public IReadOnlyList<string> Web { get; init; } = [];

	/// <summary>Mobile component type(s), e.g. ["crt.Toggle"]. Empty for unsupported components.</summary>
	[JsonPropertyName("mobile")]
	public IReadOnlyList<string> Mobile { get; init; } = [];

	/// <summary>One of: DirectMapping, WithAdaptation, AlternativeAvailable, Unsupported, RequiresManualDecision.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>
	/// For many→one rules: the web component type that becomes the single mobile component (the
	/// "anchor" — its position is used). Other present web components from <see cref="Web"/> are
	/// consumed (their properties may be pulled via <see cref="PropertyMap"/>). Optional.
	/// </summary>
	[JsonPropertyName("primaryWeb")]
	public string PrimaryWeb { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}
