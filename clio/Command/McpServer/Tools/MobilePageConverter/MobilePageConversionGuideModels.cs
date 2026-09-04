namespace Clio.Command.McpServer.Tools.MobilePageConverter;

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
/// One change to a TEMPLATE-OWNED data-section value that no mobile diff operation can express, so the
/// converted page cannot carry it. Reported per occurrence rather than described in prose because the three
/// <see cref="Kind"/>s have DIFFERENT outcomes and two different remedies.
/// </summary>
/// <remarks>
/// No diff operation in the mobile vocabulary edits an existing array element in place: the path applier
/// identifies elements by <c>_id</c> while these config elements are keyed by <c>name</c>, so a
/// name-addressed merge has no <c>_id</c> to resolve and an insert would duplicate the name. The converter
/// therefore lets the template's native value win and reports the loss here instead of shipping a silently
/// lossy body.
/// </remarks>
public sealed record DataSectionConflict {
	/// <summary>
	/// Which data section the conflict is in — <c>"modelConfig"</c> or <c>"viewModelConfig"</c>. It names the
	/// diff the caller has to hand-edit if the page's value must win.
	/// </summary>
	[JsonPropertyName("section")]
	public string Section { get; init; }

	/// <summary>
	/// Path to what changed, as segments (same shape as a diff operation's <c>path</c>) — e.g.
	/// <c>["attributes","Items","modelConfig","filterAttributes"]</c>.
	/// </summary>
	[JsonPropertyName("path")]
	public IReadOnlyList<string> Path { get; init; } = [];

