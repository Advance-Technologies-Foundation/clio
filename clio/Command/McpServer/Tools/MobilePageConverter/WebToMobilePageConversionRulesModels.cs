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

	/// <summary>
	/// Group: equivalent components. An entry is EITHER a type-equivalence (web↔mobile mapping that is not a
	/// same-type match, e.g. crt.Checkbox→crt.Toggle) OR a template group (<c>filters</c> naming the source
	/// elements it applies to plus the <c>viewConfigTemplates</c> that produce their mobile values, e.g. the
	/// grid→list row). Both shapes live in one array because they answer the same question — "what does this
	/// web component become on mobile" — and a template group also carries its own target type in
	/// <c>viewConfigTemplates[].value.type</c>, so it needs no separate web/mobile pair.
	/// </summary>
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
	[JsonPropertyName("componentPropertyOverrides")]
	public IReadOnlyList<ComponentPropertyOverrideRule> ComponentPropertyOverrides { get; init; } = [];

	/// <summary>
	/// Group: deterministic removal of converter-created layout containers that end up EMPTY after all
	/// element-map decisions — a closed allowlist of removable types, evaluated bottom-up so
	/// emptiness cascades. Null when the section is absent from the rules file — the removal pass is then
	/// a no-op (the feature is switched by data, not code).
	/// </summary>
	[JsonPropertyName("emptyContainerRemoval")]
	public EmptyContainerRemovalRule EmptyContainerRemoval { get; init; }

	/// <summary>
	/// Group: components that must be REMOVED (not converted) when found nested inside an already
	/// copied-verbatim property of a matched host element — e.g. <c>crt.SearchFilter</c> inside
	/// <c>crt.ExpansionPanel.tools</c> (the search field does not fit the panel's compact icon-only
	/// header strip). Unlike <see cref="ComponentEquivalenceRule"/> (which governs a node WALKED by
	/// the element-map builder, and would ban the type EVERYWHERE on the page), this governs a node
	/// buried inside a property the generic per-element copy already carried whole — a scope
	/// <c>filters</c>/<c>viewConfigTemplates</c> never reaches, because those only ever inspect the
	/// node currently being converted, not what its own already-built value carries nested inside
	/// one of its properties. Scoped to a specific (type, host type, host property) combination —
	/// the defect this exists for is positional (this type does not fit THIS container), not "this
	/// type is unsupported everywhere". Empty or absent switches the pass off (the feature is
	/// data-driven, like <see cref="EmptyContainerRemoval"/>).
	/// </summary>
	[JsonPropertyName("excludedComponents")]
	public IReadOnlyList<ExcludedComponentGroup> ExcludedComponents { get; init; } = [];

	/// <summary>
	/// Group: container NAMES that are NON-CONVERTING SCOPES on mobile (e.g. <c>MainHeader</c>). Such a container
	/// yields no mobile element of its own; it is KEPT through template-chrome pruning (so its app-added descendants
	/// keep it as an ancestor for a rule's <c>path</c>), its subtree is walked in scope mode, and any descendant a
	/// conversion template does not RETARGET is dropped (not present on mobile). This is deliberately DECOUPLED from
	/// <see cref="ComponentEquivalenceRule.Path"/> — <c>path</c> is a pure positive filter and never turns a
	/// container into a drop-scope by itself, so a container whose name merely appears in some rule's path is NOT
	/// made a scope. Empty or absent switches the behavior off (data-driven, like <see cref="EmptyContainerRemoval"/>).
	/// </summary>
	[JsonPropertyName("nonConvertingScopeContainers")]
	public IReadOnlyList<string> NonConvertingScopeContainers { get; init; } = [];

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
	/// Optional whitelist that NARROWS which web-node properties are carried onto the mapped mobile element —
	/// e.g. <c>["sourceSchemaName", "rootSchemaName"]</c> for the folder tree, whose app-authored binding is
	/// the only thing the mobile template does not itself supply. Leave it EMPTY (the default) for a twin of
	/// the SAME component on both sides (e.g. <c>AttachmentList → AttachmentFileList</c>, both
	/// <c>crt.FileList</c>): the element is just renamed between the web and mobile templates, so the page's
	/// DELTA over the web-template baseline is carried automatically — a property the page left at the
	/// template default is omitted so the mobile element keeps its own default (no <c>type</c> is emitted — a
	/// merge targets an element the template already owns). A twin whose web type has no mobile equivalent (a structural conversion, e.g.
	/// <c>DataTable → List</c>, crt.DataGrid → crt.List) carries nothing and stays an advisory merge, with
	/// the grid→row how-to left to the caller per <c>componentSuggestions</c>. Without a twin the web node
	/// (inherited template chrome) is pruned and its values are lost.
	/// </summary>
	[JsonPropertyName("carryProperties")]
	public IReadOnlyList<string> CarryProperties { get; init; } = [];

	/// <summary>Business meaning of the element (e.g. "Primary list component"), not conversion mechanics.</summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Rule for the two containers synthesized inside every converter-created tab: the
