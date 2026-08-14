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

	/// <summary>
	/// Optional: narrows which SOURCE elements this mapping's <see cref="ViewConfigTemplates"/> apply to. A node
	/// matches when any filter matches it. Absent or empty means every element of a <see cref="Web"/> type.
	/// <para>
	/// A filter narrows; it does NOT authorize. The resolved MOBILE type must still be one this rule maps to —
	/// a web type that survives as itself once the mobile registry gains it keeps its own properties, and a
	/// filter naming only the web type cannot express that.
	/// </para>
	/// </summary>
	[JsonPropertyName("filters")]
	public IReadOnlyList<ElementFilterRule> Filters { get; init; }

	/// <summary>
	/// Optional: where the per-record slots (<c>row.title</c>, <c>row.body</c>) a
	/// <see cref="ViewConfigTemplates"/> template references come FROM. Selection stays in code — the binding
	/// identifier gate and the type-aware title choice cannot be expressed in a template — so this carries only
	/// the participating names.
	/// </summary>
	[JsonPropertyName("rowSource")]
	public RowSourceRule RowSource { get; init; }

	/// <summary>
	/// Optional: the mobile view-config SHAPE this mapping produces, as data. Each template renders one value
	/// onto the converted element; a key the generic property copy already produced is left alone, so a template
	/// ADDS the structure the web node has no counterpart for (e.g. a list's <c>itemLayout</c>) rather than
	/// restating what was carried.
	/// </summary>
	[JsonPropertyName("viewConfigTemplates")]
	public IReadOnlyList<ViewConfigTemplateRule> ViewConfigTemplates { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>Matches a source element. Only the component type is matched today.</summary>
public sealed class ElementFilterRule {

	/// <summary>Web component type the filter matches (e.g. <c>"crt.DataGrid"</c>).</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }
}

/// <summary>
/// One templated mobile view-config value. <c>parentName</c> and <c>propertyName</c> are READ-ONLY views of what
/// the converter already computed for the element (<c>meta.*</c>): a template may reference them so the shape it
/// produces can be read in place, but it must never SET them — the rules file deciding an element's parent would
/// desynchronize it from every other <c>parentName</c> in the element map.
/// </summary>
public sealed class ViewConfigTemplateRule {

	/// <summary>Template for the element's parent, normally <c>"{{ meta.parentName }}"</c>.</summary>
	[JsonPropertyName("parentName")]
	public string ParentName { get; init; }

	/// <summary>Template for the parent's slot, normally <c>"{{ meta.propertyName }}"</c>.</summary>
	[JsonPropertyName("propertyName")]
	public string PropertyName { get; init; }

	/// <summary>
	/// The value skeleton. Strings interpolate <c>{{ token }}</c>; an object
	/// <c>{ "$each": "&lt;slot&gt;", "as": { … } }</c> repeats its <c>as</c> body once per slot member with
	/// <c>{{ item }}</c> bound to the member; a token resolving to nothing omits its key.
	/// </summary>
	[JsonPropertyName("value")]
	public JsonElement? Value { get; init; }
}

/// <summary>
/// Where a template's per-record slots come from: the web array property to read, which of an entry's properties
/// carries the bound attribute name, and which value types may lead the row.
/// </summary>
public sealed class RowSourceRule {

	/// <summary>Web array property the slots are built FROM (e.g. <c>"columns"</c>).</summary>
	[JsonPropertyName("property")]
	public string Property { get; init; }

	/// <summary>
	/// Property of a source entry holding the bound attribute name (e.g. a column's <c>"code"</c>). Its value is
	/// prefixed with <c>$</c> to form the binding. An entry missing it, or carrying one that is not a usable
	/// identifier, is skipped.
	/// </summary>
	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	/// <summary>
	/// Mobile property the rendered structure is written to (e.g. <c>"itemLayout"</c>). Used to detect a node
	/// that AUTHORED its own — real content, which wins over anything synthesized.
	/// </summary>
	[JsonPropertyName("into")]
	public string Into { get; init; }

	/// <summary>
	/// Property of a source entry holding its value type (e.g. a column's <c>"dataValueType"</c>). Read together
	/// with <see cref="TitleValueTypes"/> to choose which entry may lead the row.
	/// </summary>
	[JsonPropertyName("valueTypeFrom")]
	public string ValueTypeFrom { get; init; }

	/// <summary>
	/// Creatio <c>DataValueType</c> ids the row's leading value may bind (see <c>CreatioDataValueType</c> for the
	/// full map). The mobile designer offers only TEXT columns for a list row's title — a lookup binds to nothing
	/// and renders an empty Title column while the body rows still look correct — so <c>row.title</c> is the
	/// first entry whose value type is listed here, NOT simply the first entry. Every other entry, including one
	/// skipped over, lands in <c>row.body</c> in source order.
	/// <para>
	/// The shipped list is the DISPLAY-text subset of <c>CreatioDataValueKind.Text</c>: 1 Text, 19
	/// LocalizableString, 27 ShortText, 28 MediumText, 29 MaxSizeText, 30 LongText, 42 PhoneText, 44 WebText,
	/// 45 EmailText. Deliberately excluded although that kind also covers them: 23 HashText and 24 SecureText
	/// (surfacing a hash or a secret as the row's headline is wrong, not merely unhelpful), 18 Color and 36
	/// MetadataText (not a value a reader reads), and 43 RichText (markup, not a single-line headline).
	/// </para>
	/// <para>
	/// An entry whose type the source does not declare is ELIGIBLE — requiring a declared type would make a
	/// PARTLY typed grid behave worse than a wholly untyped one. <c>row.title</c> is empty only when entries ARE
	/// typed and none qualifies; the element's reason then says so. Empty or absent keeps first-entry behaviour.
	/// </para>
	/// </summary>
	[JsonPropertyName("titleValueTypes")]
	public IReadOnlyList<int> TitleValueTypes { get; init; }
}