	/// <summary>
	/// The <c>name</c> of the array element that changed. Present only for
	/// <c>"changed-named-element"</c> — the other two kinds have nothing to name.
	/// </summary>
	[JsonPropertyName("entry")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Entry { get; init; }

	/// <summary>
	/// What kind of change it is, which determines the outcome AND the remedy:
	/// <list type="bullet">
	/// <item><description><c>"changed-named-element"</c> — the array element exists in the template under the
	/// same <c>name</c> but the page changed its content. NOT re-applied: the template keeps its own value.
	/// Remedy: if the page's value must win, edit that entry in the diff by hand before pasting.</description></item>
	/// <item><description><c>"changed-scalar"</c> — a scalar inside a template-owned collection config changed
	/// (e.g. a collection's <c>modelConfig.path</c>). DROPPED from the emitted diff so the mobile-correct value
	/// is not clobbered. Same remedy as above.</description></item>
	/// <item><description><c>"nameless-changed-in-place"</c> — the page edited an array element that carries no
	/// <c>name</c>, so it cannot be matched. NOTHING is dropped — the page's element IS inserted — but it will
	/// DUPLICATE the template's own at runtime. Remedy: remove one of the two.</description></item>
	/// </list>
	/// </summary>
	[JsonPropertyName("kind")]
	public string Kind { get; init; }
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
/// A source element that did NOT reach the mobile page, and why. Separate from
/// <see cref="MobilePageConversionGuide.ElementMap"/> on purpose: that map is a list of operations to APPLY,
/// while this is the audit trail of what was not built — for the caller to REPORT, never to act on.
/// </summary>
/// <remarks>
/// Nothing here is derivable from the element map, because a dropped element produces no operation to read a
/// cause off. And the cause is not derivable from the element's TYPE either: on a real
/// <c>Leads_FormPage</c>, 11 of 12 dropped elements have
/// <c>componentSuggestions[].category = "DirectMapping"</c> — a type that converts perfectly well — so a
/// caller seeing only the name and type would read every one of them as conversion loss and re-insert it
/// (ENG-95827).
/// </remarks>
public sealed class DroppedElement {
	/// <summary>The source element's name.</summary>
	[JsonPropertyName("webName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebName { get; init; }

	/// <summary>The source element's web component type.</summary>
	[JsonPropertyName("webType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebType { get; init; }

	/// <summary>
	/// Why it was dropped: one or more codes from <see cref="ReasonCodes"/>, each optionally carrying
	/// <c>params</c>. Branch on <c>code</c>; the guidance article says what to tell the user about each.
	/// </summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>
/// One coded reason on a <see cref="DroppedElement"/>. <see cref="Code"/> is drawn from the closed vocabulary
/// in <see cref="ReasonCodes"/> and is the thing to branch on; <see cref="Params"/> carries the values that
/// would otherwise have been interpolated into a sentence.
/// </summary>
/// <remarks>
/// Everything a caller must DO about a code lives in the guidance article, keyed by the code — not here and
/// not in the payload. That is the whole point: the same conversion decision reads identically on every run,
/// so restating it in English per entry cost bytes and determinism without adding information (ENG-95827).
/// </remarks>
public sealed class ReasonCode {
	/// <summary>The classification, from <see cref="ReasonCodes"/>.</summary>
	[JsonPropertyName("code")]
	public string Code { get; init; }

	/// <summary>
	/// Values specific to this occurrence — a target container name, a row count, the carried property
	/// names. Omitted when the code needs none, which is the common case.
	/// </summary>
	[JsonPropertyName("params")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonNode> Params { get; init; }
}

/// <summary>
/// The closed vocabulary of <see cref="DroppedElement.Reason"/> codes: why a source element did NOT reach
/// the mobile page.
/// </summary>
/// <remarks>
/// Every code here answers a question the rest of the payload cannot. That was NOT true of the codes this
/// vocabulary used to carry for elements that DO convert — <c>leaf-supported</c> restated
/// <c>operation: "insert"</c> plus the presence of <c>mobileType</c>; <c>*-retargeted</c> and
/// <c>*-positioned</c> restated <c>parentName</c> / <c>propertyName</c> / <c>index</c>, which already carry
/// the RESULT; <c>synthesized-by-converter</c> restated an absent <c>webName</c>. An entry in
/// <see cref="MobilePageConversionGuide.ElementMap"/> is a deterministic instruction to apply, so a code
/// explaining it added bytes and nothing else and they are gone (ENG-95827).
/// <para>
/// A DROP is the opposite: nothing gets built, so there is no instruction to read the cause off. It was
/// measured on a real <c>Leads_FormPage</c> — 11 of its 12 dropped elements have
/// <c>componentSuggestions[].category = "DirectMapping"</c>, i.e. a type that converts perfectly well, so
/// without a code the entry reads as conversion loss and the natural response to conversion loss is to
/// re-insert. The causes need four DIFFERENT things said to the user: inherited chrome and a positional
/// exclusion are not loss and must not be re-added, an unsupported request IS a lost action, and an emptied
/// container is automatic housekeeping.
/// </para>
/// <para>
/// Named constants rather than inline literals because these are asserted verbatim by the unit and E2E
/// suites and documented one-for-one in the guidance article — the same reason the
/// <c>dataSectionConflicts</c> kinds and the <see cref="ElementMapEntry.ParentSource"/> values are.
/// </para>
/// </remarks>
public static class ReasonCodes {
	// ── Why an element did NOT convert ────────────────
	/// <summary>A container left with no surviving mobile child.</summary>
	public const string DropEmptyContainer = "drop-empty-container";

	/// <summary>
	/// A container with no mobile equivalent: it is NOT recreated, and its children were reparented to
	/// params.target (each carries the new parent in its own operation, so there is nothing to apply).
	/// Params: <c>webType</c>, <c>target</c>.
	/// </summary>
	public const string DropContainerNoMobileEquivalent = "drop-container-no-mobile-equivalent";

	/// <summary>
	/// An <c>excludedComponents</c> rule matched. Params: <c>webType</c>, <c>host</c>, <c>slot</c>.
	/// </summary>
	public const string DropExcludedByRule = "drop-excluded-by-rule";

	/// <summary>An ancestor was removed by an <c>excludedComponents</c> rule. Params: <c>ancestor</c>.</summary>
	public const string DropParentExcluded = "drop-parent-excluded";

	/// <summary>
	/// Chrome inherited from the WEB template, which the mobile template provides natively.
	/// Params: <c>name</c>.
	/// </summary>
	public const string DropInheritedChrome = "drop-inherited-chrome";

	/// <summary>
	/// The conversion target is absent from the mobile template, so the element could not be placed.
	/// Params: <c>target</c>.
	/// </summary>
	public const string DropTargetMissing = "drop-target-missing";

	/// <summary>
	/// A <c>crt.Button</c> whose request the Mobile app does not support. Params: <c>request</c>.
	/// </summary>
	public const string DropUnsupportedRequest = "drop-unsupported-request";

	/// <summary>The web type has no mobile counterpart in the registry. Params: <c>webType</c>.</summary>
	public const string DropTypeNotInMobileRegistry = "drop-type-not-in-mobile-registry";

	/// <summary>
	/// A request absent from the conversion map — CUSTOM or unknown, not known-unsupported. clio cannot
	/// assert it is unavailable on mobile, only that it does not know it. Params: <c>request</c>,
	/// <c>scope</c>.
	/// </summary>
	public const string DropUnknownRequest = "drop-unknown-request";

	/// <summary>
	/// No conversion rule matches this component inside a non-converting scope. Params: <c>scope</c>.
	/// </summary>
	public const string DropNoRuleInScope = "drop-no-rule-in-scope";

	/// <summary>
	/// Inside a non-converting scope and not itself a placeable action (no own convertible <c>clicked</c>).
	/// Its nested actions are still flattened. Params: <c>scope</c>.
	/// </summary>
	public const string DropNotAnActionInScope = "drop-not-an-action-in-scope";

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

	/// <summary>
	/// True when the SOURCE page declares the key this caption came from. Not serialized — it exists so the
	/// resource collector can tell a caption declared with EMPTY text (register it: the page's own deliberate
	/// "no visible label") from one whose key the page never declared (skip it: the platform resolves the
	/// caption itself, and registering a key would replace a localized title with one hardcoded culture).
	/// A single "is the text non-empty" test conflates the two, and because the caption is RE-KEYED to
	/// <c>&lt;mobileName&gt;_caption</c> the token scan cannot recover the first case — the carried token names
	/// a key the converter invented, which no source declaration backs (ENG-95827).
	/// </summary>
	[JsonIgnore]
	public bool SourceDeclared { get; init; }
}

/// <summary>
/// ONE operation of the mobile page's <c>viewConfigDiff</c>, in the mobile diff applier's own shape —
/// nothing else. Apply the list in order; add only what <c>pendingBindings</c> names.
/// </summary>
/// <remarks>
/// The shape is the applier's, verified against it rather than invented: <c>Insert</c> resolves its
/// target through <c>parentName</c> + <c>propertyName</c>, reads the position from <c>index</c> and the
/// component from <c>values</c>, and a <c>merge</c> resolves by <c>name</c> alone. Only <c>insert</c>
/// and <c>merge</c> appear here; <c>set</c>, <c>move</c> and <c>remove</c> exist in the applier but the
/// converter emits none of them today.
/// <para>
/// This replaced an <c>elementMap</c> whose entries mixed the operation with conversion METADATA —
/// <c>webName</c>, <c>webType</c>, <c>mobileName</c>, <c>mobileValues</c>, <c>parentSource</c>,
/// <c>captionResource</c> — so the caller had to transcribe each entry into an operation by hand, and a
/// transcription is a place to make mistakes. The metadata did not disappear: the source
/// correspondence is <c>nameMap</c> (renames only — everything else joins to
/// <c>sourceStructure</c> by name), an unresolvable parent is <c>unresolvedParents</c>, the caption
/// resource was pure duplication of <c>resourceStrings</c> and is gone, and an element that did not
/// convert is in <c>droppedElements</c>. The <c>web*</c> naming went with it, because the converter is
/// growing a second source kind — old mobile page to new mobile page — and a field called
/// <c>webName</c> would then be a lie (ENG-95827).
/// </para>
/// </remarks>
public sealed class ViewConfigDiffOperation {
	/// <summary><c>insert</c> or <c>merge</c>.</summary>
	[JsonPropertyName("operation")]
	public string Operation { get; init; }

	/// <summary>The mobile element this operation addresses.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The container to insert into. Absent on a <c>merge</c>, which resolves by name.</summary>
	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	/// <summary>The parent's child collection. Absent when it is the default <c>items</c>.</summary>
	[JsonPropertyName("propertyName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PropertyName { get; init; }

	/// <summary>0-based position within the parent's collection. Absent to append.</summary>
	[JsonPropertyName("index")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Index { get; init; }

	/// <summary>
	/// The component values. On an <c>insert</c> this carries the <c>type</c> and every source property
	/// the mobile component supports; on a <c>merge</c> only the delta over what the template provides,
	/// with no <c>type</c>. Absent when a merge has nothing to apply — the template's own configuration
	/// stands and there is nothing to add.
	/// </summary>
	[JsonPropertyName("values")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode Values { get; init; }
}

/// <summary>
/// The value binding an <c>insert</c> still needs, which the converter cannot place itself.
/// </summary>
/// <remarks>
/// The source element binds its value through <see cref="SourceProperty"/> (<c>control</c> or
/// <c>value</c>), and the mobile component's binding property is a TYPE-SPECIFIC rename of it — a mobile
/// <c>crt.ComboBox</c> binds via <c>value</c>, while <c>control</c> requires <c>items</c> or the page
/// crashes. Which property each mobile type wants is not derivable from anything the response carries:
/// <c>mobileContracts[].allowedProperties</c> lists BOTH for <c>crt.ComboBox</c> and <c>crt.Input</c>.
/// So the converter reports the binding it found instead of guessing at where to put it — 31 of 136
/// inserts on a real <c>Leads_FormPage</c> need one, and before this the value was simply discarded and
/// the caller told in prose to "add the value binding" with no way to know what it was (ENG-95827).
/// <para>
/// Attach <see cref="SourceValue"/> to the inserted component under the property that component's
/// contract wants. When the conversion rules gain per-type binding data this list disappears and the
/// binding is folded into <c>values</c>.
/// </para>
/// </remarks>
public sealed class PendingBinding {
	/// <summary>The mobile element from <c>viewConfigDiff</c> that needs the binding.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The property the SOURCE element bound through: <c>control</c> or <c>value</c>.</summary>
	[JsonPropertyName("sourceProperty")]
	public string SourceProperty { get; init; }

	/// <summary>The binding expression to re-attach, verbatim (e.g. <c>$UsrName</c>).</summary>
	[JsonPropertyName("sourceValue")]
	public JsonNode SourceValue { get; init; }
}

/// <summary>
/// An <c>insert</c> whose <c>parentName</c> is provided by NEITHER the diff nor the probed mobile
/// template. Report the name and stop; do not guess.
/// </summary>
/// <remarks>
/// Inserting into it throws, and authoring it may duplicate something the template owns under another
/// name. It is a conversion-RULES defect, not a page defect: a <c>containers</c> mapping names a mobile
/// container the target template does not have. The shipped rules reach it —
/// <c>BlankPageTemplate</c> maps <c>MainContainer -&gt; MainContainer</c>, but
/// <c>BlankMobilePageTemplate</c> is a standalone bare <c>crt.Scaffold</c> with no
/// <c>MainContainer</c> (ENG-95827).
/// <para>
/// Only this case is reported. A parent the diff itself inserts, or one the probed template provides,
/// needs no field: the caller can see whether the name appears in <c>viewConfigDiff</c>.
/// </para>
/// </remarks>
public sealed class UnresolvedParent {
	/// <summary>The mobile element whose parent could not be resolved.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The parent name nothing provides.</summary>
	[JsonPropertyName("parentName")]
	public string ParentName { get; init; }
}

/// <summary>
/// Instance-level conversion decision for ONE named element of the source page (ENG-89620). CONVERTER
/// BOOKKEEPING ONLY — never serialized. It is the working shape every pass mutates; the response is
/// projected out of it into <c>viewConfigDiff</c> + <c>droppedElements</c> + the metadata siblings.
/// </summary>
public sealed class ElementMapEntry {
	/// <summary>
	/// Source element name. Omitted for a SYNTHESIZED entry — a container the converter creates that has
	/// no web counterpart (the tab-body / Area layers of a converted tab). Its <c>reason</c>
	/// says so explicitly; apply it exactly like any other <c>insert</c>.
	/// </summary>
	public string WebName { get; init; }

	public string WebType { get; init; }

	/// <summary>One of: <c>merge</c> | <c>insert</c> | <c>drop</c> | <c>relocate-children</c>.</summary>
	public string Operation { get; init; }

	/// <summary>Target element name on mobile (merge / insert).</summary>
	public string Name { get; init; }

	/// <summary>Target mobile type (insert / merge), when known to the mobile registry.</summary>
	public string MobileType { get; init; }

	/// <summary>
	/// Mobile parent element to attach to. For <c>insert</c> it is the element's parent; for
	/// <c>relocate-children</c> it is the container the element's children are placed into instead.
	/// Settable (like <see cref="Values"/>): the tab-area pass retargets a tab's
	/// top-level content onto the synthesized Area container after the element map is built.
	/// </summary>
	public string ParentName { get; set; }

	/// <summary>
	/// Parent property to insert into (insert); defaults to <c>items</c>. Settable (like
	/// <see cref="ParentName"/>) for the same reason: the tab-area pass retargets a tab's top-level content onto
	/// the synthesized Area container, and the slot travels with the parent — a child the web page kept in the
	/// tab's <c>tools</c> strip lands in the Area's <c>items</c>, the only child collection a
	/// <c>crt.GridContainer</c> declares.
	/// </summary>
	public string PropertyName { get; set; }

	/// <summary>
	/// Where this entry's <see cref="ParentName"/> comes from. Set on EVERY <c>insert</c> that names a parent,
	/// and only on those — the other operations do not insert anything into a parent. One of:
	/// <list type="bullet">
	/// <item><description><c>"template"</c> — this map does not create the parent AND the probed mobile template
	/// provides it (e.g. <c>MainContainer</c>, or <c>FloatingActionButton</c> via the Scaffold's
	/// <c>floatAction</c> slot). Insert THIS child into it; do not author, recreate or duplicate the parent
	/// ELEMENT — your own copy would override the native one. This does NOT forbid the parent's own
	/// <c>merge</c> entry, which is how per-breakpoint <c>columns</c> and a shifted <c>layoutConfig</c> reach
	/// the page at all, nor the empty-slot <c>merge</c> that the two-step idiom requires when the
	/// template-provided parent does not yet carry the slot being inserted into (an insert into a property the
	/// element does not carry throws — <c>menuItems</c> on a <c>crt.FloatingActionButton</c> is the standard
	/// case, and this converter emits exactly that).</description></item>
	/// <item><description><c>"page"</c> — the parent is inserted by this map and came from the source page; its
	/// own entry says how to create it.</description></item>
	/// <item><description><c>"converter"</c> — the parent is inserted by this map and was synthesized by the
	/// converter (a tab-body grid or its Area card); it carries no <c>webName</c>, and its own entry says how to
	/// create it.</description></item>
	/// <item><description><c>"unknown"</c> — NEITHER this map nor the probed mobile template provides the
	/// parent. Do not guess: inserting into it throws, and authoring it may duplicate something the template
	/// owns under another name. This is a CONVERSION-RULES defect, not a page defect — a
	/// <c>containers</c> mapping names a mobile container the target template does not have — so report the
	/// parent name and stop rather than working around it.</description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Derived in ONE pass over the finished map (see <c>WebToMobileAnalysisService.StampParentSource</c>).
	/// It replaced a <c>parentExistsOnTemplate</c> boolean that three separate retarget code paths each set for
	/// themselves, so it was absent from an ORDINARY insert into a template-provided parent — verified on a real
	/// <c>Leads_FormPage</c> guide, where <c>FloatingActionButton</c> carried the flag and <c>MainContainer</c>,
	/// equally template-provided, did not. A caller applying the flag's rule literally therefore handled two
	/// identical situations differently (ENG-95827).
	/// <para>
	/// "Not created by this map" is decidable from the map alone, but it is NOT the same question as "the
	/// template provides it", and conflating the two is why <c>"unknown"</c> exists. The shipped rules reach
	/// that state: <c>BlankPageTemplate</c> maps to <c>BlankMobilePageTemplate</c> with a
	/// <c>MainContainer -&gt; MainContainer</c> container pair, but mobile blank is a STANDALONE root — a bare
	/// <c>crt.Scaffold</c> — and <c>MainContainer</c> comes from <c>BaseMobileTemplate</c>, a different root it
	/// does not derive from. Stamping <c>"template"</c> there would tell the caller the page already provides a
	/// container that does not exist, and the insert would fail in the applier ("is not a container for other
	/// items"). The retarget paths' own <c>RetargetTargetMissing</c> check does not cover it: a container-map
	/// twin and the <c>MainContainer</c> fallback in <c>RelocateTargetFor</c> both produce a parent without
	/// consulting it. So the template's node set is consulted here, and <c>"template"</c> is only claimed when
	/// that set actually contains the parent — the check the old boolean did have.
	/// </para>
	/// </remarks>
	public string ParentSource { get; set; }

	/// <summary>
	/// Optional 0-based insert position within the parent's <c>items</c>. Set for a positional insert — a
	/// web element mapped above/below an anchor container via a <c>&lt;container&gt;:top</c> /
	/// <c>:bottom</c> template rule (<c>:top</c> elements get an ascending index from 0 so they land before
	/// the anchor, e.g. above the mobile <c>Tabs</c>; <c>:bottom</c> elements are appended, no index) — and
	/// for every CONVERTED WEB TAB under the mobile Tabs (indexed right after the template's general tab so
	/// the template's Feed/Attachments tabs stay last — always, the converter owns this ordering).
	/// Add it to the insert operation verbatim when present. Omitted for every other element — the mobile
	/// designer owns ordering. Settable (like <see cref="ParentName"/>): the empty-container removal pass
	/// re-compacts sibling indexes after dropping an empty positional sibling, and the
	/// converted-tab placement pass assigns tab indexes after the element map is built.
	/// </summary>
	public int? Index { get; set; }

	/// <summary>For an <c>insert</c> of a named element with a localizable caption.</summary>
	public CaptionResource CaptionResource { get; init; }

	/// <summary>
	/// The prebuilt, ready-to-paste mobile component <c>values</c>. For an <c>insert</c> it carries the
	/// component <c>type</c> and EVERY source property the mobile component supports (per the mobile
	/// registry) — copied verbatim, with only mobile-unsupported properties pruned; paste it as the inserted
	/// component's <c>values</c> WITHOUT dropping anything, then add ONLY the value binding (e.g.
	/// <c>control</c>, or <c>value</c> for lookups), which is type-specific and intentionally left out. For a
	/// <c>merge</c> twin it carries the page's parameters onto the template-provided element with no
	/// <c>type</c> — the whitelisted keys when the rule declares <c>carryProperties</c>, otherwise the page's
	/// DELTA over the web-template baseline for a same-component twin (e.g. crt.FileList → crt.FileList): only
	/// what the page changed, so a property left at the template default is omitted and the mobile element
	/// keeps its own default; merge them by name. Null when there is nothing prebuilt (a structural/advisory
	/// merge, an unchanged same-component twin, or an operation that carries no values).
	/// </summary>
	public JsonNode Values { get; set; }

	/// <summary>
	/// Converter bookkeeping, NEVER serialized on this entry: why a <c>drop</c> happened. Projected into
	/// <see cref="MobilePageConversionGuide.DroppedElements"/> when the response is assembled.
	/// </summary>
	/// <remarks>
	/// It lives on the entry because the passes need it there: a drop REPLACES an entry in place
	/// (<c>elementMap[i] = Drop(...)</c>) so that the orphan cascade and the empty-container cascade can see
	/// it while they walk. Only the split into <c>elementMap</c> + <c>droppedElements</c> happens at the end.
	/// <para>
	/// An entry that CONVERTS carries no reason at all. <see cref="ReasonCodes"/> explains why.
	/// </para>
	/// </remarks>
	public IReadOnlyList<ReasonCode> Reason { get; set; }

	/// <summary>
	/// Converter bookkeeping, never serialized: the MOBILE anchor name (e.g. <c>Tabs</c>) when this
	/// <c>insert</c> was routed by a <c>&lt;anchor&gt;:top</c> / <c>:bottom</c> template rule. The anchor-row
	/// pass counts the entries the RULE routed, never "every indexed insert under that parent" — an ordinary
	/// insert can legitimately target the same mobile container and must not shift the anchor.
	/// </summary>
	internal string PositionalAnchor { get; set; }

	/// <summary>
	/// Converter bookkeeping, never serialized: for a container TWIN (<c>merge</c>) the mobile container it
	/// sits inside. A merge carries no <c>parentName</c> on purpose — the caller reuses the element the
	/// template already provides and inserts nothing — but the adaptive-layout pass still needs to know the
	/// twin is a SIBLING of the inserts it places: a mobile <c>crt.GridContainer</c> positions children by
	/// <c>layoutConfig</c> only, so a twin left unplaced beside placed siblings is not rendered at all.
	/// </summary>
	internal string MergeParentName { get; set; }
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

	/// <summary>
	/// Diagnostic set only when the converted layout came back empty despite the source page having
	/// components — e.g. <c>"empty: …"</c> when the web-template baseline could not be resolved to a
	/// distinct template (a replacing schema over a same-named base). Null in the normal case; a caller
	/// must not mistake an empty layout for a legitimately layout-less page.
	/// </summary>
	[JsonPropertyName("layoutResolution")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string LayoutResolution { get; init; }

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
	/// Ready-to-paste <c>modelConfigDiff</c> built from <see cref="ModelConfig"/> — a set of FOCUSED targeted
	/// merges (one merge per top-level key, e.g. <c>["dataSources"]</c>, plus per-array overrides unioned with
	/// the mobile template's own native arrays), not a single root merge — mirroring the diff shape a hand-built
	/// mobile page emits so the diff engine's array-replace never silently drops the template baseline (see
	/// <c>WebToMobileAnalysisService.SplitModelConfigRootMerge</c>). Paste it VERBATIM as the mobile page's
	/// <c>modelConfigDiff</c>; do NOT hand-build it, collapse it back into one root merge, or source it from a
	/// pre-existing body (that is how attribute <c>type</c> metadata gets dropped). Null when there is no model config.
	/// </summary>
	[JsonPropertyName("modelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfigDiff { get; init; }

	/// <summary>
	/// Ready-to-paste <c>viewModelConfigDiff</c> built from the filtered <see cref="ViewModelConfig"/> — a
	/// set of FOCUSED targeted merges (a page-owned <c>["attributes"]</c> merge, per-collection
	/// <c>viewModelConfig.attributes</c> augments, and per-array <c>modelConfig</c> overrides unioned with the
	/// mobile template's own native arrays), not a single root merge — mirroring the diff shape a hand-built
	/// mobile page emits so the diff engine's array-replace never silently drops the template baseline (see
	/// <c>WebToMobileAnalysisService.SplitRootMergeIntoTargetedMerges</c>). Paste it VERBATIM as the mobile
	/// page's <c>viewModelConfigDiff</c>. Null when none.
	/// </summary>
	[JsonPropertyName("viewModelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfigDiff { get; init; }

	/// <summary>
	/// Every change to a template-owned data-section value that neither diff can express, one entry per
	/// occurrence. Null when there are none, which is the normal case. See <see cref="DataSectionConflict"/>
	/// for what each <c>kind</c> costs and how to fix it — the three do not share one outcome, and two of them
	/// need opposite remedies, so read them individually rather than as one warning.
	/// </summary>
	[JsonPropertyName("dataSectionConflicts")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<DataSectionConflict> DataSectionConflicts { get; init; }

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
	/// The mobile page's <c>viewConfigDiff</c>, ready to apply in order. PASTE IT as the page's
	/// <c>viewConfigDiff</c> and add only what <see cref="PendingBindings"/> names — do not rebuild the
	/// operations, rename their fields, or infer merge-vs-insert from <c>containerMap</c>.
	/// </summary>
	/// <remarks>
	/// Every entry is an applier operation and nothing else. What did NOT convert is not an operation, so
	/// it is in <see cref="DroppedElements"/>; the source correspondence is in <see cref="NameMap"/>; a
	/// parent nothing provides is in <see cref="UnresolvedParents"/> (ENG-95827).
	/// </remarks>
	[JsonPropertyName("viewConfigDiff")]
	public IReadOnlyList<ViewConfigDiffOperation> ViewConfigDiff { get; init; } = [];

	/// <summary>
	/// Source element name → mobile element name, for the elements the converter RENAMED. Everything else
	/// keeps its name, so it joins to <c>sourceStructure</c> directly; a name in
	/// <see cref="ViewConfigDiff"/> that appears in neither was synthesized by the converter. Null when
	/// nothing was renamed.
	/// </summary>
	/// <remarks>
	/// Renames are rare — 5 of 155 entries on a real <c>Leads_FormPage</c> — which is why this is a map of
	/// the exceptions rather than a per-operation <c>webName</c>/<c>webType</c> pair repeated 155 times.
	/// </remarks>
	[JsonPropertyName("nameMap")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, string> NameMap { get; init; }

	/// <summary>
	/// The value bindings the inserts still need, which the converter cannot place itself. Apply each to the
	/// named element under the binding property its mobile contract wants. Null when none is needed.
	/// </summary>
	[JsonPropertyName("pendingBindings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<PendingBinding> PendingBindings { get; init; }

	/// <summary>
	/// Inserts whose parent is provided by NEITHER this diff nor the probed mobile template — a
	/// conversion-rules defect to report rather than work around. Null in the normal case.
	/// </summary>
	[JsonPropertyName("unresolvedParents")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<UnresolvedParent> UnresolvedParents { get; init; }

	/// <summary>
	/// Source elements that did NOT reach the mobile page, with a coded reason each. Nothing to apply —
	/// REPORT these to the user, and re-insert none of them. Null when every element converted.
	/// </summary>
	[JsonPropertyName("droppedElements")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<DroppedElement> DroppedElements { get; init; }

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
	/// <c>elementMap[].mobileValues</c>. An unsupported or unknown/custom request is handled by component
	/// type: on a <c>crt.Button</c> the whole element is DROPPED (a dead button, appearing as an
	/// <c>elementMap</c> drop and recorded under <c>droppedRequests</c>) — including a button retargeted
	/// into the FAB from a non-converting scope; on any other component type the binding is kept verbatim
	/// and flagged for manual review (the component stays). This section is an advisory SUMMARY — the
	/// actionable result is already baked into <c>mobileValues</c>. Null when the source page references no
	/// requests. (Page <c>handlers</c> are web-only and never transferred.)
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

	// ── Tab body / Area layers synthesized inside a converted tab ──────
	/// <summary>
	/// The containers the converter SYNTHESIZES inside every tab it creates: the designer's
	/// tab-body grid and the Area card inside it that receives the tab's content. Already baked into
	/// <see cref="ElementMap"/> as ordinary
	/// <c>insert</c> entries placed right after the tab's own entry — there is nothing separate to apply.
	/// This is an informational summary of a MANDATORY structure, NOT a proposal: report it at the
	/// conversion gate as fact, never offer to skip or replace it. Null when the page has no
	/// converter-created tab with content (a tab the mobile template provides is a merge twin and is never
	/// touched; an empty tab gets no layers at all).
	/// </summary>
	[JsonPropertyName("tabAreaLayers")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TabAreaLayerGroup> TabAreaLayers { get; init; }

	// ── Spacing normalized on inserted containers ──────────────────────
	/// <summary>
	/// Spacing normalization applied by the converter: mobile pages follow the mobile spacing
	/// standard, so the WEB page's container spacing is deliberately IGNORED (discarded, not translated) —
	/// every <c>crt.GridContainer</c> / <c>crt.FlexContainer</c> the converter INSERTS (converted from web
	/// and synthesized tab-body / Area layers alike) already carries gap Medium on all axes in
	/// <c>elementMap[].mobileValues</c>, so there is nothing separate to apply. Merge twins the mobile
	/// template provides are never touched. This is a SILENT normalization, NOT a gate decision: report it
	/// as one aggregated line in the plan and the final report; never ask whether to apply it and never
	/// restore the web spacing. Null when nothing was normalized.
	/// <para>
	/// BACK-COMPAT ALIAS: this section shipped before <see cref="Normalizations"/> existed and duplicates
	/// its <c>"spacing"</c> entry, shape unchanged. New callers should read <see cref="Normalizations"/>,
	/// which also carries the standards this one cannot express. REMOVAL TARGET: the only consumer is an
	/// LLM prompt, so this duplicate should go once the guidance article published for
	/// <c>normalizations</c> has shipped — it is not intended to be permanent.
	/// </para>
	/// </summary>
	[JsonPropertyName("spacingNormalization")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SpacingNormalizationInfo SpacingNormalization { get; init; }

	// ── Every property normalization the conversion rules declare ──────
	/// <summary>
	/// One section per normalization standard the CONVERSION RULES declare, keyed by the rule's
	/// <c>reportGroup</c> (e.g. <c>"spacing"</c>, <c>"metricStyle"</c>). The set of keys is open: the rules
	/// file is resolved at runtime, so a standard added there appears here without a binary change, and a
	/// key this build has never seen gets its own section rather than being folded into another standard's.
	/// <para>
	/// Each section carries the caller-facing wording from its rule, the elements normalized (with the
	/// dotted paths actually written), and anything the stamp had to skip. Read the section rather than
	/// assuming a fixed set of properties. Null when nothing was normalized at all.
	/// </para>
	/// </summary>
	[JsonPropertyName("normalizations")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, NormalizationInfo> Normalizations { get; init; }

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

	/// <summary>
	/// The guidance article that owns the conversion flow and every standing mobile rule. This response
	/// carries NO advisory prose of its own: a finding gets a structured field, a rule gets a validator or
	/// the article. Adding a prose array here is a regression (ENG-95827).
	/// </summary>
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

	/// <summary>The component-registry / rules version the guide was built against (a concrete version or "latest").</summary>
	[JsonPropertyName("resolvedTargetVersion")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedTargetVersion { get; init; }

	/// <summary>How the version was resolved: "environment", "environment-superset", or "latest-fallback".</summary>
	[JsonPropertyName("resolvedFrom")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedFrom { get; init; }

	/// <summary>Caveat when the catalog is approximate or the target version is unknown; null when the version is exact.</summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string VersionWarning { get; init; }

	/// <summary>True only on "latest-fallback": the target version is unknown, so the caller must confirm with the user before acting on the guide.</summary>
	[JsonPropertyName("requiresVersionConfirmation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool RequiresVersionConfirmation { get; init; }

	/// <summary>Stable kebab-case reason on the "latest-fallback" tier (e.g. "no-active-environment", "probe-error"); null otherwise.</summary>
	[JsonPropertyName("resolvedFromReason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedFromReason { get; init; }

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

/// <summary>
/// The tab-body / Area layers synthesized inside ONE converter-created tab. Mirrors what is
/// already baked into the element map: mobile design puts a tab's content inside a colored Area card that
/// sits in a tab-body grid, and a tab converted from web carries neither.
/// </summary>
public sealed class TabAreaLayerGroup {
	/// <summary>The converted tab the layers were synthesized into (its mobile name).</summary>
	[JsonPropertyName("tabName")]
	public string TabName { get; init; }

	/// <summary>Name of the synthesized tab-body grid (the tab's direct child).</summary>
	[JsonPropertyName("mainTabContainerName")]
	public string MainTabContainerName { get; init; }

	/// <summary>
	/// Name of the synthesized Area card (child of the tab-body grid). Null when the tab's top-level
	/// content is routing hints only — no Area is synthesized then, so an empty card never appears (AC#5).
	/// </summary>
	[JsonPropertyName("areaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AreaName { get; init; }

	/// <summary>
	/// The components moved out of the tab and into the Area, in the order they are stacked there
	/// (the source page's own order — the first entry is row 1). Already reflected in each element's
	/// <c>parentName</c> and <c>layoutConfig</c>, so there is nothing to re-parent by hand.
	/// </summary>
	[JsonPropertyName("movedChildren")]
	public IReadOnlyList<string> MovedChildren { get; init; } = [];
}

/// <summary>
/// Advisory summary of the spacing normalization: which inserted containers had their
/// spacing stamped with the mobile-standard values (gap Medium). The actionable result is already
/// baked into <c>elementMap[].mobileValues</c>; this section only feeds the plan / final-report line.
/// </summary>
public sealed class SpacingNormalizationInfo {
	/// <summary>Why the web spacing is ignored and how to report the normalization.</summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }

	/// <summary>One entry per normalized inserted container.</summary>
	[JsonPropertyName("normalized")]
	public IReadOnlyList<SpacingNormalizationEntry> Normalized { get; init; } = [];
}

/// <summary>One inserted container whose spacing was normalized to the mobile standard.</summary>
public sealed class SpacingNormalizationEntry {
	/// <summary>The container's mobile element name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The container's mobile component type (e.g. "crt.GridContainer").</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>The property names stamped onto the container's mobileValues (e.g. ["gap"]).</summary>
	[JsonPropertyName("properties")]
	public IReadOnlyList<string> Properties { get; init; } = [];
}

/// <summary>
/// One normalization standard's report: the caller-facing wording carried by the conversion rule that
/// declared it, what was normalized, and what could not be. Shared by every standard — a new one is a
/// rules-file entry, not another pair of identical DTOs.
/// </summary>
public sealed class NormalizationInfo {
	/// <summary>
	/// Caller-facing summary of this standard's outcome, composed by clio from the actual counts. Never
	/// sourced from the rules file: those resolve at runtime, and this text reaches the agent's
	/// instruction channel.
	/// </summary>
	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>One entry per element this standard normalized.</summary>
	[JsonPropertyName("normalized")]
	public IReadOnlyList<NormalizationEntry> Normalized { get; init; } = [];

	/// <summary>
	/// Elements the standard could NOT be applied to, with the branch it refused and why. Present only when
	/// something was skipped. Without it a silent no-op is indistinguishable from "nothing to normalize" —
	/// these elements keep the WEB values and may need a manual pass in the designer.
	/// </summary>
	[JsonPropertyName("skipped")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<NormalizationSkip> Skipped { get; init; }
}

/// <summary>One element normalized to a standard, and the properties actually written on it.</summary>
public sealed class NormalizationEntry {
	/// <summary>The element's mobile name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The element's mobile component type.</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The properties stamped onto this element's mobileValues. A replacing rule reports the top-level key
	/// it replaced (e.g. <c>["gap"]</c>); a merging rule reports the dotted paths of the leaves it actually
	/// changed (e.g. <c>["config.layout.border.hidden"]</c>) — never the merged root, which would
	/// under-report, and never a leaf that already held the target value.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyList<string> Properties { get; init; } = [];
}

/// <summary>One element a standard could not be stamped onto, and why.</summary>
public sealed class NormalizationSkip {
	/// <summary>The element's mobile name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The element's mobile component type.</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The dotted paths the stamp refused to enter (e.g. <c>["config.text"]</c> when the element binds its
	/// whole text config). Other paths of the same rule may still have been stamped.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyList<string> Properties { get; init; } = [];

	/// <summary>Why the branch was refused, in caller-facing wording.</summary>
	[JsonPropertyName("reason")]
	public string Reason { get; init; }
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