/// tab-body grid (layer 2) nesting the Area card inside it — the JSON nesting mirrors the
/// resulting DOM (<see cref="MainTabContainer"/> holds its <see cref="SynthesizedContainerRule.AreaContainer"/>).
/// Mirrors the mobile designer's own
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

	/// <summary>
	/// The synthesized tab-body grid (layer 2, the tab's direct child); carries the nested
	/// <see cref="SynthesizedContainerRule.AreaContainer"/> that receives the tab's content.
	/// </summary>
	[JsonPropertyName("mainTabContainer")]
	public SynthesizedContainerRule MainTabContainer { get; init; }
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

	/// <summary>
	/// The synthesized Area card nested inside this container (receives the tab's content); the nesting
	/// mirrors the resulting DOM. Null on the innermost container — the Area card carries no nested layer.
	/// </summary>
	[JsonPropertyName("areaContainer")]
	public SynthesizedContainerRule AreaContainer { get; init; }
}

/// <summary>
/// One per-mobile-type value override applied to every INSERTED element of that type.
/// The element identity keys (<c>name</c>/<c>type</c>) can never be overridden — a rules file listing
/// them is ignored for those keys.
/// </summary>
public sealed class ComponentPropertyOverrideRule {
	/// <summary>Mobile component type the override applies to (e.g. "crt.GridContainer").</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>Property name → value stamped onto the inserted element's mobile values.</summary>
	[JsonPropertyName("values")]
	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();

	/// <summary>
	/// How an object-valued entry in <see cref="Values"/> is stamped. Default (false) REPLACES the
	/// element's value outright — the long-standing behavior every flat rule relies on, and the reason a
	/// spacing rule can promise the web value is discarded rather than translated. When true the rule
	/// value is MERGED key-by-key (recursively) into the element's existing object, which is what a rule
	/// targeting a nested leaf (e.g. <c>config.text.fontSizeMode</c>) needs so the converter's sibling
	/// subtrees (e.g. <c>config.data.providing</c>) survive.
	/// <para>
	/// A merging rule NEVER overwrites a value that is PRESENT but is not an object, at ANY depth: such a
	/// value is typically a whole-value binding, and replacing it with an object assembled from the rule
	/// alone would destroy the binding and leave the component missing fields it needs (an indicator widget
	/// whose <c>config</c> is replaced loses <c>config.data</c> and renders nothing) while still looking
	/// normalized. Every branch refused this way is recorded in the report's <c>skipped</c> list.
	/// </para>
	/// <para>
	/// An ABSENT branch is the opposite case and IS created, because that is the normalization itself: a
	/// real converted metric carries <c>layout</c> with a colour and icon but no <c>border</c>, so refusing
	/// to create would make the standard unreachable on every real page. A created branch holds ONLY what
	/// the rule declares — so a rule may create a branch that is partial by the component's own schema. That
	/// is accepted deliberately: the source element had no value there to preserve, and <c>validate-page</c>
	/// is the backstop. Keep it in mind when authoring a rule whose branch may be absent.
	/// </para>
	/// <para>
	/// LEAVES are written — creating or overwriting — but only when the value actually differs, so an
	/// element already authored at the standard is left alone and is not reported as normalized.
	/// </para>
	/// <para>
	/// Note the granularity: the flag is per-rule, but the effect is per-value-shape — an object value
	/// merges, a scalar or array still replaces. One rule therefore cannot mix the two semantics for
	/// different keys.
	/// </para>
	/// </summary>
	[JsonPropertyName("mergeNestedObjects")]
	public bool MergeNestedObjects { get; init; }

	/// <summary>
	/// Free-form explanation for whoever maintains the rules file. Deliberately NOT surfaced to the
	/// caller: the guide composes its report from the actual outcome, so no wording here can drift from
	/// what was written, and the rules file — resolved at runtime from an env var, a local cache or the
	/// CDN — cannot reach the calling agent's instruction channel.
	/// </summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }
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
/// One group of <see cref="ExcludedComponentFilterRule"/>s for the
/// <see cref="WebToMobilePageConversionRules.ExcludedComponents"/> pass. A host's subtree matches when ANY
/// filter in ANY group matches a descendant node — the same "matches when any filter matches" convention
/// as <see cref="ComponentEquivalenceRule.Filters"/>/<c>MatchesAnyFilter</c>, so a future case is just
/// another filter entry, never a code change.
/// </summary>
public sealed class ExcludedComponentGroup {
	[JsonPropertyName("filters")]
	public IReadOnlyList<ExcludedComponentFilterRule> Filters { get; init; } = [];
}

/// <summary>
/// Matches a component to remove from a host, in whichever of the two element-map shapes the component
/// took: an entry of its own whose parent chain reaches the host (the primary shape — the child-array
/// traversal walks <c>tools</c>/<c>menuItems</c> children into their own entries), or a node nested
/// verbatim inside a host property the per-element copy carried whole (the fallback shape). See
/// <c>ExcludedComponentsPass</c> for the full two-phase semantics.
/// </summary>
public sealed class ExcludedComponentFilterRule {
	/// <summary>
	/// Component type to remove wherever it is found within the matched scope (e.g.
	/// <c>"crt.SearchFilter"</c>), at any nesting depth.
	/// <para>
	/// The two phases compare this value against DIFFERENT type domains, because they look at different data:
	/// the entry-graph phase matches an element-map entry's RESOLVED <c>mobileType</c>, while the
	/// verbatim-carry phase matches the raw <c>type</c> of a web node copied whole into a host property —
	/// nothing resolved it, so it is still the WEB type. The two coincide for every type the conversion rules
	/// carry over unchanged, which is every type any bundled rule targets today. They diverge only for a type
	/// a <see cref="ComponentEquivalenceRule"/> or a view-config template RENAMES on the way to mobile, and a
	/// filter naming such a type covers ONE phase only — the mobile name matches the entry graph, the web name
	/// matches the verbatim carry. No reverse lookup is attempted: teaching this pass the equivalence map to
	/// cover a case no rule has would buy a hypothetical at the cost of the coupling the whole pass avoids.
	/// A future rule that needs both sides should ship as two filter entries, one per name.
	/// </para>
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// Mobile type of the HOST element the search is confined to (e.g. <c>"crt.ExpansionPanel"</c>). The
	/// host is found STRUCTURALLY, at any depth: an <c>elementMap</c> entry whose resolved <c>MobileType</c>
	/// matches ANY ancestor on the banned entry's <c>parentName</c> chain (primary shape), or any
	/// array-element object with this <c>type</c> nested anywhere inside an entry's <c>mobileValues</c>
	/// (fallback shape — a host buried in a verbatim-carried property, with no entry of its own). This is NOT
	/// a direct-JSON-parent check either way: <see cref="Type"/> may sit several levels deeper inside one of
	/// the host's properties.
	/// </summary>
	[JsonPropertyName("parentType")]
	public string ParentType { get; init; }

	/// <summary>
	/// Optional: restrict the match to the subtree hanging off this one property (slot) of the host (e.g.
	/// <c>"tools"</c>); the comparison is case-insensitive and a host whose subtree does not enter through
	/// the named slot is a no-op for the filter (an explicit scope is an explicit boundary — there is no
	/// fallback to the whole subtree). On the entry graph the check applies to the EDGE ENTERING THE HOST —
	/// the ancestor-path entry attached directly to the host must occupy this slot (its <c>propertyName</c>,
	/// absent = <c>items</c>) — while the banned component itself may sit levels deeper through ordinary
	/// <c>items</c> edges; on a verbatim-carried host it is the host's own property of this name. Absent
	/// searches the host's whole subtree, under any slot — <c>tools</c> and <c>items</c> alike — so prefer
	/// naming the slot explicitly whenever the scope is known, to avoid matching the same type in an
	/// unrelated property (e.g. a button's <c>menuItems</c>) of the same host.
	/// </summary>
	[JsonPropertyName("propertiesContainerName")]
	public string PropertiesContainerName { get; init; }

	/// <summary>
	/// Optional free-text annotation for whoever reads or edits the RULES FILE — why this exclusion exists.
	/// Deliberately NOT surfaced in the conversion report, unlike <see cref="RequestMappingRule.Note"/>, which
	/// becomes a drop reason: that note explains a platform fact true of the request everywhere, while this one
	/// explains a product judgement about one position, and the drop reason is deliberately restricted to the
	/// mechanical fact the pass can actually derive (see <c>ExcludedComponentsPass.BuildDropReason</c>). The
	/// agent-facing "an excludedComponents drop is a positional exclusion, never conversion loss" contract is
	/// owned by the shipped guidance article, which teaches the whole drop CLASS once rather than restating a
	/// motivation per rule. Parsed so the rules file can carry the annotation without an unknown-member risk.
	/// </summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }
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

	/// <summary>
	/// Template-group entries only: the source elements the <see cref="ViewConfigTemplates"/> apply to. A node
	/// matches when ANY filter matches it. Empty on a plain type-equivalence entry (which matches by
	/// <see cref="Web"/> instead).
	/// </summary>
	[JsonPropertyName("filters")]
	public IReadOnlyList<ElementFilterRule> Filters { get; init; } = [];

	/// <summary>
	/// Template-group entries only: a PURE POSITIVE ancestor-NAME filter that narrows where the entry applies. Empty
	/// (default) = the entry applies wherever its <see cref="Filters"/> match. Non-empty = it applies only to a node
	/// whose SOURCE ancestor chain (outer→inner) contains these names as an ORDERED SUBSEQUENCE at any depth — e.g.
	/// <c>["MainHeader"]</c> restricts the entry to elements located anywhere under a container named
	/// <c>MainHeader</c>, and <c>["A","B"]</c> to a node under an <c>A</c> that itself (any depth) contains a
	/// <c>B</c> above the node. AND-combined with <see cref="Filters"/>. This is ONLY a filter: it never turns a named
	/// container into a non-converting drop-scope — that behavior is declared separately and explicitly by
	/// <see cref="WebToMobilePageConversionRules.NonConvertingScopeContainers"/>, so a container whose name merely
	/// appears here is unaffected on its own.
	/// </summary>
	[JsonPropertyName("path")]
	public IReadOnlyList<string> Path { get; init; } = [];

	/// <summary>
	/// Template-group entries only: the mobile values produced for a matching element, as data. Each template's
	/// own <c>value.type</c> declares the target mobile type — which is also what gates it and, for an entry with
	/// no <see cref="Mobile"/>, what the converter derives the element's mobile type from. Empty on a plain
	/// type-equivalence entry.
	/// </summary>
	[JsonPropertyName("viewConfigTemplates")]
	public IReadOnlyList<ViewConfigTemplateRule> ViewConfigTemplates { get; init; } = [];
}


/// <summary>Matches a source element. Only the component type is matched today.</summary>
public sealed class ElementFilterRule {

	/// <summary>Web component type the filter matches (e.g. <c>"crt.DataGrid"</c>).</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }
}

/// <summary>
/// One templated mobile view-config value, and the contract a rules author writes against.
/// </summary>
/// <remarks>
/// A path is resolved against one of two ROOTS, or against the current member inside a repeat:
/// <list type="bullet">
/// <item><c>{{ diff.name }}</c>, <c>{{ diff.parentName }}</c>, <c>{{ diff.propertyName }}</c> — what the
/// converter already computed for the operation.</item>
/// <item><c>{{ source.&lt;path&gt; }}</c> — the WEB node being converted, read as a JSON path, so indexes and
/// slices work: <c>source.items</c>, <c>source.columns[0].code</c>, <c>source.columns[1:]</c>.</item>
/// <item>a BARE path inside a <c>$each</c> body — the current member, e.g. <c>{{ code }}</c>.</item>
/// </list>
/// There is no <c>meta.*</c> root and no <c>{{ item }}</c> alias: an unresolvable path yields nothing, so a
/// template written against either would be dropped or, for a placement field, skipped WHOLE and silently.
/// <para>
/// <c>parentName</c> and <c>propertyName</c> DRIVE placement. A template may ECHO the converter's own value
/// (<c>"{{ diff.parentName }}"</c>) to leave the element where the walk found it, or it may render a DIFFERENT
/// value to RETARGET the element — it is then emitted as an insert into that declared container/property (appended,
/// no index) instead of its walked position. This is how a source element is regrouped elsewhere on mobile (e.g. a
/// MainHeader button → <c>FloatingActionButton.menuItems</c>). A template that declares neither field, or only
/// echoes, changes nothing. When a retarget names a parent the target mobile template does not provide, the
/// converter drops the element with a diagnostic rather than emitting an unresolvable insert.
/// </para>
/// </remarks>
public sealed class ViewConfigTemplateRule {

	/// <summary>Target parent to place the element under — <c>"{{ diff.parentName }}"</c> to keep the walked parent,
	/// or a different value / name to RETARGET the element there (e.g. <c>"FloatingActionButton"</c>). Absent = keep.</summary>
	[JsonPropertyName("parentName")]
	public string ParentName { get; init; }

	/// <summary>Target slot on the parent — <c>"{{ diff.propertyName }}"</c> to keep the walked slot, or a different
	/// value to place into that slot when retargeting (e.g. <c>"menuItems"</c>). Absent = keep.</summary>
	[JsonPropertyName("propertyName")]
	public string PropertyName { get; init; }

	/// <summary>
	/// The value skeleton, and the element's TARGET type: its <c>type</c> is what gates the template against the
	/// mobile type the element resolved to, so a template naming another type never applies.
	/// <para>
	/// Strings interpolate <c>{{ path }}</c> — a string that is EXACTLY one path yields that path's own value,
	/// so a slot may carry a non-string, while anything else is substituted textually (which is what makes
	/// <c>"${{ source.columns[0].code }}"</c> a literal <c>$</c> followed by the binding). An object
	/// <c>{ "$each": "&lt;path&gt;", "as": { … } }</c> repeats its <c>as</c> body once per member of the
	/// resolved collection, with the member as the root for BARE paths inside it. A path resolving to nothing
	/// drops its key rather than emitting a null or its own text.
	/// </para>
	/// </summary>
	[JsonPropertyName("value")]
	public JsonElement? Value { get; init; }

	/// <summary>
	/// Opt-in carry switch. When true, EVERY source property is copied first (only the element's <c>name</c> and
	/// its resolved <c>type</c> aside) and the template's <see cref="Value"/> is laid OVER them — so the mobile
	/// element keeps all its source properties except the ones the template explicitly names, without enumerating
	/// them (e.g. crt.Checkbox → crt.Toggle keeps <c>control</c>/<c>value</c>/<c>label</c>/… and just retypes;
	/// a grid → list keeps its <c>dataSourceName</c>/columns). Default (false) is AUTHORITATIVE: the values are
	/// formed EXCLUSIVELY from what the template declares (over the element's type). Either way
	/// <c>layoutConfig</c> is always copied — it is layout placement, not a component property.
	/// </summary>
	[JsonPropertyName("preserveSourceProperties")]
	public bool PreserveSourceProperties { get; init; }
}


